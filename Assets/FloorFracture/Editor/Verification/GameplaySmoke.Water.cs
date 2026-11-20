using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static EffectMesh FloorWater =>
            (EffectMesh)new SerializedObject(game.Room).FindProperty("floorWaterMesh").objectReferenceValue;

        private static void VerifyWaterVisibility(bool visible)
        {
            var effect = FloorWater;
            var meshes = effect.EffectMeshObjects.Values.Where(o => o.effectMeshGO).ToArray();
            Require(effect.HideMesh != visible && (!visible || meshes.Length > 0) &&
                meshes.All(o => o.effectMeshGO.GetComponent<MeshRenderer>().enabled == visible),
                "Unexpected surrounding-water visibility at " + game.Stage + " / " + game.Room.Status);
        }

        private static void VerifySurroundingWater(BoardPlacement fit)
        {
            var effect = FloorWater;
            var anchors = effect.EffectMeshObjects;
            var fixtureRoom = MRUK.Instance.Rooms.Single();
            Require(effect.Labels == MRUKAnchor.SceneLabels.FLOOR && anchors.Count > 0 && anchors.Count == fixtureRoom.FloorAnchors.Count &&
                anchors.Keys.All(a => a.Room == fixtureRoom && a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR)) &&
                anchors.Values.All(o => o.effectMeshGO && !o.collider && !o.effectMeshGO.GetComponent<Collider>()),
                "Water must use only the current room's native floor meshes and supply no gameplay colliders.");
            var renderers = anchors.Values.Select(o => o.effectMeshGO.GetComponent<MeshRenderer>()).ToArray();
            var material = effect.MeshMaterial;
            Require(renderers.All(r => r && r.enabled && r.sharedMaterial == material) &&
                material.GetTexture("_WaterNormal") == Resources.Load<Texture2D>("Endings/MetaFloorZone/materials/WaterSurface"),
                "The floor meshes must show the approved Floor Zone surface texture.");

            // Isolate the real scanned floor for a top-down GPU readback. Use the same mesh as its own reference.
            const int size = 192;
            const int testLayer = 30;
            var layerBefore = renderers.Select(r => r.gameObject.layer).ToArray();
            var originalFrame = material.GetVector("_BoardFrame");
            var originalHalfSize = material.GetVector("_BoardHalfSize");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var go = new GameObject("Floor-water verification camera");
            var target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            try
            {
                foreach (var renderer in renderers) renderer.gameObject.layer = testLayer;
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.cullingMask = 1 << testLayer;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) + .3f;
                camera.aspect = 1;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = bounds.size.y + 6f;
                camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * (bounds.extents.y + 3f),
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
                camera.targetTexture = target;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                Color32[] Read()
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    image.Apply();
                    return image.GetPixels32();
                }
                material.SetVector("_BoardHalfSize", new Vector4(-1,-1,0,0));
                var completeFloor = Read();
                material.SetVector("_BoardHalfSize", originalHalfSize);
                CheckOpening(Read(), fit);
                var rotated = new BoardPlacement(fit.Center, fit.FloorHeight, fit.AngleDegrees + 31f,
                    fit.Columns, fit.Rows, fit.Pitch);
                var angle = rotated.AngleDegrees * Mathf.Deg2Rad;
                material.SetVector("_BoardFrame", new Vector4(fit.Center.x, fit.Center.y, Mathf.Cos(angle), Mathf.Sin(angle)));
                CheckOpening(Read(), rotated);
                // An overlapping XZ footprint on a separate storey must still have complete water coverage.
                var differentLevel = originalHalfSize;
                differentLevel.z += 1f;
                material.SetVector("_BoardHalfSize", differentLevel);
                var otherStorey = Read();
                Require(otherStorey.Select(IsWater).SequenceEqual(completeFloor.Select(IsWater)),
                    "A board on a different storey punched a hole in this floor.");

                void CheckOpening(Color32[] visible, BoardPlacement board)
                {
                    int inside = 0, outside = 0, offFloor = 0;
                    float pixel = 2f * camera.orthographicSize / size;
                    var inverse = Quaternion.Inverse(board.Pose.rotation);
                    for (int y = 0; y < size; ++y)
                    for (int x = 0; x < size; ++x)
                    {
                        int i = x + size * y;
                        bool water = IsWater(visible[i]);
                        if (!IsWater(completeFloor[i]))
                        {
                            Require(!water, "Water escaped the scanned floor polygon.");
                            offFloor++;
                            continue;
                        }
                        var world = camera.ViewportToWorldPoint(new Vector3((x+.5f)/size, (y+.5f)/size, 3f));
                        var local = inverse * (world - board.Pose.position);
                        float distance = Mathf.Max(Mathf.Abs(local.x) - board.Columns * board.Pitch * .5f,
                            Mathf.Abs(local.z) - board.Rows * board.Pitch * .5f);
                        if (Mathf.Abs(distance) < pixel * 2) continue;
                        if (distance < 0) { inside++; Require(!water, "Water covers a playable pane or open shaft."); }
                        else { outside++; Require(water, "The surrounding scanned floor has an uncovered patch."); }
                    }
                    Require(inside > 100 && outside > 100 && offFloor > 100, "The floor readback did not cover all three regions.");
                }
            }
            finally
            {
                material.SetVector("_BoardFrame", originalFrame);
                material.SetVector("_BoardHalfSize", originalHalfSize);
                for (int i=0; i<renderers.Length; ++i) renderers[i].gameObject.layer = layerBefore[i];
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                RenderTexture.ReleaseTemporary(target);
            }
            Capture("whole-room-water", SmokeView.RoomOverview, bounds);
            Record("GPU readbacks of the actual MRUK floor showed water to its scanned edges with a clear glass footprint at the fitted orientation and a further 31-degree rotation. A board one metre above the floor left the full floor covered.");
        }

        private static bool IsWater(Color32 pixel) => pixel.g > pixel.r + 8;
    }
}
