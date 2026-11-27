using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    // The game's objects are saved in the scene and adopted at Play. These checks catch a return to runtime creation.
    public static partial class GameplaySmoke
    {
        private const string SpatialVoicePath = "Assets/FloorFracture/Resources/GlassSpatialVoice.prefab";
        private static readonly string[] PlainSounds =
            { "Menu and countdown", "Background music", "Shaft air", "Shaft roar", "Ending ambience", "Offline narration" };
        private static readonly string[] SpatialSounds =
        {
            "Opening draft", "Ending impact", "Tar bubbling",
            "Pane impact 0", "Pane impact 1", "Pane impact 2",
            "Glass fragments 0", "Glass fragments 1", "Glass fragments 2",
            "Shaft echo 0", "Shaft echo 1", "Shaft echo 2",
            "Thunder 0", "Thunder 1", "Thunder 2"
        };
        // Idle sources hold no clip, so an ending or the narrator cannot replay a stale one.
        private static readonly string[] ClipFreeSounds = { "Ending ambience", "Ending impact", "Offline narration" };
        private static readonly string[] ChamberParts =
        {
            "Shaft walls", "Shaft landmarks", "Left brick column", "Right brick column", "Chamber darkness", "Hazard surface",
            "Floor Zone water ripples", "Lava flames", "Particles", "Water spray", "Entry bubbles", "Fall dust",
            "View fade", "View fade/Submerged view", "Water contact splash"
        };
        private const string TilePrefabPath = "Assets/FloorFracture/Resources/GlassTile.prefab";
        private static readonly string[] PaneParts = { "Smoky pane", "Glass sides and bottom", "Rim", "Fissures" };
        private static readonly string[] BoardParts =
        {
            "Occupied square", "Opening stencil", "Perimeter", "Falling fragments",
            "Floor fog/Floor fog 0", "Floor fog/Floor fog 1", "Floor fog/Floor fog 2", "Floor fog/Floor fog 3",
            "Overhead lights/Lamp 0/Lamp hum", "Overhead lights/Lamp 1/Lamp hum", "Overhead lights/Lamp 2/Lamp hum",
            "Overhead lights/Spotlight beam 0", "Overhead lights/Spotlight beam 1", "Overhead lights/Spotlight beam 2",
            "Overhead lights/Spotlight pool 0", "Overhead lights/Spotlight pool 1", "Overhead lights/Spotlight pool 2",
            "Lightning/Sky flash", "Lightning/Flash veil"
        };
        // Parents of the effects that show only while a wave uses them.
        private static readonly string[] IdleEffects = { "Floor fog", "Overhead lights", "Lightning" };
        private const int PreviewSide = 6;
        private const float PreviewPitch = .62f;
        private static bool Quick => SessionState.GetBool(Key + "quick", false);

        /// <summary>What the last check found switched on that the idle game keeps off. The save command leaves none.</summary>
        public static string[] SavedSwitchedOn { get; private set; } = Array.Empty<string>();

        [MenuItem("Tools/Floor Fracture/Check saved scene objects")]
        private static void CheckSavedSceneMenu() => CheckSavedScene();

        /// <summary>Areas are "sound", "menu", "chamber" and "board"; none means all. Runs without Play.</summary>
        public static void CheckSavedScene(params string[] areas)
        {
            bool all = areas == null || areas.Length == 0;
            var problems = new List<string>();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool preview = !scene.IsValid() || !scene.isLoaded;
            if (preview) scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var flows = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameFlow>(true)).ToArray();
                if (flows.Length != 1) throw new InvalidOperationException("The saved scene needs exactly one GameFlow; it has " + flows.Length + ".");
                if (all || areas.Contains("sound")) CheckSavedSound(flows[0].transform, problems);
                if (all || areas.Contains("menu")) CheckSavedMenu(flows[0].transform, problems);
                if (all || areas.Contains("chamber")) CheckSavedChamber(scene, flows[0], problems);
                if (all || areas.Contains("board")) CheckSavedBoard(scene, flows[0], problems);
            }
            finally { if (preview) EditorSceneManager.ClosePreviewScene(scene); }
            if (all || areas.Contains("sound")) CheckSoundWakesAtSavedVolume(problems);
            // Something left switched on in the scene is not a fault: the game switches it off at startup.
            SavedSwitchedOn = problems.Where(problem => problem.Contains("switched on")).ToArray();
            problems.RemoveAll(problem => problem.Contains("switched on"));
            if (SavedSwitchedOn.Length > 0)
                Debug.LogWarning("[Floor Fracture verification] Saved switched on; the game switches these off when it starts:\n" + string.Join("\n", SavedSwitchedOn));
            if (problems.Count > 0)
                throw new InvalidOperationException("Saved scene check failed (" + problems.Count + "):\n" + string.Join("\n", problems));
            Debug.Log("[Floor Fracture verification] Saved scene check passed: " + (all ? "all areas" : string.Join(", ", areas)));
        }

        /// <summary>The saved-scene part of the gameplay check alone: Play to Welcome, about a minute.</summary>
        [MenuItem("Tools/Floor Fracture/Verify saved scene objects in Play Mode")]
        public static void StartQuick()
        {
            Start();
            SessionState.SetBool(Key + "quick", true);
            // Start() recorded the player's volume and RestoreEditor puts it back. A muted player must hear nothing at startup.
            PlayerPrefs.SetInt("ff.volume", 0);
        }

        // The object at parent/path, reporting a missing or repeated name at any level.
        private static Transform Expect(Transform parent, string path, List<string> problems)
        {
            var at = parent;
            foreach (string name in path.Split('/'))
            {
                var matches = at.Cast<Transform>().Where(child => child.name == name).ToArray();
                if (matches.Length != 1)
                {
                    problems.Add((matches.Length == 0 ? "missing: " : "saved " + matches.Length + " times: ") + parent.name + "/" + path);
                    return null;
                }
                at = matches[0];
            }
            return at;
        }

        private static void CheckSavedSound(Transform game, List<string> problems)
        {
            if (game.GetComponents<GameSound>().Length != 1) problems.Add("Floor Fracture needs one saved GameSound.");
            if (game.GetComponents<OfflineNarration>().Length != 1) problems.Add("Floor Fracture needs one saved OfflineNarration.");
            foreach (string name in PlainSounds)
            {
                var voice = Expect(game, name, problems);
                if (!voice) continue;
                var source = voice.GetComponent<AudioSource>();
                if (!source) problems.Add(name + " has no AudioSource.");
                else if (source.playOnAwake) problems.Add(name + " would play when the scene wakes.");
            }
            foreach (string name in SpatialSounds)
            {
                var voice = Expect(game, name, problems);
                if (!voice) continue;
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(voice.gameObject);
                if (!prefab || AssetDatabase.GetAssetPath(prefab) != SpatialVoicePath)
                    problems.Add(name + " is not a linked instance of the Spatial Audio voice prefab.");
            }
            foreach (string name in ClipFreeSounds)
            {
                var voice = game.Find(name);
                var source = voice ? voice.GetComponent<AudioSource>() : null;
                if (source && source.clip) problems.Add(name + " was saved holding the clip " + source.clip.name + ".");
            }
        }

        private static void CheckSavedMenu(Transform game, List<string> problems)
        {
            var canvas = Expect(game, GamePanel.CanvasName, problems);
            if (!canvas) return;
            foreach (string path in new[] { "Title", "Title/Warning preview", "Instructions", "Choice list", "Choice list/Content" })
                Expect(canvas, path, problems);
            for (int i = 1; i <= 5; ++i) Expect(canvas, "Choice " + i + "/Label", problems);
            Expect(game, "Start count/Count", problems);
            // Shown only on their own pages, so they are saved switched off.
            foreach (var hidden in new[] { canvas.Find("Title/Warning preview"), canvas.Find("Choice list"), game.Find("Start count") })
                if (hidden && hidden.gameObject.activeSelf) problems.Add(hidden.name + " was saved switched on.");
            if (canvas.GetComponents<UnityEngine.UI.Image>().Length != 1) problems.Add("The menu canvas needs one background Image.");
            if (Mathf.Abs(canvas.localScale.x - GamePanel.CanvasScale) > 1e-6f)
                problems.Add("The menu canvas is saved at scale " + canvas.localScale.x + ", not the " + GamePanel.CanvasScale + " it has at Play.");
            foreach (var (row, text) in new[] { ("Choice 1/Label", "Start"), ("Choice 5/Label", "Development") })
            {
                var label = canvas.Find(row);
                if (label && label.GetComponent<UnityEngine.UI.Text>().text != text) problems.Add(row + " should read " + text + ".");
            }
            if (canvas.GetComponentsInChildren<RayInteractable>(true).Length != 1 || canvas.GetComponentsInChildren<PokeInteractable>(true).Length != 1)
                problems.Add("The menu canvas needs exactly one Meta ray surface and one poke surface.");
            foreach (var item in game.GetComponentsInChildren<Transform>(true))
                if (item.name.StartsWith("Editor ")) problems.Add("The editor-only helper " + item.name + " was saved in the scene.");
            var preview = canvas.Find("Title/Warning preview");
            if (preview) RequireSavedAssets(preview, problems);
            var panel = game.GetComponent<GamePanel>();
            var heading = canvas.Find("Title");
            if (panel && heading && (!Same(canvas.GetComponent<UnityEngine.UI.Image>().color, Field(panel, "paper")) ||
                !Same(heading.GetComponent<UnityEngine.UI.Text>().color, Field(panel, "ink"))))
                problems.Add("The saved menu is not drawn in the colours set on GamePanel.");
        }

        private static void CheckSavedChamber(Scene scene, GameFlow game, List<string> problems)
        {
            var roots = scene.GetRootGameObjects().Where(root => root.name == "Fall chamber").ToArray();
            if (roots.Length != 1)
            {
                problems.Add(roots.Length == 0 ? "missing: Fall chamber (scene root)" : "saved " + roots.Length + " times: Fall chamber");
                return;
            }
            var chamber = roots[0].transform;
            // GameObject.Find must not see the chamber until a fall or a look through an opening shows it.
            if (chamber.gameObject.activeSelf) problems.Add("Fall chamber was saved switched on.");
            foreach (string part in ChamberParts) Expect(chamber, part, problems);
            var splash = chamber.Find("Water contact splash");
            if (splash && splash.gameObject.activeSelf)
                problems.Add("Water contact splash was saved switched on; it would play every time the chamber shows.");
            // Particles takes its mesh from the ending that plays.
            RequireSavedAssets(chamber, problems, "Particles");
            if (SavedChamber(game) != chamber) problems.Add("FallSequence on Floor Fracture does not reference the saved Fall chamber.");
        }

        private static void CheckSavedBoard(Scene scene, GameFlow game, List<string> problems)
        {
            var roots = scene.GetRootGameObjects().Where(root => root.name == "Confirmed glass board").ToArray();
            if (roots.Length != 1)
            {
                problems.Add(roots.Length == 0 ? "missing: Confirmed glass board (scene root)" : "saved " + roots.Length + " times: Confirmed glass board");
                return;
            }
            var board = roots[0].transform;
            if (!board.GetComponent<GlassBoardView>() || !board.GetComponent<ShardPool>()) problems.Add("The saved board needs GlassBoardView and ShardPool.");
            if (SavedBoard(game) != board.GetComponent<GlassBoardView>()) problems.Add("GameFlow does not reference the saved board.");
            for (int i = 0; i < PreviewSide * PreviewSide; ++i)
            {
                var pane = Expect(board, "Glass " + i % PreviewSide + ", " + i / PreviewSide, problems);
                if (!pane) continue;
                foreach (string part in PaneParts) Expect(pane, part, problems);
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(pane.gameObject);
                if (!prefab || AssetDatabase.GetAssetPath(prefab) != TilePrefabPath) problems.Add(pane.name + " is not a linked instance of the pane prefab.");
            }
            int panes = board.Cast<Transform>().Count(child => child.GetComponent<GlassTile>());
            if (panes != PreviewSide * PreviewSide) problems.Add("The preview board has " + panes + " panes, not " + PreviewSide * PreviewSide + ".");
            var first = board.Find("Glass 0, 0"); var second = board.Find("Glass 1, 0");
            if (first && second && Mathf.Abs(Vector3.Distance(first.localPosition, second.localPosition) - PreviewPitch) > 1e-4f)
                problems.Add("The preview panes are not " + PreviewPitch + " m apart.");
            foreach (string part in BoardParts) Expect(board, part, problems);
            // Saved as the idle game has them: nothing that only a wave shows is switched on.
            foreach (string effect in IdleEffects)
            {
                var parent = board.Find(effect);
                if (parent) foreach (var renderer in parent.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled) problems.Add(effect + "/" + renderer.name + " was saved switched on.");
            }
            foreach (var lamp in board.GetComponentsInChildren<Transform>(true).Where(item => item.name.StartsWith("Lamp ") && item.name != "Lamp hum"))
                if (lamp.gameObject.activeSelf) problems.Add(lamp.name + " was saved switched on.");
            foreach (var renderer in board.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.enabled && (renderer.name == "Fissures" || renderer.name == "Occupied square" || renderer.name == "Opening stencil"))
                    problems.Add(renderer.transform.parent.name + "/" + renderer.name + " was saved switched on.");
            RequireSavedAssets(board, problems);
            // Per-renderer colours are not saved in a scene. The scripts re-apply them, so the Scene view shows the game's resting look.
            var sample = board.Find("Glass 0, 0");
            if (sample && sample.Find("Smoky pane") && !Same(Painted(sample.Find("Smoky pane")), sample.GetComponent<GlassTile>().Resting))
                problems.Add("In edit mode the panes are not drawn in their Resting colour.");
            var outline = board.Find("Perimeter");
            if (outline && !Same(Painted(outline), Field(board.GetComponent<GlassBoardView>(), "outlineColour")))
                problems.Add("In edit mode the board outline is not drawn in its Outline colour.");
            foreach (var item in board.GetComponentsInChildren<Transform>(true))
                if (item.gameObject.layer == 30) problems.Add(item.name + " is on layer 30, which the checks render on.");

            var tile = AssetDatabase.LoadAssetAtPath<GameObject>(TilePrefabPath);
            if (!tile) { problems.Add("missing: " + TilePrefabPath); return; }
            foreach (string part in PaneParts) Expect(tile.transform, part, problems);
            RequireSavedAssets(tile.transform, problems);
        }

        private static Object SavedBoard(GameFlow flow) => new SerializedObject(flow).FindProperty("savedBoard")?.objectReferenceValue;

        private static Object SavedChamber(GameFlow flow)
        {
            var sequence = flow.GetComponent<FallSequence>();
            return sequence ? new SerializedObject(sequence).FindProperty("chamber")?.objectReferenceValue : null;
        }

        private static readonly int ColourId = Shader.PropertyToID("_Color");

        // The colour a script painted on one renderer; clear when it painted none.
        private static Color Painted(Component part)
        {
            var block = new MaterialPropertyBlock();
            part.GetComponent<Renderer>().GetPropertyBlock(block);
            return block.GetColor(ColourId);
        }

        // A colour field as the Inspector shows it.
        private static Color Field(Object owner, string name) => new SerializedObject(owner).FindProperty(name).colorValue;

        // Menu colours are stored in 8 bits per channel.
        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < .004f && Mathf.Abs(a.g - b.g) < .004f && Mathf.Abs(a.b - b.b) < .004f && Mathf.Abs(a.a - b.a) < .004f;

        // A scene file cannot hold a mesh or material generated at Play; a saved object needs asset files to show in edit mode.
        private static void RequireSavedAssets(Transform root, List<string> problems, string meshFreeName = null)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name != meshFreeName && (!filter.sharedMesh || !EditorUtility.IsPersistent(filter.sharedMesh)))
                    problems.Add(root.name + "/" + filter.name + " has no saved mesh file.");
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                if (!renderer.sharedMaterial || !EditorUtility.IsPersistent(renderer.sharedMaterial))
                    problems.Add(root.name + "/" + renderer.name + " has no saved material file.");
        }

        // GameSound now wakes with the scene, long before GameFlow.Start applies the player's volume.
        private static void CheckSoundWakesAtSavedVolume(List<string> problems)
        {
            bool had = PlayerPrefs.HasKey("ff.volume");
            int prior = PlayerPrefs.GetInt("ff.volume");
            var probe = new GameObject("Muted wake probe") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                PlayerPrefs.SetInt("ff.volume", 0);
                var sound = probe.AddComponent<GameSound>();
                typeof(GameSound).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sound, null);
                var music = probe.transform.Find("Background music");
                if (sound.MasterVolume != 0f || !music || music.GetComponent<AudioSource>().volume != 0f)
                    problems.Add("A muted player's music would be audible when the scene wakes.");
            }
            catch (TargetInvocationException e) { problems.Add("GameSound could not wake on an empty object: " + e.InnerException.Message); }
            finally
            {
                if (had) PlayerPrefs.SetInt("ff.volume", prior); else PlayerPrefs.DeleteKey("ff.volume");
                Object.DestroyImmediate(probe);
            }
        }

        // Counted with inactive objects: the saved object must be the only one, never joined by a generated twin.
        private static void RequireOne(Transform[] everything, string name)
        {
            int count = everything.Count(item => item.name == name);
            Require(count == 1, "Play has " + count + " objects named " + name + "; the saved one must be the only one.");
        }

        private static void VerifySavedSceneAdopted()
        {
            var everything = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (string name in PlainSounds.Concat(SpatialSounds)) RequireOne(everything, name);
            Require(game.GetComponents<GameSound>().Length == 1, "Play added a second GameSound beside the saved one.");
            var sound = game.GetComponent<GameSound>();
            float music = game.transform.Find("Background music").GetComponent<AudioSource>().volume;
            float musicLevel = new SerializedObject(sound).FindProperty("music").floatValue;
            Require(music <= sound.MasterVolume * musicLevel + .001f, "The music is louder than the player's volume allows.");
            if (Quick) Require(sound.MasterVolume == 0f && music == 0f, "A muted player's music was audible at Welcome.");

            var canvas = game.transform.Find(GamePanel.CanvasName);
            var menu = canvas.GetComponentsInChildren<Transform>(true);
            foreach (string name in new[] { "Title", "Instructions", "Choice list", "Content", "Warning preview", "Choice 1" }) RequireOne(menu, name);
            RequireOne(everything, "Start count");
            Require(game.GetComponents<GamePanel>().Length == 1, "Play added a second GamePanel beside the saved one.");
            Require(canvas.Find("Choice 1/Label").GetComponent<UnityEngine.UI.Text>().text == "Start",
                "The Welcome page is not using the saved first row.");

            var chambers = everything.Where(item => item.name == "Fall chamber").ToArray();
            Require(chambers.Length == 1, "Play has " + chambers.Length + " fall chambers; the saved one must be the only one.");
            Require(!chambers[0].gameObject.activeSelf, "The fall chamber is switched on at Welcome.");
            Require(SavedChamber(game) == chambers[0], "The fall sequence is not using the chamber saved in the scene.");

            var boards = Object.FindObjectsByType<GlassBoardView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Require(boards.Length == 1 && boards[0] == game.BoardView && SavedBoard(game) == game.BoardView,
                "Play is not using the one board saved in the scene.");
            foreach (string name in IdleEffects.Concat(new[] { "Opening stencil", "Falling fragments", "Occupied square", "Perimeter" }))
                RequireOne(everything, name);
            VerifyBoardFitsLayout("the first fit");

            // An edit to a colour field is what the game draws.
            var pane = game.BoardView.GetComponentsInChildren<GlassTile>().First();
            Require(Same(Painted(pane.transform.Find("Smoky pane")), pane.Resting), "A resting pane is not drawn in its Resting colour.");
            Require(Same(Painted(game.BoardView.transform.Find("Perimeter")), Field(game.BoardView, "outlineColour")),
                "The board outline is not drawn in its Outline colour.");
            Require(Same(canvas.GetComponent<UnityEngine.UI.Image>().color, Field(game.Panel, "paper")) &&
                Same(canvas.Find("Title").GetComponent<UnityEngine.UI.Text>().color, Field(game.Panel, "ink")),
                "The menu is not drawn in the colours set on GamePanel.");
            // Whatever was left switched on in the scene, nothing that only a wave shows is on at Welcome.
            foreach (string effect in IdleEffects)
            {
                var parent = game.BoardView.transform.Find(effect);
                Require(parent.GetComponentsInChildren<MeshRenderer>(true).All(renderer => !renderer.enabled),
                    "A renderer under " + effect + " is showing at Welcome.");
                Require(parent.Cast<Transform>().All(child => !child.name.StartsWith("Lamp ") || !child.gameObject.activeSelf),
                    "A lamp is switched on at Welcome.");
            }
        }

        // A board fitted to a new layout has exactly that layout's panes, under that layout's names.
        private static void VerifyBoardFitsLayout(string fit)
        {
            var panes = game.BoardView.transform.Cast<Transform>().Where(child => child.GetComponent<GlassTile>()).Select(child => child.name).ToArray();
            var expected = new HashSet<string>(PlayablePanes.Select(i => "Glass " + i % game.State.Width + ", " + i / game.State.Width));
            Require(panes.Length == expected.Count && expected.SetEquals(panes),
                "After " + fit + " the board holds " + panes.Length + " panes for a layout of " + expected.Count + ", or misnames them.");
        }

        // The same board object is fitted again and again. Nothing from the fit before may show or sound.
        private static void VerifyRefit(string fit)
        {
            var view = game.BoardView;
            Require(SavedBoard(game) == view, "After " + fit + " the game is no longer using the board saved in the scene.");
            VerifyBoardFitsLayout(fit);
            Require(view.Fog == WaveHazards.None, "After " + fit + " the board still reports fog from the fit before.");
            foreach (string effect in IdleEffects)
                Require(view.transform.Find(effect).GetComponentsInChildren<MeshRenderer>(true).All(renderer => !renderer.enabled),
                    "After " + fit + " a renderer under " + effect + " is still showing.");
            Require(view.GetComponentsInChildren<GlassTile>(true).All(tile => tile.gameObject.activeSelf && !tile.transform.Find("Fissures").GetComponent<MeshRenderer>().enabled),
                "After " + fit + " a pane is missing or still shows its warning.");
            Require(view.GetComponentsInChildren<AudioSource>(true).All(source => !source.gameObject.activeInHierarchy || !source.isPlaying),
                "After " + fit + " a sound under the board is still playing.");
            Require(!view.transform.Find("Falling fragments").GetComponent<MeshRenderer>().enabled && !view.transform.Find("Opening stencil").GetComponent<MeshRenderer>().enabled,
                "After " + fit + " shards or an opening from the fit before are still showing.");
        }
    }
}
