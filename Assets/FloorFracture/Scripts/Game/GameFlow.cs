using System;
using UnityEngine;

namespace FloorFracture
{
    public enum GameStage { Room, Welcome, Settings, PracticeStone, PracticeStep, Ready, Countdown, BetweenWaves, Paused, Fall, Results, Development, PlayArea }

    public sealed class GameFlow : MonoBehaviour
    {
        public RoomBoardPlacement Room;
        public OVRCameraRig Rig;
        public GameStage Stage { get; private set; } = GameStage.Room;
        public int Level { get; private set; } = 1;
        public int WaveNumber { get; private set; } = 1;
        public BoardState State { get; private set; }
        public GlassBoardView BoardView { get; private set; }
        public GamePanel Panel { get; private set; }
        public FallSequence Ending { get; private set; }
        GameSound sound;
        QuestRunMetrics metrics;
        PracticeStone tutorial;
        OVRHand[] trackedHands;
        bool skipPinchHeld;
        bool reduced, gentle, levelFinished, appPaused;
        int volume, lastBeat=-1, practiceStart=-1, endingIndex=-1;
        double waitUntil;
        string roundText,clockText;
        bool lastReveal;
        static readonly string[] EndingNames={"Water","Lava","Crystal","Space","Sand","Snow"};
        const string StoneInstructions="Grab the stone with your hand or controller. Drop or gently toss it onto any intact glass pane.";
        static MenuChoice Choice(string text,Action action) => new(text,action);
        static double Now => Time.unscaledTimeAsDouble;
        bool Tracked => !appPaused&&Panel&&Panel.Tracked&&Room&&Room.IsRoomLocalized;
        Vector2 HeadXZ => PlayerTileTracker.Project(BoardView.Board,Panel.Head.position);
        public int SelectedSeed { get; private set; }

        void Start()
        {
            if(!Room) Room=FindFirstObjectByType<RoomBoardPlacement>();
            if(!Rig) Rig=FindFirstObjectByType<OVRCameraRig>();
            if(!Room||!Rig) throw new InvalidOperationException("Floor Fracture scene requires its room provider and one OVRCameraRig.");
            trackedHands = Rig.GetComponentsInChildren<OVRHand>(true);
            reduced=PlayerPrefs.GetInt("ff.reduced",0)!=0; gentle=PlayerPrefs.GetInt("ff.gentle",0)!=0;
            volume=PlayerPrefs.GetInt("ff.volume",3);
            sound=gameObject.AddComponent<GameSound>(); sound.SetVolume(volume/4f);
            Ending=gameObject.AddComponent<FallSequence>(); tutorial=gameObject.AddComponent<PracticeStone>();
            Ending.ChamberEntered+=()=>{ if(BoardView) BoardView.gameObject.SetActive(false); Room.ShowFloorSurface(false); };
            Panel=gameObject.AddComponent<GamePanel>(); Panel.Initialize(Rig,sound);
#if DEVELOPMENT_BUILD
            metrics=gameObject.AddComponent<QuestRunMetrics>();
#endif
            SelectedSeed=Environment.TickCount&int.MaxValue; Room.Changed+=RoomChanged; RoomChanged();
        }
        void OnDestroy() { if(Room) Room.Changed-=RoomChanged; }
        void RoomChanged()
        {
            if(Room.Status==RoomPlacementStatus.Confirmed)
            {
                if(Stage!=GameStage.Room) return;
                StartBoard(); Home(); return;
            }
            if(State!=null) { State.Pause(); if(BoardView) BoardView.gameObject.SetActive(false); }
            if(tutorial) tutorial.End();
            if(Ending&&Ending.IsPlaying) Ending.ResetSequence();
            Stage=GameStage.Room;
            switch(Room.Status)
            {
                case RoomPlacementStatus.PermissionDenied:
                    Panel.Show("Allow spatial data",Room.Message,Choice("App permissions",Room.OpenPermissionSettings),Choice("Try again",Room.Retry)); break;
                case RoomPlacementStatus.NoRoom:
                case RoomPlacementStatus.UnsafeRoom:
                case RoomPlacementStatus.NoFit:
                    Panel.Show("Find a clear floor",Room.Message,Choice("Space Setup",Room.RequestSpaceSetup),Choice("Check again",Room.Retry)); break;
                default: Panel.Show("Reading your room",Room.Message); break;
            }
        }
        void StartBoard()
        {
            if(BoardView) { BoardView.gameObject.SetActive(false); Destroy(BoardView.gameObject); }
            var fit=Room.Placement; State=new BoardState(fit.Columns,fit.Rows,SelectedSeed,fit.Pitch);
            var board=new GameObject("Confirmed glass board"); BoardView=board.AddComponent<GlassBoardView>(); BoardView.Build(State,fit.Pose);
            Ending.SetBoard(BoardView);
            Level=WaveNumber=1; levelFinished=false;
        }
        void Home()
        {
            Room.ShowFloorSurface(true);
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
            Panel.Show("Play area",Room.Message+"\nCheck that the outlined play area is still clear. If furniture moved since the scan, choose another spot or update your room.",
                Choice("Back",Home),Choice("Try another spot",Room.Decline),Choice("Space Setup",Room.RequestSpaceSetup));
        }
        void Settings()
        {
            Room.ShowClearance(false);
            Stage=GameStage.Settings; Panel.Show("Settings","The pane previews your warning intensity. During a fall, pinch or press A or X to skip.",
                Choice("Fall motion  ·  "+(reduced?"Reduced":"Full"),()=>{reduced=!reduced; SaveSettings(); Settings();}),
                Choice("Warning  ·  "+(gentle?"Gentle":"Standard"),()=>{gentle=!gentle; SaveSettings(); Settings();}),
                Choice("Volume  ·  "+volume*25+"%",()=>{volume=(volume+1)%5; SaveSettings(); sound.SetVolume(volume/4f); sound.Click(); Settings();}),
                Choice("Back",Home));
        }
        void SaveSettings()
        {
            PlayerPrefs.SetInt("ff.reduced",reduced?1:0); PlayerPrefs.SetInt("ff.gentle",gentle?1:0);
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
            Panel.Show("Practice stepping","Stand on intact glass and find the ring beneath you. Your pane will light up with cracks. Step onto clear glass beside it.",
                Choice("Try the stone (optional)",StartStonePractice),Choice("Go to game",LeavePractice));
        }
        void StartStonePractice()
        {
            Room.ShowClearance(false);
            Stage=GameStage.PracticeStone;
            waitUntil=0;
            BoardView.RestorePracticePane();
            tutorial.Begin(State,BoardView.Board,Panel,StoneLanded);
            Panel.Show("Stone practice",StoneInstructions,
                Choice("Return stone",StartStonePractice),Choice("Practice stepping",StartPractice),Choice("Go to game",LeavePractice));
        }
        void StoneLanded(int tile)
        {
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
            Room.ShowFloorSurface(true);
            Room.ShowClearance(false);
            Stage=GameStage.Ready; BoardView.gameObject.SetActive(true);
            var start=Choice("Start wave",TryStart);
            var menu=Choice("Main menu",()=>{StartBoard(); Home();});
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
            Debug.Log($"[Floor Fracture] seed={State.Seed} waveSeed={plan.Seed} level={Level} wave={WaveNumber} mode={plan.Mode} start={sample.Index} stationary={string.Join(",",plan.StationarySafe)} deadline={plan.Deadline:F3}");
        }
        void Update()
        {
            if(!BoardView||!Panel||!Panel.Head||Stage==GameStage.Room) return;
            double now=Now;
            if(Stage==GameStage.Countdown) { UpdateWave(now); return; }
            if(Stage==GameStage.Fall||Stage==GameStage.Results) return;
            if(Stage==GameStage.BetweenWaves)
            {
                if(!Tracked) { PauseRound(); return; }
                if(now>=waitUntil)
                {
                    if(levelFinished)
                    {
                        ++Level; WaveNumber=1; State.RebuildLevel(); BoardView.ClearTransient(); levelFinished=false;
                        Ready("The board has rebuilt. Take your place for the next level."); return;
                    }
                    if(State.Sample(HeadXZ,true,now).Kind!=SupportKind.Glass) { PauseRound(); return; }
                    TryStart(); return;
                }
            }
            if(Stage==GameStage.Paused) return;
            var sample=State.Sample(HeadXZ,Tracked,now);
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
                    Panel.SetBody("Your ring is on cracked glass. Step onto a clear pane that shares an edge with it. Take your time.");
                }
                else if(waitUntil==0&&sample.Kind==SupportKind.Glass&&State.Neighbors(practiceStart,sample.Index))
                {
                    BoardView.PracticeBreak(practiceStart);
                    sound.Break(BoardView.Board.TransformPoint(GlassGeometry.Ground(State.Center(practiceStart))),1);
                    sound.Completion();
                    Panel.SetBody("You reached clear glass. The cracked pane you left just shattered.");
                    waitUntil=now+1.1;
                }
            }
            int warning=Stage==GameStage.Settings?0:Stage==GameStage.PracticeStep&&waitUntil==0?practiceStart:-1;
            BoardView.Draw(null,sample,now,gentle,warning);
            string message=Stage==GameStage.BetweenWaves?(levelFinished?"LEVEL CLEAR":"SAFE"):Stage==GameStage.PracticeStone?"TRY ANY GLASS PANE":
                Stage==GameStage.PracticeStep?(waitUntil>0?"SAFE":practiceStart<0?"STAND ON GLASS":"STEP TO CLEAR GLASS"):"";
            BoardView.SetReadout(message,Panel.Head);
        }
        void UpdateWave(double now)
        {
            var plan=State.ActiveWave;
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
                    BoardView.Shatter(cell);
                    var point=BoardView.transform.TransformPoint(GlassGeometry.Ground(State.Center(cell)));
                    float distance=(point-Panel.Head.position).sqrMagnitude;
                    if(distance<best) { best=distance; near=point; }
                }
                sound.Break(near,plan.BreakingTiles.Count);
            }
            BoardView.Draw(null,State.CurrentSample,now,gentle);
            BoardView.SetReadout("",Panel.Head);
            Debug.Log($"[Floor Fracture] result={result} tile={State.CurrentSample.Index} waves={State.WavesSurvived}");
            if(result==WaveResult.Lost) { BeginFall(); return; }
            levelFinished=result==WaveResult.LevelCleared;
            if(levelFinished) sound.Completion(); else ++WaveNumber;
            Stage=GameStage.BetweenWaves; waitUntil=now+(levelFinished?1.8:1.28);
        }
        void PauseRound()
        {
            State?.Pause();
            if(levelFinished) { State.RebuildLevel(); ++Level; WaveNumber=1; levelFinished=false; }
            Stage=GameStage.Paused; BoardView.gameObject.SetActive(false); Room.ShowFloorSurface(false);
            Panel.Show("Paused","Find the cleared board area. Show the glass when tracking is stable.",Choice("Show board",()=>Ready("Stand on a supported pane. Resume with a fresh countdown.")));
        }
        void BeginFall()
        {
            Stage=GameStage.Fall; tutorial.End();
            var point=Panel.Head.position; point.y=BoardView.transform.position.y;
            int cell=State.CurrentSample.Index;
            if(cell>=0)
            {
                point=BoardView.transform.TransformPoint(GlassGeometry.Ground(State.Center(cell)));
                BoardView.RevealLossAperture(cell);
            }
            Ending.SetOpening(point,State.Pitch,BoardView.transform.eulerAngles.y);
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
            Room.ShowFloorSurface(false);
            Stage=GameStage.Results; BoardView.gameObject.SetActive(false); Panel.Recenter();
            Panel.Show("Run complete",$"Level {Level}   ·   {State.WavesSurvived} waves survived\n{Ending.LastEndingName}",Choice("Restart",Restart),Choice("Quit",Quit));
        }
        void Restart()
        {
            Ending.ResetSequence(); sound.ResetSounds(); tutorial.End(); SelectedSeed=Environment.TickCount&int.MaxValue;
            StartBoard(); Ready("The glass is back. Return to the board and start when ready.");
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
            SelectedSeed=379; StartBoard(); Level=level; Ready("Fixed seed 379. Stand on an intact pane to reproduce this run.");
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
