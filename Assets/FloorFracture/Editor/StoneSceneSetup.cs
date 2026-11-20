using System;
using Meta.XR.BuildingBlocks.Editor;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static class StoneSceneSetup
    {
        public const string Prefab="Assets/FloorFracture/Resources/PracticeStone.prefab";
        const string Blocks="Packages/com.meta.xr.sdk.interaction.ovr/Editor/Blocks/Interactables/";
        const string OcclusionFolder="Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Occlusion/";
        public static void Install()
        {
            var depthMaterial=AssetDatabase.LoadAssetAtPath<Material>(OcclusionFolder+"Materials/DepthMaterial.mat");
            if(!depthMaterial||!depthMaterial.shader||depthMaterial.shader.name!="EnvironmentDepth/OcclusionLit"||!depthMaterial.HasProperty("_Color"))
                throw new InvalidOperationException("Meta's stock OcclusionLit material with a stone color input is missing.");
            var previous=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if(previous&&previous.GetComponent<Grabbable>()&&previous.GetComponentInChildren<GrabInteractable>(true))
            {
                var renderer=StoneRenderer(previous);
                var block=renderer.GetComponent<OcclusionBuildingBlock>();
                if(block&&block.BlockId==MetaRoomBlocks.OcclusionId&&renderer.sharedMaterial==depthMaterial) return;
                var contents=PrefabUtility.LoadPrefabContents(Prefab);
                try
                {
                    ApplyOcclusion(StoneRenderer(contents),depthMaterial);
                    PrefabUtility.SaveAsPrefabAsset(contents,Prefab);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                return;
            }
            var stone=new GameObject("PracticeStone");
            try
            {
                var body=stone.AddComponent<Rigidbody>(); body.mass=.14f; body.useGravity=false;
                body.interpolation=RigidbodyInterpolation.Interpolate; body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                stone.AddComponent<SphereCollider>().radius=.045f;
                var visual=GameObject.CreatePrimitive(PrimitiveType.Sphere); visual.name="River stone"; visual.transform.SetParent(stone.transform,false);
                visual.transform.localScale=new Vector3(.10f,.078f,.092f); UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
                var block=AssetDatabase.LoadAssetAtPath<BlockData>(Blocks+"HandGrab.asset");
                var routine=AssetDatabase.LoadAssetAtPath<InstallationRoutine>(Blocks+"InstallationRoutines/HandGrabInstallationRoutine.asset");
                if(!block||!routine||routine.TargetBlockDataId!=block.Id) throw new InvalidOperationException("SDK Grab Interaction block or installation routine is missing.");
                // With an existing target SDK 209 installs the GrabWizard synchronously. It can create a demo cube asynchronously if no target is supplied.
                var installation=routine.InstallAsync(block,stone);
                if(!installation.IsCompleted) throw new InvalidOperationException("The Grab Interaction routine changed. Wait for installation before baking this prefab.");
                var created=installation.GetAwaiter().GetResult();
                MetaRoomBlocks.RecordInstallation(block,routine,created);
                var grab=stone.GetComponent<Grabbable>();
                if(!grab||!stone.GetComponentInChildren<GrabInteractable>(true)) throw new InvalidOperationException("Grab block did not create a controller-selectable grabbable.");
                grab.InjectOptionalRigidbody(body); grab.InjectOptionalKinematicWhileSelected(true); grab.InjectOptionalThrowWhenUnselected(true);
                body.isKinematic=false; body.useGravity=false;
                ApplyOcclusion(visual.GetComponent<MeshRenderer>(),depthMaterial);
                PrefabUtility.SaveAsPrefabAsset(stone,Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(stone); }
        }

        static MeshRenderer StoneRenderer(GameObject stone)
        {
            var visual=stone.transform.Find("River stone");
            if(!visual||!visual.TryGetComponent<MeshRenderer>(out var renderer))
                throw new InvalidOperationException("The baked practice stone is missing its River stone renderer.");
            return renderer;
        }

        static void ApplyOcclusion(MeshRenderer target,Material stockMaterial)
        {
            var definition=AssetDatabase.LoadAssetAtPath<BlockData>(OcclusionFolder+"Occlusion.asset");
            if(!definition||definition.Id!=MetaRoomBlocks.OcclusionId)
                throw new InvalidOperationException("The installed Meta Occlusion building block is missing.");
            var examples=MetaRoomBlocks.Install(definition,target.gameObject);
            try
            {
                if(examples.Count!=1||!examples[0].TryGetComponent<OcclusionBuildingBlock>(out var installed)||
                    installed.BlockId!=definition.Id||target.sharedMaterial!=stockMaterial)
                    throw new InvalidOperationException("The Meta Occlusion installer did not apply its stock material to the practice stone.");
                // SDK 209 also returns its example cube hierarchy when targeting a renderer.
                // Keep the real installation metadata with the renderer that received the material.
                var marker=target.GetComponent<OcclusionBuildingBlock>();
                if(!marker) marker=Undo.AddComponent<OcclusionBuildingBlock>(target.gameObject);
                EditorUtility.CopySerializedManagedFieldsOnly(installed,marker);
                if(marker.BlockId!=definition.Id||marker.Version!=definition.Version||string.IsNullOrEmpty(marker.InstanceId))
                    throw new InvalidOperationException("Could not preserve the SDK's Occlusion installation on the practice stone.");
            }
            finally
            {
                foreach(var example in examples)
                    if(example&&example!=target.gameObject) Undo.DestroyObjectImmediate(example);
            }
        }
    }
}
