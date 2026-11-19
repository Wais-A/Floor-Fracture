using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FloorFracture.Tests
{
    public class BoardFitterTests
    {
        [TestCase(2, 2)] [TestCase(2, 3)] [TestCase(2, 4)]
        [TestCase(3, 2)] [TestCase(3, 3)] [TestCase(3, 4)]
        [TestCase(4, 2)] [TestCase(4, 3)] [TestCase(4, 4)]
        public void ChoosesLargestBoardIncludingBothRectangularOrientations(int columns, int rows)
        {
            float x = columns * 0.25f + 0.4f, z = rows * 0.25f + 0.4f;
            var floor = Floor(Rectangle(-x, -z, x, z));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(columns));
            Assert.That(fit.Rows, Is.EqualTo(rows));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
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

        [Test]
        public void RotatedExactWidthFloorPreservesPoseAndMetricPitch()
        {
            const float angle = 23.68f;
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
            var boundary = Rectangle(-4,-2,4,2);
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
