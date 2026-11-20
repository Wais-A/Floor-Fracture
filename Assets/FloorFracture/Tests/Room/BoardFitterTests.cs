using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FloorFracture.Tests
{
    public class BoardFitterTests
    {
        [Test]
        public void FillsTheClearSquareThatPreviouslyProducedTwoByTwoAt57Centimeters()
        {
            const float side = 1.946f;
            var floor = Floor(Rectangle(0f, 0f, side, side));
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(0.1f, 0.1f), out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(3));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(side / 3f).Within(0.0001f));
            Assert.That(fit.Center.x, Is.EqualTo(side / 2f).Within(0.0001f));
            Assert.That(fit.Center.y, Is.EqualTo(side / 2f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void FurnitureLimitsOnlyTheOccupiedSideOfTheSameSquare()
        {
            const float side = 1.946f;
            float openWidth = side * 2f / 3f;
            var floor = Floor(Rectangle(0f, 0f, side, side), Rectangle(openWidth, 0f, side, side));
            var fullSquare = new BoardPlacement(P(side / 2f, side / 2f), 0f, 0f, 3, 3, side / 3f);
            Assert.That(BoardFitter.Fits(floor, fullSquare), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { floor }, fullSquare.Center, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(2));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(side / 3f).Within(0.0001f));
            Assert.That(fit.Center.x + fit.Columns * fit.Pitch / 2f, Is.EqualTo(openWidth).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [TestCase(1.00f, 1.00f, 2, 2, 0.50f, 1.00f, 1.00f)]
        [TestCase(1.80f, 1.80f, 3, 3, 0.60f, 1.80f, 1.80f)]
        [TestCase(1.94f, 1.94f, 3, 3, 1.94f / 3f, 1.94f, 1.94f)]
        [TestCase(2.06f, 2.06f, 4, 4, 0.515f, 2.06f, 2.06f)]
        [TestCase(2.30f, 2.80f, 4, 5, 0.56f, 2.24f, 2.80f)]
        [TestCase(2.80f, 2.30f, 5, 4, 0.56f, 2.80f, 2.24f)]
        [TestCase(3.20f, 3.60f, 6, 7, 3.60f / 7f, 6f * 3.60f / 7f, 3.60f)]
        [TestCase(4.00f, 4.20f, 6, 6, 4f / 6f, 4.00f, 4.00f)]
        [TestCase(5.60f, 5.60f, 8, 8, 0.67f, 5.36f, 5.36f)]
        [TestCase(7.00f, 7.00f, 8, 8, 0.67f, 5.36f, 5.36f)]
        [TestCase(6.40f, 2.00f, 10, 3, 0.64f, 6.40f, 1.92f)]
        [TestCase(1.80f, 6.80f, 3, 11, 0.60f, 1.80f, 6.60f)]
        [TestCase(8.04f, 2.68f, 12, 4, 0.67f, 8.04f, 2.68f)]
        [TestCase(21.44f, 1.34f, 32, 2, 0.67f, 21.44f, 1.34f)]
        [TestCase(1.34f, 21.44f, 2, 32, 0.67f, 1.34f, 21.44f)]
        public void MaximizesTheWholeGlassFootprint(float roomX, float roomZ, int columns, int rows,
            float pitch, float glassX, float glassZ)
        {
            var floor = Floor(Rectangle(-roomX / 2, -roomZ / 2, roomX / 2, roomZ / 2));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(columns));
            Assert.That(fit.Rows, Is.EqualTo(rows));
            Assert.That(fit.Pitch, Is.EqualTo(pitch).Within(0.0001f));
            Assert.That(fit.Columns * fit.Pitch, Is.EqualTo(glassX).Within(0.0001f));
            Assert.That(fit.Rows * fit.Pitch, Is.EqualTo(glassZ).Within(0.0001f));
            var outline = fit.FootprintOutline();
            Assert.That((outline[1] - outline[0]).magnitude, Is.EqualTo(glassX).Within(0.0001f));
            Assert.That((outline[2] - outline[1]).magnitude, Is.EqualTo(glassZ).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void AddsARowWhenClearFloorExtendsAlongTheLongSide()
        {
            // The two earlier fitters chose seven by two and eight by three on this floor.
            var floor = Floor(Rectangle(-2.75f, -1.07f, 2.75f, 1.47f));
            var strip = new BoardPlacement(Vector2.zero, 0f, 0f, 7, 2, 0.67f);
            var wider = new BoardPlacement(P(0f, 0.2f), 0f, 0f, 8, 3, 0.58f);
            Assert.That(BoardFitter.Fits(floor, strip), Is.True);
            Assert.That(BoardFitter.Fits(floor, wider), Is.True);

            Assert.That(BoardFitter.TryFind(new[] { floor }, strip.Center, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(11));
            Assert.That(fit.Rows, Is.EqualTo(5));
            Assert.That(fit.Pitch, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(fit.Center.y, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeepsTwoRowsWhenTheLongSideMeetsAWallOrFurniture(bool furniture)
        {
            var floor = furniture
                ? Floor(Rectangle(-2.68f, -0.67f, 2.68f, 1.07f),
                    Rectangle(-2.68f, 0.67f, 2.68f, 1.07f))
                : Floor(Rectangle(-2.68f, -0.67f, 2.68f, 0.67f));
            var wider = new BoardPlacement(P(0f, 0.2f), 0f, 0f, 8, 3, 0.58f);
            Assert.That(BoardFitter.Fits(floor, wider), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(8));
            Assert.That(fit.Rows, Is.EqualTo(2));
            Assert.That(fit.Pitch, Is.EqualTo(0.67f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            Assert.That(fit.ToWorldXZ(0f, fit.Rows * fit.Pitch / 2f).y, Is.EqualTo(0.67f).Within(0.0001f));
        }

        [Test]
        public void EqualGlassAreaFavorsTheWiderShapeThenLargerPanes()
        {
            var corridor = Floor(Rectangle(-2f, -0.5f, 2f, 0.5f));
            var wider = Floor(Rectangle(4f, -1f, 6f, 1f));
            // Both floors hold 4 m². The square admits 3 × 3 or 4 × 4 at that area.
            Assert.That(BoardFitter.TryFind(new[] { corridor, wider }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(3));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(2f / 3f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(wider, fit), Is.True);
        }

        [Test]
        public void ABoardWithMoreGlassBeatsASmallerSquareNearThePlayer()
        {
            var square = Floor(Rectangle(-1.2f, -1.2f, 1.2f, 1.2f));
            var corridor = Floor(Rectangle(5f, 0f, 9.69f, 1.34f));
            Assert.That(BoardFitter.TryFind(new[] { square, corridor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(4.69f * 1.34f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(corridor, fit), Is.True);
        }

        [Test]
        public void FindsTheLargerSixDegreeFitBetweenThreeSupports()
        {
            var floor = Floor(Rectangle(-2f, -1.6f, 2f, 1.6f), new[] { P(1.6f, -1.3f) },
                new[] { P(-1.2f, 1f) }, new[] { P(-1.7f, -0.2f) });
            var witness = new BoardPlacement(P(0.094f, -0.145f), 0f, 6f, 7, 5, 0.5085f);
            Assert.That(BoardFitter.Fits(floor, witness), Is.True);
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch,
                Is.GreaterThanOrEqualTo(witness.Columns * witness.Rows * witness.Pitch * witness.Pitch));
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
            foreach (var pair in new[] { new[] { P(4f, 0), P(-4f, 0) }, new[] { P(-4f, 0), P(4f, 0) } })
            {
                Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var tied,
                    suggestedCenters: pair), Is.True);
                Assert.That(tied.Center, Is.EqualTo(P(-4f, 0)));
            }
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-5f, 0), out var nearby,
                suggestedCenters: suggestions), Is.True);
            Assert.That(nearby.Center, Is.EqualTo(P(-5f, 0)));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fallback,
                suggestedCenters: new[] { P(0.1f, 0) }), Is.True);
            Assert.That(fallback.Columns * fallback.Rows, Is.EqualTo(64));
            Assert.That(BoardFitter.Fits(floor, fallback), Is.True);
        }

        [Test]
        public void MatchesOrExceedsEveryAlignedShapeOnRectangularScans()
        {
            for (int x = 0; x <= 15; ++x)
            for (int z = 0; z <= 12; ++z)
            {
                float roomX = 1.8f + x * 0.24f, roomZ = 1.8f + z * 0.31f;
                var floor = Floor(Rectangle(0, 0, roomX, roomZ));
                Assert.That(BoardFitter.TryFind(new[] { floor }, P(roomX / 2, roomZ / 2), out var fit), Is.True);
                float expected = 0f;
                for (int c = 2; c <= 32; ++c)
                for (int r = 2; r <= 64 / c; ++r)
                {
                    float pitch = Mathf.Min(0.67f, Mathf.Min(roomX / c, roomZ / r));
                    if (pitch >= 0.5f) expected = Mathf.Max(expected, c * r * pitch * pitch);
                }
                float glassArea = fit.Columns * fit.Rows * fit.Pitch * fit.Pitch;
                Assert.That(glassArea, Is.GreaterThanOrEqualTo(expected - 0.0001f));
                Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            }
        }

        [Test]
        public void PreservesAnExactRotatedLegacyPatchBetweenFurniture()
        {
            var patch = new BoardPlacement(P(3.3f, -2.5f), 0f, 27.8f, 3, 4);
            // Furniture blocks the left end of a longer rotated floor, leaving one exact patch.
            Vector2[] InRoom(float left, float right) => new[] {
                patch.ToWorldXZ(left, -1f), patch.ToWorldXZ(right, -1f),
                patch.ToWorldXZ(right, 1f), patch.ToWorldXZ(left, 1f) };
            var floor = Floor(InRoom(-4f, 0.75f), InRoom(-4f, -0.75f));
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
            var notch = Floor(new[] { P(-2,-2), P(2,-2), P(2,2), P(0.5f,2), P(0.5f,-0.5f),
                P(-0.5f,-0.5f), P(-0.5f,2), P(-2,2) });
            Assert.That(BoardFitter.Fits(notch, MinimumBoard()), Is.False);
        }

        [Test]
        public void InteriorFurnitureCrossingWallsAndEnclosingVolumesAreRejected()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.1f,0.1f,0.2f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(-2,0.2f), P(2,0.2f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(0.4f,0.4f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(-2,-2,2,2)), MinimumBoard()), Is.False);
        }

        [Test]
        public void RejectsFurnitureInsideGlassAndAcceptsExactEdgeContact()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.495f,0.1f,1f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.5f,0.1f,1f,0.2f)), MinimumBoard()), Is.True);
        }

        [TestCase(23.68f)]
        [TestCase(0.005f)]
        public void RotatedExactWidthFloorPreservesPoseAndMetricPitch(float angle)
        {
            var actual = new BoardPlacement(P(3.1f,-2.7f), 0.12f, angle, 4, 2);
            var floor = new RoomFloor(actual.FootprintOutline(), new List<Vector2[]>(), 0.12f);
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
            var small = Floor(Rectangle(0,0,8,0.999f));
            Assert.That(BoardFitter.TryFind(new[] { small }, Vector2.zero, out _), Is.False);
        }

        [Test]
        public void FindsOffsetSlotBoundedOnlyByFurnitureAndAThinWall()
        {
            var floor = Floor(Rectangle(-4,-0.5f,4,0.5f), Rectangle(-4,-1,0.123f,1),
                new[] { P(1.1232f,-1), P(1.1232f,1) }, Rectangle(1.124f,-1,4,1));
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-3,0), out var found), Is.True);
            Assert.That(found.Columns * found.Rows, Is.EqualTo(4));
            Assert.That(found.Center.x, Is.InRange(0.6229f,0.6233f));
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
            var withRejection = Floor(boundary, first.FootprintOutline());
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
