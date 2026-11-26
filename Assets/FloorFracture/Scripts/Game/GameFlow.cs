using System;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    public enum GameStage { Room, Welcome, Settings, Ready, Countdown, BetweenWaves, Paused, Fall, Results, Development, PlayArea, CountIn, LevelSelect }

    public sealed class GameFlow : MonoBehaviour
    {
        public RoomBoardPlacement Room;
        public OVRCameraRig Rig;
        [SerializeField] bool useDynamicResolution = true;
        GameStage stage = GameStage.Room;
        GameStage pausedStage;
        int pausedPane=-1;
        bool pausedBoardVisible;
        Action restorePausedPage;
        double? menuPausedAt;
        double menuPauseSeconds, cueDelaySeconds;
        int nextCueGroup;
        public GameStage Stage
        {
            get => stage;
            private set
            {
                if (stage == value) return;
                if (narration) narration.Stop();
                if (Panel) Panel.HideStartCount();
                stage = value;
                if (sound)
                {
                    sound.SetMusicAudible(value != GameStage.Fall);
                    if (!CanHearOpening) sound.ClearHoleDraft();
                }
            }
        }
        public int Level { get; private set; } = 1;
        const string HighestLevelKey = "ff.highestLevel";
        const int LevelsPerPage = 3;
        static int HighestLevel => Math.Max(1, PlayerPrefs.GetInt(HighestLevelKey, 1));
        bool developmentRun, pickerFromResults;
        public int WaveNumber { get; private set; } = 1;
        public BoardState State { get; private set; }
        public GlassBoardView BoardView { get; private set; }
        public GamePanel Panel { get; private set; }
        public FallSequence Ending { get; private set; }
        GameSound sound;
        OfflineNarration narration;
        QuestRunMetrics metrics;
        OVRHand[] trackedHands;
        bool skipPinchHeld;
        bool reduced, gentle, narrated, levelFinished, appPaused;
        int volume, endingIndex=-1;
        double waitUntil;
        double countInStartedAt;
        int countInNumber;
        bool showingGo;
        const double GoSeconds = .35;
        static readonly string[] EndingNames={"Water","Lava","Tar Pit","Space","Sand","Snow"};
        static MenuChoice Choice(string text,Action action) => new(text,action);
        // Waves, sliding panes and between-wave delays all consume this gameplay clock.
        public double GameplayTime => (menuPausedAt ?? Time.unscaledTimeAsDouble) - menuPauseSeconds - cueDelaySeconds;
        bool Tracked => !appPaused&&Panel&&Panel.Tracked&&Room&&Room.IsRoomLocalized;
        bool CanHearOpening => Stage == GameStage.Countdown || Stage == GameStage.BetweenWaves;
        float eyeDepth = .08f;
        Vector2 HeadXZ => PlayerTileTracker.ProjectStance(BoardView.Board.worldToLocalMatrix, Panel.Head.position, Panel.Head.rotation, eyeDepth);
        public int SelectedSeed { get; private set; }

        void Start()
        {
            if(!Room) Room=FindAnyObjectByType<RoomBoardPlacement>();
            if(!Rig) Rig=FindAnyObjectByType<OVRCameraRig>();
            if(!Room||!Rig) throw new InvalidOperationException("Floor Fracture scene requires its room provider and one OVRCameraRig.");
            // SDK 209 looks up the rig prematurely when dynamic resolution runs in OVRManager.Awake.
            if (useDynamicResolution && OVRManager.instance)
                OVRManager.instance.EnableDynamicResolution(reallocateToMaxRes: true);
            float profileDepth = OVRPlugin.eyeDepth;
            if (profileDepth >= .035f && profileDepth <= .14f) eyeDepth = profileDepth;
            trackedHands = Rig.GetComponentsInChildren<OVRHand>(true);
            reduced=PlayerPrefs.GetInt("ff.reduced",0)!=0; gentle=PlayerPrefs.GetInt("ff.gentle",0)!=0;
            volume=PlayerPrefs.GetInt("ff.volume",3);
            sound=gameObject.AddComponent<GameSound>(); sound.SetVolume(volume/4f);
            narrated=PlayerPrefs.GetInt("ff.narration",0)!=0;
            narration=gameObject.AddComponent<OfflineNarration>(); narration.Configure(narrated,volume/4f);
            Ending=gameObject.AddComponent<FallSequence>();
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
            if (Room.Status != RoomPlacementStatus.Confirmed) return false;
            menuPausedAt=null;
            restorePausedPage=null;
            menuPauseSeconds=cueDelaySeconds=0;
            if(BoardView) { BoardView.gameObject.SetActive(false); Destroy(BoardView.gameObject); }
            var fit=Room.Placement; State=new BoardState(fit.Layout,SelectedSeed,fit.Pitch);
            var board=new GameObject("Confirmed glass board"); BoardView=board.AddComponent<GlassBoardView>(); BoardView.Build(State,fit.Pose,sound);
            Ending.SetBoard(BoardView);
            Level=WaveNumber=1; levelFinished=false;
            developmentRun=false;
            return true;
        }
        void Home()
        {
            Stage=GameStage.Welcome; BoardView.ClearTransient(); BoardView.gameObject.SetActive(true);
            Room.ShowClearance(true);
            var choices=new System.Collections.Generic.List<MenuChoice>{
                Choice("Start",()=>Ready("Step onto intact glass, then start the wave.")),
                Choice("Choose level",OpenLevelPicker),
                Choice("Settings",Settings),Choice("Play area",PlayArea)};
            if(Debug.isDebugBuild||Application.isEditor) choices.Add(Choice("Development",Development));
            Panel.Show("Floor Fracture",Room.Message+"\nWatch which panes flash. Step onto one that stayed clear before the glass breaks.",choices.ToArray());
        }
        void OpenLevelPicker()
        {
            pickerFromResults = Stage == GameStage.Results;
            Room.ShowClearance(false);
            ShowLevelPage(1 + (HighestLevel - 1) / LevelsPerPage * LevelsPerPage);
        }

        void ShowLevelPage(int first)
        {
            Stage = GameStage.LevelSelect;
            int highest = HighestLevel;
            var choices = new List<MenuChoice>();
            for (int offset = 0; offset < LevelsPerPage && offset <= highest - first; ++offset)
            {
                int level = first + offset;
                choices.Add(Choice($"Level {level}", () => RestartAt(level)));
            }
            bool hasNext = highest - first >= LevelsPerPage;
            if (!hasNext && highest < int.MaxValue)
                choices.Add(Choice($"Level {highest + 1} · Locked", null));
            if (first > 1) choices.Add(Choice("Earlier levels", () => ShowLevelPage(first - LevelsPerPage)));
            if (hasNext) choices.Add(Choice("Later levels", () => ShowLevelPage(first + LevelsPerPage)));
            choices.Add(Choice("Back", LeaveLevelPicker));
            Panel.Show("Choose level", "Reach a level to unlock it. Each attempt starts at wave 1.", choices.ToArray());
        }

        void LeaveLevelPicker()
        {
            if (pickerFromResults) ResultsPage();
            else Home();
        }
        void PlayArea()
        {
            Stage=GameStage.PlayArea;
            Room.ShowClearance(true);
            var choices=new System.Collections.Generic.List<MenuChoice>{
                Choice("Back",Home),Choice("Refit saved floor",Room.Retry),Choice("Try another spot",Room.Decline),Choice("Space Setup",Room.RequestSpaceSetup)};
            choices.Add(Choice("Board size  ·  "+(Room.EntireFloor?"Entire connected floor":"Up to 16 tiles"),()=>Room.SetEntireFloor(!Room.EntireFloor)));
            if (Application.isEditor||Debug.isDebugBuild)
                choices.Add(Choice("Placement debug  ·  "+(Room.PlacementDebug?"On":"Off"),()=>{Room.TogglePlacementDebug();PlayArea();}));
            Panel.Show("Play area",Room.Message+"\n"+(Room.PlacementDebug ? RoomBoardPlacement.DebugLegend :
                "Changing board size refits your saved floor. Space Setup opens the Quest scanner."),choices.ToArray());
        }
        void Settings()
        {
            Room.ShowClearance(false);
            Stage=GameStage.Settings; Panel.Show("Settings","Use the controller Menu button or hand menu gesture to pause. During a fall, pinch or press A or X to skip.",
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
        void Ready(string text)
        {
            Unlock(Level);
            Room.ShowClearance(false);
            Stage=GameStage.Ready; BoardView.gameObject.SetActive(true);
            var start=Choice("Start wave",StartCountIn);
            var menu=Choice("Main menu",ReturnToMainMenu);
            if (Level==5 && WaveNumber==1)
                text+=" Marked groups fall in order. Your starting pane breaks last.";
            if (Level>=6 && WaveNumber==1)
                text+=" When rows crack in turn, keep stepping onto clear glass.";
            if (Level>=2 && WaveNumber==1)
                text+=" When a gap opens around you, cross one opening to any clear pane within reach.";
            if (Level==6 && WaveNumber==1)
                text+=" When no glass is left in reach, step onto the pane that starts to slide.";
            Panel.Show("Level "+Level,text,start,menu);
        }
        void Unlock(int level)
        {
            if (developmentRun || level <= HighestLevel) return;
            PlayerPrefs.SetInt(HighestLevelKey, level);
            PlayerPrefs.Save();
        }
        void AdvanceLevel()
        {
            ++Level; WaveNumber=1; State.RebuildLevel(); BoardView.ClearTransient(); levelFinished=false;
        }
        void StartCountIn()
        {
            if (Stage != GameStage.Ready) return;
            if(!Tracked) { Panel.SetBody("Look around until the headset recognizes the room and tracks its position."); return; }
            double now = GameplayTime;
            var sample=State.Sample(HeadXZ,true,now);
            if(sample.Kind!=SupportKind.Glass) { Panel.SetBody("Step onto intact glass. Your ring appears on the pane beneath you."); return; }
            Stage = GameStage.CountIn;
            Panel.Hide();
            countInStartedAt = now;
            countInNumber = 0;
            UpdateCountIn(sample, now);
        }

        void UpdateCountIn(TileSample sample, double now)
        {
            double elapsed = Math.Max(0, now - countInStartedAt);
            if (elapsed < 3)
            {
                BoardView.Draw(null, sample, now, gentle);
                int number = 3 - (int)elapsed;
                if (countInNumber != number)
                {
                    countInNumber = number;
                    Panel.ShowStartCount(number);
                    sound.StartCountCue(false);
                }
                return;
            }
            if (sample.Kind != SupportKind.Glass)
            {
                Ready("Step back onto intact glass, then start the wave.");
                return;
            }
            StartWave(sample, now, true);
        }

        void StartWave(TileSample sample, double now, bool manual)
        {
            var plan=State.BeginWave(sample.Index,Level,WaveNumber,now);
            nextCueGroup=0;
            Ending.SelectLevel(Level);
            Stage=GameStage.Countdown; Panel.Hide();
            showingGo = manual;
            if (manual) sound.StartCountCue(true);
            UpdateWave(plan.StartsAt);
            if (Debug.isDebugBuild) Debug.Log($"[Floor Fracture] seed={State.Seed} waveSeed={plan.Seed} level={Level} wave={WaveNumber} mode={plan.Mode} start={sample.Index} stationary={string.Join(",",plan.StationarySafe)} deadline={plan.Deadline:F3}");
        }
        void Update()
        {
            if (!Panel || !Panel.Head) return;
            // The SDK supplies Start for both Touch Menu and HandStatus.MenuPressed.
            if (!appPaused && OVRInput.GetDown(OVRInput.RawButton.Start,OVRInput.Controller.Touch | OVRInput.Controller.Hands))
            {
                HandleMenuButton();
                return;
            }
            if(!BoardView||Stage==GameStage.Room) return;
            if ((Stage==GameStage.Ready || Stage==GameStage.CountIn || Stage==GameStage.Fall) && !Tracked) { PauseRound(); return; }
            double now=GameplayTime;
            if(Stage==GameStage.Countdown) { UpdateWave(now); return; }
            if (Stage==GameStage.Paused && menuPausedAt.HasValue && pausedStage==GameStage.CountIn && !Tracked)
                InterruptRound();
            if(Stage==GameStage.Fall||Stage==GameStage.Results||Stage==GameStage.Paused) return;
            var sample=State.Sample(HeadXZ,Tracked,now);
            if (Stage == GameStage.CountIn) { UpdateCountIn(sample, now); return; }
            if(Stage==GameStage.BetweenWaves)
            {
                if(sample.Kind==SupportKind.Outside||sample.Kind==SupportKind.Untracked) { PauseRound(); return; }
                if(now>=waitUntil)
                {
                    if(levelFinished)
                    {
                        AdvanceLevel();
                        Ready("The board has rebuilt. Take your place for the next level."); return;
                    }
                    if(sample.Kind!=SupportKind.Glass) { PauseRound(); return; }
                    StartWave(sample, now, false); return;
                }
            }
            BoardView.Draw(null,sample,now,gentle);
        }
        void UpdateWave(double now)
        {
            var plan=State.ActiveWave;
            double frameTime=plan.TimeForVisibleCue(now,ref nextCueGroup);
            cueDelaySeconds+=now-frameTime;
            now=frameTime;
            if (showingGo)
            {
                float alpha = Mathf.Clamp01((float)(1 - (now - plan.StartsAt) / GoSeconds));
                if (alpha > 0) Panel.ShowStartCount(0, alpha);
                else { Panel.HideStartCount(); showingGo = false; }
            }
            var resolvingSliders=State.Sliders;
            int previousGroupCount=State.AppliedBreakGroupCount;
            WaveResult result=State.Tick(HeadXZ,Tracked,now);
            if(result==WaveResult.Paused) { PauseRound(); return; }
            for (int i=previousGroupCount; i<State.AppliedBreakGroupCount; i++)
                ShatterGroup(plan.BreakGroups[i],resolvingSliders,now);
            var support = State.CurrentSample;
            if(result==WaveResult.Pending)
            {
                BoardView.Draw(plan,State.CurrentSample,now,gentle);
                return;
            }
            BoardView.Draw(null,State.CurrentSample,now,gentle);
            if (Debug.isDebugBuild) Debug.Log($"[Floor Fracture] result={result} tile={support.Index} support={support.Kind} slider={support.IsSlider} stanceCell={State.IndexAt(support.Stance)} scheduledSafe={plan.IsSafe(support.Index)} waves={State.WavesSurvived}");
            if(result==WaveResult.Lost) { BeginFall(); return; }
            levelFinished=result==WaveResult.LevelCleared;
            // Save the unlock now: the player may pause or quit during the 1.8 s celebration.
            if(levelFinished) { Unlock(Level+1); sound.Completion(); } else ++WaveNumber;
            Stage=GameStage.BetweenWaves; waitUntil=now+(levelFinished?1.8:plan.RecoverySeconds);
        }
        void ShatterGroup(WaveBreakGroup group, IReadOnlyList<SlidingPane> sliders,double now)
        {
            metrics?.Mark("shatter",group.Tiles.Count);
            Vector3 nearest=Vector3.zero;
            float closest=float.PositiveInfinity;
            foreach(int cell in group.Tiles)
            {
                var center=State.Center(cell);
                for (int i=0; i<sliders.Count; ++i)
                    if (sliders[i].Tile==cell) { center=sliders[i].CenterAt(group.BreaksAt); break; }
                var point=BoardView.Shatter(cell,center,now-group.BreaksAt);
                float distance=(point-Panel.Head.position).sqrMagnitude;
                if(distance<closest) { closest=distance; nearest=point; }
            }
            sound.Break(nearest,group.Tiles.Count);
        }
        void HandleMenuButton()
        {
            switch (Stage)
            {
                case GameStage.Paused: ShowBoardAfterPause(); break;
                case GameStage.Results: ReturnToMainMenu(); break;
                case GameStage.LevelSelect: LeaveLevelPicker(); break;
                case GameStage.Settings:
                case GameStage.Development:
                case GameStage.PlayArea: Home(); break;
                case GameStage.Welcome:
                case GameStage.Room: Panel.Recenter(); break;
                default: PauseRound(true); break;
            }
        }

        void PauseRound(bool fromMenu=false)
        {
            if (fromMenu)
            {
                menuPausedAt=Time.unscaledTimeAsDouble;
                restorePausedPage=Panel.Visible ? Panel.RememberPage() : null;
            }
            pausedStage=Stage;
            pausedPane=State.IndexAt(HeadXZ);
            pausedBoardVisible=BoardView.gameObject.activeSelf;
            if (pausedStage==GameStage.Fall) Ending.SetTrackingPaused(true);
            else
            {
                if (!fromMenu) State.Pause();
                if (!fromMenu)
                {
                    BoardView.ClearTransient();
                    BoardView.Draw(null,State.CurrentSample,GameplayTime,gentle);
                }
                sound.ResetSounds();
            }
            Stage=GameStage.Paused;
            BoardView.gameObject.SetActive(false);
            Panel.Recenter();
            if (fromMenu)
                Panel.Show("Paused","Resume this run or return to the main menu.",
                    Choice("Resume",ShowBoardAfterPause),Choice("Main menu",ReturnToMainMenu));
            else
                Panel.Show("Paused","Return to the cleared room, then choose Show board.",
                    Choice("Show board",ShowBoardAfterPause),Choice("End run · Play area",()=>{if (ResetRun()) PlayArea();}),
                    Choice("Main menu",ReturnToMainMenu));
        }
        void ShowBoardAfterPause()
        {
            if(!Tracked) { Panel.SetBody("Return to the cleared room and look around, then resume."); return; }
            bool manual=menuPausedAt.HasValue;
            if (manual && (pausedStage==GameStage.Countdown || pausedStage==GameStage.BetweenWaves) && State.IndexAt(HeadXZ)<0)
            {
                Panel.SetBody("Step back onto the board, then resume.");
                return;
            }
            BoardView.gameObject.SetActive(pausedBoardVisible);
            if (manual)
            {
                menuPauseSeconds += Time.unscaledTimeAsDouble-menuPausedAt.Value;
                menuPausedAt=null;
                // The clock stood still during the pause, so a step taken then would skip the timed move.
                if (pausedStage==GameStage.Countdown && State.IndexAt(HeadXZ)!=pausedPane)
                {
                    State.Pause();
                    BoardView.ClearTransient();
                    restorePausedPage=null;
                    Ready("You moved while paused. Resume with a fresh countdown.");
                    return;
                }
                if (pausedStage==GameStage.CountIn || pausedStage==GameStage.Countdown || pausedStage==GameStage.BetweenWaves)
                {
                    Stage=pausedStage;
                    Panel.Hide();
                    if (Stage==GameStage.CountIn) Panel.ShowStartCount(countInNumber);
                    return;
                }
                if (restorePausedPage!=null)
                {
                    Stage=pausedStage;
                    restorePausedPage();
                    restorePausedPage=null;
                    return;
                }
            }
            if (pausedStage==GameStage.Fall)
            {
                Stage=GameStage.Fall;
                Panel.Hide();
                Ending.SetTrackingPaused(false);
                skipPinchHeld=HasSkipPinch();
                return;
            }
            if(levelFinished) AdvanceLevel();
            Ready("Stand on a supported pane. Resume with a fresh countdown.");
        }
        void BeginFall(bool developmentPreview=false)
        {
            Stage=GameStage.Fall;
            var board=BoardView.Board;
            Vector2 hit=PlayerTileTracker.Project(board,Panel.Head.position);
            // Natural losses already exposed the actual broken panes. A leaning eye may
            // project across a seam onto surviving glass, which must stay in the arena.
            if (State.LastLoss==WaveLossReason.None)
            {
                int cell=State.IndexAt(hit);
                if (cell < 0)
                {
                    float nearest=float.PositiveInfinity;
                    for (int i=0; i<State.Count; ++i)
                    {
                        if (!State.Layout.Contains(i)) continue;
                        float distance=(State.Center(i)-hit).sqrMagnitude;
                        if (distance < nearest) { nearest=distance; cell=i; }
                    }
                    hit=State.Center(cell);
                }
                BoardView.RevealLossAperture(cell);
            }
            Ending.SetOpening(board.TransformPoint(GlassGeometry.Ground(hit)),State.Pitch,board.eulerAngles.y);
            Panel.Hide(); Ending.Begin(Panel.Head,reduced,ShowResults,developmentPreview);
            skipPinchHeld = HasSkipPinch();
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
                if(Ending && Panel) Ending.ShowOpenPanes(Panel.Head,Stage==GameStage.Development);
                if (CanHearOpening && sound)
                {
                    if (Tracked && BoardView)
                        sound.UpdateHoleDraft(BoardView.TryGetViewedOpening(Panel.Head, out var point) ? point : (Vector3?)null);
                    else sound.ClearHoleDraft();
                }
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
            BoardView.gameObject.SetActive(false); Panel.Recenter();
            ResultsPage();
            if (!Ending.LastWasSkipped) narration.Speak(Stage,Panel.Head);
        }
        void ResultsPage()
        {
            Stage=GameStage.Results;
            string score = State.WavesSurvived == 1 ? "1 wave survived" : $"{State.WavesSurvived} waves survived";
            string result = State.LastLoss switch
            {
                WaveLossReason.GapGraceExpired => "You stayed over an open hole too long to reach the glass.",
                WaveLossReason.GapAtZero => "You were over an open hole at zero.",
                WaveLossReason.MovingPane => "At zero, the moving glass under the ring shattered.",
                WaveLossReason.MemoryPane => "At zero, the ring was on a pane that flashed during the Memory reveal.",
                WaveLossReason.CrackedPane => "At zero, the glass under the ring shattered after flashing amber.",
                _ => "Ending preview"
            };
            Panel.Show("Run complete",$"Level {Level}   ·   {score}   ·   {Ending.LastEndingName}\n{result}\n{OfflineNarration.ResultsCaption}",
                Choice($"Retry Level {Level}",()=>RestartAt(Level, developmentRun)),
                Choice("Choose level",OpenLevelPicker),Choice("Main menu",ReturnToMainMenu));
        }
        void RestartAt(int level, bool fromDevelopment=false)
        {
            if (level < 1 || (!fromDevelopment && level > HighestLevel)) return;
            SelectedSeed=Environment.TickCount&int.MaxValue;
            if (!ResetRun()) return;
            Level=level;
            developmentRun=fromDevelopment;
            Ready("The glass is back. Return to the board and start when ready.");
        }
        void ReturnToMainMenu()
        {
            if (ResetRun()) Home();
        }
        bool ResetRun()
        {
            Ending.ResetSequence(); sound.ResetSounds();
            if (StartBoard()) return true;
            RoomChanged();
            return false;
        }
        void Development()
        {
            if(!Debug.isDebugBuild&&!Application.isEditor) return;
            Room.ShowClearance(false);
            Stage=GameStage.Development;
            Panel.Show("Development","Seed "+SelectedSeed+"   ·   selected ending "+(endingIndex<0?"Shuffle":EndingNames[endingIndex]),
                Choice("Seed 379 · level 1",()=>DevelopmentRun(1)),Choice("Seed 379 · level 3",()=>DevelopmentRun(3)),
                Choice("Next ending",()=>{endingIndex=(endingIndex+1)%EndingNames.Length; Ending.SetDevelopmentEnding(endingIndex); Development();}),
                Choice("Preview loss",()=>BeginFall(true)),Choice("Later levels",DevelopmentLevels),Choice("Back",Home));
        }
        void DevelopmentLevels()
        {
            Stage=GameStage.Development;
            Panel.Show("Later levels","Try a wave on your fitted board.",
                Choice("Level 5",()=>DevelopmentRun(5)),Choice("Level 7",()=>DevelopmentRun(7)),
                Choice("Level 12",()=>DevelopmentRun(12)),Choice("Back",Development));
        }
        void DevelopmentRun(int level)
        {
            SelectedSeed=379; if (!StartBoard()) return;
            developmentRun=true;
            Level=level; Ready("Fixed seed 379. Stand on an intact pane to reproduce this run.");
        }
        void OnApplicationPause(bool pause) { appPaused=pause; if(pause) InterruptRound(); }
        // In the Editor, clicking into the Meta XR Simulator window takes focus; that is not the player leaving.
        void OnApplicationFocus(bool focus) { if(!focus && !Application.isEditor) InterruptRound(); }
        void InterruptRound()
        {
            if (Stage==GameStage.Paused && pausedStage==GameStage.CountIn && menuPausedAt.HasValue)
            {
                menuPauseSeconds += Time.unscaledTimeAsDouble - menuPausedAt.Value;
                menuPausedAt = null;
            }
            if(Stage==GameStage.CountIn||Stage==GameStage.Countdown||Stage==GameStage.BetweenWaves||Stage==GameStage.Fall||
                Stage==GameStage.Ready) PauseRound();
        }
    }
}
