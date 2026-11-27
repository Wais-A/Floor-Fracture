using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorFracture.Editor
{
    /// <summary>
    /// Saves the objects the game used to build at Play into the game scene, by running the same
    /// builders in edit mode. Builders adopt what is already saved, so running this again adds only
    /// what is missing and keeps edits made in the Inspector.
    /// </summary>
    public static class SceneObjectsSetup
    {
        public const string MeshFolder = "Assets/FloorFracture/Generated", MaterialFolder = "Assets/FloorFracture/Resources/Materials";

        [MenuItem("Tools/Floor Fracture/Save game objects into scene")]
        public static void Save()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before saving the game objects.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(GameProjectSetup.MainScene);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(GameProjectSetup.MainScene, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                GameFlow flow = null;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var found in root.GetComponentsInChildren<GameFlow>(true))
                    {
                        if (flow) throw new InvalidOperationException("Scene contains more than one GameFlow.");
                        flow = found;
                    }
                if (!flow) throw new InvalidOperationException("Prepare the game scene first: it has no GameFlow.");
                var game = flow.gameObject;
                var sound = SceneParts.Require<GameSound>(game);
                var narration = SceneParts.Require<OfflineNarration>(game);
                SceneParts.Require<FallSequence>(game);
                var panel = SceneParts.Require<GamePanel>(game);
                Saved.Clear();
                sound.Build();
                narration.Build();

                // The menu's buttons change per page, so the scene holds the Welcome page, where the menu
                // would open for a head 1.6 m up at the origin looking along +Z.
                panel.BuildSample("Floor Fracture", "Watch which panes flash. Step onto one that stayed clear before the glass breaks.",
                    "Start", "Choose level", "Settings", "Play area", "Development");
                var canvas = (RectTransform)game.transform.Find(GamePanel.CanvasName);
                // Set through the RectTransform: a plain Transform position leaves its saved anchored position stale until a reload.
                canvas.anchoredPosition3D = new Vector3(0, 1.36f, .56f);
                canvas.localRotation = Quaternion.identity;
                Snapshot(canvas.Find("Title/Warning preview"));
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, GameProjectSetup.MainScene))
                    throw new IOException("Could not save the Floor Fracture scene.");
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (openedHere && previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // A generated object and the file it was saved to, within one Save.
        private static readonly Dictionary<UnityEngine.Object, UnityEngine.Object> Saved = new Dictionary<UnityEngine.Object, UnityEngine.Object>();

        /// <summary>
        /// A scene or prefab file cannot hold what a builder generates at Play. Under root, every generated
        /// mesh and material is replaced by an asset file of the same name, so the saved object shows in edit mode.
        /// </summary>
        internal static void Snapshot(Transform root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh && !EditorUtility.IsPersistent(filter.sharedMesh))
                    filter.sharedMesh = SaveCopy(filter.sharedMesh, MeshFolder, ".asset");
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; ++i)
                    if (materials[i] && !EditorUtility.IsPersistent(materials[i]))
                        materials[i] = SaveCopy(materials[i], MaterialFolder, ".mat");
                renderer.sharedMaterials = materials;
            }
        }

        private static T SaveCopy<T>(T generated, string folder, string extension) where T : UnityEngine.Object
        {
            if (Saved.TryGetValue(generated, out var done)) return (T)done;
            if (string.IsNullOrEmpty(generated.name)) throw new InvalidOperationException("A generated " + typeof(T).Name + " has no name to save it under.");
            Directory.CreateDirectory(folder);
            string path = folder + "/" + generated.name + extension;
            var file = AssetDatabase.LoadAssetAtPath<T>(path);
            if (file && Saved.ContainsValue(file))
                throw new InvalidOperationException("Two generated objects are both named " + generated.name + ".");
            if (file) EditorUtility.CopySerialized(generated, file);
            else file = UnityEngine.Object.Instantiate(generated);
            file.name = generated.name;
            file.hideFlags = HideFlags.None;
            if (AssetDatabase.Contains(file)) EditorUtility.SetDirty(file);
            else AssetDatabase.CreateAsset(file, path);
            Saved[generated] = file;
            return file;
        }
    }
}
