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
        public static void Install()
        {
            var previous=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if(previous&&previous.GetComponent<Grabbable>()&&previous.GetComponentInChildren<GrabInteractable>(true)) return;
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
                PrefabUtility.SaveAsPrefabAsset(stone,Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(stone); }
        }
    }
}
