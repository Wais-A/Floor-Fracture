using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.XR.OpenXR;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private const BuildTargetGroup Desktop = BuildTargetGroup.Standalone;

        /// <summary>
        /// On the Mac the Meta XR Simulator drops world-space UI under Single Pass Instanced: the menu is missing or
        /// flickers far away. The desktop profile must be Multi-pass, and Meta's setup rule that switches it must stay ignored.
        /// </summary>
        [MenuItem("Tools/Floor Fracture/Check desktop XR profile")]
        public static void CheckDesktopXrProfile()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(Desktop);
            Require(settings, "The project has no desktop OpenXR settings.");
            Require(settings.renderMode == OpenXRSettings.RenderMode.MultiPass,
                "The desktop OpenXR Render Mode is " + settings.renderMode + ", so the menu is missing in the Meta XR Simulator. " +
                "Set it to Multi-pass under Project Settings > XR Plug-in Management > OpenXR, desktop tab.");
            // Meta's Project Setup Tool "fixes" this rule by switching the loaded desktop profile, without saving it.
            foreach (var (message, ignored) in DesktopSetupRules())
                Require(ignored || !message.Contains("render mode"),
                    "Meta's Project Setup rule \"" + message + "\" is not ignored for the desktop platform. Its fix removes the menu " +
                    "from the Meta XR Simulator. Ignore it under Project Settings > Meta XR, desktop tab.");
        }

        // Meta keeps its rules internal, so they are read by name.
        private static IEnumerable<(string message, bool ignored)> DesktopSetupRules()
        {
            var list = Type.GetType("OVRProjectSetup, Oculus.VR.Editor")?.GetMethod("GetTasks",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(BuildTargetGroup) }, null);
            Require(list != null, "Could not read Meta's Project Setup rules; update CheckDesktopXrProfile for this SDK.");
            foreach (var rule in (IEnumerable)list.Invoke(null, new object[] { Desktop }))
            {
                var holder = rule.GetType().GetProperty("Message")?.GetValue(rule);
                var message = holder?.GetType().GetMethod("GetValue")?.Invoke(holder, new object[] { Desktop }) as string;
                var isIgnored = rule.GetType().GetMethod("IsIgnored");
                if (message != null && isIgnored != null)
                    yield return (message, (bool)isIgnored.Invoke(rule, new object[] { Desktop }));
            }
        }
    }
}
