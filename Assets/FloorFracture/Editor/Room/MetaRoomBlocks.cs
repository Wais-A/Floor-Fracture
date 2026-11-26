using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FloorFracture.Editor
{
    public static class MetaRoomBlocks
    {
        public const string CameraId = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        public const string PassthroughId = "f0540b20-dfd6-420e-b20d-c270f88dc77e";
        public const string ControllersId = "5817f7c0-f2a5-45f9-a5ca-64264e0166e8";
        public const string RoomId = "d4413f4f-542f-451f-92ee-4801f27d676a";
        public const string FloorSpawnsId = "93762ef0-9738-45f4-bb87-bdb9a0acb994";
        public const string EffectMeshId = "65c6e778-10b3-4490-89da-008a18aa116a";
        public const string GuardianId = "715d6e5e-2d15-475d-9b2b-43ce3eca2f3d";
        public const string DepthProviderId = "ece9de6e-19c8-4f64-bb33-313ed23e6c5c";
        public const string EnvironmentRaycastId = "3ce5fc30-a5c9-4ad0-a76d-3583bcd405b0";
        public const string InteractionsId = "81f55626-5fad-45e9-a1df-184f330da7ba";
        public const string OvrInteractionsRoutineId = "2063f481-1b63-4eda-9ee8-a00df98536bc";

        // v209 exposes BlockData and its definitions publicly, while its installation
        // entry point is internal. Bind that one exact overload so dependencies, existing
        // objects, versions, instance IDs and variant checkpoints follow Meta's installer.
        private static readonly Func<BlockData, GameObject, Task<List<GameObject>>> InstallWithDependencies =
            (Func<BlockData, GameObject, Task<List<GameObject>>>)SdkMethod(typeof(BlockData), "InstallWithDependencies", typeof(GameObject))
                .CreateDelegate(typeof(Func<BlockData, GameObject, Task<List<GameObject>>>));
        private static readonly Func<InstallationRoutine, InstallationRoutineCheckpoint> MakeCheckpoint =
            (Func<InstallationRoutine, InstallationRoutineCheckpoint>)SdkMethod(typeof(InstallationRoutine), "ToCheckpoint")
                .CreateDelegate(typeof(Func<InstallationRoutine, InstallationRoutineCheckpoint>));

        public static List<GameObject> Install(BlockData definition, GameObject target = null)
        {
            if (!definition) throw new ArgumentNullException(nameof(definition));
            var installation = InstallWithDependencies(definition, target);
            if (!installation.IsCompleted)
                throw new InvalidOperationException("The Meta block needs interactive variant selection: " + definition.BlockName);
            return installation.GetAwaiter().GetResult();
        }

        // Used after explicitly choosing one of Meta's public InstallationRoutine
        // implementations for an interactable. Preserve its actual selected variants.
        public static void RecordInstallation(BlockData definition, InstallationRoutine routine, IEnumerable<GameObject> returnedObjects)
        {
            if (!definition || !routine || routine.TargetBlockDataId != definition.Id)
                throw new InvalidOperationException("The selected SDK installation routine belongs to a different block.");
            foreach (var result in returnedObjects)
            {
                if (!result) continue;
                var block = result.GetComponent<BuildingBlock>();
                if (block && block.BlockId != definition.Id)
                    throw new InvalidOperationException(result.name + " already represents a different Meta block.");
                if (!block) block = Undo.AddComponent<BuildingBlock>(result);
                var serialized = new SerializedObject(block);
                serialized.FindProperty("blockId").stringValue = definition.Id;
                serialized.FindProperty("version").intValue = definition.Version;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                block.InstallationRoutineCheckpoint = MakeCheckpoint(routine);
                PrefabUtility.RecordPrefabInstancePropertyModifications(block);
                EditorUtility.SetDirty(block);
            }
        }

        internal static List<BuildingBlock> Ensure(Scene scene, string assetPath, string id, int count)
        {
            var definition = AssetDatabase.LoadAssetAtPath<BlockData>(assetPath);
            if (!definition || definition.Id != id) throw new InvalidOperationException("Meta SDK block definition changed: " + assetPath);
            var installed = InScene(scene, id);
            if (installed.Count < count) { Install(definition); installed = InScene(scene, id); }
            if (installed.Count != count)
                throw new InvalidOperationException($"Expected {count} {definition.BlockName} block instances; found {installed.Count}.");
            foreach (var block in installed)
                if (block.Version != definition.Version || string.IsNullOrEmpty(block.InstanceId))
                    throw new InvalidOperationException("Incomplete SDK installation metadata on " + block.name);
            return installed;
        }

        private static List<BuildingBlock> InScene(Scene scene, string id)
        {
            var result = new List<BuildingBlock>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var block in root.GetComponentsInChildren<BuildingBlock>(true))
                    if (block.BlockId == id) result.Add(block);
            return result;
        }

        private static MethodInfo SdkMethod(Type type, string name, params Type[] args) =>
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, args, null) ??
            throw new MissingMethodException(type.FullName, name + " (expected Meta XR SDK 209)");
    }
}
