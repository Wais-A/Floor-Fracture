using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.XR.BuildingBlocks;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    [InitializeOnLoad]
    public static class GameplaySmoke
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
            Require(game.Stage == GameStage.Room && game.Room.Status == RoomPlacementStatus.Preview, "Room preview failed: " + game.Room.Message);
            var placement = game.Room.Placement;
            report.columns = placement.Columns; report.rows = placement.Rows;
            var outlines = game.Room.transform.Find("Placement outlines");
            Require(outlines && outlines.gameObject.activeInHierarchy && outlines.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                "The preview has no visible outline geometry.");
            Record("MRUK Rec Room supplied floor and furniture, and the placement preview drew its clearance grid.");
            Capture("room-preview", true);
            yield return RayChoose(1, true);
            yield return Stage(GameStage.Welcome);
            Require(game.Room.Status == RoomPlacementStatus.Confirmed && !outlines.gameObject.activeSelf, "Placement remained unconfirmed or its preview remained visible.");
            yield return RayChoose(3, false); yield return Stage(GameStage.Settings);
            Record("The left simulated ray confirmed the footprint and the right ray opened Settings through independent trigger presses.");
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
            var stoneBlock = tutorial.Body.GetComponentInChildren<BuildingBlock>(true);
            Require(stoneBlock && stoneBlock.BlockId == "f8766c17-aaf8-4b55-9431-de8deea83842" &&
                stoneBlock.InstallationRoutineCheckpoint?.InstallationRoutineId == "f8338144-8280-4ff6-9455-ae2bb9c20fe8",
                "The practice stone did not retain its Grab Interaction block installation.");
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
                    yield return Frames(2); Capture("classic-glass", true);
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
                if (index == 0) { yield return Frames(1); Capture("neighboring-hole", true); }
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
                    Capture("ending-" + ending.ToLowerInvariant(), false);
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

            Choose("Main menu"); Choose("Settings");
            if (PlayerPrefs.GetInt("ff.reduced",0) == 0) Choose("Fall motion");
            Choose("Back"); Choose("Development"); Choose("Next ending"); Choose("Preview loss");
            yield return Stage(GameStage.Fall);
            Require(game.Ending.LastEndingName == "Water", "The development chooser did not select Water.");
            yield return Stage(GameStage.Results, 8);
            Choose("Restart"); yield return Stage(GameStage.Ready);
            Record("The development chooser played Water with reduced motion through its natural results transition.");
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
            Vector3 drop = hit ? new Vector3(local.x,0.6f,local.y) : new Vector3(3.5f,0.6f,3.5f);
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
                markers.Count(b => b.BlockId == MetaRoomBlocks.ControllersId) == 2,
                "The SDK rig, underlay, room, or controller block metadata is missing.");
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
            Record("Five actual Meta Building Blocks share one tracked rig, one underlay, a left/right controller model pair and two SDK ray/direct-grab pairs.");
        }

        private static void StandOn(int tile)
        {
            var board = game.BoardView.Board;
            var c = game.State.Center(tile);
            SetHead(new Pose(board.TransformPoint(new Vector3(c.x,1.6f,c.y)), board.rotation * Quaternion.Euler(15f,0f,0f)));
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

        private static void Capture(string name, bool board)
        {
            string detail = SmokeCapture.Write(Folder, name, game, board);
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
            public string imagesDescribe = "Monoscopic Editor renders of the actual scene. Board images use an elevated observer camera.";
            public int columns, rows, seed;
            public List<string> checks = new List<string>(), images = new List<string>(), captureNotes = new List<string>(), endingOrder = new List<string>();
            public List<SmokeWave> waves = new List<SmokeWave>();
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
