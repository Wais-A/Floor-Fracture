using System.Collections;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        static IEnumerator VerifyImmersiveRoom()
        {
            var immersive = game.Room.GetComponent<ImmersiveRoom>();
            Require(immersive && !immersive.On, "The gameplay scene must start in mixed reality with its Immersive room component.");
            var floor = game.Room.FloorEffect;
            var camera = game.Rig.centerEyeAnchor.GetComponent<Camera>();
            var hands = game.Rig.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "OVRHandVisualLeft" || t.name == "OVRHandVisualRight")
                .SelectMany(t => t.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
            var realHand = hands[0].sharedMaterial;

            Choose("Immersive room"); yield return Frames(2);
            Require(PlayerPrefs.GetInt("ff.immersive") == 1 && immersive.On, "Immersive room setting did not persist.");
            Require(!game.Rig.GetComponent<OVRManager>().isInsightPassthroughEnabled && camera.backgroundColor.a == 1,
                "Immersive room left passthrough on or the camera background transparent.");
            var room = immersive.RoomEffect;
            Require(room.Labels == MRUKAnchor.SceneLabels.GLOBAL_MESH && room.EffectMeshObjects.Values.Any(o => o.effectMeshGO && o.effectMeshGO.activeInHierarchy),
                "Immersive room did not draw the recorded room's global mesh.");
            Require(!floor.HideMesh && floor.EffectMeshObjects.Count > 0 && floor.MeshMaterial.GetFloat("_RequireEnvironmentDepth") == 0,
                "Immersive room did not draw the stone floor without depth.");
            Require(hands.All(h => h.sharedMaterial.name == "OculusHand"), "Immersive room kept the passthrough hand cut-out.");
            int roomPieces = room.EffectMeshObjects.Count;
            Capture("immersive-room", SmokeView.BoardObserver);

            Choose("Immersive room"); yield return Frames(2);
            Require(PlayerPrefs.GetInt("ff.immersive") == 0 && !immersive.On && game.Rig.GetComponent<OVRManager>().isInsightPassthroughEnabled &&
                camera.backgroundColor.a == 0, "Turning Immersive room off did not restore passthrough.");
            Require(room.EffectMeshObjects.Count == 0 && floor.EffectMeshObjects.Count == 0 && floor.HideMesh &&
                floor.MeshMaterial.GetFloat("_RequireEnvironmentDepth") == 1 && hands.All(h => h.sharedMaterial == realHand),
                "Turning Immersive room off left the room meshes, stone floor or virtual hands behind.");
            Record($"Immersive room turned passthrough off, drew the recorded room's global mesh ({roomPieces} piece(s)) " +
                "and the stone floor, showed virtual hands, and restored mixed reality when switched off.");
        }
    }
}
