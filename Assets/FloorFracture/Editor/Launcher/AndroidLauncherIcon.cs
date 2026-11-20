#if UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace FloorFracture.Editor
{
    public sealed class AndroidLauncherIcon : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            // Unity 6.6 writes custom adaptive layers but leaves its own unversioned icon in the launcher.
            string source = Path.Combine(Application.dataPath, "FloorFracture/Editor/Launcher/Legacy");
            string resources = Path.GetFullPath(Path.Combine(unityLibraryPath, "../launcher/src/main/res"));
            foreach (string density in new[] { "ldpi", "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
            {
                string directory = Path.Combine(resources, "mipmap-" + density);
                Directory.CreateDirectory(directory);
                File.Copy(Path.Combine(source, density + ".png"), Path.Combine(directory, "app_icon.png"), true);
            }
        }
    }
}
#endif
