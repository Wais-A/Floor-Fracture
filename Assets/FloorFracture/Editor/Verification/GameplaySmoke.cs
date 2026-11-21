using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.XR.BuildingBlocks;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    [InitializeOnLoad]
    public static partial class GameplaySmoke
    {
        private const string Key = "FloorFracture.GameplaySmoke.";
        private const string ScenePath = "Assets/FloorFracture/Scenes/FloorFracture.unity";
        private static readonly string[] Preferences = { "ff.reduced", "ff.gentle", "ff.volume", "ff.narration", "ff.entireFloor" };
        private static readonly Stack<IEnumerator> Steps = new Stack<IEnumerator>();
        private static GameFlow game;
        private static Pose head, left, right;
        private static SmokeReport report;
        private static string pendingException;
        private static string Folder => SessionState.GetString(Key + "folder", string.Empty);
        private static double Now => Time.unscaledTimeAsDouble;

        static GameplaySmoke()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += ModeChanged;
            Application.logMessageReceived += ObserveLog;
        }

        [MenuItem("Tools/Floor Fracture/Verify gameplay in Play Mode")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetInt(Key + "phase", 0) != 0)
                throw new InvalidOperationException("Start the gameplay check from Edit Mode after the previous run finishes.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (!scene) throw new FileNotFoundException("Prepare the actual gameplay scene first.", ScenePath);

            string folder = Path.GetFullPath("Builds/Verification/PlayMode-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            SessionState.SetString(Key + "folder", folder);
            var prior = EditorSceneManager.playModeStartScene;
            SessionState.SetString(Key + "initialScene", prior ? AssetDatabase.GetAssetPath(prior) : "");
            foreach (string name in Preferences)
            {
                SessionState.SetBool(Key + name + ".exists", PlayerPrefs.HasKey(name));
                SessionState.SetInt(Key + name + ".value", PlayerPrefs.GetInt(name));
            }
            PlayerPrefs.DeleteKey("ff.volume");
            PlayerPrefs.DeleteKey("ff.narration");
            PlayerPrefs.DeleteKey("ff.entireFloor");
            report = new SmokeReport { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, scene = ScenePath };
            report.depthProviderReadyInAuthoredScene = AuthoredDepthReady();
            Save();
            Steps.Clear();
            pendingException = null;
            SessionState.SetInt(Key + "phase", 1);
            EditorSceneManager.playModeStartScene = scene;
            Debug.Log("[Floor Fracture verification] Starting Play Mode. Report folder: " + folder);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            int phase = SessionState.GetInt(Key + "phase", 0);
            if (phase == 2 && !EditorApplication.isPlayingOrWillChangePlaymode) { RestoreEditor(); return; }
            if (phase != 1 || !EditorApplication.isPlaying) return;
            try
            {
                if (report == null) report = JsonUtility.FromJson<SmokeReport>(File.ReadAllText(Path.Combine(Folder, "report.json")));
                if (pendingException != null) throw new InvalidOperationException(pendingException);
                if (Steps.Count == 0) Steps.Push(Scenario());
                while (Steps.Count > 0)
                {
                    var step = Steps.Peek();
                    if (!step.MoveNext()) { Steps.Pop(); continue; }
                    if (step.Current is IEnumerator nested) { Steps.Push(nested); continue; }
                    return;
                }
                Complete(true, string.Empty);
            }
            catch (Exception e) { Complete(false, e.ToString()); }
        }

        private static void ModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetInt(Key + "phase", 0) == 1)
                Complete(false, "Play Mode exited before the scenario finished.");
        }

        private static void ObserveLog(string message, string stack, LogType kind)
        {
            if (kind != LogType.Exception && kind != LogType.Assert && kind != LogType.Error) return;
            if (!EditorApplication.isPlaying || SessionState.GetInt(Key + "phase", 0) != 1) return;
            if (stack.Contains("FloorFracture.") || stack.Contains("FallSequence.") || stack.Contains("FallScenery."))
                pendingException = message + Environment.NewLine + stack;
        }

        private static void Complete(bool passed, string failure)
        {
            SessionState.SetInt(Key + "phase", 2);
            SessionState.SetBool(Key + "passed", passed);
            if (report == null) report = JsonUtility.FromJson<SmokeReport>(File.ReadAllText(Path.Combine(Folder, "report.json")));
            report.result = passed ? "passed" : "failed";
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.failure = failure;
            Save();
            Steps.Clear();
            Debug.Log("[Floor Fracture verification] " + report.result + " at " + Path.Combine(Folder, "report.json") +
                (passed ? string.Empty : Environment.NewLine + failure));
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        private static void RestoreEditor()
        {
            var prior = SessionState.GetString(Key + "initialScene", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(prior) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(prior);
            foreach (string name in Preferences)
            {
                if (SessionState.GetBool(Key + name + ".exists", false)) PlayerPrefs.SetInt(name, SessionState.GetInt(Key + name + ".value", 0));
                else PlayerPrefs.DeleteKey(name);
            }
            RestoreTrapVerification();
            PlayerPrefs.Save();
            SessionState.SetInt(Key + "phase", 0);
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + "passed", false) ? 0 : 1);
        }

        private static IEnumerable<int> PlayablePanes => Enumerable.Range(0, game.State.Count).Where(game.State.Layout.Contains);
        private static int MiddlePane => PlayablePanes.OrderBy(i => game.State.Center(i).sqrMagnitude).First();
        private static int CornerPane => PlayablePanes.First();
        private static int AdjacentPane(int i) => PlayablePanes.First(j => game.State.Neighbors(i, j));

        private static IEnumerator Scenario()
        {
            yield return Wait(() => (game = Object.FindAnyObjectByType<GameFlow>()) && game.Panel && game.Room, "game initialization", 30);
            InitializeTrapVerification();
            Require(game.gameObject.scene.path == ScenePath, "The running GameFlow belongs to a different scene.");
            Require(!XRSettings.isDeviceActive, "An XR display is active. Run the desktop fixture with the headset disconnected from Link.");
            VerifyInstalledBlocks();
            Require(new SerializedObject(game.Room).FindProperty("useEditorFixture").boolValue,
                "Enable the Editor MRUK room fixture before running this check.");
            SetHead(new Pose(new Vector3(0f,1.6f,0f), Quaternion.identity));
            yield return Wait(() => game.Room.Status != RoomPlacementStatus.Loading, "MRUK fixture discovery", 55);
            Require(game.Stage == GameStage.Welcome && game.Room.Status == RoomPlacementStatus.Confirmed && game.State != null,
                "The verified fixture fit did not go directly to Welcome: " + game.Room.Message);
            var placement = game.Room.Placement;
            report.columns = placement.Columns; report.rows = placement.Rows;
            report.pitch = placement.Pitch;
            report.sdkFloorCandidates = game.Room.ScannedSpawnCount;
            Require(report.sdkFloorCandidates > 0, "The roomy MRUK fixture did not produce a candidate center through the installed Find Spawn Positions block.");
            Require(game.State.Width == placement.Columns && game.State.Height == placement.Rows && Mathf.Abs(game.State.Pitch - placement.Pitch) < 0.001f &&
                Mathf.Abs(Vector2.Distance(game.State.Center(0), game.State.Center(1)) - placement.Pitch) < 0.001f,
                "The playing board did not use the fitted dimensions and pane width.");
            var outlines = game.Room.transform.Find("Placement outlines");
            Require(outlines && outlines.gameObject.activeInHierarchy && outlines.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                "The Welcome screen did not show the fitted board's perimeter.");
            Require(placement.Layout.PaneCount<=16 && placement.Pitch>=.60f && placement.Pitch<=.65f,"The recorded living room did not get the default larger panes.");
            VerifyMenuAndSpatialAudio();
            yield return VerifyHandRecoveryMenu();
            VerifyOccupancyRingMesh();
            VerifySpawnMarkersCleared();
            Record($"The recorded MRUK living room supplied its floor, furniture and global mesh. The {placement.Columns} × {placement.Rows} board at {placement.Pitch:F3} m pitch appeared at Welcome without a clearance approval prompt.");
            Capture("room-preview", SmokeView.BoardObserver);
            game.Room.TogglePlacementDebug();
            var diagnostics = game.Room.transform.Find("Placement diagnostics");
            var colors = diagnostics.GetComponent<MeshFilter>().sharedMesh.colors32;
            Require(diagnostics.gameObject.activeInHierarchy &&
                colors.Any(c => c.g == 255 && c.r == 64) &&
                colors.Any(c => c.r == 255 && c.g == 72) &&
                colors.Any(c => c.r == 255 && c.g == 156),
                "Placement diagnostics must show accepted panes, detected furniture and mesh obstacles. Observed colors: " + string.Join(", ", colors.Distinct()));
            Capture("floor-placement-debug", SmokeView.RoomOverview, diagnostics.GetComponent<Renderer>().bounds);
            game.Room.TogglePlacementDebug();
            Require(!diagnostics.gameObject.activeSelf, "Placement diagnostics must disappear when switched off.");
            Record("The recorded room displayed accepted panes, furniture and mesh obstacles in their diagnostic colors. Switching it off restored the normal view.");
            VerifyGlassAndPassthrough();
            yield return VerifyStartCount();
            yield return VerifyMenuRecenter();
            yield return VerifyMenuPauseAndQuit();
            yield return VerifyTrapRotation();
            yield return VerifyTileCracks();
            yield return VerifyStagedPressure();
            yield return RayChoose(3, false); yield return Stage(GameStage.Settings);
            Require(!outlines.gameObject.activeSelf, "The Welcome board perimeter stayed visible inside Settings.");
            yield return RayChoose(5, true); yield return Stage(GameStage.Welcome);
            Record("The right simulated ray opened Settings and the left ray selected Back through independent trigger presses.");
            Choose("Settings"); yield return Stage(GameStage.Settings);
            int oldMotion = PlayerPrefs.GetInt("ff.reduced",0);
            Choose("Fall motion"); Require(PlayerPrefs.GetInt("ff.reduced") != oldMotion, "Fall setting did not persist.");
            if (PlayerPrefs.GetInt("ff.reduced") != 0) Choose("Fall motion");
            yield return VerifySettingsPreviewAndVolume();
            Choose("Development"); Choose("Seed 379 · level 1"); yield return Stage(GameStage.Ready);
            Choose("Main menu"); yield return Stage(GameStage.Welcome);
            Require(game.State.Seed == 379, "Development run did not preserve the reproducible seed.");
            report.seed = game.State.Seed;
            Record("Welcome and Settings routed through their visible menu choices. The development menu selected seed 379.");

            int first = MiddlePane;
            yield return VerifyTutorial(first);
            Choose("Start wave"); yield return Stage(GameStage.Countdown);
            VerifyBoardLock();

            bool sawLastTile = false;
            int initialCount = game.State.Layout.PaneCount;
            for (int index = 0; index < initialCount; index++)
            {
                var plan = game.State.ActiveWave;
                Require(plan != null && plan.StartingTile != plan.GuaranteedTile, "Wave did not require a move.");
                if (index == 0)
                {
                    Require(plan.Mode == WaveMode.Classic, "First wave was not Classic.");
                    yield return Frames(2); Capture("classic-glass", SmokeView.BoardObserver);
                    LookAtTile(plan.StartingTile);
                    yield return Frames(2); Capture("classic-player-view", SmokeView.CenterEye);
                }
                Require(plan.Mode!=WaveMode.Memory && Math.Abs(plan.Duration - (2.0 - .08 * Math.Min(plan.Number - 1, 3))) < .00001,
                    "Level one must shorten successive switches while keeping its regular reveal.");
                var marked = game.BoardView.Board.Find($"Glass {plan.StartingTile % game.State.Width}, {plan.StartingTile / game.State.Width}");
                var warningRim = marked.Find("Rim").GetComponent<MeshFilter>();
                var warningCracks = marked.Find("Fissures").GetComponent<MeshRenderer>();
                yield return Frames(1);
                Require(warningRim.sharedMesh == GlassGeometry.Rim && warningCracks.enabled,"The reveal did not mark the pane that will break.");
                if (plan.Mode == WaveMode.LastTile) sawLastTile = true;
                int score = game.State.WavesSurvived;
                StandOn(plan.GuaranteedTile);
                yield return Frames(2);
                Require(game.State.CurrentSample.Index == plan.GuaranteedTile && game.GameplayTime < plan.Deadline && game.State.WavesSurvived == score,
                    "The early step did not register while the countdown remained live.");
                bool recallSeen = false;
                while (game.Stage == GameStage.Countdown)
                {
                    if (game.GameplayTime < plan.Deadline)
                    {
                        Require(game.State.IsIntact(plan.StartingTile) && game.State.WavesSurvived == score, "A break or score occurred before zero.");
                        if (!recallSeen && game.GameplayTime > plan.RevealEndsAt + .05 && !plan.ShowsWarnings(game.GameplayTime))
                        {
                            Require(warningRim.sharedMesh == GlassGeometry.SafeRim && !warningCracks.enabled,"Recall must completely clear the earlier warning.");
                            recallSeen = true;
                        }
                    }
                    Require(game.GameplayTime < plan.Deadline + 2, "The scheduled deadline did not resolve.");
                    yield return null;
                }
                Require(game.Stage == GameStage.BetweenWaves && game.GameplayTime >= plan.Deadline && game.State.BreaksApplied &&
                    game.State.WavesSurvived == score + 1 && !game.State.IsIntact(plan.StartingTile), "The survival result disagreed with the saved deadline or support.");
                Require(recallSeen, "The wave did not clear its warning before zero.");
                RecordWave(plan, true);
                if (index == 0)
                {
                    yield return Frames(1); Capture("neighboring-hole", SmokeView.BoardObserver);
                    LookAtTile(plan.StartingTile);
                    yield return Wait(() => game.GameplayTime >= plan.Deadline + 0.55, "shards falling below the floor", 3);
                    var destination = GameObject.Find("Fall chamber").transform.Find("Hazard surface").GetComponent<MeshRenderer>();
                    var selected = new MaterialPropertyBlock();
                    destination.GetPropertyBlock(selected);
                    Require(destination.enabled && !game.Ending.IsPlaying, "The first safe step did not reveal a landing.");
                    Require(Mathf.RoundToInt(selected.GetFloat("_Theme"))==game.Ending.CurrentLevelEnding,
                        "A surviving wave revealed the wrong destination.");
                    Capture("neighboring-hole-player-view", SmokeView.CenterEye);
                }
                if (index == 0 || plan.Mode == WaveMode.LastTile)
                    yield return VerifyExitDuringWaveResult(plan);
                VerifyBoardLock();
                yield return Wait(() => game.Stage != GameStage.BetweenWaves, "next wave or level", 8);
                if (game.Stage == GameStage.Ready) break;
                Require(game.Stage == GameStage.Countdown, "A safe stationary route did not advance the level.");
            }
            Require(game.Stage == GameStage.Ready && game.Level == 2 && sawLastTile, "Last Tile did not rebuild the confirmed board.");
            Record("Walked each guaranteed stationary step through Last Tile. Level 2 rebuilt and waited for readiness.");

            Choose("Main menu"); Choose("Development"); Choose("Seed 379 · level 1");
            StandOn(first); Choose("Start wave"); yield return Stage(GameStage.Countdown);
            var losing = game.State.ActiveWave;
            report.previewBeforeNaturalLoss=EndingNames[game.Ending.CurrentLevelEnding];
            Require(losing.Mode == WaveMode.Classic && game.State.WavesSurvived == 0, "Fresh run did not start with Classic and a zero score.");
            while (game.Stage == GameStage.Countdown)
            {
                if (game.GameplayTime < losing.Deadline) Require(game.State.IsIntact(losing.StartingTile), "The loss tile vanished before zero.");
                Require(game.GameplayTime < losing.Deadline + 2, "Standing still did not produce a deadline result.");
                yield return null;
            }
            Require(game.Stage == GameStage.Fall && game.GameplayTime >= losing.Deadline && game.State.BreaksApplied &&
                game.State.WavesSurvived == 0 && !game.State.IsIntact(losing.StartingTile), "Standing still produced an incorrect Classic deadline loss.");
            RecordWave(losing, false);
            Require(game.Ending.LastEndingName == report.previewBeforeNaturalLoss,
                "A natural fall used a different trap than the wave that broke.");
            game.Ending.Skip();
            yield return Stage(GameStage.Results);
            string reason=game.Panel.transform.Find("Floor Fracture Menu/Instructions").GetComponent<UnityEngine.UI.Text>().text;
            Require(reason.Contains("glass under the ring shattered"),"Results did not identify the natural deadline loss.");
            Choose("Restart");
            yield return Stage(GameStage.Ready);
            Require(game.State.WavesSurvived==0 && game.State.ActiveWave==null &&
                PlayablePanes.All(game.State.IsIntact),"Restart did not restore playable glass.");
            Record("A natural deadline loss used the level's landing. Skip reached Results and Restart restored the board.");

            yield return VerifyEveryEndingExit();
            yield return ObserveSelectedFall("Water", false, 1);
            yield return VerifyOutsideBoardPreview();
            yield return ObserveSelectedFall("Lava", false, 1);
            yield return ObserveSelectedFall("Water", true, 5);
            yield return LevelThree();

            VerifyBoardLock();
            yield return VerifyLockedRoomRecovery();
            game.Room.Retry();
            yield return Wait(() => game.Stage==GameStage.Welcome && game.Room.Status==RoomPlacementStatus.Confirmed,
                "explicit refit after locked play",55);

            yield return VerifyRoomRecovery(MRUK.LoadDeviceResult.NoScenePermission,
                RoomPlacementStatus.PermissionDenied, "App permissions", "Try again");
            yield return VerifyRoomRecovery(MRUK.LoadDeviceResult.NoRoomsFound,
                RoomPlacementStatus.NoRoom, "Space Setup", "Check again");
            yield return VerifyEmptyRoomOnWake();
            yield return VerifyLateFloorBoundary();
            yield return VerifyRoomChangeDuringSurvey();
            yield return VerifyScanUpdateAfterRejectedFit();
            // Both checks use the optional stone. Run them after the first-time lesson.
            yield return VerifyOpeningDraft();
            yield return VerifyOfflineNarration();
            Choose("Play area");
            Choose("Board size");
            yield return Wait(()=>game.Stage==GameStage.Welcome && game.Room.EntireFloor,"full-floor refit",55);
            int fullCount=game.State.Layout.PaneCount;
            Require(fullCount>=placement.Layout.PaneCount && PlayerPrefs.GetInt("ff.entireFloor")==1,
                "Full floor lost the enabled setting or produced fewer playable panes on the same scan.");
            Choose("Play area");
            Choose("Board size");
            yield return Wait(()=>game.Stage==GameStage.Welcome && !game.Room.EntireFloor,"default board refit",55);
            Require(game.State.Layout.PaneCount<=16,"Returning to the default board did not reapply the cap.");
            Record($"Play area refit the saved room in both modes: {game.State.Layout.PaneCount} default panes and {fullCount} full-floor panes.");
        }

        private static void VerifyBoardLock()
        {
            var scannedRoom=MRUK.Instance.Rooms.Single();
            var view=game.BoardView;
            var state=game.State;
            var fit=game.Room.Placement;
            var stage=game.Stage;
            var wave=state.ActiveWave;
            var score=state.WavesSurvived;
            scannedRoom.AnchorUpdatedEvent.Invoke(scannedRoom.FloorAnchors.First());
            scannedRoom.AnchorUpdatedEvent.Invoke(scannedRoom.GlobalMeshAnchor);
            MRUK.Instance.RoomUpdatedEvent.Invoke(scannedRoom);
            Require(game.Room.Status==RoomPlacementStatus.Confirmed && game.Stage==stage &&
                game.BoardView==view && game.State==state && state.ActiveWave==wave && state.WavesSurvived==score &&
                game.Room.Placement.Equals(fit),"A scene update replaced the session board or interrupted its wave.");
        }

        private static IEnumerator VerifyLockedRoomRecovery()
        {
            // Only withdraw the recorded room from MRUK's localized room list.
            // Its anchors and the game-owned board remain alive throughout.
            // This test interrupts a live wave. A smaller board can already have
            // cleared level three during the preceding moving-pane test.
            // A jump opens its gap on the first frame. Tracking loss should restore
            // the board from before that wave, including those provisional panes.
            var panes=PlayablePanes.Where(game.State.IsIntact).ToArray();
            yield return Wait(() => game.Stage==GameStage.Ready || game.Stage==GameStage.Countdown,
                "the next playable wave after the moving-pane check", 8);
            if (game.Stage==GameStage.Ready)
            {
                panes=PlayablePanes.Where(game.State.IsIntact).ToArray();
                StandOn(MiddlePane);
                Choose("Start wave");
                yield return Stage(GameStage.Countdown);
            }
            var rooms=MRUK.Instance.Rooms;
            var original=rooms.Single();
            var board=game.BoardView;
            var state=game.State;
            var fit=game.Room.Placement;
            int score=state.WavesSurvived;
            rooms.Remove(original);
            try
            {
                yield return Stage(GameStage.Paused);
                Choose("Show board");
                Require(game.Stage==GameStage.Paused && !board.gameObject.activeSelf,"An unlocalized board resumed.");
            }
            finally { rooms.Add(original); }
            yield return Frames(2);
            Require(game.Stage==GameStage.Paused,"Tracking recovery resumed play without the player's tap.");
            Choose("Show board");
            Require(game.Stage==GameStage.Ready && board==game.BoardView && state==game.State &&
                fit.Equals(game.Room.Placement) && score==state.WavesSurvived &&
                panes.SequenceEqual(PlayablePanes.Where(state.IsIntact)),
                $"Tracking recovery reached {game.Stage} with {state.WavesSurvived}/{score} waves and " +
                $"{PlayablePanes.Count(state.IsIntact)}/{panes.Length} committed panes.");
            Record("Room updates during play kept the board and wave. Losing and restoring the tracked room required Show board and preserved surviving glass and score.");
        }

        private static IEnumerator VerifyRoomChangeDuringSurvey()
        {
            bool interrupted = false;
            void OnSurveyStarted()
            {
                if (game.Room.Status != RoomPlacementStatus.Loading ||
                    !game.Room.Message.StartsWith("Floor found.", StringComparison.Ordinal)) return;
                game.Room.Changed -= OnSurveyStarted;
                System.Threading.SynchronizationContext.Current.Post(_ =>
                {
                    var room = MRUK.Instance.Rooms.Single();
                    room.AnchorUpdatedEvent.Invoke(room.FloorAnchors[0]);
                    interrupted = true;
                }, null);
            }
            game.Room.Changed += OnSurveyStarted;
            try
            {
                game.Room.Retry();
                yield return Wait(() => interrupted && game.Room.Status == RoomPlacementStatus.Confirmed &&
                    game.Stage == GameStage.Welcome, "automatic recovery after an in-flight anchor update", 55);
            }
            finally { game.Room.Changed -= OnSurveyStarted; }
            Record("An MRUK anchor update interrupted the running fit. Its result was discarded and the updated room reached Welcome automatically.");
        }

        private static IEnumerator VerifyScanUpdateAfterRejectedFit()
        {
            MRUKAnchor floor = null;
            Vector2[] fullBoundary = null;
            int surveys = 0;
            void SupplySmallFloor()
            {
                floor = MRUK.Instance.Rooms.Single().FloorAnchors.Single();
                fullBoundary = floor.PlaneBoundary2D.ToArray();
                var center = fullBoundary.Aggregate(Vector2.zero, (sum, point) => sum + point) / fullBoundary.Length;
                const float half = .35f;
                floor.PlaneBoundary2D.Clear();
                floor.PlaneBoundary2D.AddRange(new[] { center + new Vector2(-half, -half), center + new Vector2(half, -half),
                    center + new Vector2(half, half), center + new Vector2(-half, half) });
            }
            void CountFits()
            {
                if (game.Room.Status == RoomPlacementStatus.Loading && game.Room.Message.StartsWith("Floor found.", StringComparison.Ordinal))
                    surveys++;
            }
            MRUK.Instance.SceneLoadedEvent.AddListener(SupplySmallFloor);
            try
            {
                game.Room.Retry();
                yield return Wait(() => game.Room.Status == RoomPlacementStatus.NoFit, "small observed floor rejected", 30);
                Require(!game.BoardView.gameObject.activeSelf, "A 70 cm floor exposed playable glass.");
                game.Room.Changed += CountFits;
                var room = MRUK.Instance.Rooms.Single();
                room.AnchorUpdatedEvent.Invoke(floor);
                yield return Wait(() => surveys == 1 && game.Room.Status == RoomPlacementStatus.NoFit, "unchanged scan rejected after an update", 30);
                Require(!game.BoardView.gameObject.activeSelf, "An update with insufficient space exposed the board.");

                floor.PlaneBoundary2D.Clear();
                floor.PlaneBoundary2D.AddRange(fullBoundary);
                room.AnchorUpdatedEvent.Invoke(floor);
                room.AnchorUpdatedEvent.Invoke(room.GlobalMeshAnchor);
                MRUK.Instance.RoomUpdatedEvent.Invoke(room);
                yield return Wait(() => game.Stage == GameStage.Welcome && game.Room.Status == RoomPlacementStatus.Confirmed,
                    "newly supported floor recovered after rejection", 55);
                Require(surveys == 2, "The floor, mesh and room notifications started more than one fit for the same update.");
            }
            finally
            {
                MRUK.Instance.SceneLoadedEvent.RemoveListener(SupplySmallFloor);
                game.Room.Changed -= CountFits;
                if (floor && fullBoundary != null)
                {
                    floor.PlaneBoundary2D.Clear();
                    floor.PlaneBoundary2D.AddRange(fullBoundary);
                }
            }
            Record("An updated 70 cm scan still withheld glass. Publishing the full saved floor then reached Welcome automatically. The floor, mesh and room notifications produced one fit.");
        }

        private static IEnumerator VerifyLateFloorBoundary()
        {
            MRUKAnchor anchor = null;
            Vector2[] boundary = null;
            void WithholdFloor()
            {
                anchor = MRUK.Instance.Rooms.Single().FloorAnchors.Single();
                boundary = anchor.PlaneBoundary2D.ToArray();
                anchor.PlaneBoundary2D.Clear();
            }
            MRUK.Instance.SceneLoadedEvent.AddListener(WithholdFloor);
            try
            {
                game.Room.Retry();
                yield return Wait(() => game.Room.Status == RoomPlacementStatus.UnsafeRoom, "missing floor recovery menu", 30);
                Require(anchor && boundary.Length >= 3 && !game.BoardView.gameObject.activeSelf &&
                    game.Panel.Visible, "Incomplete floor geometry exposed the game board.");
            }
            finally
            {
                MRUK.Instance.SceneLoadedEvent.RemoveListener(WithholdFloor);
                if (anchor && boundary != null) anchor.PlaneBoundary2D.AddRange(boundary);
            }
            yield return Wait(() => game.Room.Status == RoomPlacementStatus.Confirmed && game.Stage == GameStage.Welcome,
                "automatic fit after the saved boundary arrived", 55);
            Record("Withheld the real MRUK fixture's floor outline beyond the localization timeout. The game kept the board hidden, then fitted the restored scan and opened Welcome without a rescan or button press.");
        }

        private static void InjectUnavailableRoom(MRUK.LoadDeviceResult result)
        {
            // The Editor fixture supplies room data. Inject the same result the SDK returns on device.
            var receiveResult = typeof(RoomBoardPlacement).GetMethod("AcceptLoad",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(receiveResult != null && !(bool)receiveResult.Invoke(game.Room, new object[] { result }),
                "The room loader accepted an unavailable scan.");
        }

        private static IEnumerator VerifyEmptyRoomOnWake()
        {
            foreach (var signal in new[] { nameof(MRUK.RoomCreatedEvent), nameof(OVRManager.HMDMounted), nameof(OVRManager.TrackingAcquired) })
            {
                InjectUnavailableRoom(MRUK.LoadDeviceResult.NoRoomsFound);
                Require(game.Room.Status == RoomPlacementStatus.NoRoom && !game.BoardView.gameObject.activeSelf,
                    "An empty scan exposed playable glass before " + signal);
                if (signal == nameof(MRUK.RoomCreatedEvent))
                    MRUK.Instance.RoomCreatedEvent.Invoke(MRUK.Instance.Rooms.Single());
                else
                {
                    // Exercise only this component's subscription on the native event.
                    var field = typeof(OVRManager).GetField(signal,
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    var listeners = (field?.GetValue(null) as Action)?.GetInvocationList();
                    var onWake = listeners?.OfType<Action>().SingleOrDefault(callback => ReferenceEquals(callback.Target, game.Room));
                    Require(onWake != null, "Room recovery is missing its subscription to " + signal);
                    onWake();
                }
                yield return Wait(() => game.Room.Status == RoomPlacementStatus.Confirmed && game.Stage == GameStage.Welcome,
                    "automatic fit after " + signal, 55);
                VerifyFloorVisibility(false);
            }
            Record("After an empty room query, a late MRUK RoomCreated event, putting on the headset, and positional tracking acquisition each restored Welcome independently. No menu press or rescan was used.");
        }

        private static void VerifyStableRecoveryTargets()
        {
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName);
            var setStatus = typeof(RoomBoardPlacement).GetMethod("SetState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3[] firstPositions = null;
            var statuses = new[] { RoomPlacementStatus.NoRoom, RoomPlacementStatus.UnsafeRoom, RoomPlacementStatus.NoFit };
            foreach (var status in statuses)
            {
                var message = status == RoomPlacementStatus.UnsafeRoom
                    ? "The floor in your scan is at a different height from the headset's floor level. Check the floor height in Space Setup, then return here."
                    : "Looking for your saved floor.";
                setStatus.Invoke(game.Room, new object[] { status, message });
                Canvas.ForceUpdateCanvases();
                var buttons = canvas.GetComponentsInChildren<UnityEngine.UI.Button>();
                Require(buttons.Select(b => b.GetComponentInChildren<UnityEngine.UI.Text>().text)
                    .SequenceEqual(new[] { "Check again", "Space Setup", "Show / hide placement" }), "Tracking status changed which recovery action was under the hand.");
                var positions = buttons.Select(b => (RectTransform)b.transform).Select(r => r.TransformPoint(r.rect.center)).ToArray();
                if (firstPositions == null) firstPositions = positions;
                for (int i = 0; i < positions.Length; i++)
                    Require(Vector3.Distance(firstPositions[i], positions[i]) < .0005f,
                        "A change in tracking message length moved the recovery buttons.");
            }
            Choose("Show / hide placement");
            Require(game.Room.PlacementDebug && game.Stage == GameStage.Room && !game.BoardView.gameObject.activeSelf,
                "Inspecting a rejected room must keep the glass hidden.");
            Choose("Show / hide placement");
            Require(!game.Room.PlacementDebug, "The recovery overlay could not be switched off.");
            Record("Check again and Space Setup kept the same order and world positions across missing room, unsafe floor and no fit, including a multiline change in the tracking message.");
        }

        private static IEnumerator VerifyRoomRecovery(MRUK.LoadDeviceResult result, RoomPlacementStatus status,
            string recoveryLabel, string retryLabel)
        {
            InjectUnavailableRoom(result);
            if (status == RoomPlacementStatus.NoRoom)
            {
                VerifyStableRecoveryTargets();
                InjectUnavailableRoom(result);
            }
            var labels = game.Panel.GetComponentsInChildren<UnityEngine.UI.Text>()
                .Where(label => label.isActiveAndEnabled).Select(label => label.text).ToArray();
            Require(game.Room.Status == status && game.Stage == GameStage.Room && !game.BoardView.gameObject.activeSelf &&
                labels.Contains(recoveryLabel) && labels.Contains(retryLabel) &&
                labels.Any(text => text == game.Room.Message || text.StartsWith(game.Room.Message + "\n", StringComparison.Ordinal)),
                "The room failure must hide the board and display its explanation and both recovery actions.");
            VerifyFloorVisibility(false);
            yield return Frames(2);
            Choose(retryLabel);
            yield return Wait(() => game.Stage == GameStage.Welcome && game.Room.Status == RoomPlacementStatus.Confirmed,
                "a valid room after " + result, 55);
            Require(game.BoardView.gameObject.activeSelf && game.State.ActiveWave == null,
                "Retry must restore the board and leave the next round idle.");
            VerifyFloorVisibility(false);
            Record("Injected MRUK " + result + ". The blocked board showed " + recoveryLabel +
                " and " + retryLabel + ". Retry loaded the fixture and restored Welcome.");
        }

        private static IEnumerator LevelThree()
        {
            Choose("Main menu"); Choose("Development"); Choose("Seed 379 · level 3");
            int startingTile = FindSliderRoute();
            StandOn(startingTile); Choose("Start wave"); yield return Stage(GameStage.Countdown);
            var wave = game.State.ActiveWave;
            var fog = game.BoardView.transform.Find("Low fog").GetComponent<MeshRenderer>();
            Require(wave.Level == 3 && wave.Fog, "The level-three fixture did not open with the planned fog wave.");
            LookAtTile(wave.GuaranteedTile); yield return Frames(2);
            Require(fog.enabled && fog.gameObject.activeInHierarchy, "The level-three fog renderer stayed off during the fog wave.");
            Capture("level3-fog-player-view", SmokeView.CenterEye);
            while (game.State.Slider == null)
            {
                Require(wave.Mode != WaveMode.LastTile, "The selected stationary path reached the finale before offering moving glass.");
                StandOn(wave.GuaranteedTile); yield return Stage(GameStage.BetweenWaves, 8);
                Require(!game.State.IsIntact(wave.StartingTile), "The planned route did not open the previous starting tile.");
                RecordWave(wave, true);
                yield return Stage(GameStage.Countdown, 4);
                wave = game.State.ActiveWave;
            }
            var moving = game.BoardView.transform.Find("Sliding pane");
            var ring = game.BoardView.transform.Find("Occupied square").GetComponent<MeshRenderer>();
            var canceled = wave;
            var canceledPane = game.State.Slider;
            yield return Frames(2);
            game.gameObject.SendMessage("OnApplicationPause", true);
            Require(game.Stage == GameStage.Paused && !game.BoardView.gameObject.activeInHierarchy &&
                game.State.Slider == null && game.State.ActiveWave == null &&
                !game.transform.Find("Menu and countdown").GetComponent<AudioSource>().isPlaying,
                "Pausing did not withdraw the current slider offer or stop its countdown cue.");
            game.gameObject.SendMessage("OnApplicationPause", false);
            var rooms=MRUK.Instance.Rooms;
            var localizedRoom=rooms.Single();
            rooms.Remove(localizedRoom);
            try
            {
                Require(game.Panel.Tracked && !game.Room.IsRoomLocalized, "The retry fixture needs a tracked headset in an unlocalized room.");
                Choose("Show board");
                Require(game.Stage == GameStage.Paused && !game.BoardView.gameObject.activeInHierarchy,
                    "Show board revealed the previous placement before the room was localized.");
                VerifyFloorVisibility(false);
            }
            finally { rooms.Add(localizedRoom); }
            Choose("Show board");
            Require(game.Room.IsRoomLocalized && game.Stage == GameStage.Ready && game.BoardView.gameObject.activeInHierarchy &&
                !moving.gameObject.activeSelf && !ring.enabled && !fog.enabled,
                "Showing the board exposed the canceled moving pane, ring, fog or countdown.");
            VerifyFloorVisibility(false);
            StandOn(canceled.StartingTile);
            Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            wave = game.State.ActiveWave;
            var slide = game.State.Slider;
            Require(wave != null && slide != null && !ReferenceEquals(canceledPane,slide) &&
                wave.Seed == canceled.Seed && wave.Deadline > canceled.Deadline &&
                wave.IsSafe(slide.Tile) && moving.gameObject.activeInHierarchy,
                "Retry must show a fresh safe moving pane from the same stationary start.");
            Record("Suspending a wave withdrew its moving pane. Show board waited for room localization and reopened the existing holes. Starting from the same glass restored the safe moving pane with a full countdown.");
            var ordinary = game.BoardView.transform.Find($"Glass {wave.GuaranteedTile%game.State.Width}, {wave.GuaranteedTile/game.State.Width}");
            int score = game.State.WavesSurvived;
            Require(!slide.Locked && !game.State.IsIntact(slide.Tile) && game.State.IsIntact(wave.GuaranteedTile),
                "The optional moving pane must occupy an existing hole with a permanent alternative.");
            yield return Frames(2);
            Require(moving.gameObject.activeInHierarchy &&
                moving.GetComponentsInChildren<MeshRenderer>(true).Length == ordinary.GetComponentsInChildren<MeshRenderer>(true).Length &&
                moving.localScale==ordinary.localScale && Mathf.Abs(moving.localPosition.y-ordinary.localPosition.y)<.0001f &&
                fog.enabled==wave.Fog, "The moving pane must match the ordinary glass height, scale and surfaces.");
            foreach (string part in new[]{"Smoky pane","Rim","Fissures"})
            {
                var movingPart=moving.Find(part);
                var fixedPart=ordinary.Find(part);
                Require(movingPart.GetComponent<MeshFilter>().sharedMesh==fixedPart.GetComponent<MeshFilter>().sharedMesh &&
                    movingPart.GetComponent<MeshRenderer>().sharedMaterial==fixedPart.GetComponent<MeshRenderer>().sharedMaterial,
                    "The two glass types use different "+part+" geometry or material.");
            }

            // The pane starts at the other end of its two-hole lane.
            StandAt(game.State.Center(slide.OtherHole));
            yield return Wait(() => game.State.CurrentSample.Kind == SupportKind.Gap, "projected head missed moving glass", 2);
            StandOn(wave.GuaranteedTile); yield return Frames(2);
            Require(game.Stage == GameStage.Countdown && !slide.Locked && game.State.CurrentSample.Index == wave.GuaranteedTile &&
                game.State.CurrentSample.Kind == SupportKind.Glass, "The deliberate miss did not recover to the stationary escape.");
            yield return Wait(() => game.GameplayTime > slide.MotionStartsAt + 0.10, "sliding pane to move", 3);
            Vector3 oldCenter = moving.localPosition;
            LookAtTile(slide.Tile); yield return Delay(0.2);
            Require((moving.localPosition - oldCenter).sqrMagnitude > 0.00005f, "The glass did not travel along the two-hole lane.");
            Capture("level3-moving-pane-player-view", SmokeView.CenterEye);
            StandAt(slide.CenterAt(game.GameplayTime)); yield return Frames(2);
            Require(slide.Locked && game.State.CurrentSample.IsSlider, "Landing on moving glass did not lock it.");
            Vector3 caught = moving.localPosition;
            Vector3 ringOffset = ring.transform.localPosition-caught;
            float ringEdge = GlassGeometry.RingOuterRadius*ring.transform.localScale.x;
            Require(ring.enabled && Mathf.Abs(ringOffset.x)+ringEdge<=slide.HalfSize &&
                Mathf.Abs(ringOffset.z)+ringEdge<=slide.HalfSize, "The occupied ring extends beyond the glass supporting the player.");
            LookAtTile(slide.Tile); yield return Delay(0.20);
            Require((moving.localPosition - caught).sqrMagnitude < 0.000001f, "The caught sliding pane kept moving.");
            Capture("level3-locked-pane-player-view", SmokeView.CenterEye);
            yield return Stage(GameStage.BetweenWaves, 8);
            Require(game.State.WavesSurvived == score + 1 && !game.State.IsIntact(slide.Tile) && game.State.Slider == slide && slide.Locked,
                "The deadline did not retain the caught pane over its original opening.");
            RecordWave(wave, true);
            yield return Stage(GameStage.Countdown, 4);
            var closing=game.State.ActiveWave;
            Require(closing.IsBreaking(slide.Tile),"The previously caught moving pane missed the next warning.");
            var movingGroup=closing.BreakGroupFor(slide.Tile);
            yield return Wait(()=>game.GameplayTime>=movingGroup.RevealStartsAt+.03,"the moving pane's sweep group",2);
            Require(moving.Find("Fissures").GetComponent<MeshRenderer>().enabled &&
                moving.Find("Rim").GetComponent<MeshFilter>().sharedMesh==GlassGeometry.Rim,
                "The moving pane failed to flash during its scheduled clue.");
            StandOn(closing.GuaranteedTile);
            yield return Stage(GameStage.BetweenWaves, 8);
            yield return Frames(1);
            var pieces=game.BoardView.transform.Find("Falling fragments").GetComponent<MeshFilter>().sharedMesh.vertices;
            int verticesPerPane=GlassGeometry.Pieces.Sum(face=>face.Length);
            int paneIndex=closing.BreakGroups.SelectMany(group=>group.Tiles).Reverse().ToList().IndexOf(slide.Tile);
            var paneVertices=pieces.Skip(paneIndex*verticesPerPane).Take(verticesPerPane).ToArray();
            Require(paneVertices.Length==verticesPerPane && !moving.gameObject.activeSelf && game.State.Slider==null,
                "The moving glass must turn into the same sixteen fragments as the stationary glass.");
            var bounds=new Bounds(paneVertices[0],Vector3.zero);
            foreach(var vertex in paneVertices) bounds.Encapsulate(vertex);
            Require(Mathf.Abs(bounds.center.x-caught.x)<.035f && Mathf.Abs(bounds.center.z-caught.z)<.035f &&
                Mathf.Abs(bounds.size.x-slide.HalfSize*2)<.045f && Mathf.Abs(bounds.size.z-slide.HalfSize*2)<.045f,
                "Moving glass fragments did not start at the caught pose and full visible width.");
            Record($"Level 3 seed 379 moved ordinary full-sized glass between holes {slide.Tile} and {slide.OtherHole}. A miss recovered, a catch held through zero, and the warned pane broke into {GlassGeometry.Pieces.Length} fitted shards at ({caught.x:F3}, {caught.z:F3}) m with measured width {bounds.size.x:F3} m.");
        }

        private static int FindSliderRoute()
        {
            // Select a reproducible physical starting tile on the fitted board. Only the real
            // head poses and production countdown drive the subsequent gameplay assertions.
            foreach (int start in PlayablePanes)
            {
                var simulation = new BoardState(game.State.Layout, game.State.Seed, game.State.Pitch);
                int current = start;
                double time = 0;
                for (int number = 1; number <= simulation.Count; number++)
                {
                    var plan = simulation.BeginWave(current, 3, number, time);
                    if (simulation.Slider != null) return start;
                    if (plan.Mode == WaveMode.LastTile) break;
                    current = plan.GuaranteedTile;
                    if (simulation.Tick(simulation.Center(current), true, plan.Deadline) != WaveResult.Survived) break;
                    time = plan.Deadline + 1.3;
                }
            }
            throw new InvalidOperationException("Seed 379 produced no optional moving pane on this fitted board.");
        }

        private static void VerifyInstalledBlocks()
        {
            var roots = game.gameObject.scene.GetRootGameObjects();
            var markers = roots.SelectMany(root => root.GetComponentsInChildren<BuildingBlock>(true)).ToArray();
            Require(roots.SelectMany(root => root.GetComponentsInChildren<OVRCameraRig>(true)).Count() == 1,
                "More than one tracked camera rig is present.");
            Require(markers.Count(b => b.BlockId == MetaRoomBlocks.CameraId) == 1 &&
                markers.Count(b => b.BlockId == MetaRoomBlocks.PassthroughId) == 1 &&
                markers.Count(b => b.BlockId == MetaRoomBlocks.RoomId) == 1 &&
                markers.Count(b => b.BlockId == MetaRoomBlocks.FloorSpawnsId) == 1 &&
                markers.Count(b => b.BlockId == MetaRoomBlocks.ControllersId) == 2,
                "The SDK rig, underlay, room, Find Spawn Positions, or controller block metadata is missing.");
            var spawnBlock = markers.Single(b => b.BlockId == MetaRoomBlocks.FloorSpawnsId);
            var spawn = roots.SelectMany(root => root.GetComponentsInChildren<FindSpawnPositions>(true)).Single();
            Require((spawn.transform == spawnBlock.transform || spawn.transform.IsChildOf(spawnBlock.transform)) && spawn.SpawnOnStart == MRUK.RoomFilter.None,
                "Find Spawn Positions did not retain its installed definition and manually controlled spawning.");
            var interaction = markers.Single(b => b.BlockId == MetaRoomBlocks.InteractionsId);
            Require(interaction.InstallationRoutineCheckpoint?.InstallationRoutineId == MetaRoomBlocks.OvrInteractionsRoutineId,
                "The comprehensive interaction rig is missing its SDK installation checkpoint.");
            var models = game.Rig.GetComponentsInChildren<OVRControllerHelper>(true)
                .Where(h => !h.CompareTag("EditorOnly")).ToArray();
            Require(models.Length == 2 && models.Any(h => h.m_controller == OVRInput.Controller.LTouch) &&
                models.Any(h => h.m_controller == OVRInput.Controller.RTouch), "The player build has duplicate or missing controller models.");
            Require(interaction.GetComponentsInChildren<RayInteractor>(true).Count(r => r.name == "ControllerRayInteractor") == 2 &&
                interaction.GetComponentsInChildren<GrabInteractor>(true).Count(g => g.name == "ControllerGrabInteractor") == 2,
                "The SDK controller ray or direct-grab interactors are incomplete.");
            Record("Meta Camera Rig, Passthrough, MR Utility Kit, Find Spawn Positions, Controller Tracking, and Interactions Rig supply the single tracked rig, room, floor candidates and left/right ray and grab controls.");
            VerifyRoomVisuals(roots, markers);
        }

        private static void VerifySpawnMarkersCleared()
        {
            var spawn = Object.FindAnyObjectByType<FindSpawnPositions>();
            Require(spawn && !spawn.SpawnedObjects.Any(VisibleMarker) && (!spawn.SpawnObject || !spawn.SpawnObject.scene.IsValid() || !VisibleMarker(spawn.SpawnObject)),
                "Find Spawn Positions left a visible candidate marker after room fitting.");
        }

        private static bool VisibleMarker(GameObject marker) => marker && marker.activeInHierarchy && marker.GetComponentsInChildren<Renderer>().Any(r => r.enabled);

        private static void StandOn(int tile) => StandAt(game.State.Center(tile));

        private static void StandAt(Vector2 c)
        {
            var board = game.BoardView.Board;
            SetStandingHead(c,board.rotation * Quaternion.Euler(15f,0f,0f));
        }

        private static float ProfileEyeDepth()
        {
            float depth = OVRPlugin.eyeDepth;
            return depth >= .035f && depth <= .14f ? depth : .08f;
        }

        // Turning a simulated head pivots the eye around a planted stance, just as the
        // production support estimate expects when the wearer looks down.
        private static void SetStandingHead(Vector2 stance, Quaternion rotation, float eyeHeight = 1.6f)
        {
            var board = game.BoardView.Board;
            var eye = board.TransformPoint(new Vector3(stance.x,eyeHeight,stance.y));
            var projected = PlayerTileTracker.ProjectStance(board.worldToLocalMatrix,eye,rotation,ProfileEyeDepth());
            eye += board.TransformVector(GlassGeometry.Ground(stance-projected));
            SetHead(new Pose(eye,rotation));
        }

        private static void LookAtTile(int tile)
        {
            var board = game.BoardView.Board;
            var cell = game.State.Center(tile);
            var target = board.TransformPoint(new Vector3(cell.x, 0f, cell.y));
            var marker = board.Find("Occupied square").GetComponent<MeshRenderer>();
            if (marker.enabled && game.State.CurrentSample.Index == tile) target = marker.bounds.center;
            Vector3 direction = (target - head.position).normalized;
            var stance = PlayerTileTracker.ProjectStance(board.worldToLocalMatrix,head.position,head.rotation,ProfileEyeDepth());
            SetStandingHead(stance,Quaternion.LookRotation(direction,Vector3.up));
        }

        private static void SetHead(Pose value)
        {
            head = value;
            left = new Pose(head.position + head.rotation * new Vector3(-0.22f,-0.27f,0.32f), head.rotation);
            right = new Pose(head.position + head.rotation * new Vector3(0.22f,-0.27f,0.32f), head.rotation);
            game.Panel.EditorSetPose(head,left,right,false,false);
        }

        private static void SetHands(Pose value, bool held)
        {
            left = value; game.Panel.EditorSetPose(head,left,right,held,false);
        }

        private static IEnumerator RayChoose(int rowNumber, bool leftHand)
        {
            game.Panel.EditorSetTriggers(false, false);
            yield return Frames(2);
            var row = game.Panel.transform.Find("Floor Fracture Menu/Choice " + rowNumber) as RectTransform;
            Require(row && row.gameObject.activeInHierarchy, "The ray target row is unavailable.");
            var point = row.TransformPoint(row.rect.center);
            var grip = leftHand ? left : right;
            grip.rotation = Quaternion.LookRotation(point - grip.position, Vector3.up);
            if (leftHand) left = grip; else right = grip;
            game.Panel.EditorSetPose(head, left, right, false, false);
            yield return Frames(2);
            game.Panel.EditorSetTriggers(leftHand, !leftHand);
            yield return Frames(2);
            game.Panel.EditorSetTriggers(false, false);
            yield return Frames(2);
        }

        private static void Choose(string prefix) => Require(game.Panel.EditorChoose(prefix), "No displayed menu choice beginning with '" + prefix + "' in " + game.Stage);
        private static IEnumerator Stage(GameStage stage, double timeout = 5) => Wait(() => game.Stage == stage, stage.ToString(), timeout);

        private static IEnumerator Frames(int count)
        {
            int until = Time.frameCount + count;
            return Wait(() => Time.frameCount >= until, count + " rendered game frames", 20);
        }

        private static IEnumerator Delay(double seconds)
        {
            double until = Now + seconds;
            return Wait(() => Now >= until, "game time to advance", seconds + 8);
        }

        private static IEnumerator Wait(Func<bool> completed, string label, double timeout)
        {
            double end = EditorApplication.timeSinceStartup + timeout;
            while (!completed())
            {
                if (EditorApplication.timeSinceStartup > end)
                    throw new TimeoutException("Timed out waiting for " + label + ". " + (game ? game.Stage + " / " + game.Room?.Message : "No GameFlow."));
                yield return null;
            }
        }

        private static void Capture(string name, SmokeView view, Bounds? floorBounds = null)
        {
            string detail = SmokeCapture.Write(Folder, name, game, view, floorBounds);
            if (detail == null) report.images.Add(name + ".png");
            else report.captureNotes.Add(name + " — " + detail);
            Save();
        }

        private static void RecordWave(WavePlan wave, bool survived)
        {
            report.waves.Add(new SmokeWave { mode=wave.Mode.ToString(), start=wave.StartingTile, guaranteed=wave.GuaranteedTile,
                survived=survived, plannedDeadline=wave.Deadline, observedAfterResolution=game.GameplayTime, score=game.State.WavesSurvived });
            Save();
        }

        private static void Record(string detail) { report.checks.Add(detail); Save(); }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Save() => File.WriteAllText(Path.Combine(Folder, "report.json"), JsonUtility.ToJson(report, true));

        [Serializable]
        private sealed class SmokeReport
        {
            public string result = "running", startedUtc, finishedUtc, unityVersion, scene, failure;
            public string fixture = "Installed MRUK MeshLivingRoom1, Editor Play Mode";
            public string input = "Simulated world poses, grip state, and visible menu selection. Unity physics and unscaled game time.";
            public string imagesDescribe = "Monoscopic Editor renders of the actual scene. Player-view images use the simulated center eye. Play-area-passthrough looks down on the glass and the surrounding real floor. Room-preview, classic-glass, and neighboring-hole are elevated observer shots. Fall sequences use the same stationary eye position, looking toward the opening, slightly downward, and straight ahead as labeled.";
            public int columns, rows, seed, sdkFloorCandidates;
            public float pitch;
            public bool depthProviderReadyInAuthoredScene, depthSupportedInFixture;
            public List<string> checks = new List<string>(), images = new List<string>(), captureNotes = new List<string>(), endingOrder = new List<string>();
            public List<SmokeWave> waves = new List<SmokeWave>();
            public List<FallEvidence> falls = new List<FallEvidence>();
            public List<EndingExitEvidence> endingExits = new List<EndingExitEvidence>();
            public string previewBeforeNaturalLoss;
            public List<LandingReadback> openLandings = new List<LandingReadback>();
            public List<LandingView> landingViews = new List<LandingView>();
        }

        [Serializable]
        private sealed class SmokeWave
        {
            public string mode;
            public int start, guaranteed, score;
            public bool survived;
            public double plannedDeadline, observedAfterResolution;
        }
    }
}
