using System;
using UnityEngine;

namespace FloorFracture
{
    public enum GameStage { Room, Welcome, Settings, PracticeStone, PracticeStep, Ready, Countdown, BetweenWaves, Paused, Fall, Results, Development, PlayArea }

    public sealed class GameFlow : MonoBehaviour
    {
        public RoomBoardPlacement Room;
        public OVRCameraRig Rig;
        GameStage stage = GameStage.Room;
        public GameStage Stage
        {
            get => stage;
            private set { if (stage != value && narration) narration.Stop(); stage = value; }
        }
        public int Level { get; private set; } = 1;
        public int WaveNumber { get; private set; } = 1;
        public BoardState State { get; private set; }
        public GlassBoardView BoardView { get; private set; }
        public GamePanel Panel { get; private set; }
        public FallSequence Ending { get; private set; }
        GameSound sound;
        OfflineNarration narration;
        QuestRunMetrics metrics;
        PracticeStone tutorial;
        OVRHand[] trackedHands;
        bool skipPinchHeld;
        bool reduced, gentle, narrated, levelFinished, appPaused;
        int volume, lastBeat=-1, practiceStart=-1, endingIndex=-1;
        double waitUntil;
        string roundText,clockText;
        bool lastReveal;
        static readonly string[] EndingNames={"Water","Lava","Crystal","Space","Sand","Snow"};
        const string StoneInstructions="Grab the stone with your hand or controller. " + OfflineNarration.StoneCaption;
        static MenuChoice Choice(string text,Action action) => new(text,action);
        static double Now => Time.unscaledTimeAsDouble;
        bool Tracked => !appPaused&&Panel&&Panel.Tracked&&Room&&Room.IsRoomLocalized;
        float eyeDepth = .08f;
        Vector2 HeadXZ => PlayerTileTracker.ProjectStance(BoardView.Board.worldToLocalMatrix, Panel.Head.position, Panel.Head.rotation, eyeDepth);
        public int SelectedSeed { get; private set; }

        void Start()
        {
            if(!Room) Room=FindAnyObjectByType<RoomBoardPlacement>();
            if(!Rig) Rig=FindAnyObjectByType<OVRCameraRig>();
            if(!Room||!Rig) throw new InvalidOperationException("Floor Fracture scene requires its room provider and one OVRCameraRig.");
            float profileDepth = OVRPlugin.eyeDepth;
            if (profileDepth >= .035f && profileDepth <= .14f) eyeDepth = profileDepth;
            trackedHands = Rig.GetComponentsInChildren<OVRHand>(true);
            reduced=PlayerPrefs.GetInt("ff.reduced",0)!=0; gentle=PlayerPrefs.GetInt("ff.gentle",0)!=0;
            volume=PlayerPrefs.GetInt("ff.volume",3);
            sound=gameObject.AddComponent<GameSound>(); sound.SetVolume(volume/4f);
            narrated=PlayerPrefs.GetInt("ff.narration",0)!=0;
            narration=gameObject.AddComponent<OfflineNarration>(); narration.Configure(narrated,volume/4f);
            Ending=gameObject.AddComponent<FallSequence>(); tutorial=gameObject.AddComponent<PracticeStone>();
            Panel=gameObject.AddComponent<GamePanel>(); Panel.Initialize(Rig,sound);
#if DEBUG && UNITY_ANDROID && !UNITY_EDITOR
            if (Debug.isDebugBuild) metrics=gameObject.AddComponent<QuestRunMetrics>();
#endif
            SelectedSeed=Environment.TickCount&int.MaxValue; Room.Changed+=RoomChanged; RoomChanged();
        }
        void OnDestroy() { if(Room) Room.Changed-=RoomChanged; }
        void OnDisable() { if(narration) narration.Stop(); }
        void RoomChanged()
        {
            if(Room.Status==RoomPlacementStatus.Confirmed)
            {
                if(Stage!=GameStage.Room) return;
                if (StartBoard()) Home();
                return;
            }
            if(Ending&&Ending.IsPlaying) Ending.ResetSequence();
            if(State!=null) { State.Pause(); if(BoardView) BoardView.gameObject.SetActive(false); }
            if(tutorial) tutorial.End();
            Stage=GameStage.Room;
            switch(Room.Status)
            {
                case RoomPlacementStatus.PermissionDenied:
                    Panel.Show("Allow spatial data",Room.Message,Choice("App permissions",Room.OpenPermissionSettings),Choice("Try again",Room.Retry)); break;
                case RoomPlacementStatus.NoRoom:
                case RoomPlacementStatus.UnsafeRoom:
                case RoomPlacementStatus.NoFit:
                    Panel.ShowRoomRecovery(Room.PlacementDebug ? Room.Message + "\n" + RoomBoardPlacement.DebugLegend : Room.Message,
                        Room.Retry, Room.RequestSpaceSetup, Application.isEditor || Debug.isDebugBuild ? ToggleRoomDiagnostics : null); break;
                default: Panel.Show("Reading your room",Room.Message); break;
            }
        }
        void ToggleRoomDiagnostics()
        {
            Room.TogglePlacementDebug();
            RoomChanged();
        }

        bool StartBoard()
        {
            if (Room.Status != RoomPlacementStatus.Confirmed || !Room.IsRoomLocalized)
            {
                Room.Retry();
                return false;
            }
            if(BoardView) { BoardView.gameObject.SetActive(false); Destroy(BoardView.gameObject); }
            var fit=Room.Placement; State=new BoardState(fit.Layout,SelectedSeed,fit.Pitch);
            var board=new GameObject("Confirmed glass board"); BoardView=board.AddComponent<GlassBoardView>(); BoardView.Build(State,fit.Pose);
            Ending.SetBoard(BoardView,Room);
            Level=WaveNumber=1; levelFinished=false;
            return true;
        }
        void Home()
        {
            Room.SetPlayAreaVisible(true);
            Stage=GameStage.Welcome; tutorial.End(); BoardView.ClearTransient(); BoardView.gameObject.SetActive(true);
            Room.ShowClearance(true);
            var choices=new System.Collections.Generic.List<MenuChoice>{
                Choice("Start",()=>Ready("Step onto intact glass, then start the wave.")),
                Choice("Tutorial",StartPractice),Choice("Settings",Settings),Choice("Play area",PlayArea)};
            if(Debug.isDebugBuild||Application.isEditor) choices.Add(Choice("Development",Development));
            Panel.Show("Floor Fracture",Room.Message+"\nMove to glass that will hold before zero.",choices.ToArray());
        }
        void PlayArea()
        {
            Stage=GameStage.PlayArea;
            Room.ShowClearance(true);
            var choices=new System.Collections.Generic.List<MenuChoice>{
                Choice("Back",Home),Choice("Refit saved floor",Room.Retry),Choice("Try another spot",Room.Decline),Choice("Space Setup",Room.RequestSpaceSetup)};
            if (Application.isEditor||Debug.isDebugBuild)
                choices.Add(Choice("Placement debug  ·  "+(Room.PlacementDebug?"On":"Off"),()=>{Room.TogglePlacementDebug();PlayArea();}));
            Panel.Show("Play area",Room.Message+"\n"+(Room.PlacementDebug ? RoomBoardPlacement.DebugLegend :
                "Check that the outlined panes are clear. Space Setup opens the Quest scanner. You can use a Touch controller there."),choices.ToArray());
        }
        void Settings()
        {
            Room.ShowClearance(false);
            Stage=GameStage.Settings; Panel.Show("Settings","The sample shows your glass warning. During a fall, pinch or press A or X to skip.",
                Choice("Fall motion  ·  "+(reduced?"Reduced":"Full"),()=>{reduced=!reduced; SaveSettings(); Settings();}),
                Choice("Warning  ·  "+(gentle?"Gentle":"Standard"),()=>{gentle=!gentle; SaveSettings(); Settings();}),
                Choice("Volume  ·  "+volume*25+"%",()=>{volume=(volume+1)%5; SaveSettings(); sound.SetVolume(volume/4f); Settings();}),
                Choice("Narration  ·  "+(narrated?"On":"Off"),()=>{narrated=!narrated; SaveSettings(); Settings();}),
                Choice("Back",Home));
            Panel.ShowWarningPreview(gentle);
        }
        void SaveSettings()
        {
            PlayerPrefs.SetInt("ff.reduced",reduced?1:0); PlayerPrefs.SetInt("ff.gentle",gentle?1:0);
            PlayerPrefs.SetInt("ff.narration",narrated?1:0); narration.Configure(narrated,volume/4f);
            PlayerPrefs.SetInt("ff.volume",volume); PlayerPrefs.Save();
        }
        void StartPractice()
        {
            Room.ShowClearance(false);
            tutorial.End();
            BoardView.ClearTransient();
            Stage=GameStage.PracticeStep;
            waitUntil=0;
            practiceStart=-1;
            Panel.Show("Practice stepping","Stand on intact glass and find the ring beneath you. " + OfflineNarration.StepCaption,
                Choice("Try the stone (optional)",StartStonePractice),Choice("Go to game",LeavePractice));
            narration.Speak(Stage,Panel.Head);
        }
        void StartStonePractice()
        {
            Room.ShowClearance(false);
            bool entering = Stage != GameStage.PracticeStone;
            Stage=GameStage.PracticeStone;
            waitUntil=0;
            BoardView.RestorePracticePane();
            tutorial.Begin(State,BoardView.Board,Panel,StoneLanded);
            Panel.Show("Stone practice",StoneInstructions,
                Choice("Return stone",StartStonePractice),Choice("Practice stepping",StartPractice),Choice("Go to game",LeavePractice));
            if (entering) narration.Speak(Stage,Panel.Head);
        }
        void StoneLanded(int tile)
        {
            narration.Stop();
            if(tile<0)
            {
                Panel.SetBody("The stone missed the glass. Pick it up for another try on any intact pane.");
                return;
            }
            BoardView.PracticeBreak(tile);
            sound.Break(BoardView.Board.TransformPoint(GlassGeometry.Ground(State.Center(tile))),1);
            Panel.SetBody("That pane shattered. The glass and stone will return for another try.");
            waitUntil=Now+1.1;
        }
        void LeavePractice()
        {
            tutorial.End();
            BoardView.ClearTransient();
            Ready("Stand on intact glass and start when ready.");
        }
        void Ready(string text,bool offerStone=false)
        {
            Room.SetPlayAreaVisible(true);
            Room.ShowClearance(false);
            Stage=GameStage.Ready; BoardView.gameObject.SetActive(true);
            var start=Choice("Start wave",TryStart);
            var menu=Choice("Main menu",()=>{if (StartBoard()) Home();});
            Panel.Show("Level "+Level,text,offerStone ? new[]{start,Choice("Try the stone (optional)",StartStonePractice),menu} : new[]{start,menu});
        }
        void TryStart()
        {
            if(!Tracked) { Panel.SetBody("Look around until the headset recognizes the room and tracks its position."); return; }
            var sample=State.Sample(HeadXZ,true,Now);
            if(sample.Kind!=SupportKind.Glass) { Panel.SetBody("Step onto intact glass. Your ring appears on the pane beneath you."); return; }
            var plan=State.BeginWave(sample.Index,Level,WaveNumber,Now);
            BoardView.AnchorReadout(Panel.Head);
            Stage=GameStage.Countdown; lastBeat=-1; clockText=null; Panel.Hide();
            roundText=plan.Mode switch {WaveMode.Memory=>"MEMORY",WaveMode.LastTile=>"LAST TILE",_=>"CLASSIC"};
            // The first warning, occupancy ring and countdown tone belong to the
            // same frame that starts the timer, including a just-locked moving pane.
            UpdateWave(plan.StartsAt);
            Debug.Log($"[Floor Fracture] seed={State.Seed} waveSeed={plan.Seed} level={Level} wave={WaveNumber} mode={plan.Mode} start={sample.Index} stationary={string.Join(",",plan.StationarySafe)} deadline={plan.Deadline:F3}");
        }
        void Update()
        {
            if(!BoardView||!Panel||!Panel.Head||Stage==GameStage.Room) return;
            double now=Now;
            if(Stage==GameStage.Countdown) { UpdateWave(now); return; }
            if(Stage==GameStage.Fall||Stage==GameStage.Results||Stage==GameStage.Paused) return;
            var sample=State.Sample(HeadXZ,Tracked,now);
            if(Stage==GameStage.BetweenWaves)
            {
                if(sample.Kind==SupportKind.Outside||sample.Kind==SupportKind.Untracked) { PauseRound(); return; }
                if(now>=waitUntil)
                {
                    if(levelFinished)
                    {
                        ++Level; WaveNumber=1; State.RebuildLevel(); BoardView.ClearTransient(); levelFinished=false;
                        Ready("The board has rebuilt. Take your place for the next level."); return;
                    }
                    if(sample.Kind!=SupportKind.Glass) { PauseRound(); return; }
                    TryStart(); return;
                }
            }
            if(Stage==GameStage.PracticeStone&&waitUntil>0&&now>=waitUntil)
            {
                waitUntil=0;
                BoardView.RestorePracticePane();
                tutorial.ReturnStone();
                Panel.SetBody(StoneInstructions);
            }
            if(Stage==GameStage.PracticeStep)
            {
                if(waitUntil>0&&now>=waitUntil)
                {
                    BoardView.RestorePracticePane();
                    Ready("You reached clear glass. In a wave, leave cracked glass before zero.",true);
                }
                else if(waitUntil==0&&sample.Kind==SupportKind.Glass&&practiceStart<0)
                {
                    practiceStart=sample.Index;
                    Panel.SetBody(OfflineNarration.StepCaption + " Choose a pane that shares an edge with yours. Take your time.");
                }
                else if(waitUntil==0&&sample.Kind==SupportKind.Glass&&State.Neighbors(practiceStart,sample.Index))
                {
                    narration.Stop();
                    BoardView.PracticeBreak(practiceStart);
                    sound.Break(BoardView.Board.TransformPoint(GlassGeometry.Ground(State.Center(practiceStart))),1);
                    sound.Completion();
                    Panel.SetBody("You reached clear glass. The cracked pane you left just shattered.");
                    waitUntil=now+1.1;
                }
            }
            int warning=Stage==GameStage.PracticeStep&&waitUntil==0?practiceStart:-1;
            BoardView.Draw(null,sample,now,gentle,warning);
            string message=Stage==GameStage.BetweenWaves?(levelFinished?"LEVEL CLEAR":"SAFE"):Stage==GameStage.PracticeStone?"TRY ANY GLASS PANE":
                Stage==GameStage.PracticeStep?(waitUntil>0?"SAFE":practiceStart<0?"STAND ON GLASS":"STEP TO CLEAR GLASS"):"";
            BoardView.SetReadout(message,Panel.Head);
        }
        void UpdateWave(double now)
        {
            var plan=State.ActiveWave;
            var resolvingSlider=State.Slider;
            WaveResult result=State.Tick(HeadXZ,Tracked,now);
            if(result==WaveResult.Paused) { PauseRound(); return; }
            if(result==WaveResult.Pending)
            {
                int beat=Mathf.Max(1,Mathf.CeilToInt((float)(plan.Deadline-now)));
                bool reveal=plan.ShowsWarnings(now);
                if(beat!=lastBeat || reveal!=lastReveal || clockText==null)
                {
                    if(beat!=lastBeat) sound.Tick(beat);
                    lastBeat=beat; lastReveal=reveal;
                    string cue=plan.Mode==WaveMode.Memory?(reveal?"MEMORY  ·  LOOK":"MEMORY  ·  RECALL"):roundText;
                    clockText=$"<size=180>{beat}</size>\n{cue}   ·   L{Level}   W{WaveNumber}";
                }
                BoardView.Draw(plan,State.CurrentSample,now,gentle);
                BoardView.SetReadout(clockText,Panel.Head); return;
            }
            if(State.BreaksApplied)
            {
                metrics?.Mark("shatter",plan.BreakingTiles.Count);
                Vector3 near=Vector3.zero; float best=float.PositiveInfinity;
                foreach(int cell in plan.BreakingTiles)
                {
                    Vector2 center = resolvingSlider != null && resolvingSlider.Tile == cell
                        ? resolvingSlider.CenterAt(plan.Deadline) : State.Center(cell);
                    var point=BoardView.Shatter(cell,center);
                    float distance=(point-Panel.Head.position).sqrMagnitude;
                    if(distance<best) { best=distance; near=point; }
                }
                sound.Break(near,plan.BreakingTiles.Count);
            }
            BoardView.Draw(null,State.CurrentSample,now,gentle);
            BoardView.SetReadout("",Panel.Head);
            var support = State.CurrentSample;
            Debug.Log($"[Floor Fracture] result={result} tile={support.Index} support={support.Kind} slider={support.IsSlider} stanceCell={State.IndexAt(support.Stance)} scheduledSafe={plan.IsSafe(support.Index)} waves={State.WavesSurvived}");
            if(result==WaveResult.Lost) { BeginFall(); return; }
            levelFinished=result==WaveResult.LevelCleared;
            if(levelFinished) sound.Completion(); else ++WaveNumber;
            Stage=GameStage.BetweenWaves; waitUntil=now+(levelFinished?1.8:1.28);
        }
        void PauseRound()
        {
            State.Pause();
            if(levelFinished) { State.RebuildLevel(); ++Level; WaveNumber=1; levelFinished=false; }
            Stage=GameStage.Paused; BoardView.gameObject.SetActive(false);
            BoardView.ClearTransient();
            BoardView.Draw(null,State.CurrentSample,Now,gentle);
            BoardView.SetReadout("",Panel.Head);
            sound.ResetSounds();
            Room.SetPlayAreaVisible(false);
            Panel.Show("Paused","Find the cleared board area. Show the glass when tracking is stable.",Choice("Show board",ShowBoardAfterPause));
        }
        void ShowBoardAfterPause()
        {
            if(!Tracked) { Panel.SetBody("Return to the cleared room and look around. When the headset recognizes it, choose Show board."); return; }
            Ready("Stand on a supported pane. Resume with a fresh countdown.");
        }
        void BeginFall()
        {
            Stage=GameStage.Fall; tutorial.End();
            var board=BoardView.Board;
            Vector2 hit=PlayerTileTracker.Project(board,Panel.Head.position);
            // Natural losses already exposed the actual broken panes. A leaning eye may
            // project across a seam onto surviving glass, which must stay in the arena.
            if (State.LastLoss==WaveLossReason.None) BoardView.RevealLossAperture(State.IndexAt(hit));
            Ending.SetOpening(board.TransformPoint(GlassGeometry.Ground(hit)),State.Pitch,board.eulerAngles.y);
            Panel.Hide(); Ending.Begin(Panel.Head,reduced,ShowResults);
            skipPinchHeld = HasSkipPinch();
            BoardView.SetReadout("Pinch or A / X  ·  Skip",Panel.Head);
        }

        bool HasSkipPinch()
        {
            foreach (var hand in trackedHands)
                if (hand.isActiveAndEnabled && hand.IsActive() && hand.IsDataHighConfidence
                    && !hand.IsSystemGestureInProgress && hand.IsPressed()) return true;
            return false;
        }

        void LateUpdate()
        {
            if(Stage!=GameStage.Fall)
            {
                if(Ending && Panel) Ending.ShowOpenPanes(Panel.Head);
                return;
            }
            bool pinch = HasSkipPinch();
            if ((pinch && !skipPinchHeld) || OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)
                || OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) Ending.Skip();
            skipPinchHeld = pinch;
#if UNITY_EDITOR
            if(UnityEngine.InputSystem.Keyboard.current?.spaceKey.wasPressedThisFrame==true) Ending.Skip();
#endif
        }
        void ShowResults()
        {
            if(Stage!=GameStage.Fall) return;
            Room.SetPlayAreaVisible(false);
            Stage=GameStage.Results; BoardView.gameObject.SetActive(false); Panel.Recenter();
            string score = State.WavesSurvived == 1 ? "1 wave survived" : $"{State.WavesSurvived} waves survived";
            string result = State.LastLoss switch
            {
                WaveLossReason.GapGraceExpired => $"You stayed over an open hole for {PlayerTileTracker.GapGrace:0.0} seconds.",
                WaveLossReason.GapAtZero => "You were over an open hole at zero.",
                WaveLossReason.MovingPane => "At zero, the moving glass under the ring shattered.",
                WaveLossReason.MemoryPane => "At zero, the ring was on glass that cracked during the Memory reveal.",
                WaveLossReason.CrackedPane => "At zero, the glass under the ring shattered.",
                _ => "Ending preview"
            };
            Panel.Show("Run complete",$"Level {Level}   ·   {score}   ·   {Ending.LastEndingName}\n{result}\n{OfflineNarration.ResultsCaption}",
                Choice("Restart",Restart),Choice("Quit",Quit));
            if (!Ending.LastWasSkipped) narration.Speak(Stage,Panel.Head);
        }
        void Restart()
        {
            Ending.ResetSequence(); sound.ResetSounds(); tutorial.End(); SelectedSeed=Environment.TickCount&int.MaxValue;
            if (StartBoard()) Ready("The glass is back. Return to the board and start when ready.");
        }
        void Development()
        {
            if(!Debug.isDebugBuild&&!Application.isEditor) return;
            Room.ShowClearance(false);
            Stage=GameStage.Development;
            Panel.Show("Development","Seed "+SelectedSeed+"   ·   selected ending "+(endingIndex<0?"Shuffle":EndingNames[endingIndex]),
                Choice("Seed 379 · level 1",()=>DevelopmentRun(1)),Choice("Seed 379 · level 3",()=>DevelopmentRun(3)),
                Choice("Next ending",()=>{endingIndex=(endingIndex+1)%EndingNames.Length; Ending.SetDevelopmentEnding(endingIndex); Development();}),
                Choice("Preview loss",BeginFall),Choice("Back",Home));
        }
        void DevelopmentRun(int level)
        {
            SelectedSeed=379; if (!StartBoard()) return;
            Level=level; Ready("Fixed seed 379. Stand on an intact pane to reproduce this run.");
        }
        void OnApplicationPause(bool pause) { appPaused=pause; if(pause) InterruptRound(); }
        void OnApplicationFocus(bool focus) { if(!focus) InterruptRound(); }
        void InterruptRound() { if(Stage==GameStage.Countdown||Stage==GameStage.BetweenWaves) PauseRound(); }
        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
    }
}
