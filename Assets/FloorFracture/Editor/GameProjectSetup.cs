using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace FloorFracture.Editor
{
    public static class GameProjectSetup
    {
#if UNITY_EDITOR_OSX
        [InitializeOnLoadMethod]
        private static void RegisterBatchShutdown()
        {
            if (Application.isBatchMode)
                EditorApplication.quitting += StopInteractionSdkBatchSession;
        }

        private static void StopInteractionSdkBatchSession()
        {
            // ISDK 209 starts telemetry on Editor import. Its runtime shutdown hook
            // does not run for Edit Mode tests or a standalone batch build.
            EditorApplication.quitting -= StopInteractionSdkBatchSession;
            var telemetry = typeof(Oculus.Interaction.Grabbable).Assembly.GetType("Oculus.Interaction.Telemetry.ISDKEngineTelemetryNative");
            var shutdown = telemetry?.GetMethod("ShutDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (shutdown != null)
            {
                shutdown.Invoke(null, null);
                Debug.Log("[Floor Fracture] Closed the Interaction SDK batch telemetry session.");
            }
        }
#endif
        public const string MainScene="Assets/FloorFracture/Scenes/FloorFracture.unity";
        public const string TilePrefab="Assets/FloorFracture/Resources/GlassTile.prefab";

        [MenuItem("Tools/Floor Fracture/Prepare game scene")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/FloorFracture/Scenes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureMixedReality();
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(TilePrefab))
            {
                var go=new GameObject("GlassTile"); go.AddComponent<GlassTile>();
                PrefabUtility.SaveAsPrefabAsset(go,TilePrefab); UnityEngine.Object.DestroyImmediate(go);
            }
            Scene previous=SceneManager.GetActiveScene();
            Scene scene=SceneManager.GetSceneByPath(MainScene);
            bool openedHere=!scene.IsValid()||!scene.isLoaded;
            if(openedHere)
                scene=File.Exists(MainScene)?EditorSceneManager.OpenScene(MainScene,OpenSceneMode.Additive):
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RoomBoardPlacement room=RoomSceneSetup.InstallIntoActiveScene();
                GameFlow flow=null;
                foreach(var root in scene.GetRootGameObjects())
                {
                    var found=root.GetComponentInChildren<GameFlow>(true);
                    if(!found) continue;
                    if(flow) throw new InvalidOperationException("Scene contains more than one GameFlow.");
                    flow=found;
                }
                if(!flow) flow=new GameObject("Floor Fracture").AddComponent<GameFlow>();
                flow.Room=room;
                foreach(var root in scene.GetRootGameObjects())
                {
                    var rig=root.GetComponentInChildren<OVRCameraRig>(true);
                    if(rig) flow.Rig=rig;
                }
                if(!flow.Rig) throw new InvalidOperationException("Room setup produced no tracked rig.");
                MenuSceneSetup.Install(flow.transform);
                StoneSceneSetup.Install();
                SpatialAudioSceneSetup.Install();
                Light keyLight=null;
                foreach(var root in scene.GetRootGameObjects())
                    if(root.name=="Glass and controllers light") keyLight=root.GetComponent<Light>();
                if(!keyLight)
                {
                    keyLight=new GameObject("Glass and controllers light").AddComponent<Light>();
                    keyLight.type=LightType.Directional; keyLight.shadows=LightShadows.None;
                    keyLight.color=new Color(.89f,.96f,1f); keyLight.intensity=1.05f;
                    keyLight.transform.rotation=Quaternion.Euler(55,-35,0);
                    RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight=new Color(.52f,.56f,.59f);
                }
                EditorUtility.SetDirty(flow);
                if(!EditorSceneManager.SaveScene(scene,MainScene)) throw new IOException("Could not save the Floor Fracture scene.");
                EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(MainScene,true)};
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if(openedHere&&previous.IsValid()&&previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true);
                }
            }
            Debug.Log("[Floor Fracture] Saved main scene, tile prefab, Android build list and MR configuration.");
        }

        static void ConfigureMixedReality()
        {
            const BuildTargetGroup android=BuildTargetGroup.Android;
            var manager=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(android)?.Manager;
            if(!manager) throw new InvalidOperationException("Android XR Plug-in Management settings are missing.");
            if(!manager.activeLoaders.OfType<OpenXRLoader>().Any()&&
                !XRPackageMetadataStore.AssignLoader(manager,typeof(OpenXRLoader).FullName,android))
                throw new InvalidOperationException("Could not assign the Android OpenXR loader.");
            var loader=manager.activeLoaders.OfType<OpenXRLoader>().First();
            if(manager.activeLoaders.Count!=1&&!manager.TrySetLoaders(new List<XRLoader>{loader}))
                throw new InvalidOperationException("Could not select OpenXR as the single Android loader.");
            EditorUtility.SetDirty(manager);
            FeatureHelpers.RefreshFeatures(android);
            var settings=OpenXRSettings.GetSettingsForBuildTargetGroup(android);
            EnableFeature<MetaQuestFeature>(settings);
            EnableFeature<ARSessionFeature>(settings);
            EnableFeature<AROcclusionFeature>(settings);
            EnableFeature<OculusTouchControllerProfile>(settings);
            EnableFeature<HandTracking>(settings);
            var config=OVRProjectConfig.CachedProjectConfig;
            config.anchorSupport=OVRProjectConfig.AnchorSupport.Enabled;
            config.sceneSupport=OVRProjectConfig.FeatureSupport.Required;
            config.insightPassthroughSupport=OVRProjectConfig.FeatureSupport.Required;
            config.handTrackingSupport=OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.systemLoadingScreenBackground=OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            OVRProjectConfig.CommitProjectConfig(config);
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
        }

        static void EnableFeature<T>(OpenXRSettings settings) where T:OpenXRFeature
        {
            var feature=settings?settings.GetFeature<T>():null;
            if(!feature) throw new InvalidOperationException("Android OpenXR feature is missing: "+typeof(T).Name);
            if(feature.enabled) return;
            feature.enabled=true;
            EditorUtility.SetDirty(feature);
        }

        public static void BuildAndroid()
        {
            string dest=Environment.GetEnvironmentVariable("FLOOR_FRACTURE_APK");
            if(string.IsNullOrWhiteSpace(dest)) dest=Path.GetFullPath("Builds/Quest/FloorFracture.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            try
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                    scenes=new[]{MainScene},locationPathName=dest,target=BuildTarget.Android,
                    options=Environment.GetEnvironmentVariable("FLOOR_FRACTURE_DEV")=="1"?BuildOptions.Development:BuildOptions.None});
                Debug.Log($"[Floor Fracture] build={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} path={report.summary.outputPath}");
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException("Floor Fracture Android build failed.");
            }
            finally
            {
#if UNITY_EDITOR_OSX
                if (Application.isBatchMode) StopInteractionSdkBatchSession();
#endif
            }
        }
    }
}
