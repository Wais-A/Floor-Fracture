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
        [Test] public void SeededLevelCurveAndSingleClueWindows() => BoardRuleChecks.ProgressiveWaves();
        [TestCase(.40f)]
        [TestCase(.67f)]
        public void OneHoleJumpOpensAtTakeoffAndResolvesAtLanding(float pitch)
        {
            var board = new BoardState(BoardLayout.Rectangle(3, 1), 379, pitch);
            var wave = board.BeginWave(0, 14, 1, 0);
            Assert.That(wave.JumpGap, Is.EqualTo(1));
            Assert.That(wave.GuaranteedTile, Is.EqualTo(2));
            Assert.That(wave.Duration, Is.EqualTo(1.85));
            board.Tick(board.Center(0), true, 0);
            Assert.That(board.IsIntact(1), Is.False);
            board.Pause();
            Assert.That(board.IsIntact(1), Is.True);
            wave = board.BeginWave(0, 14, 1, 10);
            board.Tick(board.Center(0), true, 10);
            board.Tick(board.Center(1), true, 10.6);
            Assert.That(board.Tick(board.Center(1), true, 10.95), Is.EqualTo(WaveResult.Pending));
            board.Tick(board.Center(2), true, 11);
            Assert.That(board.Tick(board.Center(2), true, wave.Deadline), Is.EqualTo(WaveResult.LevelCleared));

            board.RebuildLevel();
            board.BeginWave(0, 14, 1, 20);
            board.Tick(board.Center(0), true, 20);
            board.Tick(board.Center(1), true, 20.4);
            Assert.That(board.Tick(board.Center(1), true, 20.86), Is.EqualTo(WaveResult.Lost));
        }

        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 1)]
        [TestCase(5, 1)]
        [TestCase(7, 1)]
        [TestCase(12, 1)]
        public void FirstEligibleLaneOffersAJumpAndLandingEarnsAnOrdinarySwitch(int level, int number)
        {
            var board = new BoardState(9, 9, 379);
            var wave = board.BeginWave(0, level, number, 0);
            Assert.That(wave.IsJump, Is.True);
            board.Pause();
            wave = board.BeginWave(0, level, number, 10);
            Assert.That(wave.IsJump, Is.True, "Pausing must not consume the jump.");
            int landed = wave.GuaranteedTile;
            Assert.That(board.Tick(board.Center(landed), true, wave.Deadline), Is.EqualTo(WaveResult.Survived));
            var step = board.BeginWave(landed, level, number + 1, 20);
            Assert.That(step.IsJump, Is.False);
            Assert.That(board.Neighbors(landed, step.GuaranteedTile), Is.True);
        }

        [Test]
        public void JumpRouteCannotCrossAnUnfittedCellOrWrapARow()
        {
            // The upper row detours around the missing physical floor cell 1.
            var cutout = new BoardLayout(3, 2, new[]{0, 2, 3, 4, 5});
            var board = new BoardState(cutout, 379, .6f);
            var wave = board.BeginWave(0, 14, 1, 0);
            Assert.That(wave.IsJump, Is.False);
            Assert.That(board.CanStepFrom(0, wave.GuaranteedTile), Is.True);
            board = new BoardState(BoardLayout.Rectangle(2, 2), 379, .65f);
            Assert.That(board.BeginWave(1, 14, 1, 0).IsJump, Is.False);
        }

        [Test]
        public void JumpAllowanceOnlyAppliesToTheMarkedGap()
        {
            var board = new BoardState(5, 5, 379);
            var wave = board.BeginWave(12, 14, 1, 0);
            board.Tick(board.Center(12), true, 0);
            foreach (int adjacent in new[] {6,7,8,11,13,16,17,18})
                Assert.That(board.IsIntact(adjacent), Is.False, "A corner or side left a walkable shortcut.");
            Assert.That(board.IsIntact(wave.GuaranteedTile), Is.True);
            int wrong = wave.JumpGap == 11 ? 13 : 11;
            board.Tick(board.Center(wrong), true, .5);
            Assert.That(board.Tick(board.Center(wrong), true, .81), Is.EqualTo(WaveResult.Lost));
        }
        [Test] public void EarlyBreakDeadlinesAndTrackingRollback() => BoardRuleChecks.TimedBreaksAndRollback();
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
