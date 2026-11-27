using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    // Checks of the save command itself. Each changes the scene to provoke the command, then puts the saved files back.
    public static partial class GameplaySmoke
    {
        // One object from every area, including ones the command had already adopted earlier in the session.
        private static readonly string[] DeletedForCheck =
        {
            "Floor Fracture/Thunder 1", "Floor Fracture/Start count",
            "Floor Fracture/Floor Fracture Menu/Instructions", "Floor Fracture/Floor Fracture Menu/Choice 1",
            "Floor Fracture/Floor Fracture Menu/Choice list/Content",
            "Fall chamber/Lava flames",
            "Confirmed glass board/Glass 2, 2", "Confirmed glass board/Floor fog/Floor fog 1", "Confirmed glass board/Overhead lights/Lamp 1"
        };

        /// <summary>Deleted objects come back, and a changed menu width reaches the Settings sample pane. Runs without Play.</summary>
        [MenuItem("Tools/Floor Fracture/Check the save command")]
        public static void CheckSaveCommand() => WithSavedFilesRestored(() =>
        {
            SceneObjectsSetup.Save();
            foreach (string path in DeletedForCheck)
            {
                var found = Locate(path);
                Require(found, "The scene has no " + path + " to delete for this check.");
                Object.DestroyImmediate(found.gameObject);
            }
            SceneObjectsSetup.Save();
            CheckSavedScene();
            var list = Locate("Floor Fracture/" + GamePanel.CanvasName + "/Choice list").GetComponent<UnityEngine.UI.ScrollRect>();
            Require(list.content && list.viewport, "The rebuilt choice list has nothing to scroll.");

            var panel = new SerializedObject(Locate("Floor Fracture").GetComponent<GamePanel>());
            panel.FindProperty("width").floatValue = 800;
            panel.ApplyModifiedPropertiesWithoutUndo();
            SceneObjectsSetup.Save();
            // The sample sits half its 144-unit size in from the right end of the title, which spans the width less both margins.
            float expected = 800 - 2 * panel.FindProperty("margin").floatValue - 72;
            float placed = Locate("Floor Fracture/" + GamePanel.CanvasName + "/Title/Warning preview").localPosition.x;
            Require(Mathf.Abs(placed - expected) < .5f, "With the menu 800 wide the Settings sample pane sits at " + placed + ", not " + expected + ".");

            // One row deleted by hand and no save since, as Play would find it: a page with a single choice still hides every other row.
            Object.DestroyImmediate(Locate("Floor Fracture/" + GamePanel.CanvasName + "/Choice 2").gameObject);
            Locate("Floor Fracture").GetComponent<GamePanel>().BuildSample("Rows", "", "Only");
            foreach (Transform child in Locate("Floor Fracture/" + GamePanel.CanvasName))
                if (child.name.StartsWith("Choice ") && child.name != "Choice 1" && child.name != "Choice list")
                    Require(!child.gameObject.activeSelf, "With Choice 2 deleted, " + child.name + " still shows on a page with one choice.");
            Debug.Log("[Floor Fracture verification] Save command check passed.");
        });

        /// <summary>Prepare game scene keeps the saved menu the size its page gave it.</summary>
        public static void CheckPrepareKeepsMenu() => WithSavedFilesRestored(() =>
        {
            SceneObjectsSetup.Save();
            var size = ((RectTransform)Locate("Floor Fracture/" + GamePanel.CanvasName)).sizeDelta;
            GameProjectSetup.Prepare();
            var after = ((RectTransform)Locate("Floor Fracture/" + GamePanel.CanvasName)).sizeDelta;
            Require(after == size, "Prepare game scene resized the saved menu from " + size + " to " + after + ".");
            CheckSavedScene();
            Debug.Log("[Floor Fracture verification] Prepare game scene kept the saved menu.");
        });

        // The check edits and saves the scene and the pane prefab. Whatever it finds, both files end as they began.
        private static void WithSavedFilesRestored(Action check)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run this check from Edit Mode.");
            string[] files = { ScenePath, TilePrefabPath };
            var kept = files.Select(File.ReadAllBytes).ToArray();
            try { check(); }
            finally
            {
                // Leave the game scene first. Replacing the file of an open scene that has unsaved changes left the Editor refusing to reload scripts.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                for (int i = 0; i < files.Length; ++i) File.WriteAllBytes(files[i], kept[i]);
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(ScenePath);
            }
        }

        // "Root/child/grandchild" in the open game scene, inactive objects included.
        private static Transform Locate(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath);
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            var root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == rootName);
            if (!root) return null;
            return slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
        }
    }
}
