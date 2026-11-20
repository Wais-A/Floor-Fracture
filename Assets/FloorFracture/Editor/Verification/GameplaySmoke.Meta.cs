using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.BuildingBlocks;
using Meta.XR.EnvironmentDepth;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private const string StockDepthMaterial = "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Occlusion/Materials/DepthMaterial.mat";
        private static readonly HashSet<string> DemoPrefabPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Cube/Prefabs/[BB] Cube.prefab",
            "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Occlusion/Prefabs/ExampleCubePrefab.prefab",
            "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Occlusion/Prefabs/Occlusion.prefab",
            "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/InstantContentPlacement/Prefabs/RaycastVisualize.prefab",
            "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/SharedSpatialAnchorCore/Prefabs/SharedSpatialAnchorCore.prefab"
        };

        private static bool AuthoredDepthEnabled()
        {
            // The stock provider disables itself at runtime on a desktop without a depth sensor.
            // Read the actual scene's authored enablement before that SDK lifecycle runs.
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool preview = !scene.IsValid() || !scene.isLoaded;
            if (preview) scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var managers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EnvironmentDepthManager>(true)).ToArray();
                return managers.Length == 1 && managers[0].enabled && managers[0].gameObject.activeInHierarchy;
            }
            finally { if (preview) EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void VerifyRoomVisuals(GameObject[] roots, BuildingBlock[] markers)
        {
            var guardian = roots.SelectMany(root => root.GetComponentsInChildren<RoomGuardian>(true)).Single();
            var guardianBlock = markers.Single(marker => marker.BlockId == MetaRoomBlocks.GuardianId);
            var effect = guardian.GetComponent<EffectMesh>();
            Require(guardian.transform == guardianBlock.transform && !guardianBlock.gameObject.activeInHierarchy &&
                effect && !effect.isActiveAndEnabled && effect.EffectMeshObjects.Count == 0,
                "The gameplay scene revealed the SDK wall and furniture overlay.");

            var depth = roots.SelectMany(root => root.GetComponentsInChildren<EnvironmentDepthManager>(true)).Single();
            var block = markers.Single(marker => marker.BlockId == MetaRoomBlocks.DepthProviderId);
            report.depthSupportedInFixture = EnvironmentDepthManager.IsSupported;
            Require(report.depthProviderEnabledInAuthoredScene && depth.gameObject.activeInHierarchy &&
                (depth.transform == block.transform || depth.transform.IsChildOf(block.transform)) && (!report.depthSupportedInFixture || depth.enabled),
                "The enabled SDK Occlusion Dependencies block did not supply the scene's sole environment depth manager.");

            var models = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true));
            foreach (var model in models)
            {
                var item = model.gameObject;
                Require(item.name != "ExampleCubePrefab" && item.name != "RaycastVisualize" && item.name != "[BuildingBlock] Occlusion" &&
                    item.name != "SharedSpatialAnchorCore" && !(model.parent == null && item.name == "Occlusion"),
                    "An SDK demonstration object remained in the gameplay scene: " + item.name);
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(item)) continue;
                string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item);
                Require(!DemoPrefabPaths.Contains(source), "The gameplay scene contains the SDK demonstration prefab " + source);
            }
            Require(!markers.Any(marker => marker.BlockId == "7358e191-33b7-43ca-b066-b2f00531ac66" ||
                marker.BlockId == "975428dc-db04-4e43-be4a-232f192cd3eb"), "An SDK example cube or shared spatial anchor block was installed in the main scene.");
            Record("The gameplay scene keeps the app’s wall and furniture overlay inactive. MRUK fits the board and the brick samples the live stereo environment depth. The saved scene enables one SDK environment depth provider and contains no example cube, occlusion root, raycast marker, or shared anchor.");
        }

        private static void VerifyStoneOcclusion(PracticeStone tutorial)
        {
            var river = tutorial.Body.transform.Find("River stone");
            var renderer = river ? river.GetComponent<MeshRenderer>() : null;
            var stock = AssetDatabase.LoadAssetAtPath<Material>(StockDepthMaterial);
            Require(renderer && renderer.enabled && stock && renderer.sharedMaterial == stock && stock.shader.name == "EnvironmentDepth/OcclusionLit",
                "The river stone did not render with Meta's installed EnvironmentDepth/OcclusionLit material.");
            var marker = renderer.GetComponent<BuildingBlock>();
            Require(marker && marker.BlockId == MetaRoomBlocks.OcclusionId && !string.IsNullOrEmpty(marker.InstanceId),
                "The river stone surface did not retain its real Occlusion block marker.");
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            int colorId = Shader.PropertyToID("_Color");
            var tint = properties.GetColor(colorId);
            Require(properties.HasColor(colorId) && tint.a > 0.5f && tint.maxColorComponent > 0.2f && tint != Color.white,
                "The practice stone's runtime _Color tint was absent from the stock occlusion shader.");
            Record("The actual tutorial stone kept the stock Meta occlusion material and its individual visible _Color tint at runtime.");
        }

        private static void VerifyMenuAndSpatialAudio()
        {
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName).GetComponent<Canvas>();
            var targets = canvas.GetComponentsInChildren<PointableCanvas>(true).Where(target => target.Canvas == canvas).ToArray();
            var rays = targets.SelectMany(target => target.GetComponents<RayInteractable>()).ToArray();
            var pokes = targets.SelectMany(target => target.GetComponents<PokeInteractable>()).ToArray();
            Require(canvas.isActiveAndEnabled && rays.Length == 1 && pokes.Length == 1 && rays[0].isActiveAndEnabled && pokes[0].isActiveAndEnabled,
                "The visible gameplay Canvas needs one SDK ray surface and one SDK poke surface targeting that same Canvas.");
            var row = canvas.transform.Find("Choice 3") as RectTransform;
            Require(row && row.gameObject.activeInHierarchy, "The welcome menu has no visible row for the poke surface check.");
            Vector3 center = row.TransformPoint(row.rect.center);
            Require(pokes[0].SurfacePatch != null && pokes[0].ClosestSurfacePatchHit(center - canvas.transform.forward * 0.02f, out var hit) &&
                Vector3.Distance(hit.Point, center) < 0.04f, "The SDK poke surface did not cover the center of the real menu choice.");
            var hands = game.Rig.GetComponentsInChildren<PokeInteractor>(true).Where(poke => poke.name == "HandPokeInteractor").ToArray();
            Require(hands.Length == 2 && HasPokeHand("Left") && HasPokeHand("Right"),
                "The comprehensive rig did not retain one SDK fingertip poke interactor on each hand.");
            Record("The gameplay Canvas has one registered SDK ray surface and one SDK poke surface. Both SDK hand branches include a poke interactor, and the physical poke surface covers the menu row.");

            var sound = game.GetComponent<GameSound>();
            var positional = sound.GetComponentsInChildren<AudioSource>(true).Where(source => source.transform.parent == sound.transform && source.spatialBlend >= 0.99f).ToArray();
            Require(positional.Length == 10, "The gameplay audio pool did not contain the ten expected positional sources.");
            foreach (var source in positional)
            {
                var spatial = source.GetComponents<MetaXRAudioSource>();
                var block = source.GetComponent<BuildingBlock>();
                Require(block && block.BlockId == "6737262b-4fab-4779-a38b-fe74012fa505" && spatial.Length == 1 &&
                    spatial[0].isActiveAndEnabled && spatial[0].EnableSpatialization,
                    "The pooled game source is missing its enabled Meta spatializer: " + source.name);
            }
            Record("All ten pooled positional glass and ending-impact sources retain the actual Spatial Audio block and one enabled MetaXRAudioSource with spatialization enabled.");

            bool HasPokeHand(string side) => hands.Any(poke => poke.GetComponentsInParent<Transform>(true)
                .Any(parent => parent.name == "ComprehensiveInteractors" + side));
        }
    }
}
