using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Meta.XR;
using NUnit.Framework;
using UnityEngine;

namespace FloorFracture.Tests
{
    public class FloorSurveyTests
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int> indices = new();
        private static Vector2[] Box(float x0, float z0, float x1, float z1) => new[]
        {
            new Vector2(x0, z0),
            new Vector2(x1, z0),
            new Vector2(x1, z1),
            new Vector2(x0, z1)
        };
        private static Vector3 P(float x, float z, float y = 0) => new Vector3(x, y, z);
        private void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
        }

        private void Surface(float left, float bottom, float right, float top, float y = 0)
        {
            Triangle(P(left, bottom, y), P(right, bottom, y), P(right, top, y));
            Triangle(P(left, bottom, y), P(right, top, y), P(left, top, y));
        }

        private void Post(float x, float z, float height)
        {
            var a = P(x, z);
            var b = P(x + .025f, z);
            Triangle(a, a + Vector3.up * height, b + Vector3.up * height);
            Triangle(a, b + Vector3.up * height, b);
        }

        private FloorSurvey Survey(params Vector2[][] furniture)
        {
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            return new FloorSurvey(new RoomFloor(Box(-3, -3, 3, 3), new List<Vector2[]>(furniture)), scan, 2.2f, null, CancellationToken.None);
        }

        [SetUp]
        public void ResetMesh()
        {
            vertices.Clear();
            indices.Clear();
        }

        [Test]
        public void NativeFloorAcceptsShallowNoiseAndLiveDepthStillExcludesRealObjects()
        {
            const int width = 31;
            var first = new Vector3?[width * width];
            var second = new Vector3?[first.Length];
            for (int i = 0; i < first.Length; i++)
            {
                float x = (i % width - width / 2) * RoomBoardPlacement.DepthSpacing;
                float z = (i / width - width / 2) * RoomBoardPlacement.DepthSpacing;
                float y = x < -.45f && z < -.45f ? .16f : (x > .5f && z < 0 ? .035f : 0);
                first[i] = P(x, z, y);
                if (x < .4f || z < .4f) second[i] = first[i];
            }
            var liveObjects = new List<Vector2[]>();
            var surface = RoomBoardPlacement.BuildDepthSurface(first, second, width, 0, 0, out int samples, out _, liveObjects);
            Assert.That(samples, Is.GreaterThan(500));
            Assert.That(liveObjects, Is.Not.Empty);
            Surface(-3, -3, 3, 3, .032f);
            Post(.55f, -.6f, .7f);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var saved), Is.True);
            var floor = new RoomFloor(Box(-2, -2, 2, 2), new List<Vector2[]> { Box(-.75f, .5f, -.5f, .75f) });
            var old = new FloorSurvey(floor, saved, 2.2f, null, default);
            var current = new FloorSurvey(floor, saved, 2.2f, null, default, liveObjects, surface);
            Assert.That(old.Check(Box(-.3f, -.3f, .3f, .3f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(current.ChecksEveryTile(new BoardPlacement(Vector2.zero, 0, 0, 2, 1, .4f)), Is.True);
            Assert.That(current.Check(Box(-.9f, -.9f, -.6f, -.6f)), Is.EqualTo(TileClearance.LiveObstacle));
            Assert.That(current.Check(Box(.5f, -.7f, .6f, -.5f)), Is.EqualTo(TileClearance.MeshObstacle));
            Assert.That(current.Check(Box(.65f, .65f, .85f, .85f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(current.Check(Box(-.7f, .55f, -.6f, .65f)), Is.EqualTo(TileClearance.Furniture));
        }

        [Test]
        public void TwoDepthPassesAreRequiredAndAWidespreadRaisedSurfaceCannotBecomeFloor()
        {
            var first = new Vector3?[64];
            var second = new Vector3?[64];
            for (int i = 0; i < first.Length; i++) first[i] = P(i % 8 * .075f, i / 8 * .075f);
            var objects = new List<Vector2[]>();
            Assert.That(RoomBoardPlacement.BuildDepthSurface(first, second, 8, 0, 0, out _, out _, objects), Is.Null);
            for (int i = 0; i < first.Length; i++) second[i] = first[i] += Vector3.up * .08f;
            Assert.That(RoomBoardPlacement.BuildDepthSurface(first, second, 8, 0, 0, out _, out _, objects), Is.Null);
        }

        [TestCase(0f)]
        [TestCase(-.08f)]
        public void BroadMeasuredFloorGroundsBoardEvenWhenTrackingUsesTheLabel(float trackingOffset)
        {
            Surface(-3, -3, .5f, 3, -.09f);
            Surface(.6f, -3, 3, 3, -.09f);
            Surface(-1.1f, -.2f, -.9f, .2f, -.065f);
            var shift = new Vector3(7, 2, -9);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.Translate(shift), out var scan), Is.True);
            var floor = new RoomFloor(Box(4, -12, 10, -6), new List<Vector2[]> { Box(8, -9, 8.5f, -8.5f) }, shift.y);
            var aligned = scan.AlignFloor(floor, shift.y + trackingOffset);
            Assert.That(aligned.MeshFloorHeight, Is.EqualTo(shift.y - .09f).Within(.001f));
            Assert.That(aligned.Height, Is.EqualTo(shift.y - .09f).Within(.001f));
            var survey = new FloorSurvey(aligned, scan, 2.2f, null, default);
            Assert.That(survey.ChecksEveryTile(new BoardPlacement(new Vector2(7, -10), aligned.Height, 0, 1, 2, .4f)), Is.True);
            Assert.That(survey.Check(Box(5.95f, -9.1f, 6.05f, -8.9f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(survey.Check(Box(7.4f, -9.3f, 7.7f, -9.1f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(survey.Check(Box(8.1f, -8.9f, 8.3f, -8.7f)), Is.EqualTo(TileClearance.Furniture));
        }

        [Test]
        public void DuplicatedSunkenPatchAndRaisedPlatformsCannotResetTheRoomBaseline()
        {
            Surface(-3, -3, 0, 3);
            for (int repeat = 0; repeat < 200; repeat++) Surface(0, 0, .5f, .5f, -.10f);
            Surface(0, .6f, 3, 3, .08f);
            var floor = new RoomFloor(Box(-3, -3, 3, 3), new List<Vector2[]>());
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            Assert.That(scan.AlignFloor(floor, -.1f), Is.SameAs(floor));
        }

        [Test]
        public void NativeFloorSpansAnUnmeasuredSeamUnlessThereIsEvidenceOfADrop()
        {
            Surface(-3, -3, .11f, 3);
            Surface(.12f, -3, 3, 3);
            // A duplicated face and a seam in the mesh do not contradict the mapped floor.
            Surface(-3, -3, .11f, 3);
            var survey = Survey();
            Assert.That(survey.Check(Box(-.3f, -.3f, .3f, .3f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(survey.Check(Box(-1, -.3f, -.5f, .3f)), Is.EqualTo(TileClearance.Clear));
        }

        [Test]
        public void RejectsMeshOnlyChairLegAtCornerAndFurnitureInsideFootClearance()
        {
            Surface(-3, -3, 3, 3);
            Post(.22f, .21f, .7f);
            var survey = Survey(Box(1.03f, -.1f, 1.05f, .1f));
            Assert.That(survey.Check(Box(-.3f, -.3f, .3f, .3f)), Is.EqualTo(TileClearance.MeshObstacle));
            var fit = new BoardPlacement(Vector2.zero, 0, 0, 4, 4, .5f);
            Assert.That(survey.Check(fit.TileOutline(3, 1, FloorSurvey.FootMargin)), Is.EqualTo(TileClearance.Furniture));
            Assert.That(survey.ChecksEveryTile(fit), Is.False);
        }

        [Test]
        public void KeepsFullFootprintsInsideRotatedBoundaryAndRefitsAfterLiveVeto()
        {
            Surface(-3, -3, 3, 3);
            var survey = Survey();
            Assert.That(survey.ChecksEveryTile(new BoardPlacement(new Vector2(2.5f, 0), 0, 39, 2, 2, .5f)), Is.False);
            var first = new BoardPlacement(Vector2.zero, 0, 23, 3, 3, .55f);
            Assert.That(survey.ChecksEveryTile(first), Is.True);
            survey.MarkWorld(first.TileOutline(1, 1, FloorSurvey.FootMargin), TileClearance.LiveUnknown);
            Assert.That(survey.ChecksEveryTile(first), Is.False);
            var options = new List<RoomFloor>();
            survey.AddClearRectangles(options);
            Assert.That(BoardFitter.TryFindOnSurfaces(options, Vector2.zero, out var revised, default, null, FloorSurvey.FootMargin, true), Is.True);
            Assert.That(survey.ChecksEveryTile(revised), Is.True);
        }

        [Test]
        public void CeilingDoesNotBlockMappedFloorAndAnElevatedSlabDoes()
        {
            Surface(-3, -3, 3, 3, 2.6f);
            Assert.That(Survey().Check(Box(-.2f, -.2f, .2f, .2f)), Is.EqualTo(TileClearance.Clear));
            Surface(-3, -3, 3, 3);
            Surface(-.4f, -.4f, .4f, .4f, 1.1f);
            Assert.That(Survey().Check(Box(-.2f, -.2f, .2f, .2f)), Is.EqualTo(TileClearance.MeshObstacle));
        }

        [Test]
        public void MissingEmptyCorruptOrUnlocalizedMeshCannotCertifyFloor()
        {
            Assert.That(FloorMeshScan.TryRead(null, out _), Is.False);
            var v = new[]
            {
                P(0, 0),
                P(1, 0),
                P(0, 1)
            };
            Assert.That(FloorMeshScan.TryCreate(v, new int[0], Matrix4x4.identity, out _), Is.False);
            Assert.That(FloorMeshScan.TryCreate(v, new[] { 0, 1, 9 }, Matrix4x4.identity, out _), Is.False);
            v[1].y = float.NaN;
            Assert.That(FloorMeshScan.TryCreate(v, new[] { 0, 1, 2 }, Matrix4x4.identity, out _), Is.False);
            var floor = new RoomFloor(Box(-3, -3, 3, 3), new List<Vector2[]>());
            var missing = new FloorSurvey(floor, null, 2.2f, null, default);
            var candidates = new List<RoomFloor>();
            missing.AddClearRectangles(candidates);
            Assert.That(candidates, Is.Empty);
            Assert.That(missing.Check(Box(-.2f, -.2f, .2f, .2f)), Is.EqualTo(TileClearance.Unobserved));
        }

        [Test]
        public void AShallowReconstructionSlopeLeavesBothEndsPlayable()
        {
            // The native floor is continuous across the four-centimetre reconstruction drift.
            var a = P(-3, -2);
            var b = P(3, -2, .04f);
            var c = P(3, 2, .04f);
            var d = P(-3, 2);
            Triangle(a, b, c);
            Triangle(a, c, d);
            var survey = Survey();
            var left = new BoardPlacement(new Vector2(-1.6f, 0), 0, 0, 2, 2, .5f);
            var right = new BoardPlacement(new Vector2(1.6f, 0), 0, 0, 2, 2, .5f);
            Assert.That(survey.ChecksEveryTile(left), Is.True);
            Assert.That(survey.ChecksEveryTile(right), Is.True);
            Assert.That(survey.Check(Box(1.5f, -.2f, 1.7f, .2f)), Is.EqualTo(TileClearance.Clear));
        }

        [Test]
        public void OnlyThePartOfAnOverhangInsideStandingHeightBlocksGlass()
        {
            Surface(-3, -3, 3, 3);
            Triangle(P(-2, -1, 1.5f), P(2, -1, 3.5f), P(2, 1, 3.5f));
            var survey = Survey();
            Assert.That(survey.Check(Box(-1.3f, -.95f, -1.1f, -.85f)), Is.EqualTo(TileClearance.MeshObstacle));
            Assert.That(survey.Check(Box(.9f, -.55f, 1.2f, -.3f)), Is.EqualTo(TileClearance.Clear));
        }

        [Test]
        public void AVerifiedDiagonalAisleCanHoldPanesWhenTheRoomAlignedRectangleCannot()
        {
            Surface(-3, -3, 3, 3, -.35f);
            var corridor = new BoardPlacement(Vector2.zero, 0, 37, 2, 5, .64f);
            var outline = corridor.FootprintOutline();
            Vector3 At(int i) => P(outline[i].x, outline[i].y);
            Triangle(At(0), At(1), At(2));
            Triangle(At(0), At(2), At(3));
            var coarse = Survey();
            var options = new List<RoomFloor>();
            Assert.That(coarse.AddClearRectangles(options), Is.LessThan(1.16f));
            Assert.That(options, Is.Empty);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var result = FloorSurvey.Refine(new List<FloorSurvey> { coarse }, scan, 2.2f, null,
                Vector2.zero, null, default);
            Assert.That(result.fit.HasValue, Is.True);
            Assert.That(result.surveys[0].ChecksEveryTile(result.fit.Value), Is.True);
            Assert.That(result.fit.Value.Layout.PaneCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(result.surveys[0].Refresh(coarse.Floor, scan, 2.2f, null, default)
                .ChecksEveryTile(result.fit.Value), Is.True);
        }

        [Test]
        public void RefinementCannotBridgeAMeasuredDropOrRemoveFootClearance()
        {
            Surface(-3, -3, 3, 3, -.35f);
            Surface(-1, -2, -.12f, 2);
            Surface(.12f, -2, 1, 2);
            var original = Survey();
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var result = FloorSurvey.Refine(new List<FloorSurvey> { original }, scan, 2.2f, null,
                Vector2.zero, null, default);
            Assert.That(result.fit.HasValue, Is.True);
            var fit = result.fit.Value;
            Assert.That(result.surveys[0].ChecksEveryTile(fit), Is.True);
            for (int z = 0; z < fit.Rows; z++)
                for (int x = 0; x < fit.Columns; x++)
                    if (fit.Layout.HasPane(x, z))
                        foreach (var p in fit.TileOutline(x, z, FloorSurvey.FootMargin))
                            Assert.That(Mathf.Sign(fit.Center.x) * p.x, Is.GreaterThanOrEqualTo(.12f - .0001f));
        }

        [Test]
        public void FitsFourCompactPanesInTheReported104CentimetrePatch()
        {
            Surface(-3, -3, 3, 3, -.35f);
            Surface(-.52f, -.52f, .52f, .52f);
            var coarse = Survey();
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var result = FloorSurvey.Refine(new List<FloorSurvey> { coarse }, scan, 2.2f, null, Vector2.zero, null, default);
            Assert.That(result.fit.HasValue, Is.True);
            var fit = result.fit.Value;
            Assert.That(fit.Layout.PaneCount, Is.EqualTo(4));
            Assert.That(fit.Pitch, Is.InRange(BoardState.CompactMinPitch, .44f));
            Assert.That(result.surveys[0].ChecksEveryTile(fit), Is.True);
        }

        [Test]
        public void ConnectedBoardTurnsAroundFurnitureAndPausesAtTheMissingCorner()
        {
            Surface(-3, -3, 3, 3, -.35f);
            Surface(0, 0, 2.5f, 2.5f);
            var furniture = Box(.82f, .82f, 2.5f, 2.5f);
            var survey = Survey(furniture);
            var result = FloorSurvey.FindConnected(new[] {survey}, Vector2.one, default, out var owner);
            Assert.That(result.HasValue, Is.True);
            var fit = result.Value;
            Assert.That(fit.Layout.IsRectangle, Is.False);
            Assert.That(fit.Layout.IsPlayable && owner.ChecksEveryTile(fit), Is.True);
            Assert.That(BoardFitter.Fits(survey.Floor, fit, FloorSurvey.FootMargin), Is.True);
            Assert.That(BoardFitter.Fits(survey.Floor, new BoardPlacement(fit.Center, fit.FloorHeight, fit.AngleDegrees,
                fit.Columns, fit.Rows, fit.Pitch), FloorSurvey.FootMargin), Is.False);
            var board = new BoardState(fit.Layout, 0, fit.Pitch);
            for (int i = 0; i < board.Count; i++)
            {
                Assert.That(board.IsIntact(i), Is.EqualTo(fit.Layout.Contains(i)));
                if (!fit.Layout.Contains(i))
                {
                    Assert.That(board.IndexAt(board.Center(i)), Is.EqualTo(-1));
                    Assert.That(board.Sample(board.Center(i), true, 0).Kind, Is.EqualTo(SupportKind.Outside));
                    continue;
                }
                var wave = board.BeginWave(i, 3, 1, 0);
                Assert.That(board.Neighbors(i, wave.GuaranteedTile), Is.True);
                foreach (int tile in wave.SafeTiles) Assert.That(fit.Layout.Contains(tile), Is.True);
                board.Pause();
            }
        }

        [Test]
        public void CappedFitStaysNearTheWearerAndFullFloorCanExceedSixtyFourPanes()
        {
            Surface(-5, -5, 5, 5);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var floor = new RoomFloor(Box(-5, -5, 5, 5), new List<Vector2[]>());
            var survey = new FloorSurvey(floor, scan, 2.2f, null, default);
            var wearer = new Vector2(2.7f, 2.6f);
            var capped = FloorSurvey.FindConnected(new[] { survey }, wearer, default, out _).Value;
            Assert.That(capped.Layout.PaneCount, Is.EqualTo(16));
            Assert.That(capped.Pitch, Is.InRange(.60f, .65f));
            Assert.That(Vector2.Distance(capped.Center, wearer), Is.LessThan(.65f));
            Assert.That(survey.ChecksEveryTile(capped), Is.True);

            var full = FloorSurvey.FindConnected(new[] { survey }, wearer, default, out _, entireFloor: true).Value;
            Assert.That(full.Pitch, Is.InRange(.60f, .65f));
            Assert.That(full.Layout.PaneCount, Is.GreaterThan(150));
            Assert.That(full.Layout.IsPlayable && survey.ChecksEveryTile(full), Is.True);
            Assert.That(BoardFitter.Fits(floor, full, FloorSurvey.FootMargin), Is.True);
        }

        [Test]
        public void CappedCompactFitKeepsTheLargestPitchThatOffersFourPanes()
        {
            Surface(0, 0, 1.29f, 2.28f);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var floor = new RoomFloor(Box(0, 0, 1.29f, 2.28f), new List<Vector2[]>());
            var survey = new FloorSurvey(floor, scan, 2.2f, null, default);
            var fit = FloorSurvey.FindConnected(new[] { survey }, Vector2.one, default, out _).Value;
            Assert.That(fit.Pitch, Is.EqualTo(.55f));
            Assert.That(fit.Layout.PaneCount, Is.GreaterThanOrEqualTo(4));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NarrowConcaveFloorKeepsBothLegsOfAnAisleAtCompactPitch(bool entireFloor)
        {
            const float length = 2.56f, width = .64f;
            Surface(0, 0, length, width);
            Surface(0, width, width, length);
            var boundary = new[] {
                Vector2.zero, new Vector2(length, 0), new Vector2(length, width),
                new Vector2(width, width), new Vector2(width, length), new Vector2(0, length)
            };
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var survey = new FloorSurvey(new RoomFloor(boundary, new List<Vector2[]>()), scan, 2.2f, null, default);
            var result = FloorSurvey.Refine(new List<FloorSurvey> { survey }, scan, 2.2f, null,
                Vector2.one * .3f, null, default, entireFloor);
            Assert.That(result.fit.HasValue, Is.True);
            var fit = result.fit.Value;
            Assert.That(fit.Pitch, Is.InRange(.40f, .48f));
            Assert.That(fit.Layout.PaneCount, Is.GreaterThanOrEqualTo(7));
            Assert.That(fit.Layout.IsRectangle, Is.False);
            Assert.That(fit.Layout.IsPlayable && result.surveys[0].ChecksEveryTile(fit), Is.True);
            Assert.That(BoardFitter.Fits(survey.Floor, fit, FloorSurvey.FootMargin), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReconfirmedLiveObstacleRemainsExcludedAcrossRetryAndRefresh(bool entireFloor)
        {
            Surface(-3, -3, 3, 3);
            var survey = Survey();
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            survey.RecordLiveObstacle(Vector2.zero);
            var result = FloorSurvey.Refine(new List<FloorSurvey> { survey }, scan, 2.2f, null,
                Vector2.zero, null, default, entireFloor);
            Assert.That(result.fit.HasValue, Is.True);
            var refreshed = result.surveys[0].Refresh(survey.Floor, scan, 2.2f, null, default);
            Assert.That(refreshed.Check(Box(-.02f, -.02f, .02f, .02f)), Is.EqualTo(TileClearance.LiveObstacle));
            Assert.That(refreshed.ChecksEveryTile(result.fit.Value), Is.True);
            if (!entireFloor) Assert.That(result.fit.Value.Layout.PaneCount, Is.LessThanOrEqualTo(16));
        }

        [Test]
        public void RequiresTwoPanesSharingAnEdgeAndKeepsDetectedObjectsOutOfFutureBoards()
        {
            var diagonal = new BoardLayout(2, 2, 0b1001);
            Assert.That(diagonal.IsPlayable, Is.False);
            var strip = new BoardLayout(1, 2, 3);
            var game = new BoardState(strip, 1, .4f);
            var wave = game.BeginWave(0, 1, 1, 0);
            Assert.That(game.Tick(game.Center(1), true, wave.Deadline), Is.EqualTo(WaveResult.LevelCleared));
            Surface(-.3f, -.5f, .3f, .5f);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), Matrix4x4.identity, out var scan), Is.True);
            var floor = new RoomFloor(Box(-.3f, -.5f, .3f, .5f), new List<Vector2[]>());
            var open = new FloorSurvey(floor, scan, 2.2f, null, default);
            Assert.That(FloorSurvey.FindConnected(new[] {open}, Vector2.zero, default, out _).HasValue, Is.True);
            var blocked = new FloorSurvey(floor, scan, 2.2f, null, default,
                new[] { new[] { Vector2.zero } });
            Assert.That(FloorSurvey.Refine(new List<FloorSurvey>{blocked}, scan, 2.2f, null, Vector2.zero, null, default).fit.HasValue, Is.False);
        }

        [Test]
        public void LiveObjectMonitoringOnlyUsesSurfaceHitsOnThePaneAwayFromTheWearer()
        {
            var fit = new BoardPlacement(new Vector2(2, 0), .2f, 35, new BoardLayout(2, 2, 7), .45f);
            var target = fit.ToWorldXZ(-.225f, -.225f);
            var hit = new EnvironmentRaycastHit { status = EnvironmentRaycastHitStatus.Hit, point = new Vector3(target.x, .35f, target.y) };
            Assert.That(RoomBoardPlacement.IsObstacleOnPane(hit, fit, 0, Vector3.up), Is.True);
            Assert.That(RoomBoardPlacement.IsObstacleOnPane(hit, fit, 3, Vector3.up), Is.False);
            Assert.That(RoomBoardPlacement.IsObstacleOnPane(hit, fit, 0, hit.point + Vector3.up), Is.False);
            hit.point.y = fit.FloorHeight;
            Assert.That(RoomBoardPlacement.IsObstacleOnPane(hit, fit, 0, Vector3.up), Is.False);
        }

        [Test]
        public void SmallBumpsAndAnIsolatedHighSpikeDoNotPunchHolesInTheBoard()
        {
            Surface(-3, -3, 3, 3);
            Surface(.3f, .3f, .42f, .42f, .025f);
            Surface(.6f, .6f, .63f, .63f, .13f);
            for (int i = 0; i < 20; i++) Triangle(P(.25f, .25f), P(.28f, .28f, .27f), P(.29f, .25f));
            Surface(.9f, .9f, 1.3f, 1.3f, .14f);
            var survey = Survey();
            Assert.That(survey.Check(Box(.1f, .1f, .7f, .7f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(survey.Check(Box(1f, 1f, 1.2f, 1.2f)), Is.EqualTo(TileClearance.MeshObstacle));
            Assert.That(survey.Check(Box(-.7f, -.7f, -.3f, -.3f)), Is.EqualTo(TileClearance.Clear));
        }

        [Test]
        public void DenseMeshAndLiveRaysAgreeOnLowNoiseAndAClearObstacle()
        {
            for (int x = 0; x < 12; x++)
                for (int z = 0; z < 12; z++)
                    Surface(x * .02f, z * .02f, (x + 1) * .02f, (z + 1) * .02f, .14f);
            var survey = Survey();
            Assert.That(survey.Check(Box(.05f, .05f, .15f, .15f)), Is.EqualTo(TileClearance.MeshObstacle));
            var fit = new BoardPlacement(Vector2.zero, 0, 0, 2, 1, .5f);
            var eye = new Vector3(0, 1.5f, -1.3f);
            var hit = new EnvironmentRaycastHit { status = EnvironmentRaycastHitStatus.Hit, point = new Vector3(.25f, .06f, 0) };
            Assert.That(RoomBoardPlacement.ClassifyFloorRay(hit, fit, 1, eye), Is.EqualTo(TileClearance.Clear));
            hit.point.y = .14f;
            Assert.That(RoomBoardPlacement.ClassifyFloorRay(hit, fit, 1, eye), Is.EqualTo(TileClearance.LiveObstacle));
        }

        [Test]
        public void LowerSurfaceOnlyBlocksWhereTheMappedFloorHasAMeasuredDrop()
        {
            Surface(-2, -2, 2, 2, -.28f);
            Surface(-2, -2, 0, 2);
            var survey = Survey();
            Assert.That(survey.Check(Box(-1.2f, -.5f, -.9f, -.2f)), Is.EqualTo(TileClearance.Clear));
            Assert.That(survey.Check(Box(.8f, -.5f, 1.1f, -.2f)), Is.EqualTo(TileClearance.MeshObstacle));
        }

        [Test]
        public void RefinedGlobalAnchorKeepsObservedGlassAndRejectsANewObstacle()
        {
            Surface(-2, -2, 2, 2);
            var fit = new BoardPlacement(Vector2.zero, 0, 0, 2, 2, .55f);
            var floor = new RoomFloor(Box(-2, -2, 2, 2), new List<Vector2[]>());
            var firstPose = Matrix4x4.identity;
            var refinedPose = Matrix4x4.Translate(new Vector3(.001f, 0, -.001f));
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), firstPose, out var original), Is.True);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), refinedPose, out var refined), Is.True);
            Assert.That(original.Matches(refined), Is.False);
            Assert.That(new FloorSurvey(floor, refined, 2.2f, null, default).ChecksEveryTile(fit), Is.True);
            Surface(.1f, .1f, .3f, .3f, .18f);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), refinedPose, out var obstructed), Is.True);
            Assert.That(new FloorSurvey(floor, obstructed, 2.2f, null, default).ChecksEveryTile(fit), Is.False);
        }

        [Test]
        public void DiagnosticsDistinguishAcceptedRejectedAndUnknownPanesAndSwitchOff()
        {
            Surface(-3, -3, .10f, 3);
            Surface(.15f, -3, 3, 3);
            Post(1, 1, .45f);
            var survey = Survey(Box(1.5f, -2, 2, -1));
            survey.MarkWorld(Box(-1, -2, -.8f, -1.8f), TileClearance.LiveObstacle);
            survey.MarkWorld(Box(.6f, -2, .8f, -1.8f), TileClearance.Unobserved);
            survey.MarkWorld(Box(-.6f, -2, -.4f, -1.8f), TileClearance.LiveUnknown);
            var board = new BoardPlacement(new Vector2(-1.7f, 1), 0, 0, 2, 2, .5f);
            Assert.That(survey.ChecksEveryTile(board), Is.True);
            var owner = new GameObject("Placement overlay test");
            var outline = new RoomOutlineView(owner.transform, null);
            var overlay = owner.transform.GetChild(0).gameObject;
            var mesh = overlay.GetComponent<MeshFilter>().sharedMesh;
            try
            {
                outline.ShowSurvey(new List<FloorSurvey> { survey }, board);
                var colors = mesh.colors32;
                bool HasColor(byte red, byte green) => System.Array.Exists(colors, c => c.r == red && c.g == green);
                Assert.That(HasColor(64, 255) && HasColor(255, 72) && HasColor(255, 156) && HasColor(182, 112) && HasColor(96, 168) && HasColor(255, 231) && HasColor(255, 117), Is.True);
                outline.ShowSurvey(new List<FloorSurvey> { survey }, null);
                Assert.That(System.Array.Exists(mesh.colors32, c => c.r == 64 && c.g == 255), Is.False);
                outline.Show(false, default);
                Assert.That(overlay.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(owner);
            }
        }

        [TestCase(EnvironmentRaycastHitStatus.NotReady)]
        [TestCase(EnvironmentRaycastHitStatus.NotSupported)]
        [TestCase(EnvironmentRaycastHitStatus.NoHit)]
        [TestCase(EnvironmentRaycastHitStatus.HitPointOutsideOfCameraFrustum)]
        [TestCase(EnvironmentRaycastHitStatus.HitPointOccluded)]
        [TestCase(EnvironmentRaycastHitStatus.RayOccluded)]
        public void UnmeasuredRaysDoNotIdentifyAnObstacle(EnvironmentRaycastHitStatus status)
        {
            var fit = new BoardPlacement(Vector2.zero, 0, 0, 2, 2, .5f);
            var hit = new EnvironmentRaycastHit { status = status, point = Vector3.zero };
            Assert.That(RoomBoardPlacement.ClassifyFloorRay(hit, fit, 0, Vector3.up), Is.EqualTo(TileClearance.LiveUnknown));
        }

        [TestCase(false, false, false, -.075f)]
        [TestCase(true, false, false, 0f)]
        [TestCase(false, true, false, 0f)]
        [TestCase(false, false, true, 0f)]
        public void GroundsOnlyOnBroadRepeatableLiveFloor(bool singlePane, bool transient, bool obstructed, float expectedOffset)
        {
            var fit = new BoardPlacement(new Vector2(3, 2), 1.25f, 0, 2, 1, .45f);
            var eye = new Vector3(3, 2.8f, 0);
            int passes = 0;
            var exclusions = new List<Vector2>();
            Task<EnvironmentRaycastHit?> Sample(Vector3 target)
            {
                if (singlePane && target.x >= 2.97f) return Task.FromResult<EnvironmentRaycastHit?>(null);
                target.y = fit.FloorHeight - ((transient && passes > 0) ? .01f : .075f);
                // A small object is only clearly raised once the actual floor level is known.
                if (obstructed && target.x > 3.32f && target.z > 2.13f) target.y = fit.FloorHeight + .06f;
                return Task.FromResult<EnvironmentRaycastHit?>(new EnvironmentRaycastHit
                    { status = EnvironmentRaycastHitStatus.Hit, point = target });
            }
            var result = RoomBoardPlacement.ProbeGroundedPanes(fit, () => eye, Sample,
                () => { passes++; return Task.CompletedTask; }, exclusions.Add).GetAwaiter().GetResult();
            Assert.That(result.clear, Is.EqualTo(!obstructed));
            Assert.That(exclusions.Count > 0, Is.EqualTo(obstructed));
            Assert.That(result.grounded.FloorHeight, Is.EqualTo(fit.FloorHeight + expectedOffset).Within(.0001f));
            Assert.That(result.grounded.Center, Is.EqualTo(fit.Center));
        }

        [Test]
        public void LiveFitKeepsClearPanesWithPartialCameraCoverageAndLocalizesConfirmedChanges()
        {
            // Compact side-by-side panes, already verified on a saved mesh.
            var fit = new BoardPlacement(Vector2.zero, 0, 0, BoardLayout.Rectangle(2, 1), .4f);
            var eye = new Vector3(0, 1.6f, -1.5f);
            var blocked = new List<Vector2>();
            var obstacle = new Vector3(.2f, .18f, 0f);
            bool confirmed = false, repeated = false, noFloor = false;
            Task<EnvironmentRaycastHit?> Sample(Vector3 target)
            {
                bool onObject = (target - obstacle).sqrMagnitude < .0001f ||
                    (Mathf.Abs(target.x - obstacle.x) < .01f && Mathf.Abs(target.z) < .01f);
                var hit = new EnvironmentRaycastHit { status = EnvironmentRaycastHitStatus.Hit, point = target };
                if (noFloor || target.z > .1f) hit.status = EnvironmentRaycastHitStatus.HitPointOccluded;
                else if (onObject) hit.point = repeated && !confirmed ? new Vector3(.2f, 0, 0) : obstacle;
                else if (target.z < -.1f) hit.point = new Vector3(-1, .5f, -.6f); // foreground outside the board
                else hit.point.z += .11f; // grazing ray on the same floor
                return Task.FromResult<EnvironmentRaycastHit?>(hit);
            }
            bool? Check() => RoomBoardPlacement.ProbeLivePanes(fit, () => eye, Sample,
                () => { repeated = true; return Task.CompletedTask; }, blocked.Add).GetAwaiter().GetResult();
            Assert.That(Check(), Is.True);
            Assert.That(repeated && blocked.Count == 0, Is.True);
            repeated = false;
            confirmed = true;
            Assert.That(Check(), Is.False);
            Assert.That(blocked, Is.EquivalentTo(new[] {new Vector2(.2f, 0)}));
            noFloor = true;
            Assert.That(Check(), Is.Null);
            var invalid = new EnvironmentRaycastHit {status = EnvironmentRaycastHitStatus.Hit, point = new Vector3(0, float.NaN, 0)};
            Assert.That(RoomBoardPlacement.ClassifyFloorRay(invalid, fit, 0, eye), Is.EqualTo(TileClearance.LiveUnknown));
            invalid.point = new Vector3(-.2f, -.15f, 0);
            Assert.That(RoomBoardPlacement.ClassifyFloorRay(invalid, fit, 0, eye), Is.EqualTo(TileClearance.LiveObstacle));
        }

        [Test]
        public void AppliesTheGlobalAnchorTransformToSupportAndObstacles()
        {
            Surface(-2, -2, 2, 2);
            Post(.1f, .1f, 1);
            var matrix = Matrix4x4.TRS(new Vector3(11, 1.2f, -6), Quaternion.Euler(0, 48, 0), Vector3.one);
            Assert.That(FloorMeshScan.TryCreate(vertices.ToArray(), indices.ToArray(), matrix, out var scan), Is.True);
            var survey = new FloorSurvey(new RoomFloor(Box(8, -9, 14, -3), new List<Vector2[]>(), 1.2f), scan, 2.2f, null, default);
            Assert.That(survey.Check(Box(10.8f, -6.2f, 11.3f, -5.7f)), Is.EqualTo(TileClearance.MeshObstacle));
            Assert.That(survey.Check(Box(11.6f, -5.9f, 11.8f, -5.7f)), Is.EqualTo(TileClearance.Clear));
        }
    }
}
