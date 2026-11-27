using System;
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
                SceneParts.Require<GamePanel>(game);
                sound.Build();
                narration.Build();
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
    }
}
