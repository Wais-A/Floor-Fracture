using System;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
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
            Assert.That(wave.JumpOpensAt, Is.EqualTo(1));
            Assert.That(wave.Deadline-wave.JumpOpensAt, Is.EqualTo(1.85).Within(1e-8));
            board.Tick(board.Center(0),true,.999);
            Assert.That(board.IsIntact(1),Is.True);
            board.Tick(board.Center(0),true,1);
            Assert.That(board.IsIntact(1),Is.False);
            board.Pause();
            Assert.That(board.IsIntact(1),Is.True);
            wave=board.BeginWave(0,14,1,10);
            board.Tick(board.Center(0),true,wave.JumpOpensAt);
            board.Tick(board.Center(1),true,11.1);
            Assert.That(board.Tick(board.Center(1),true,11.48), Is.EqualTo(WaveResult.Pending));
            board.Tick(board.Center(2),true,11.5);
            Assert.That(board.Tick(board.Center(2),true,wave.Deadline), Is.EqualTo(WaveResult.LevelCleared));

            board.RebuildLevel();
            wave=board.BeginWave(0,14,1,20);
            board.Tick(board.Center(0),true,wave.JumpOpensAt);
            board.Tick(board.Center(1),true,21.1);
            Assert.That(board.Tick(board.Center(1),true,21.56),Is.EqualTo(WaveResult.Lost));
        }

        [TestCase(2, 2)]
        [TestCase(3, 3)]
        [TestCase(4, 1)]
        [TestCase(5, 1)]
        [TestCase(7, 1)]
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

        [TestCase(5,false)]
        [TestCase(5,true)]
        [TestCase(12,false)]
        public void EveryOneGapDirectionHasTheSameCrossingTimeAndAcceptsItsLanding(int width,bool irregular)
        {
            var layout=irregular ? new BoardLayout(width,width,Enumerable.Range(0,width*width).Except(new[]{4,20})) :
                BoardLayout.Rectangle(width,width);
            int start=(width/2)*width+width/2;
            var choices=new[]{start-2*width,start-2,start+2,start+2*width};
            foreach (int landing in choices)
            {
                var board=new BoardState(layout,379,.5f);
                var wave=board.BeginWave(start,14,1,0);
                Assert.That(wave.JumpLandings, Is.EquivalentTo(choices));
                board.Tick(board.Center(start),true,wave.JumpOpensAt);
                for(int dy=-1;dy<=1;++dy)
                    for(int dx=-1;dx<=1;++dx)
                        if(dx!=0 || dy!=0) Assert.That(board.IsIntact(start+dy*width+dx),Is.False);
                int gap=(start+landing)/2;
                board.Tick(board.Center(gap),true,1.1);
                Assert.That(board.Tick(board.Center(gap),true,1.54),Is.EqualTo(WaveResult.Pending));
                board.Tick(board.Center(landing),true,1.545);
                Assert.That(board.Tick(board.Center(landing),true,wave.Deadline),Is.EqualTo(WaveResult.Survived));
            }
        }

        [Test]
        public void DiagonalHolesKeepOrdinaryGapGrace()
        {
            var diagonal=new BoardState(5,5,379);
            var jump=diagonal.BeginWave(12,14,1,0);
            diagonal.Tick(diagonal.Center(12),true,jump.JumpOpensAt);
            diagonal.Tick(diagonal.Center(6),true,1.1);
            Assert.That(diagonal.Tick(diagonal.Center(6),true,1.41),Is.EqualTo(WaveResult.Lost));
        }

        [TestCase(2,6,1)]
        [TestCase(2,9,1)]
        [TestCase(3,9,2)]
        [TestCase(4,6,2)]
        [TestCase(5,9,3)]
        public void RowLengthFitsTheFloor(int width,int level,int beats)
        {
            var board=new BoardState(BoardLayout.Rectangle(width,1),379,.5f);
            var wave=board.BeginWave(0,level,1,0);
            Assert.That(wave.RowRoute,Is.EqualTo(Enumerable.Range(1,beats)));
            double cue=0;
            for(int i=0;i<beats;++i)
            {
                var group=wave.BreakGroupFor(i);
                Assert.That(group.RevealStartsAt,Is.EqualTo(cue).Within(1e-8));
                cue+=new[]{1.25,1.10,1.00}[i];
                Assert.That(group.BreaksAt,Is.EqualTo(cue).Within(1e-8));
            }
            Assert.That(wave.Deadline,Is.EqualTo(cue).Within(1e-8));
        }

        [Test]
        public void LateRenderedRowsStillGiveEachFollowingStepItsFullTime()
        {
            var wave=new BoardState(4,4,379).BeginWave(0,9,1,10);
            int next=0;
            double delay=0;
            double Frame(double wall)
            {
                double raw=wall-delay;
                double visible=wave.TimeForVisibleCue(raw,ref next);
                delay+=raw-visible;
                return visible;
            }
            Assert.That(Frame(10),Is.EqualTo(10));
            Assert.That(Frame(12),Is.EqualTo(11.25));
            Assert.That(Frame(13.09),Is.LessThan(wave.BreakGroups[1].BreaksAt));
            Assert.That(Frame(13.4),Is.EqualTo(wave.BreakGroups[1].BreaksAt).Within(1e-8));
            Assert.That(Frame(14.39),Is.LessThan(wave.Deadline));
            Assert.That(Frame(14.4),Is.EqualTo(wave.Deadline).Within(1e-8));
        }

        [Test]
        public void AdvancedJumpPairEndsAfterTwoWaves()
        {
            var board=new BoardState(12,12,0);
            int start=0;
            for(int number=1;number<=3;++number)
            {
                var wave=board.BeginWave(start,10,number,number*10);
                Assert.That(wave.IsJump,Is.EqualTo(number<=2));
                start=wave.GuaranteedTile;
                Assert.That(board.Tick(board.Center(start),true,wave.Deadline),Is.EqualTo(WaveResult.Survived));
            }
        }

        [TestCase(4,0,0,7,2)]
        [TestCase(6,1,7,10,2)]
        [TestCase(12,0,0,6,4)]
        public void OrdinaryPanesSlideOnlyIntoOldHolesAndCanBeCaughtAnywhere(int width,int seed,int start,int level,int number)
        {
            var layout=width==6 ? new BoardLayout(6,5,Enumerable.Range(0,30).Except(new[]{2,3,13,14,25})) :
                BoardLayout.Rectangle(width,width);
            for(int mover=0;mover<2;++mover)
            for(int sample=0;sample<=8;++sample)
            {
                var board=new BoardState(layout,seed,.6f);
                int from=start;
                for(int i=1;i<number;++i)
                {
                    var previous=board.BeginWave(from,level,i,i*10);
                    from=previous.GuaranteedTile;
                    Assert.That(board.Tick(board.Center(from),true,previous.Deadline),Is.EqualTo(WaveResult.Survived));
                }
                var intact=new HashSet<int>(Enumerable.Range(0,board.Count).Where(board.IsIntact));
                var wave=board.BeginWave(from,level,number,number*10);
                Assert.That(board.Sliders.Count,Is.EqualTo(2));
                var reserved=new HashSet<int>();
                foreach (var pane in board.Sliders)
                {
                    Assert.That(intact.Contains(pane.Tile) && pane.Tile!=from && wave.IsSafe(pane.Tile));
                    int stride=(pane.OtherHole-pane.Tile)/pane.TravelCells;
                    for(int step=0;step<=pane.TravelCells;++step)
                    {
                        int cell=pane.Tile+stride*step;
                        Assert.That(layout.Contains(cell) && !board.IsIntact(cell) && reserved.Add(cell));
                        if(step>0) Assert.That(intact.Contains(cell),Is.False);
                    }
                }
                if(width!=6) Assert.That(board.Sliders.Max(s=>s.TravelCells),Is.GreaterThan(1));
                var slider=board.Sliders[mover];
                double t=slider.MotionStartsAt+slider.OneWaySeconds*sample/8;
                if(t>=wave.Deadline)
                    Assert.That(board.Tick(board.Center(wave.GuaranteedTile),true,wave.Deadline),Is.EqualTo(WaveResult.Survived));
                var position=slider.CenterAt(t);
                var support=board.Sample(position,true,t);
                Assert.That(support.IsSlider && support.Index==slider.Tile && slider.Locked);
                Assert.That(slider.CenterAt(t+10),Is.EqualTo(position));
                Assert.That(board.Sliders[1-mover].Locked,Is.False);
                Assert.That(wave.StationarySafe.Any(tile=>board.CanStepFrom(slider.Tile,tile)),Is.True);
                if(board.ActiveWave!=null)
                {
                    board.Pause();
                    Assert.That(board.Sliders,Is.Empty);
                    Assert.That(Enumerable.Range(0,board.Count).Where(board.IsIntact),Is.EquivalentTo(intact));
                }
            }
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
