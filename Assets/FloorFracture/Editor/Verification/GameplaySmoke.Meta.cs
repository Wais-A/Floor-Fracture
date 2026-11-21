using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.BuildingBlocks;
using Meta.XR.EnvironmentDepth;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.Input;
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
            var raycast = markers.Single(marker => marker.BlockId == MetaRoomBlocks.EnvironmentRaycastId)
                .GetComponent<Meta.XR.EnvironmentRaycastManager>();
            Require(raycast && raycast.CustomTrackingSpace == game.Rig.trackingSpace &&
                new SerializedObject(game.Room).FindProperty("liveManager").objectReferenceValue == raycast,
                "Placement must use Meta's installed Environment Raycast Manager in the headset tracking space.");
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
            Require(positional.Length == 11, "The gameplay audio pool did not contain the eleven expected positional sources.");
            foreach (var source in positional)
            {
                var spatial = source.GetComponents<MetaXRAudioSource>();
                var block = source.GetComponent<BuildingBlock>();
                Require(block && block.BlockId == "6737262b-4fab-4779-a38b-fe74012fa505" && spatial.Length == 1 &&
                    spatial[0].isActiveAndEnabled && spatial[0].EnableSpatialization,
                    "The pooled game source is missing its enabled Meta spatializer: " + source.name);
            }
            Record("All eleven pooled positional glass, tar and landing sources retain the actual Spatial Audio block and one enabled MetaXRAudioSource with spatialization enabled.");

            bool HasPokeHand(string side) => hands.Any(poke => poke.GetComponentsInParent<Transform>(true)
                .Any(parent => parent.name == "ComprehensiveInteractors" + side));
        }

        private sealed class MenuHandSample : IDataSource<HandDataAsset>
        {
            internal readonly HandDataAsset Data = new();
            public int CurrentDataVersion { get; private set; }
            public event Action InputDataAvailable;
            public HandDataAsset GetData() => Data;
            public void MarkInputDataRequiresUpdate() { CurrentDataVersion++; InputDataAvailable?.Invoke(); }
        }

        private static IEnumerator VerifyHandRecoveryMenu()
        {
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName);
            var rays = game.Rig.GetComponentsInChildren<RayInteractor>(true).Where(r => r.name == "HandRayInteractor").ToArray();
            Require(rays.Length == 2, "The game needs the two hand rays from the Meta Interactions Rig.");
            int retry = 0, rescan = 0;
            void ShowChoices(string message) => game.Panel.ShowRoomRecovery(message, () => retry++, () => rescan++);
            ShowChoices("Looking for your saved floor.");
            game.Panel.Recenter();
            foreach (var authored in rays)
            {
                ShowChoices("Looking for your saved floor.");
                var pose = authored.GetComponentInChildren<HandPointerPose>(true);
                var selector = authored.GetComponentInChildren<IndexPinchSelector>(true);
                Require(pose && selector && new SerializedObject(authored).FindProperty("_rayOrigin").objectReferenceValue == pose.transform &&
                    new SerializedObject(authored).FindProperty("_selector").objectReferenceValue == selector &&
                    new SerializedObject(pose).FindProperty("_hand").objectReferenceValue == new SerializedObject(selector).FindProperty("_hand").objectReferenceValue,
                    "A hand ray did not use the SDK system aim pose and index pinch from the same hand.");
                bool isLeft = authored.GetComponentsInParent<Transform>(true).Any(t => t.name == "ComprehensiveInteractorsLeft");
                var host = new GameObject("Synthetic input for Meta hand ray");
                host.SetActive(false);
                try
                {
                    var sample = new MenuHandSample();
                    var data = sample.Data;
                    data.Config.Handedness = isLeft ? Handedness.Left : Handedness.Right;
                    data.IsDataValid = data.IsConnected = data.IsTracked = data.IsHighConfidence = true;
                    data.HandScale = 1;
                    data.PointerPoseOrigin = data.RootPoseOrigin = PoseOrigin.RawTrackedPose;
                    var hand = host.AddComponent<Hand>();
                    hand.InjectAllDataModifier(DataSource<HandDataAsset>.UpdateModeFlags.AfterPreviousStep, sample, sample, false);
                    var copy = UnityEngine.Object.Instantiate(authored.gameObject, host.transform);
                    copy.GetComponent<HandRef>().InjectHand(hand);
                    var ray = copy.GetComponent<RayInteractor>();
                    var row = (RectTransform)canvas.Find(isLeft ? "Choice 1" : "Choice 2");
                    var target = row.TransformPoint(row.rect.center);
                    var eye = game.Panel.Head;
                    var wrist = eye.position + eye.right * (isLeft ? -.16f : .16f) + eye.forward * .27f + Vector3.down * .40f;
                    var direction = (target - wrist).normalized;
                    Require(Vector3.Angle(direction, Vector3.ProjectOnPlane(direction, Vector3.up)) < 30,
                        "The recovery action required a steep upward ray from the chest-height hand fixture.");
                    data.Root = new Pose(wrist, Quaternion.LookRotation(Vector3.up, Vector3.back));
                    data.PointerPose = new Pose(wrist, Quaternion.LookRotation(direction));
                    host.SetActive(true);
                    yield return Frames(3);
                    sample.MarkInputDataRequiresUpdate();
                    yield return Frames(4);
                    Require(ray.CollisionInfo.HasValue && Vector3.Distance(ray.CollisionInfo.Value.Point, target) < .003f &&
                        Vector3.Angle(ray.Forward, direction) < .2f, "The SDK hand ray followed the palm axis or missed the recovery row.");
                    var fill = row.GetComponent<UnityEngine.UI.Image>();
                    var idleRow = canvas.Find(isLeft ? "Choice 2" : "Choice 1").GetComponent<UnityEngine.UI.Image>();
                    Require(fill.color != idleRow.color, "A hand hovering the recovery row did not highlight it.");
                    var highlight = fill.color;
                    ShowChoices("The floor in your scan is at a different height from the headset's floor level. Check the floor height in Space Setup, then return here.");
                    yield return Frames(2);
                    Require(fill.color == highlight && Vector3.Distance(target, row.TransformPoint(row.rect.center)) < .0005f,
                        "A tracking update cleared the hand highlight or moved the action under it.");
                    int priorRetry = retry, priorRescan = rescan;
                    void Pinch(bool down)
                    {
                        data.IsFingerPinching[(int)HandFinger.Index] = down;
                        data.FingerPinchStrength[(int)HandFinger.Index] = down ? 1 : 0;
                        sample.MarkInputDataRequiresUpdate();
                    }
                    Pinch(true); yield return Frames(3);
                    Pinch(false); yield return Frames(3);
                    Require(retry == priorRetry + (isLeft ? 1 : 0) && rescan == priorRescan + (isLeft ? 0 : 1),
                        "The SDK index pinch did not activate its own hovered row exactly once.");
                    Pinch(true); yield return Frames(3);
                    game.Panel.Show("Play area", "Choose where to play.",
                        new MenuChoice("Back", () => retry++), new MenuChoice("Try another spot", () => rescan++));
                    Pinch(false); yield return Frames(3);
                    Require(retry + rescan == priorRetry + priorRescan + 1, "A held pinch activated an action on its replacement page.");
                }
                finally { UnityEngine.Object.Destroy(host); }
                yield return Frames(2);
            }
            Require(retry == 1 && rescan == 1, "Both left and right recovery actions must be exercised.");
            game.Room.Retry();
            yield return Wait(() => game.Stage == GameStage.Welcome, "Welcome after hand input checks", 55);
            Choose("Play area");
            Choose("Refit saved floor");
            yield return Wait(() => game.Stage == GameStage.Welcome && game.Room.Status == RoomPlacementStatus.Confirmed,
                "saved room refit from the in-game Play area menu", 55);
            Record("Both authored Meta hand-ray prefabs selected their intended recovery row once using synthetic system aim and index pinch with the palm pointing upward. Both targets were under 30 degrees above a chest-height hand. A pinch held across a page change triggered no new action.");
            Record("Refit saved floor in the Play area menu rechecked the same MRUK room and returned to Welcome.");
        }
    }
}
