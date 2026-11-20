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
        private static readonly string[] Preferences = { "ff.reduced", "ff.gentle", "ff.volume" };
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
            report = new SmokeReport { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, scene = ScenePath };
            report.depthProviderEnabledInAuthoredScene = AuthoredDepthEnabled();
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
            PlayerPrefs.Save();
            SessionState.SetInt(Key + "phase", 0);
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + "passed", false) ? 0 : 1);
        }

        private static IEnumerator Scenario()
        {
            yield return Wait(() => (game = Object.FindAnyObjectByType<GameFlow>()) && game.Panel && game.Room, "game initialization", 30);
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
                "The playing board did not use the rectangular dimensions and actual tile pitch measured during fitting.");
            var outlines = game.Room.transform.Find("Placement outlines");
            Require(outlines && outlines.gameObject.activeInHierarchy && outlines.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                "The Welcome screen did not show the verified board's clearance perimeter.");
            VerifyMenuAndSpatialAudio();
            VerifySpawnMarkersCleared();
            Record($"MRUK Rec Room supplied floor and furniture. The {placement.Columns} × {placement.Rows} board at {placement.Pitch:F3} m pitch appeared at Welcome without a clearance approval prompt.");
            Capture("room-preview", SmokeView.BoardObserver);
            yield return RayChoose(3, false); yield return Stage(GameStage.Settings);
            Require(!outlines.gameObject.activeSelf, "The Welcome clearance perimeter stayed visible inside Settings.");
            yield return RayChoose(4, true); yield return Stage(GameStage.Welcome);
            Record("The right simulated ray opened Settings and the left ray selected Back through independent trigger presses.");
            Choose("Settings"); yield return Stage(GameStage.Settings);
            int oldMotion = PlayerPrefs.GetInt("ff.reduced",0), oldWarning = PlayerPrefs.GetInt("ff.gentle",0), oldVolume = PlayerPrefs.GetInt("ff.volume",3);
            Choose("Fall motion"); Require(PlayerPrefs.GetInt("ff.reduced") != oldMotion, "Fall setting did not persist.");
            if (PlayerPrefs.GetInt("ff.reduced") != 0) Choose("Fall motion");
            Choose("Warning"); Require(PlayerPrefs.GetInt("ff.gentle") != oldWarning, "Warning setting did not persist.");
            if (PlayerPrefs.GetInt("ff.gentle") != 0) Choose("Warning");
            Choose("Volume"); Require(PlayerPrefs.GetInt("ff.volume") != oldVolume, "Volume setting did not persist.");
            Choose("Back"); yield return Stage(GameStage.Welcome);
            Choose("Development"); Choose("Seed 379 · level 1"); yield return Stage(GameStage.Ready);
            Choose("Main menu"); yield return Stage(GameStage.Welcome);
            Require(game.State.Seed == 379, "Development run did not preserve the reproducible seed.");
            report.seed = game.State.Seed;
            Record("Welcome and Settings routed through their visible menu choices. The development menu selected seed 379.");

            int first = (game.State.Height / 2) * game.State.Width + game.State.Width / 2;
            StandOn(first);
            Choose("Tutorial"); yield return Stage(GameStage.PracticeStone);
            var tutorial = game.GetComponent<PracticeStone>();
            Require(tutorial && tutorial.Body && tutorial.Body.gameObject.activeInHierarchy && !tutorial.Body.useGravity && tutorial.Body.GetComponent<Grabbable>() &&
                tutorial.Body.GetComponentInChildren<GrabInteractable>(true), "The reachable Meta grab stone was missing.");
            var stoneBlock = tutorial.Body.GetComponentsInChildren<BuildingBlock>(true)
                .SingleOrDefault(block => block.BlockId == "f8766c17-aaf8-4b55-9431-de8deea83842");
            Require(stoneBlock &&
                stoneBlock.InstallationRoutineCheckpoint?.InstallationRoutineId == "f8338144-8280-4ff6-9455-ae2bb9c20fe8",
                "The practice stone did not retain its Grab Interaction block installation.");
            VerifyStoneOcclusion(tutorial);
            yield return DropStone(tutorial, false);
            yield return Frames(3);
            Vector3 forward = Vector3.ProjectOnPlane(game.Panel.Head.forward, Vector3.up).normalized;
            Vector3 reachablePoint = game.Panel.Head.position + forward * 0.34f + Vector3.down * 0.27f;
            Require(game.Stage == GameStage.PracticeStone && tutorial.Body.gameObject.activeInHierarchy &&
                !tutorial.Body.useGravity && Vector3.Distance(tutorial.Body.position, reachablePoint) < 0.04f,
                $"Miss recovery expected {reachablePoint:F3}, observed {tutorial.Body.position:F3}, " +
                $"gravity {tutorial.Body.useGravity}, active {tutorial.Body.gameObject.activeInHierarchy}, stage {game.Stage}.");
            yield return DropStone(tutorial, true);
            yield return Stage(GameStage.PracticeStep);
            Record("The left grip picked up and released a real Rigidbody. A miss reset it and a hit advanced the tutorial.");
            StandOn(first);
            yield return Delay(1.4);
            Require(game.State.CurrentSample.Index == first, "The tutorial marker did not select the first step.");
            int neighbor = first % game.State.Width == 0 ? first + 1 : first - 1;
            StandOn(neighbor);
            yield return Stage(GameStage.Ready);
            Record("Two distinct headset projections completed practice and left the first countdown waiting for readiness.");
            Choose("Start wave"); yield return Stage(GameStage.Countdown);

            bool sawMemory = false, sawLastTile = false;
            int initialCount = game.State.Count;
            for (int index = 0; index < initialCount; index++)
            {
                var plan = game.State.ActiveWave;
                Require(plan != null && plan.StartingTile != plan.GuaranteedTile, "Wave did not require a move.");
                if (index == 0)
                {
                    Require(plan.Mode == WaveMode.Classic, "First wave was not Classic.");
                    yield return Frames(2); Capture("classic-glass", SmokeView.BoardObserver);
                    LookAtTile(plan.StartingTile, true);
                    yield return Frames(2); Capture("classic-player-view", SmokeView.CenterEye);
                }
                if (plan.Mode == WaveMode.Memory) sawMemory = true;
                if (plan.Mode == WaveMode.LastTile) sawLastTile = true;
                int score = game.State.WavesSurvived;
                StandOn(plan.GuaranteedTile);
                yield return Frames(2);
                Require(game.State.CurrentSample.Index == plan.GuaranteedTile && Now < plan.Deadline && game.State.WavesSurvived == score,
                    "The early step did not register while the countdown remained live.");
                bool recallSeen = false;
                while (game.Stage == GameStage.Countdown)
                {
                    if (Now < plan.Deadline)
                    {
                        Require(game.State.IsIntact(plan.StartingTile) && game.State.WavesSurvived == score, "A break or score occurred before zero.");
                        if (plan.Mode == WaveMode.Memory && Now > plan.RevealEndsAt && !plan.ShowsWarnings(Now)) recallSeen = true;
                    }
                    Require(Now < plan.Deadline + 2, "The scheduled deadline did not resolve.");
                    yield return null;
                }
                Require(game.Stage == GameStage.BetweenWaves && Now >= plan.Deadline && game.State.BreaksApplied &&
                    game.State.WavesSurvived == score + 1 && !game.State.IsIntact(plan.StartingTile), "The survival result disagreed with the saved deadline or support.");
                if (plan.Mode == WaveMode.Memory) Require(recallSeen, "Memory did not enter recall before zero.");
                RecordWave(plan, true);
                if (index == 0)
                {
                    yield return Frames(1); Capture("neighboring-hole", SmokeView.BoardObserver);
                    LookAtTile(plan.StartingTile, false);
                    yield return Wait(() => Now >= plan.Deadline + 0.55, "shards falling below the floor", 3);
                    Capture("neighboring-hole-player-view", SmokeView.CenterEye);
                }
                yield return Wait(() => game.Stage != GameStage.BetweenWaves, "next wave or level", 8);
                if (game.Stage == GameStage.Ready) break;
                Require(game.Stage == GameStage.Countdown, "A safe stationary route did not advance the level.");
            }
            Require(game.Stage == GameStage.Ready && game.Level == 2 && sawLastTile, "Last Tile did not rebuild the confirmed board.");
            if (initialCount >= 9) Require(sawMemory, "The larger first level never entered Memory.");
            Record("Walked each guaranteed stationary step through Last Tile. Level 2 rebuilt and waited for readiness.");

            Choose("Main menu"); Choose("Development"); Choose("Seed 379 · level 1");
            StandOn(first); Choose("Start wave"); yield return Stage(GameStage.Countdown);
            var losing = game.State.ActiveWave;
            Require(losing.Mode == WaveMode.Classic && game.State.WavesSurvived == 0, "Fresh run did not start with Classic and a zero score.");
            while (game.Stage == GameStage.Countdown)
            {
                if (Now < losing.Deadline) Require(game.State.IsIntact(losing.StartingTile), "The loss tile vanished before zero.");
                Require(Now < losing.Deadline + 2, "Standing still did not produce a deadline result.");
                yield return null;
            }
            Require(game.Stage == GameStage.Fall && Now >= losing.Deadline && game.State.BreaksApplied &&
                game.State.WavesSurvived == 0 && !game.State.IsIntact(losing.StartingTile), "Standing still produced an incorrect Classic deadline loss.");
            RecordWave(losing, false);
            var order = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                if (i != 0) { Choose("Main menu"); Choose("Development"); Choose("Preview loss"); }
                yield return Stage(GameStage.Fall);
                Require(game.Ending.IsPlaying, "The graphical loss controller was inactive.");
                string ending = game.Ending.LastEndingName;
                Require(!string.IsNullOrEmpty(ending), "No ending was selected.");
                order.Add(ending);
                if (i < 6)
                {
                    yield return Delay(3.75);
                    Require(game.Stage == GameStage.Fall, "The reveal ended before it could be captured.");
                    Capture("ending-" + ending.ToLowerInvariant(), SmokeView.Ending);
                }
                if (i != 1) game.Ending.Skip();
                yield return Stage(GameStage.Results, 10);
                Choose("Restart"); yield return Stage(GameStage.Ready);
                Require(!game.Ending.IsPlaying && game.Level == 1 && game.State.WavesSurvived == 0 &&
                    game.State.ActiveWave == null && game.BoardView.gameObject.activeSelf, "Restart left scoring, ending, or board state behind.");
                for (int cell = 0; cell < game.State.Count; cell++) Require(game.State.IsIntact(cell), "Restart left a hole.");
            }
            Require(new HashSet<string>(order.GetRange(0,6)).Count == 6 && order[5] != order[6],
                "The six-ending shuffle repeated early or across its boundary.");
            report.endingOrder = order;
            Record("Six natural endings played before a repeat. The next bag changed ending. Full playback, skip, results, and restart used the normal production controller.");

            yield return ObserveSelectedFall("Water", false, 1);
            yield return ObserveSelectedFall("Lava", false, 1);
            yield return ObserveSelectedFall("Water", true, 5);
            yield return LevelThree();

            var scannedRoom = MRUK.Instance.Rooms.Single();
            scannedRoom.AnchorUpdatedEvent.Invoke(scannedRoom.FloorAnchors.First());
            Require(game.Room.Status == RoomPlacementStatus.UnsafeRoom && game.Stage == GameStage.Room && !game.BoardView.gameObject.activeSelf,
                "A significant floor-anchor update left the previously confirmed board playable.");
            Choose("Check again");
            yield return Wait(() => game.Room.Status == RoomPlacementStatus.Confirmed && game.Stage == GameStage.Welcome, "fresh verified fit after floor update", 55);
            VerifySpawnMarkersCleared();
            Record("A simulated significant MRUK floor-anchor event withdrew the board. Reloading the real fixture produced a new verified fit and returned directly to Welcome.");

            yield return VerifyRoomRecovery(MRUK.LoadDeviceResult.NoScenePermission,
                RoomPlacementStatus.PermissionDenied, "App permissions", "Try again");
            yield return VerifyRoomRecovery(MRUK.LoadDeviceResult.NoRoomsFound,
                RoomPlacementStatus.NoRoom, "Space Setup", "Check again");
        }

        private static IEnumerator VerifyRoomRecovery(MRUK.LoadDeviceResult result, RoomPlacementStatus status,
            string recoveryLabel, string retryLabel)
        {
            // The Editor fixture supplies room data. Inject the same result the SDK returns on device.
            var receiveResult = typeof(RoomBoardPlacement).GetMethod("AcceptLoad",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(receiveResult != null && !(bool)receiveResult.Invoke(game.Room, new object[] { result }),
                "The room loader accepted an unavailable scan.");
            var labels = game.Panel.GetComponentsInChildren<UnityEngine.UI.Text>()
                .Where(label => label.isActiveAndEnabled).Select(label => label.text).ToArray();
            Require(game.Room.Status == status && game.Stage == GameStage.Room && !game.BoardView.gameObject.activeSelf &&
                labels.Contains(recoveryLabel) && labels.Contains(retryLabel) && labels.Contains(game.Room.Message),
                "The room failure must hide the board and display its explanation and both recovery actions.");
            yield return Frames(2);
            Choose(retryLabel);
            yield return Wait(() => game.Stage == GameStage.Welcome && game.Room.Status == RoomPlacementStatus.Confirmed,
                "a valid room after " + result, 55);
            Require(game.BoardView.gameObject.activeSelf && game.State.ActiveWave == null,
                "Retry must restore the board and leave the next round idle.");
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
            LookAtTile(wave.GuaranteedTile, true); yield return Frames(2);
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
            var slide = game.State.Slider;
            var moving = game.BoardView.transform.Find("Sliding pane");
            var arrow = moving.Find("Direction").GetComponent<MeshRenderer>();
            int score = game.State.WavesSurvived;
            Require(!slide.Locked && !game.State.IsIntact(slide.Tile) && game.State.IsIntact(wave.GuaranteedTile),
                "The optional moving pane must occupy an existing hole with a permanent alternative.");
            yield return Frames(2);
            Require(moving.gameObject.activeInHierarchy && arrow.enabled && fog.enabled == wave.Fog, "The moving pane, its direction cue, or the planned fog state was missing.");

            // Miss across the direction of travel, which cannot intersect the moving footprint.
            var across = new Vector2(-slide.Direction.y, slide.Direction.x);
            float missOffset = (game.State.Pitch * 0.5f + slide.HalfSize) * 0.5f;
            StandAt(game.State.Center(slide.Tile) + across * missOffset);
            yield return Wait(() => game.State.CurrentSample.Kind == SupportKind.Gap, "projected head missed moving glass", 2);
            StandOn(wave.GuaranteedTile); yield return Frames(2);
            Require(game.Stage == GameStage.Countdown && !slide.Locked && game.State.CurrentSample.Index == wave.GuaranteedTile &&
                game.State.CurrentSample.Kind == SupportKind.Glass, "The deliberate miss did not recover to the stationary escape.");
            yield return Wait(() => Now > slide.MotionStartsAt + 0.10, "sliding pane to move", 3);
            Vector3 oldCenter = moving.localPosition;
            LookAtTile(slide.Tile, false); yield return Delay(0.2);
            Require((moving.localPosition - oldCenter).sqrMagnitude > 0.00005f, "The rendered slide marker did not travel along its cue.");
            Capture("level3-moving-pane-player-view", SmokeView.CenterEye);
            StandAt(slide.CenterAt(Now)); yield return Frames(2);
            Require(slide.Locked && game.State.CurrentSample.IsSlider && !arrow.enabled, "Landing on moving glass did not lock the pane and remove its arrow.");
            Vector3 caught = moving.localPosition;
            LookAtTile(slide.Tile, true); yield return Delay(0.20);
            Require((moving.localPosition - caught).sqrMagnitude < 0.000001f, "The caught sliding pane kept moving.");
            Capture("level3-locked-pane-player-view", SmokeView.CenterEye);
            yield return Stage(GameStage.BetweenWaves, 8);
            Require(game.State.WavesSurvived == score + 1 && !game.State.IsIntact(slide.Tile) && game.State.Slider == slide && slide.Locked,
                "The deadline did not retain the caught pane over its original opening.");
            RecordWave(wave, true);
            Record($"Level 3 seed 379 showed fog, then a moving pane over hole {slide.Tile}. A deliberate miss recovered on permanent tile {wave.GuaranteedTile}. Catching the moving glass locked its position and survived the deadline.");
        }

        private static int FindSliderRoute()
        {
            // Select a reproducible physical starting tile on the fitted board. Only the real
            // head poses and production countdown drive the subsequent gameplay assertions.
            for (int start = 0; start < game.State.Count; start++)
            {
                var simulation = new BoardState(game.State.Width, game.State.Height, game.State.Seed, game.State.Pitch);
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

        private static IEnumerator DropStone(PracticeStone stone, bool hit)
        {
            var board = game.BoardView.Board;
            Pose grip = new Pose(stone.Body.position, board.rotation);
            SetHands(grip, false); yield return Frames(2);
            SetHands(grip, true); yield return Frames(2);
            var grabbable = stone.Body.GetComponent<Grabbable>();
            Require(grabbable.SelectingPointsCount == 1, "The ISDK grabbable did not register the simulated controller.");
            var local = game.State.Center(stone.Target);
            Vector3 drop = hit ? new Vector3(local.x,0.6f,local.y) :
                new Vector3((game.State.Width * 0.5f + 1f) * game.State.Pitch, 0.6f, (game.State.Height * 0.5f + 1f) * game.State.Pitch);
            grip.position = board.TransformPoint(drop);
            SetHands(grip, true); yield return Frames(20);
            Require(Vector3.Distance(stone.Body.position, board.TransformPoint(drop)) < 0.04f, "The held stone did not follow the injected grip pose.");
            SetHands(grip, false);
            yield return Wait(() => grabbable.SelectingPointsCount == 0 && !stone.Body.isKinematic && stone.Body.useGravity, "ISDK stone release", 3);
            if (hit) yield return Stage(GameStage.PracticeStep, 5);
            else yield return Wait(() => !stone.Body.useGravity, "miss reset", 5);
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
            SetHead(new Pose(board.TransformPoint(new Vector3(c.x,1.6f,c.y)), board.rotation * Quaternion.Euler(15f,0f,0f)));
        }

        private static void LookAtTile(int tile, bool includeClock)
        {
            var board = game.BoardView.Board;
            var cell = game.State.Center(tile);
            var target = board.TransformPoint(new Vector3(cell.x, 0f, cell.y));
            Vector3 direction = (target - head.position).normalized;
            if (includeClock)
            {
                var clock = board.Find("Countdown and score").GetComponent<MeshRenderer>();
                direction += (clock.bounds.center - head.position).normalized;
            }
            SetHead(new Pose(head.position, Quaternion.LookRotation(direction, Vector3.up)));
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

        private static void Capture(string name, SmokeView view)
        {
            string detail = SmokeCapture.Write(Folder, name, game, view);
            if (detail == null) report.images.Add(name + ".png");
            else report.captureNotes.Add(name + " — " + detail);
            Save();
        }

        private static void RecordWave(WavePlan wave, bool survived)
        {
            report.waves.Add(new SmokeWave { mode=wave.Mode.ToString(), start=wave.StartingTile, guaranteed=wave.GuaranteedTile,
                survived=survived, plannedDeadline=wave.Deadline, observedAfterResolution=Now, score=game.State.WavesSurvived });
            Save();
        }

        private static void Record(string detail) { report.checks.Add(detail); Save(); }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Save() => File.WriteAllText(Path.Combine(Folder, "report.json"), JsonUtility.ToJson(report, true));

        [Serializable]
        private sealed class SmokeReport
        {
            public string result = "running", startedUtc, finishedUtc, unityVersion, scene, failure;
            public string fixture = "Installed MRUK OtherRoom02_RecRoom, Editor Play Mode";
            public string input = "Simulated world poses, grip state, and visible menu selection. Unity physics and unscaled game time.";
            public string imagesDescribe = "Monoscopic Editor renders of the actual scene. Player-view images use the simulated center eye. Room-preview, classic-glass, and neighboring-hole are elevated observer shots. Fall sequences use the same stationary eye position, looking toward the opening, slightly downward, and straight ahead as labeled.";
            public int columns, rows, seed, sdkFloorCandidates;
            public float pitch;
            public bool depthProviderEnabledInAuthoredScene, depthSupportedInFixture;
            public List<string> checks = new List<string>(), images = new List<string>(), captureNotes = new List<string>(), endingOrder = new List<string>();
            public List<SmokeWave> waves = new List<SmokeWave>();
            public List<FallEvidence> falls = new List<FallEvidence>();
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
