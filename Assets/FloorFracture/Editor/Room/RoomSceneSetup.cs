using System;
using System.Linq;
using Meta.XR;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.EnvironmentDepth;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Input.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorFracture.Editor
{
    public static class RoomSceneSetup
    {
        private const string CorePrefabs = "Packages/com.meta.xr.sdk.core/Prefabs/";
        private const string CoreBlocks = "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/";
        private const string RoomBlock = "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/MRUtilityKit/MRUtilityKit.asset";
        private const string GuardianBlock = "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/RoomGuardian/RoomGuardian.asset";
        private const string FloorSpawnsBlock = "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/FindSpawnPositions/FindSpawnPositions.asset";
        private const string InteractionsBlock = "Packages/com.meta.xr.sdk.interaction.ovr/Editor/Blocks/Interactions/Interactions.asset";
        private const string RoomPrefab = "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab";
        private const string OutlineShader = "Assets/FloorFracture/Scripts/Room/RoomOutlines.shader";
        private const string EffectBlockFolder = "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/EffectMesh/";
        private const string RoomAssets = "Assets/FloorFracture/Scripts/Room/";

        // Called by the main scene author. Re-running keeps the same rig and room owner.
        public static RoomBoardPlacement InstallIntoActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            // Meta's installers search all loaded scenes for an existing rig. A rig in
            // a different scene would allow an installer to mutate that scene instead.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var other = SceneManager.GetSceneAt(i);
                if (other != scene && other.isLoaded && (Single<OVRCameraRig>(other) || Single<MRUK>(other)))
                    throw new InvalidOperationException("Another loaded scene contains an XR rig or MRUK. Open the gameplay scene on its own before installing Meta blocks.");
            }
            var rig = Single<OVRCameraRig>(scene);
            if (!rig) rig = Instantiate(CorePrefabs + "OVRCameraRig.prefab", scene).GetComponent<OVRCameraRig>();
            MetaRoomBlocks.Ensure(scene, CoreBlocks + "CameraRig/CameraRigBB.asset", MetaRoomBlocks.CameraId, 1);
            rig.usePerEyeCameras = false;
            rig.disableEyeAnchorCameras = false;
            rig.EnsureGameObjectIntegrity();
            Record(rig);
            var manager = rig.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.localDimming = false;
            var managerData = new SerializedObject(manager);
            managerData.FindProperty("_enableDynamicResolution").boolValue = false;
            managerData.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            managerData.FindProperty("requestScenePermissionOnStartup").boolValue = false;
            managerData.ApplyModifiedPropertiesWithoutUndo();
            Record(manager);

            foreach (var camera in rig.GetComponentsInChildren<Camera>(true))
            {
                bool center = camera.transform == rig.centerEyeAnchor;
                camera.enabled = center;
                camera.tag = center ? "MainCamera" : "Untagged";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = false;
                camera.nearClipPlane = 0.05f;
                Record(camera.gameObject);
                Record(camera);
            }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (!camera.enabled || camera.transform.IsChildOf(rig.transform) || !camera.CompareTag("MainCamera")) continue;
                    camera.enabled = false;
                    Record(camera);
                    var listener = camera.GetComponent<AudioListener>();
                    if (listener) { listener.enabled = false; Record(listener); }
                }

            var originalLayer = Single<OVRPassthroughLayer>(scene);
            var passthroughBlock = MetaRoomBlocks.Ensure(scene, CoreBlocks + "Passthrough/Passthrough.asset", MetaRoomBlocks.PassthroughId, 1)[0];
            var layer = passthroughBlock.GetComponent<OVRPassthroughLayer>();
            if (!layer) throw new InvalidOperationException("The installed Passthrough block has no passthrough underlay.");
            if (originalLayer && originalLayer != layer)
            {
                // Migrate the earlier generated layer on the camera rig to Meta's
                // separate Passthrough block, preserving its authored appearance.
                EditorUtility.CopySerializedManagedFieldsOnly(originalLayer, layer);
                Undo.DestroyObjectImmediate(originalLayer);
            }
            layer.enabled = true;
            layer.hidden = false;
            layer.textureOpacity = 1f;
            Record(layer);
            InstallController(scene, rig.leftControllerAnchor, OVRInput.Controller.LTouch, "Left Touch controller");
            InstallController(scene, rig.rightControllerAnchor, OVRInput.Controller.RTouch, "Right Touch controller");
            MetaRoomBlocks.Ensure(scene, CoreBlocks + "ControllerTracking/ControllerTracking.asset", MetaRoomBlocks.ControllersId, 2);

            var mruk = Single<MRUK>(scene);
            if (!mruk) mruk = Instantiate(RoomPrefab, scene).GetComponent<MRUK>();
            MetaRoomBlocks.Ensure(scene, RoomBlock, MetaRoomBlocks.RoomId, 1);
            InstallGuardianAndDepth(scene, rig);
            var floorSpawns = MetaRoomBlocks.Ensure(scene, FloorSpawnsBlock, MetaRoomBlocks.FloorSpawnsId, 1)[0].GetComponent<FindSpawnPositions>();
            if (!floorSpawns || !floorSpawns.SpawnObject)
                throw new InvalidOperationException("Meta's Find Spawn Positions block must supply its floor marker prefab.");
            floorSpawns.SpawnOnStart = MRUK.RoomFilter.None;
            floorSpawns.SpawnLocations = FindSpawnPositions.SpawnLocation.OnTopOfSurfaces;
            floorSpawns.Labels = MRUKAnchor.SceneLabels.FLOOR;
            floorSpawns.SpawnAmount = 8;
            floorSpawns.MaxIterations = 12;
            Record(floorSpawns);
            mruk.EnableWorldLock = true;
            mruk.SceneSettings.LoadSceneOnStartup = false;
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            mruk.SceneSettings.RoomPrefabs = Array.Empty<GameObject>();
            mruk.SceneSettings.SceneJsons = Array.Empty<TextAsset>();
            Record(mruk);
            var placement = mruk.GetComponent<RoomBoardPlacement>();
            if (!placement) placement = Undo.AddComponent<RoomBoardPlacement>(mruk.gameObject);
            ConfigureFloorRaycasts(placement, rig);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(OutlineShader);
            if (!shader) throw new InvalidOperationException("Missing placement preview shader at " + OutlineShader);
            var data = new SerializedObject(placement);
            data.FindProperty("rig").objectReferenceValue = rig;
            data.FindProperty("floorSpawns").objectReferenceValue = floorSpawns;
            data.FindProperty("floorEffect").objectReferenceValue = InstallFloorSurface(scene);
            data.FindProperty("outlineShader").objectReferenceValue = shader;
            data.ApplyModifiedPropertiesWithoutUndo();
            Record(placement);
            var interaction = MetaRoomBlocks.Ensure(scene, InteractionsBlock, MetaRoomBlocks.InteractionsId, 1)[0];
            if (interaction.InstallationRoutineCheckpoint?.InstallationRoutineId != MetaRoomBlocks.OvrInteractionsRoutineId)
                throw new InvalidOperationException("The Interactions Rig block did not use the OVR comprehensive rig routine.");
            ConfigureInteractions(rig, interaction.gameObject);
            InstallImmersiveRoom(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            return placement;
        }

        private const string RoomMeshMaterial = "Packages/com.meta.xr.mrutilitykit/Core/Materials/GRAY_Wall.mat";
        private const string VirtualHandMaterial = "Packages/com.meta.xr.sdk.interaction/Runtime/Materials/OculusHand.mat";

        // Optional VR mode: Meta's Effect Mesh draws the scanned room while passthrough is off.
        public static ImmersiveRoom InstallImmersiveRoom(Scene scene)
        {
            var placement = Single<RoomBoardPlacement>(scene);
            var layer = Single<OVRPassthroughLayer>(scene);
            var rig = Single<OVRCameraRig>(scene);
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoomMeshMaterial);
            var hand = AssetDatabase.LoadAssetAtPath<Material>(VirtualHandMaterial);
            if (!placement || !layer || !rig || !material || !hand)
                throw new InvalidOperationException("Immersive room needs the room owner, Meta passthrough, the camera rig, MRUK's grey material and the stock hand material.");
            var hands = rig.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "OVRHandVisualLeft" || t.name == "OVRHandVisualRight")
                .SelectMany(t => t.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
            if (hands.Length == 0) throw new InvalidOperationException("The Interactions Rig has no OVRHandVisual hand meshes.");

            var effect = InstallEffectMesh(scene, ImmersiveRoomName);
            effect.Labels = MRUKAnchor.SceneLabels.GLOBAL_MESH;
            effect.SpawnOnStart = MRUK.RoomFilter.None;
            effect.MeshMaterial = material;
            effect.Colliders = false;
            effect.CastShadow = false;
            effect.HideMesh = true;
            Record(effect);

            var immersive = placement.GetComponent<ImmersiveRoom>();
            if (!immersive) immersive = Undo.AddComponent<ImmersiveRoom>(placement.gameObject);
            var data = new SerializedObject(immersive);
            data.FindProperty("passthrough").objectReferenceValue = layer;
            data.FindProperty("roomEffect").objectReferenceValue = effect;
            data.FindProperty("virtualHand").objectReferenceValue = hand;
            var list = data.FindProperty("hands");
            list.arraySize = hands.Length;
            for (int i = 0; i < hands.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = hands[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            Record(immersive);
            return immersive;
        }

        private const string FloorSurfaceName = "[BuildingBlock] Effect Mesh - Surrounding Floor";
        private const string ImmersiveRoomName = "[BuildingBlock] Effect Mesh - Immersive Room";

        private static EffectMesh InstallFloorSurface(Scene scene) => ConfigureFloorSurface(InstallEffectMesh(scene, FloorSurfaceName));

        // Two Effect Mesh blocks share Meta's block id, so each is found by the name this setup gives it.
        private static EffectMesh InstallEffectMesh(Scene scene, string name)
        {
            var marker = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BuildingBlock>(true))
                .SingleOrDefault(block => block.BlockId == MetaRoomBlocks.EffectMeshId && block.name == name);
            if (!marker)
            {
                // Choose Meta's semantic-surface variant. The global-mesh variant includes walls and objects.
                var definition = AssetDatabase.LoadAssetAtPath<BlockData>(EffectBlockFolder + "EffectMesh.asset");
                var routine = AssetDatabase.LoadAssetAtPath<InstallationRoutine>(EffectBlockFolder + "EffectMeshInstallationRoutine.asset");
                if (!definition || !routine || definition.Id != MetaRoomBlocks.EffectMeshId ||
                    new SerializedObject(routine).FindProperty("EffectMeshTheme").intValue != 0)
                    throw new InvalidOperationException("Meta's default Effect Mesh installer is unavailable.");
                var objects = routine.Install(definition, null);
                MetaRoomBlocks.RecordInstallation(definition, routine, objects);
                marker = objects.Single().GetComponent<BuildingBlock>();
                marker.name = name;
            }
            var effect = marker.GetComponent<EffectMesh>();
            if (!effect) throw new InvalidOperationException("Meta's Effect Mesh block has no EffectMesh component.");
            return effect;
        }

        private static EffectMesh ConfigureFloorSurface(EffectMesh effect)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(RoomAssets + "SurroundingBrick.shader");
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoomAssets + "SurroundingBrick.mat");
            if (!effect || !shader || !material || material.shader != shader)
                throw new InvalidOperationException("The floor Effect Mesh needs the surrounding brick material and its shader.");
            effect.Labels = MRUKAnchor.SceneLabels.FLOOR;
            effect.SpawnOnStart = MRUK.RoomFilter.None;
            effect.MeshMaterial = material;
            effect.Colliders = false;
            effect.CastShadow = false;
            effect.HideMesh = true;
            Record(effect);
            return effect;
        }

        private static void InstallGuardianAndDepth(Scene scene, OVRCameraRig rig)
        {
            var block = MetaRoomBlocks.Ensure(scene, GuardianBlock, MetaRoomBlocks.GuardianId, 1)[0];
            var guardian = block.GetComponent<RoomGuardian>();
            var mesh = block.GetComponent<EffectMesh>();
            if (!guardian || !mesh || Single<RoomGuardian>(scene) != guardian)
                throw new InvalidOperationException("The Room Guardian block must supply one guardian and its room mesh.");
            // The SDK proximity grid is an optional visualization. MRUK and the depth
            // provider work independently of this block, including in close quarters.
            block.gameObject.SetActive(false);
            Record(block.gameObject);

            // The Occlusion Dependencies prefab owns depth acquisition for the whole scene.
            var providerBlock = MetaRoomBlocks.Ensure(scene, CoreBlocks + "Occlusion/OcclusionDependencyBlock.asset",
                MetaRoomBlocks.DepthProviderId, 1)[0];
            var depth = providerBlock.GetComponent<EnvironmentDepthManager>();
            if (!depth || Single<EnvironmentDepthManager>(scene) != depth)
                throw new InvalidOperationException("The gameplay scene must have one environment depth provider.");
            depth.enabled = true;
            depth.CustomTrackingSpace = rig.trackingSpace;
            depth.OcclusionShadersMode = OcclusionShadersMode.SoftOcclusion;
            depth.RemoveHands = false;
            Record(depth);
            providerBlock.gameObject.SetActive(false);
            Record(providerBlock.gameObject);
        }

        public static void ConfigureFloorRaycasts(RoomBoardPlacement placement, OVRCameraRig rig)
        {
            var scene = placement.gameObject.scene;
            const string definition = "Packages/com.meta.xr.mrutilitykit/Editor/BuildingBlocks/InstantContentPlacement/EnvironmentRaycastManager.asset";
            var sharedDepth = Single<EnvironmentDepthManager>(scene);
            if (!sharedDepth || sharedDepth.GetComponent<BuildingBlock>()?.BlockId != MetaRoomBlocks.DepthProviderId)
                throw new InvalidOperationException("Floor raycasts require the installed Occlusion Dependencies block.");
            var block = MetaRoomBlocks.Ensure(scene, definition, MetaRoomBlocks.EnvironmentRaycastId, 1)[0];
            // The stock raycast prefab bundles its own depth provider. Share the scene's calibrated provider.
            var bundledDepth = block.GetComponent<EnvironmentDepthManager>();
            if (bundledDepth) Undo.DestroyObjectImmediate(bundledDepth);
            var manager = block.GetComponent<EnvironmentRaycastManager>();
            manager.CustomTrackingSpace = rig.trackingSpace;
            block.gameObject.SetActive(false); // Activate only once the headset reports support.
            Record(block.gameObject);
            Record(manager);
            var data = new SerializedObject(placement);
            data.FindProperty("depthProvider").objectReferenceValue = sharedDepth;
            data.FindProperty("liveManager").objectReferenceValue = manager;
            data.ApplyModifiedPropertiesWithoutUndo();
            Record(placement);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void ConfigureInteractions(OVRCameraRig rig, GameObject interaction)
        {
            if (!interaction.transform.IsChildOf(rig.transform) || interaction.GetComponentsInChildren<OVRCameraRigRef>(true).Length != 1)
                throw new InvalidOperationException("The Interactions Rig block could not resolve the existing camera rig.");
            var left = rig.leftControllerAnchor.GetComponentInChildren<OVRControllerHelper>(true);
            var right = rig.rightControllerAnchor.GetComponentInChildren<OVRControllerHelper>(true);
            var visuals = interaction.GetComponentsInChildren<ControllerVisual>(true);
            if (visuals.Length != 2) throw new InvalidOperationException("The comprehensive rig needs one controller visual per hand.");
            foreach (var visual in visuals)
            {
                var data = new SerializedObject(visual);
                var original = data.FindProperty("_root").objectReferenceValue as GameObject;
                var model = original ? original.GetComponent<OVRControllerHelper>() : null;
                if (!model || (model.m_controller != OVRInput.Controller.LTouch && model.m_controller != OVRInput.Controller.RTouch))
                    throw new InvalidOperationException("Unexpected controller model in " + visual.name);
                var tracked = model.m_controller == OVRInput.Controller.LTouch ? left : right;
                if (model == tracked) continue;
                // Core Controller Tracking and the comprehensive rig use the same model.
                // Share its tracked instance. The unused nested copy is stripped from
                // the player, while ISDK retains the controller source and visibility.
                visual.InjectRoot(tracked.gameObject);
                Record(visual);
                original.SetActive(false);
                original.tag = "EditorOnly";
                Record(original);
            }
            foreach (var child in interaction.GetComponentsInChildren<Transform>(true))
            {
                // Only physical stepping should move the player on scanned glass.
                if (child.name != "Locomotor" && child.name != "LocomotionControllerInteractorGroup" &&
                    child.name != "LocomotionHandInteractorGroup" && child.name != "MicroGesturesLocomotionHandInteractorGroup") continue;
                child.gameObject.SetActive(false);
                Record(child.gameObject);
            }
            var rays = interaction.GetComponentsInChildren<RayInteractor>(true).Count(i => i.name == "ControllerRayInteractor");
            var grabs = interaction.GetComponentsInChildren<GrabInteractor>(true).Count(i => i.name == "ControllerGrabInteractor");
            if (rays != 2 || grabs != 2)
                throw new InvalidOperationException($"The two-controller rig contains {rays} SDK controller rays and {grabs} grabs.");
        }

        private static void InstallController(Scene scene, Transform anchor, OVRInput.Controller hand, string name)
        {
            var helper = anchor.GetComponentInChildren<OVRControllerHelper>(true);
            if (!helper)
            {
                var model = Instantiate(CorePrefabs + "OVRControllerPrefab.prefab", scene);
                model.name = name;
                model.transform.SetParent(anchor, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                helper = model.GetComponent<OVRControllerHelper>();
                Record(model);
                Record(model.transform);
            }
            helper.m_controller = hand;
            Record(helper);
        }

        private static T Single<T>(Scene scene) where T : Component
        {
            T result = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var item in root.GetComponentsInChildren<T>(true))
                {
                    if (result) throw new InvalidOperationException("Multiple " + typeof(T).Name + " objects in " + scene.name);
                    result = item;
                }
            return result;
        }

        private static GameObject Instantiate(string path, Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new InvalidOperationException("Installed SDK prefab missing at " + path);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        }

        private static void Record(UnityEngine.Object item)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            EditorUtility.SetDirty(item);
        }
    }
}
