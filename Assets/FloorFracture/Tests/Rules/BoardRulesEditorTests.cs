using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FloorFracture.Tests
{
    public sealed class BoardRulesEditorTests
    {
        [Test] public void ScalableRectanglesAndSparseLayouts() => BoardRuleChecks.VariableSizeLayouts();
        [Test] public void CoordinatesAndSeams() => BoardRuleChecks.CoordinatesAndSeams();
        [Test] public void PlantedStanceAndGapEdges() => BoardRuleChecks.PlantedStanceAndGapEdges();
        [Test] public void DeadlinesAndPause() => BoardRuleChecks.DeadlinesAndPause();
        [Test] public void ModesAndReset() => BoardRuleChecks.ModesAndReset();
        [Test] public void SlidersAndGrace() => BoardRuleChecks.SlidingAndGrace();
        [Test] public void PausingRollsBackOnlyTheCurrentWavesSlider() => BoardRuleChecks.CancelledSlidingWave();
        [Test] public void ResultsExplainTheActualMemoryAndMovingPaneLoss() => BoardRuleChecks.ReportsResolvedLoss();
        [Test] public void EverySeededSurvivalPath() => Debug.Log(BoardRuleChecks.CheckSequences());
        [Test] public void LargerBoardSurvivalPaths() => Debug.Log(BoardRuleChecks.CheckLargerBoards());

        [TestCase(8, 8, 63, 1312379596, 55)]
        [TestCase(7, 2, 13, -682530180, 6)]
        [TestCase(4, 3, 5, -1147934979, 1)]
        [TestCase(2, 2, 0, -255926220, 2)]
        public void ExistingShapesKeepTheirSeededPatterns(int columns, int rows, int start, int seed, int step)
        {
            var wave = new BoardState(columns, rows, 379).BeginWave(start, 3, 2, 10);
            Assert.That(wave.Seed, Is.EqualTo(seed));
            Assert.That(wave.GuaranteedTile, Is.EqualTo(step));
        }

        [Test]
        public void LongDimensionsAndStartingPanesUseDistinctBitsInThePatternSeed()
        {
            int Pattern(int width, int height, int start) =>
                new BoardState(width, height, 379).BeginWave(start, 3, 2, 10).Seed;

            Assert.That(Pattern(3, 19, 1), Is.Not.EqualTo(Pattern(19, 3, 1)));
            Assert.That(Pattern(2, 32, 0), Is.Not.EqualTo(Pattern(2, 32, 2)));
        }

        [TestCase(8, 9)]
        [TestCase(3, 22)]
        [TestCase(33, 2)]
        [TestCase(65, 3)]
        public void RectangularResetFitsTheEntireBoard(int columns, int rows)
        {
            var board = new BoardState(2, 2, 379);
            board.Reset(columns, rows, 379);
            int last = columns * rows - 1;
            Assert.That(board.Layout.PaneCount, Is.EqualTo(last + 1));
            Assert.That(board.IsIntact(last), Is.True);
            Assert.That(board.BeginWave(last, 1, 1, 0).IsBreaking(last), Is.True);
        }

        [TestCase(2, 1)]
        [TestCase(1, 32)]
        [TestCase(2, int.MaxValue)]
        public void RejectsUnsupportedDimensionsBeforeResettingTheBoard(int columns, int rows)
        {
            var board = new BoardState(32, 2, 379);
            Assert.Throws<ArgumentOutOfRangeException>(() => board.Reset(columns, rows, 0));
            Assert.That(board.Width, Is.EqualTo(32));
            Assert.That(board.Height, Is.EqualTo(2));
            Assert.That(board.IsIntact(63), Is.True);
            Assert.That(board.IsIntact(64), Is.False);
        }

        [MenuItem("Tools/Floor Fracture/Run board rule checks")]
        public static void RunAll() => Debug.Log(BoardRuleChecks.RunAll());
    }
}
