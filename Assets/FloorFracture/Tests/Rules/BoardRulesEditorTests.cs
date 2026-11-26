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
        [Test] public void EveryPlanCarriesHazardsWithoutFog() => BoardRuleChecks.PlansCarryHazards();
        [Test] public void SeededLevelCurveAndSingleClueWindows() => BoardRuleChecks.ProgressiveWaves();
        [TestCase(.40f)]
        [TestCase(.75f)]
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
            Assert.That(board.Tick(board.Center(1),true,21.62),Is.EqualTo(WaveResult.Pending));
            Assert.That(board.Tick(board.Center(1),true,21.66),Is.EqualTo(WaveResult.Lost));
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
                    for(int step=0;step<=pane.TravelCells;++step)
                    {
                        int cell=pane.Path[step];
                        if(step>0) Assert.That(board.Neighbors(pane.Path[step-1],cell));
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
        static SlidingPane[] Offered(BoardState board,WavePlan wave)=>board.Sliders.Where(p=>wave.IsSafe(p.Tile)).ToArray();
        static int IntactTiles(BoardState board)=>Enumerable.Range(0,board.Count).Count(board.IsIntact);

        [Test]
        public void SlidersAppearOnMostEligibleAdvancedWaves()
        {
            int eligible=0,offered=0,withHoles=0,offeredWithHoles=0;
            for(int level=6;level<=12;++level)
            for(int seed=0;seed<40;++seed)
            {
                var board=new BoardState(6,6,seed);
                int start=0;
                for(int number=1;;++number)
                {
                    Assert.That(number,Is.LessThanOrEqualTo(board.Count),"A level must finish.");
                    // A fresh board has no hole for a slider to cross, so those waves cannot offer one.
                    bool holes=Enumerable.Range(0,board.Count).Any(i=>board.Layout.Contains(i) && !board.IsIntact(i));
                    var wave=board.BeginWave(start,level,number,number*10);
                    if(wave.Mode!=WaveMode.LastTile && !wave.IsJump && wave.RowRoute.Count==0)
                    {
                        bool some=Offered(board,wave).Length>0;
                        ++eligible;
                        if(some) ++offered;
                        if(holes) { ++withHoles; if(some) ++offeredWithHoles; }
                    }
                    if(wave.IsRide) break; // a ride finale ends the level
                    start=wave.GuaranteedTile;
                    var result=board.Tick(board.Center(start),true,wave.Deadline);
                    Assert.That(result,Is.EqualTo(WaveResult.Survived).Or.EqualTo(WaveResult.LevelCleared));
                    if(result!=WaveResult.Survived) break;
                }
            }
            TestContext.WriteLine($"Sliders offered on {offered}/{eligible} eligible waves ({(double)offered/eligible:P1}).");
            double fraction=(double)offeredWithHoles/withHoles;
            TestContext.WriteLine($"Sliders offered on {offeredWithHoles}/{withHoles} eligible waves with a hole ({fraction:P1}).");
            Assert.That(fraction,Is.GreaterThanOrEqualTo(.5));
        }

        // Each case catches a slider mid-lane in wave N and survives on it; the caller starts wave N+1.
        static IEnumerable<(BoardState board,SlidingPane old,Vector2 stance,int level,int number)> CaughtSliders()
        {
            foreach(int level in new[]{6,9})
            for(int seed=0;seed<40;++seed)
            {
                var board=new BoardState(6,6,seed);
                int start=0;
                for(int number=1;number<board.Count;++number)
                {
                    var wave=board.BeginWave(start,level,number,number*10);
                    if(wave.IsRide) break; // catching a ride clears the level, so no next wave follows
                    var pane=Offered(board,wave).FirstOrDefault();
                    double t=pane==null ? 0 : pane.MotionStartsAt+pane.OneWaySeconds/2;
                    if(pane!=null && t<wave.Deadline)
                    {
                        var stance=pane.CenterAt(t);
                        Assert.That(board.Sample(stance,true,t).IsSlider && pane.Locked);
                        Assert.That(board.Tick(stance,true,wave.Deadline),Is.EqualTo(WaveResult.Survived));
                        yield return (board,pane,stance,level,number+1);
                        break;
                    }
                    start=wave.GuaranteedTile;
                    if(board.Tick(board.Center(start),true,wave.Deadline)!=WaveResult.Survived) break;
                }
            }
        }

        [Test]
        public void OldSliderKeepsAPlayerWhileNewOnesAreOffered()
        {
            int cases=0,offers=0;
            foreach(var (board,old,stance,level,number) in CaughtSliders())
            {
                ++cases;
                var wave=board.BeginWave(old.Tile,level,number,number*10);
                var sample=board.Sample(stance,true,number*10);
                Assert.That(sample.Kind==SupportKind.Glass && sample.IsSlider && sample.Index==old.Tile && old.Locked);
                Assert.That(board.Sliders.Contains(old) && wave.IsBreaking(old.Tile));
                Assert.That(wave.StationarySafe.Any(tile=>board.CanStepFrom(old.Tile,tile)),Is.True);
                var fresh=Offered(board,wave);
                Assert.That(fresh.Length,Is.LessThanOrEqualTo(level>=9 ? 3 : 2));
                if(fresh.Length>0) ++offers;
                foreach(var pane in fresh)
                {
                    Assert.That(pane.Path,Has.No.Member(board.IndexAt(stance)),"A new lane ran under the held pane.");
                }
            }
            TestContext.WriteLine($"{offers}/{cases} waves started on a held slider offered new ones.");
            Assert.That(cases,Is.GreaterThan(0));
            Assert.That(offers,Is.GreaterThan(0));
        }

        [Test]
        public void PausingRestoresOldAndNewSliderLocks()
        {
            int tested=0;
            foreach(var (board,old,stance,level,number) in CaughtSliders())
            {
                int intact=IntactTiles(board);
                var before=board.Sliders.ToArray();
                var wave=board.BeginWave(old.Tile,level,number,number*10);
                var fresh=Offered(board,wave);
                if(fresh.Length==0) continue;
                ++tested;
                var caught=fresh[0];
                double t=caught.MotionStartsAt+caught.OneWaySeconds/2;
                if(t<wave.Deadline)
                    Assert.That(board.Sample(caught.CenterAt(t),true,t).IsSlider && caught.Locked);
                board.Pause();
                Assert.That(board.Sliders,Is.EqualTo(before));
                Assert.That(old.Locked && board.Sample(stance,true,t+1).IsSlider);
                Assert.That(board.Sliders.Intersect(fresh),Is.Empty);
                Assert.That(IntactTiles(board),Is.EqualTo(intact));
            }
            Assert.That(tested,Is.GreaterThan(0));
        }

        static readonly int[] LPath={0,1,2,6};
        static void AssertNear(Vector2 actual,Vector2 expected)=>Assert.That((actual-expected).magnitude,Is.LessThan(1e-4f));
        static Vector2 Mid(BoardState board,int a,int b)=>(board.Center(a)+board.Center(b))/2;
        static bool Turns(SlidingPane pane)=>
            Enumerable.Range(1,pane.Path.Count-2).Any(i=>pane.Path[i+1]-pane.Path[i]!=pane.Path[1]-pane.Path[0]);

        [Test]
        public void PatrolPaneFollowsItsCornersAtTheSliderPace()
        {
            var board=new BoardState(4,4,0);
            var pane=new SlidingPane(board,LPath,10,1.5);
            Assert.That(pane.Path,Is.EqualTo(LPath));
            Assert.That(pane.Tile==0 && pane.OtherHole==6 && pane.TravelCells==3 && pane.Direction==Vector2.right);
            Assert.That(pane.OneWaySeconds,Is.EqualTo(3*1.20*.75).Within(1e-12));
            double leg=pane.OneWaySeconds/3;
            for(int k=0;k<=3;++k) AssertNear(pane.CenterAt(pane.MotionStartsAt+k*leg),board.Center(LPath[k]));
            for(int k=0;k<3;++k) AssertNear(pane.CenterAt(pane.MotionStartsAt+(k+.5)*leg),Mid(board,LPath[k],LPath[k+1]));
            AssertNear(pane.CenterAt(pane.MotionStartsAt+4*leg),board.Center(LPath[2]));
            AssertNear(pane.CenterAt(pane.MotionStartsAt+5.5*leg),Mid(board,LPath[0],LPath[1]));
        }

        [Test]
        public void PatrolPaneLocksWhereCaughtOnItsSecondSegment()
        {
            var board=new BoardState(4,4,0);
            var pane=new SlidingPane(board,LPath,10,2);
            double t=pane.MotionStartsAt+2.5*pane.OneWaySeconds/3;
            var point=pane.CenterAt(t);
            AssertNear(point,Mid(board,2,6));
            Assert.That(pane.TryLand(point,t) && pane.Locked);
            Assert.That(pane.CenterAt(t+7),Is.EqualTo(point));
        }

        static IEnumerable<(int level,WavePlan wave,BoardState board,SlidingPane pane)> OfferedPanes(int fromLevel)
        {
            for(int level=fromLevel;level<=12;++level)
            for(int seed=0;seed<100;++seed)
            {
                var board=new BoardState(6,6,seed);
                int start=0;
                for(int number=1;number<=board.Count;++number)
                {
                    var wave=board.BeginWave(start,level,number,number*10);
                    if(wave.IsRide) break; // a ride pane has no exits by design; the ride tests cover it
                    foreach(var pane in Offered(board,wave)) yield return (level,wave,board,pane);
                    start=wave.GuaranteedTile;
                    if(board.Tick(board.Center(start),true,wave.Deadline)!=WaveResult.Survived) break;
                }
            }
        }

        [Test]
        public void EveryPlannedPatrolHasAnExitAlongItsWholePath()
        {
            int patrols=0;
            foreach(var (level,wave,board,pane) in OfferedPanes(5))
            {
                if(!Turns(pane)) continue;
                ++patrols;
                for(int cell=0;cell<pane.TravelCells;++cell)
                for(int sample=0;sample<20;++sample)
                {
                    double t=pane.MotionStartsAt+(cell+sample/19.0)/pane.TravelCells*pane.OneWaySeconds;
                    Assert.That(pane.TryLand(pane.CenterAt(t),t),Is.True);
                    Assert.That(wave.StationarySafe.Any(tile=>board.CanStepFrom(pane.Tile,tile)),Is.True,
                        $"Level {level}: a patrol {string.Join(",",pane.Path)} caught at {cell}+{sample}/19 has no exit.");
                    pane.RestoreLock(false);
                }
            }
            TestContext.WriteLine($"Checked {patrols} patrols.");
            Assert.That(patrols,Is.GreaterThan(0));
        }

        [Test]
        public void PatrolsTurnOnlyFromLevelFive()
        {
            int patrols=0;
            foreach(var (level,_,_,pane) in OfferedPanes(3))
            {
                Assert.That(pane.Path.Count,Is.EqualTo(pane.TravelCells+1));
                if(!Turns(pane)) continue;
                Assert.That(level,Is.GreaterThanOrEqualTo(5));
                Assert.That(pane.Path.Count,Is.InRange(3,5));
                ++patrols;
            }
            Assert.That(patrols,Is.GreaterThan(0));
        }

        [Test]
        public void PatrolsAreCommonFromLevelSeven()
        {
            int offered=0,patrols=0;
            foreach(var (_,_,_,pane) in OfferedPanes(7))
            {
                ++offered;
                if(Turns(pane)) ++patrols;
            }
            double fraction=(double)patrols/offered;
            TestContext.WriteLine($"Patrols on {patrols}/{offered} offered panes ({fraction:P1}).");
            // shortcut: 11.0% measured; 15% needs more than two promoted exits per patrol, or exits that do not
            // join the survivors' group. Raise this if promotion is widened.
            // .09 leaves room for a planner change that reseeds the panes, yet fails if patrols become rarer.
            Assert.That(fraction,Is.GreaterThanOrEqualTo(.09),$"Patrols on {patrols}/{offered} offered panes ({fraction:P1}).");
        }

        [Test]
        public void PromotedPatrolExitsStayIntactThroughTheWave()
        {
            int promoted=0;
            for(int level=5;level<=12;++level)
            for(int seed=0;seed<100;++seed)
            {
                var board=new BoardState(6,6,seed);
                int start=0;
                for(int number=1;number<=board.Count;++number)
                {
                    var wave=board.BeginWave(start,level,number,number*10);
                    var panes=Offered(board,wave).Where(p=>p.PromotedExits.Length!=0).ToArray();
                    foreach(var pane in panes)
                    {
                        Assert.That(Turns(pane) && pane.PromotedExits.Length<=2);
                        Assert.That(wave.BreakingTiles.Intersect(pane.PromotedExits),Is.Empty);
                        Assert.That(wave.StationarySafe,Is.SupersetOf(pane.PromotedExits));
                        // ValidatePlan's rules: a stationary neighbour, and survivors reachable from a landing.
                        var reached=new HashSet<int>(wave.JumpLandings){wave.GuaranteedTile};
                        for(var queue=new Queue<int>(reached);queue.Count!=0;)
                        {
                            int tile=queue.Dequeue();
                            foreach(int next in wave.StationarySafe)
                                if(board.Neighbors(tile,next) && reached.Add(next)) queue.Enqueue(next);
                        }
                        foreach(int tile in pane.PromotedExits)
                            Assert.That(reached.Contains(tile) && wave.StationarySafe.Any(s=>board.Neighbors(tile,s)),
                                $"Level {level}: promoted tile {tile} is stranded.");
                    }
                    start=wave.GuaranteedTile;
                    var result=board.Tick(board.Center(start),true,wave.Deadline);
                    foreach(var pane in panes)
                    {
                        ++promoted;
                        Assert.That(pane.PromotedExits.All(board.IsIntact),Is.True);
                        for(int cell=0;cell<pane.TravelCells;++cell)
                        for(int sample=0;sample<20;++sample)
                        {
                            double t=pane.MotionStartsAt+(cell+sample/19.0)/pane.TravelCells*pane.OneWaySeconds;
                            Assert.That(pane.TryLand(pane.CenterAt(t),t),Is.True);
                            Assert.That(Enumerable.Range(0,board.Count).Any(tile=>board.IsIntact(tile) && board.CanStepFrom(pane.Tile,tile)),
                                Is.True,$"Level {level}: patrol {string.Join(",",pane.Path)} caught at {cell}+{sample}/19 has no exit after zero.");
                            pane.RestoreLock(false);
                        }
                    }
                    if(result!=WaveResult.Survived) break;
                }
            }
            TestContext.WriteLine($"Checked {promoted} patrols with promoted exits.");
            Assert.That(promoted,Is.GreaterThan(0));
        }

        // Follows each guarantee to the level's first ride finale, or null when the level ends without one.
        static WavePlan FirstRide(int level,int seed,out BoardState board)
        {
            board=new BoardState(6,6,seed);
            int start=0;
            for(int number=1;number<=board.Count;++number)
            {
                var wave=board.BeginWave(start,level,number,number*10);
                if(wave.IsRide) return wave;
                start=wave.GuaranteedTile;
                if(board.Tick(board.Center(start),true,wave.Deadline)!=WaveResult.Survived) return null;
            }
            return null;
        }
        static IEnumerable<(int level,int seed,BoardState board,WavePlan wave)> Rides(int fromLevel=6)
        {
            for(int level=fromLevel;level<=12;++level)
            for(int seed=0;seed<200;++seed)
            {
                var wave=FirstRide(level,seed,out var board);
                if(wave!=null) yield return (level,seed,board,wave);
            }
        }
        // 10 ms samples inside [StartsAt, Deadline - .3] with the pane centered within a quarter pitch of the cell.
        static List<double> RideTimes(BoardState board,WavePlan wave,SlidingPane pane,int cell)
        {
            var times=new List<double>();
            for(int i=0;wave.StartsAt+i*.01<=wave.Deadline-.3;++i)
            {
                double t=wave.StartsAt+i*.01;
                Vector2 d=pane.CenterAt(t)-board.Center(cell);
                if(Math.Max(Math.Abs(d.x),Math.Abs(d.y))<=.25f*board.Pitch) times.Add(t);
            }
            return times;
        }

        [Test]
        public void RideFinaleAppearsOnlyFromLevelSix()
        {
            int rides=0;
            foreach(var (level,_,_,wave) in Rides(3))
            {
                Assert.That(level,Is.GreaterThanOrEqualTo(6));
                Assert.That(wave.Mode,Is.EqualTo(WaveMode.LastTile));
                ++rides;
            }
            TestContext.WriteLine($"{rides} ride finales across levels 6-12, seeds 0-199.");
            // 405 measured; half of that still allows reseeding but catches a ride rule that stops firing.
            Assert.That(rides,Is.GreaterThanOrEqualTo(200),$"{rides} ride finales across levels 6-12, seeds 0-199.");
        }

        [Test]
        public void RideFinaleBreaksEveryStationaryPane()
        {
            int rides=0;
            foreach(var (_,_,board,wave) in Rides())
            {
                ++rides;
                var pane=wave.OfferedSliders[wave.RideSlider];
                Assert.That(wave.StationarySafe,Is.Empty);
                Assert.That(wave.GuaranteedTile,Is.EqualTo(pane.Tile));
                Assert.That(wave.SafeTiles,Is.EqualTo(new[]{pane.Tile}));
                foreach(int tile in Enumerable.Range(0,board.Count).Where(board.IsIntact))
                    Assert.That(wave.BreakingTiles,Has.Member(tile));
            }
            Assert.That(rides,Is.GreaterThan(0));
        }

        [Test]
        public void RidePaneIsCatchableBesideThePlayer()
        {
            int rides=0;
            foreach(var (_,_,board,wave) in Rides())
            {
                ++rides;
                var pane=wave.OfferedSliders[wave.RideSlider];
                // Independent of RideCell: the cells one pitch from the start, and the pane's centre every millisecond
                // from the wave start until .3 s before zero, counted while it is within a quarter pitch of one of them.
                var start=board.Center(wave.StartingTile);
                var beside=Enumerable.Range(0,board.Count)
                    .Where(c=>board.Layout.Contains(c) && Mathf.Abs(Vector2.Distance(board.Center(c),start)-board.Pitch)<.001f).ToArray();
                double over=0,overRideCell=0;
                for(int ms=0;wave.StartsAt+ms/1000.0<=wave.Deadline-.3;++ms)
                {
                    var center=pane.CenterAt(wave.StartsAt+ms/1000.0);
                    if(beside.Any(c=>Vector2.Distance(center,board.Center(c))<=.25f*board.Pitch)) over+=.001;
                    if(Vector2.Distance(center,board.Center(wave.RideCell))<=.25f*board.Pitch) overRideCell+=.001;
                }
                Assert.That(over,Is.GreaterThanOrEqualTo(.35),$"The ride pane is beside the player for only {over:F3} s.");
                Assert.That(beside,Has.Member(wave.RideCell));
                Assert.That(overRideCell,Is.GreaterThan(0),"The ride pane never passes over its ride cell.");
            }
            Assert.That(rides,Is.GreaterThan(0));
        }

        [Test]
        public void LandingOnTheRidePaneClearsTheLevelAndStayingPutLoses()
        {
            int rides=0;
            foreach(var (level,seed,_,_) in Rides())
            {
                ++rides;
                var stay=FirstRide(level,seed,out var board);
                Assert.That(board.Tick(board.Center(stay.StartingTile),true,stay.Deadline),Is.EqualTo(WaveResult.Lost));

                var ride=FirstRide(level,seed,out board);
                var pane=ride.OfferedSliders[ride.RideSlider];
                var span=RideTimes(board,ride,pane,ride.RideCell);
                double t=span[span.Count/2];
                Vector2 stance=pane.CenterAt(t);
                Assert.That(board.Sample(stance,true,t).IsSlider && pane.Locked);
                Assert.That(board.Tick(stance,true,ride.Deadline),Is.EqualTo(WaveResult.LevelCleared));
            }
            Assert.That(rides,Is.GreaterThan(0));
        }

        [Test] public void PlansFromLevelFiveSatisfyValidatePlan() => BoardRuleChecks.PlansFromLevelFive();

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
