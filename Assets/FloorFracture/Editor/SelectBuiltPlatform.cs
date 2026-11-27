using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace FloorFracture.Editor
{
    // Unity OpenXR's Meta Quest validation (MetaQuestFeature.cs:538) reads the platform tab selected in the Editor,
    // not the platform being built. With the desktop tab selected it finds no Meta Quest feature and throws, which
    // fails a Quest build from Build Profiles. Select the built platform before OpenXR's check (order 0) runs.
    // shortcut: works around a package bug; delete once com.unity.xr.openxr reads the build's own platform there.
    internal sealed class SelectBuiltPlatform : IPreprocessBuildWithReport
    {
        public int callbackOrder => -2;
        public void OnPreprocessBuild(BuildReport report) =>
            EditorUserBuildSettings.selectedBuildTargetGroup = report.summary.platformGroup;
    }
}
