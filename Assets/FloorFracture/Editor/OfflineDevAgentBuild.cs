using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FloorFracture.Editor
{
    // Meta SDK 209 injects its editor connection into this Resources asset at callback order 1.
    internal sealed class PreserveDevAgentEditorSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1;
        public void OnPreprocessBuild(BuildReport report) => OfflineDevAgentBuild.PreserveEditorSettings();
    }

    internal sealed class OfflineDevAgentBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string AssetPath = "Assets/Resources/DevAgentSettings.asset";
        private const string Backup = "Library/FloorFracture.DevAgentSettings.original";
        public int callbackOrder => 2;

        internal static void PreserveEditorSettings()
        {
            Restore();
            var settings = AssetDatabase.LoadMainAssetAtPath(AssetPath);
            if (!settings) return;
            AssetDatabase.SaveAssetIfDirty(settings);
            BackUpFile();
        }

        private static void BackUpFile()
        {
            File.Copy(AssetPath, Backup + ".tmp", true);
            File.Move(Backup + ".tmp", Backup);
            ScheduleRecovery();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(AssetPath)) return;
            var settings = AssetDatabase.LoadMainAssetAtPath(AssetPath);
            if (!settings) throw new BuildFailedException("Could not read the local Meta DevAgent settings.");
            // A fresh checkout can acquire its first settings asset in Meta's earlier callback.
            if (!File.Exists(Backup)) BackUpFile();

            // A fresh instance has the SDK's disabled, unconfigured defaults. No editor token,
            // host address or optional Wit configuration is copied into the player.
            var offline = ScriptableObject.CreateInstance(settings.GetType());
            try
            {
                offline.name = settings.name;
                EditorUtility.CopySerialized(offline, settings);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            finally { Object.DestroyImmediate(offline); }
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        [InitializeOnLoadMethod]
        private static void ScheduleRecovery()
        {
            if (!File.Exists(Backup) && !File.Exists(Backup + ".tmp")) return;
            EditorApplication.delayCall -= RecoverWhenIdle;
            EditorApplication.delayCall += RecoverWhenIdle;
            EditorApplication.quitting -= RecoverWhenIdle;
            EditorApplication.quitting += RecoverWhenIdle;
        }

        private static void RecoverWhenIdle()
        {
            if (BuildPipeline.isBuildingPlayer) { ScheduleRecovery(); return; }
            Restore();
        }

        private static void Restore()
        {
            File.Delete(Backup + ".tmp");
            if (!File.Exists(Backup)) return;
            // Import the original file after Meta's post-build callback, including on the next
            // editor open if an interrupted build never reached postprocessing.
            File.Copy(Backup, AssetPath, true);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            File.Delete(Backup);
            EditorApplication.delayCall -= RecoverWhenIdle;
            EditorApplication.quitting -= RecoverWhenIdle;
        }
    }
}
