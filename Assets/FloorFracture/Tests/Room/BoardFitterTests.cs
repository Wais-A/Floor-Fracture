using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FloorFracture.Tests
{
    public class BoardFitterTests
    {
        // Room sides include the clearance. Glass spans are the distance players actually use.
        [TestCase(1.80f, 1.80f, 2, 2, 0.50f, 1.00f, 1.00f)]
        [TestCase(1.94f, 1.94f, 2, 2, 0.57f, 1.14f, 1.14f)]
        [TestCase(2.06f, 2.06f, 2, 2, 0.63f, 1.26f, 1.26f)]
        [TestCase(2.30f, 2.80f, 3, 4, 0.50f, 1.50f, 2.00f)]
        [TestCase(2.80f, 2.30f, 4, 3, 0.50f, 2.00f, 1.50f)]
        [TestCase(3.20f, 3.60f, 3, 4, 0.67f, 2.01f, 2.68f)]
        [TestCase(4.00f, 4.20f, 5, 5, 0.64f, 3.20f, 3.20f)]
        [TestCase(5.60f, 5.60f, 7, 7, 0.67f, 4.69f, 4.69f)]
        [TestCase(7.00f, 7.00f, 8, 8, 0.67f, 5.36f, 5.36f)]
        [TestCase(6.40f, 2.00f, 8, 2, 0.60f, 4.80f, 1.20f)]
        [TestCase(1.80f, 6.80f, 2, 8, 0.50f, 1.00f, 4.00f)]
        public void BalancesPaneWidthAndPhysicalCoverage(float roomX, float roomZ, int columns, int rows,
            float pitch, float glassX, float glassZ)
        {
            var floor = Floor(Rectangle(-roomX / 2, -roomZ / 2, roomX / 2, roomZ / 2));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(columns));
            Assert.That(fit.Rows, Is.EqualTo(rows));
            Assert.That(fit.Pitch, Is.EqualTo(pitch).Within(0.0001f));
            Assert.That(fit.Columns * fit.Pitch, Is.EqualTo(glassX).Within(0.0001f));
            Assert.That(fit.Rows * fit.Pitch, Is.EqualTo(glassZ).Within(0.0001f));
            var padded = fit.ClearanceOutline();
            Assert.That((padded[1] - padded[0]).magnitude, Is.EqualTo(glassX + 0.8f).Within(0.0001f));
            Assert.That((padded[2] - padded[1]).magnitude, Is.EqualTo(glassZ + 0.8f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void RectanglesCertifyHeadFirstThenClosestSuggestedCenterAndKeepFallback()
        {
            var floor = Floor(Rectangle(-12f, -3.08f, 12f, 3.08f), new[] { Vector2.zero });
            var suggestions = new[] { P(6f, 0), P(1f, 0), P(4f, 0), P(float.NaN, 0) };
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var chosen,
                suggestedCenters: suggestions), Is.True);
            Assert.That(chosen.Columns * chosen.Rows, Is.EqualTo(64));
            Assert.That(chosen.Center, Is.EqualTo(P(4f, 0)));
            Assert.That(BoardFitter.Fits(floor, chosen), Is.True);
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-5f, 0), out var nearby,
                suggestedCenters: suggestions), Is.True);
            Assert.That(nearby.Center, Is.EqualTo(P(-5f, 0)));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fallback,
                suggestedCenters: new[] { P(0.1f, 0) }), Is.True);
            Assert.That(fallback.Columns * fallback.Rows, Is.EqualTo(64));
            Assert.That(BoardFitter.Fits(floor, fallback), Is.True);
        }

        [Test]
        public void NeverSacrificesLegacyGlassAreaOnRectangularScans()
        {
            for (int x = 0; x <= 15; ++x)
            for (int z = 0; z <= 12; ++z)
            {
                float roomX = 1.8f + x * 0.24f, roomZ = 1.8f + z * 0.31f;
                var floor = Floor(Rectangle(0, 0, roomX, roomZ));
                Assert.That(BoardFitter.TryFind(new[] { floor }, P(roomX / 2, roomZ / 2), out var fit), Is.True);
                // For an axis-aligned room the old aligned board can use these exact counts.
                int oldColumns = Mathf.Min(4, Mathf.FloorToInt((roomX - 0.8f) / 0.5f + 0.00001f));
                int oldRows = Mathf.Min(4, Mathf.FloorToInt((roomZ - 0.8f) / 0.5f + 0.00001f));
                Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.GreaterThanOrEqualTo(oldColumns * oldRows * 0.25f));
            }
        }

        [Test]
        public void PreservesAnExactRotatedLegacyPatchBetweenFurniture()
        {
            var patch = new BoardPlacement(P(3.3f, -2.5f), 0f, 27.8f, 3, 4);
            // Furniture blocks the left end of a longer rotated floor, leaving one exact patch.
            Vector2[] InRoom(float left, float right) => new[] {
                patch.ToWorldXZ(left, -1.4f), patch.ToWorldXZ(right, -1.4f),
                patch.ToWorldXZ(right, 1.4f), patch.ToWorldXZ(left, 1.4f) };
            var floor = Floor(InRoom(-4f, 1.15f), InRoom(-4f, -1.15f));
            Assert.That(BoardFitter.Fits(floor, patch), Is.True);
            Assert.That(BoardFitter.TryFind(new[] { floor }, patch.ToWorldXZ(-3f, 0), out var fit), Is.True);
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.GreaterThanOrEqualTo(3f));
        }

        [Test]
        public void ConcavityBetweenFourInteriorCornersIsRejected()
        {
            var notch = Floor(new[] { P(-2,-2), P(2,-2), P(2,2), P(0.1f,2), P(0.1f,0.4f),
                P(-0.1f,0.4f), P(-0.1f,2), P(-2,2) });
            Assert.That(BoardFitter.Fits(notch, MinimumBoard()), Is.False);
        }

        [Test]
        public void FourCornersTouchingTheOutsideOfAnExactNotchIsNotFloor()
        {
            var notch = Floor(new[] { P(-2,-2), P(2,-2), P(2,2), P(0.9f,2), P(0.9f,-0.9f),
                P(-0.9f,-0.9f), P(-0.9f,2), P(-2,2) });
            Assert.That(BoardFitter.Fits(notch, MinimumBoard()), Is.False);
        }

        [Test]
        public void InteriorFurnitureCrossingWallsAndEnclosingVolumesAreRejected()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.1f,0.1f,0.2f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(-2,0.2f), P(2,0.2f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(0.4f,0.5f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(-2,-2,2,2)), MinimumBoard()), Is.False);
        }

        [Test]
        public void RejectsFurnitureOnlyInMarginAndAcceptsExactClearance()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.895f,0.1f,1f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.9f,0.1f,1f,0.2f)), MinimumBoard()), Is.True);
        }

        [TestCase(23.68f)]
        [TestCase(0.005f)]
        public void RotatedExactWidthFloorPreservesPoseAndMetricPitch(float angle)
        {
            var actual = new BoardPlacement(P(3.1f,-2.7f), 0.12f, angle, 4, 2);
            var floor = new RoomFloor(actual.ClearanceOutline(), new List<Vector2[]>(), 0.12f);
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(0,0), out var found), Is.True);
            Assert.That(found.Rows * found.Columns, Is.EqualTo(8));
            Assert.That(found.FloorHeight, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That((found.Center - actual.Center).magnitude, Is.LessThan(0.0001f));
            Assert.That((found.ToWorldXZ(0.5f,0) - found.Center).magnitude, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, found), Is.True);
        }

        [Test]
        public void KeepsFullSizedTilesWhenTheMinimumCannotFit()
        {
            var small = Floor(Rectangle(0,0,8,1.799f));
            Assert.That(BoardFitter.TryFind(new[] { small }, Vector2.zero, out _), Is.False);
        }

        [Test]
        public void FindsOffsetSlotBoundedOnlyByFurnitureAndAThinWall()
        {
            var floor = Floor(Rectangle(-4,-0.9f,4,0.9f), Rectangle(-4,-1,0.123f,1),
                new[] { P(1.9232f,-1), P(1.9232f,1) }, Rectangle(1.924f,-1,4,1));
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-3,0), out var found), Is.True);
            Assert.That(found.Columns * found.Rows, Is.EqualTo(4));
            Assert.That(found.Center.x, Is.InRange(1.0229f,1.0233f));
        }

        [Test]
        public void RejectsCrossedOrMissingFloorGeometry()
        {
            var invalid = Floor(new[] { P(-3,-2), P(2,3), P(-2,2), P(3,-2) });
            Assert.That(BoardFitter.TryFind(new[] { invalid }, P(0,0), out _), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { Floor(new Vector2[0]) }, P(0,0), out _), Is.False);
        }

        [Test]
        public void ExcludingADeclinedPatchProducesADisjointPreview()
        {
            var boundary = Rectangle(-7,-2,7,2);
            Assert.That(BoardFitter.TryFind(new[] { Floor(boundary) }, Vector2.zero, out var first), Is.True);
            var withRejection = Floor(boundary, first.ClearanceOutline());
            Assert.That(BoardFitter.TryFind(new[] { withRejection }, Vector2.zero, out var second), Is.True);
            Assert.That(BoardFitter.Fits(withRejection, first), Is.False);
            Assert.That(BoardFitter.Fits(withRejection, second), Is.True);
        }

        private static BoardPlacement MinimumBoard() => new BoardPlacement(Vector2.zero, 0, 0, 2, 2);
        private static Vector2 P(float x, float z) => new Vector2(x,z);
        private static RoomFloor Floor(Vector2[] boundary, params Vector2[][] obstacles) =>
            new RoomFloor(boundary, new List<Vector2[]>(obstacles));
        private static Vector2[] Rectangle(float left, float bottom, float right, float top) =>
            new[] { P(left,bottom), P(right,bottom), P(right,top), P(left,top) };
    }
}
