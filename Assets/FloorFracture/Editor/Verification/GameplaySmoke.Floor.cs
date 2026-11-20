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
        private static EffectMesh FloorSurface =>
            (EffectMesh)new SerializedObject(game.Room).FindProperty("floorEffect").objectReferenceValue;

        private static void VerifyFloorVisibility(bool visible)
        {
            var effect = FloorSurface;
            var meshes = effect.EffectMeshObjects.Values.Where(o => o.effectMeshGO).ToArray();
            var depth = Object.FindAnyObjectByType<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>(FindObjectsInactive.Include);
            var registered = depth.MaskMeshFilters;
            Require(effect.HideMesh != visible && (!visible || meshes.Length > 0) &&
                meshes.All(o => o.effectMeshGO.GetComponent<MeshRenderer>().enabled == visible) &&
                meshes.All(o => registered.Count(m => m == o.effectMeshGO.GetComponent<MeshFilter>()) == (visible ? 1 : 0)),
                "Unexpected surrounding brick visibility at " + game.Stage + " / " + game.Room.Status);
        }

        private static void VerifySurroundingBrick(BoardPlacement fit)
        {
            var effect = FloorSurface;
            var anchors = effect.EffectMeshObjects;
            var fixtureRoom = MRUK.Instance.Rooms.Single();
            Require(effect.Labels == MRUKAnchor.SceneLabels.FLOOR && anchors.Count > 0 && anchors.Count == fixtureRoom.FloorAnchors.Count &&
                anchors.Keys.All(a => a.Room == fixtureRoom && a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR)) &&
                anchors.Values.All(o => o.effectMeshGO && !o.collider && !o.effectMeshGO.GetComponent<Collider>()),
                "Brick must use only the current room's native floor meshes and supply no gameplay colliders.");
            var renderers = anchors.Values.Select(o => o.effectMeshGO.GetComponent<MeshRenderer>()).ToArray();
            var material = effect.MeshMaterial;
            Require(renderers.All(r => r && r.enabled && r.sharedMaterial == material) &&
                material.shader.name == "Floor Fracture/Surrounding Brick" && material.HasProperty("_MortarColor"),
                "The native floor patches must use the shaft-style brick material.");

            // Isolate the real scanned floor for a top-down GPU readback. Use the same mesh as its own reference.
            const int size = 192;
            const int testLayer = 30;
            var layerBefore = renderers.Select(r => r.gameObject.layer).ToArray();
            var originalFrame = material.GetVector("_BoardFrame");
            var originalHalfSize = material.GetVector("_BoardHalfSize");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var go = new GameObject("Floor surface verification camera");
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
                var colors = completeFloor.Where(IsBrick).ToArray();
                Require(colors.Length > 100 && colors.Max(c => c.r) - colors.Min(c => c.r) > 20 &&
                    colors.Count(c => c.r > c.g && c.g > c.b) > colors.Length * .8f,
                    "Expected warm brick faces with distinguishable recessed joints.");
                material.SetFloat("_RequireEnvironmentDepth", 1);
                Require(!Read().Any(IsBrick), "A Quest without a depth frame would paint virtual brick over real objects.");
                VerifyFloorDepth(renderers, material, camera, size, Read, completeFloor);
                material.SetFloat("_RequireEnvironmentDepth", 0);
                material.SetVector("_BoardHalfSize", originalHalfSize);
                CheckOpening(Read(), fit);
                var rotated = new BoardPlacement(fit.Center, fit.FloorHeight, fit.AngleDegrees + 31f,
                    fit.Columns, fit.Rows, fit.Pitch);
                var angle = rotated.AngleDegrees * Mathf.Deg2Rad;
                material.SetVector("_BoardFrame", new Vector4(fit.Center.x, fit.Center.y, Mathf.Cos(angle), Mathf.Sin(angle)));
                CheckOpening(Read(), rotated);
                // An overlapping XZ footprint on a separate storey must still have complete brick coverage.
                var differentLevel = originalHalfSize;
                differentLevel.z += 1f;
                material.SetVector("_BoardHalfSize", differentLevel);
                var otherStorey = Read();
                Require(otherStorey.Select(IsBrick).SequenceEqual(completeFloor.Select(IsBrick)),
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
                        bool brick = IsBrick(visible[i]);
                        if (!IsBrick(completeFloor[i]))
                        {
                            Require(!brick, "Brick escaped the scanned floor polygon.");
                            offFloor++;
                            continue;
                        }
                        var world = camera.ViewportToWorldPoint(new Vector3((x+.5f)/size, (y+.5f)/size, 3f));
                        var local = inverse * (world - board.Pose.position);
                        float distance = Mathf.Max(Mathf.Abs(local.x) - board.Columns * board.Pitch * .5f,
                            Mathf.Abs(local.z) - board.Rows * board.Pitch * .5f);
                        if (Mathf.Abs(distance) < pixel * 2) continue;
                        if (distance < 0) { inside++; Require(!brick, "Brick covers a playable pane or open shaft."); }
                        else { outside++; Require(brick, "The surrounding scanned floor has an uncovered patch."); }
                    }
                    Require(inside > 100 && outside > 100 && offFloor > 100, "The floor readback did not cover all three regions.");
                }
            }
            finally
            {
                material.SetFloat("_RequireEnvironmentDepth", 0);
                material.SetVector("_BoardFrame", originalFrame);
                material.SetVector("_BoardHalfSize", originalHalfSize);
                for (int i=0; i<renderers.Length; ++i) renderers[i].gameObject.layer = layerBefore[i];
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                RenderTexture.ReleaseTemporary(target);
            }
            Capture("whole-room-brick", SmokeView.RoomOverview, bounds);
            Capture("brick-player-height", SmokeView.FloorEdge);
            Record("GPU readbacks of the actual MRUK floor showed masonry to its scanned edges with a clear glass footprint at the fitted orientation and a further 31-degree rotation. A board one metre above the floor left the full floor covered.");
        }

        // Magenta is the sentinel for passthrough. No virtual geometry was drawn there.
        private static bool IsBrick(Color32 pixel) => !(pixel.r > 245 && pixel.g < 8 && pixel.b > 245);

        private enum DepthObject { Floor, Couch, ChairLeg, Bin }

        private static DepthObject ObjectAt(float u, float v, bool binMoved)
        {
            float y = Mathf.Abs(v - .5f);
            if (u > .14f && u < .33f && y < .23f) return DepthObject.Couch;
            if (y < .15f && (Mathf.Abs(u - .435f) < .022f || Mathf.Abs(u - .555f) < .022f))
                return DepthObject.ChairLeg;
            if (new Vector2(u - (binMoved ? .81f : .69f), y).sqrMagnitude < .053f * .053f)
                return DepthObject.Bin;
            return DepthObject.Floor;
        }

        private static bool HasUniformNeighborhood(float u, float v, float edge, bool binMoved, DepthObject shape)
        {
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (ObjectAt(u + x * edge, v + y * edge, binMoved) != shape) return false;
            return true;
        }

        private static void VerifyFloorDepth(MeshRenderer[] renderers, Material brick, Camera camera,
            int imageSize, System.Func<Color32[]> read, Color32[] reference)
        {
            const int size = 96;
            const float near = .1f, floorDistance = 3f;
            var depth = Object.FindAnyObjectByType<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>(FindObjectsInactive.Include);
            VerifyFloorVisibility(true);
            Require(depth.MaskBias > 0 && depth.MaskBias < .04f, "The floor mask would erase low foreground objects.");
            var raw = new Texture2DArray(size, size, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var floor = new Texture2DArray(size, size, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            var soft = new Texture2DArray(size, size, 2, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var combined = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                { dimension = TextureDimension.Tex2DArray, volumeDepth = 2 };
            var mask = new Material(Shader.Find("Meta/EnvironmentDepth/DepthMask")) { enableInstancing = true };
            var surface = new Material(brick);
            using var commands = new CommandBuffer();
            try
            {
                var projection = Matrix4x4.zero;
                projection.m00 = projection.m12 = 1f / camera.orthographicSize;
                projection.m03 = -camera.transform.position.x * projection.m00;
                projection.m13 = -camera.transform.position.z * projection.m12;
                projection.m23 = 1 - 2 * near / floorDistance;
                projection.m33 = 1;
                surface.SetMatrixArray("_EnvironmentDepthReprojectionMatrices", new[] { projection, projection });
                surface.SetVector("_EnvironmentDepthZBufferParams", new Vector4(-2 * near, -1, 0, 0));
                surface.SetTexture("_PreprocessedEnvironmentDepthTexture", soft);
                surface.SetVector("_PreprocessedEnvironmentDepthTexture_TexelSize", new Vector4(1f/size, 1f/size, size, size));
                surface.EnableKeyword("SOFT_OCCLUSION");
                foreach (var renderer in renderers) renderer.sharedMaterial = surface;
                combined.Create();
                mask.SetTexture("_EnvironmentDepthTexture", raw);
                mask.SetTexture("_MaskTexture", floor);
                commands.SetRenderTarget(new RenderTargetIdentifier(combined, 0, CubemapFace.Unknown, -1));
                commands.DrawProcedural(Matrix4x4.identity, mask, 1, MeshTopology.Triangles, 3, 2);

                foreach (bool moved in new[] { false, true })
                {
                    var expectedDepth = new float[2][];
                    for (int eye = 0; eye < 2; eye++)
                    {
                        var samples = new Color[size * size];
                        var scanned = new Color[samples.Length];
                        expectedDepth[eye] = new float[samples.Length];
                        for (int i = 0; i < samples.Length; i++)
                        {
                            var shape = ObjectAt(((i % size) + .5f)/size, ((i / size) + .5f)/size, moved);
                            float metres = shape switch
                            {
                                DepthObject.Couch => 1.50f + eye * .05f,
                                DepthObject.ChairLeg => 2.40f + eye * .03f,
                                DepthObject.Bin => 2.85f + eye * .01f,
                                _ => i % 2 == 0 ? 2.96f : 3.04f
                            };
                            samples[i].r = 1 - near / metres;
                            scanned[i].r = 1 - near / (floorDistance * (1 - depth.MaskBias));
                            expectedDepth[eye][i] = shape == DepthObject.Floor ? 1 : samples[i].r;
                        }
                        raw.SetPixels(samples, eye);
                        floor.SetPixels(scanned, eye);
                    }
                    raw.Apply(false);
                    floor.Apply(false);
                    Graphics.ExecuteCommandBuffer(commands);
                    var download = AsyncGPUReadback.Request(combined, 0);
                    download.WaitForCompletion();
                    Require(!download.hasError, "Failed to read Meta's depth mask output.");
                    for (int eye = 0; eye < 2; eye++)
                    {
                        var channel = download.GetData<float>(eye);
                        var filtered = new Color[channel.Length];
                        for (int i = 0; i < filtered.Length; i++)
                        {
                            Require(Mathf.Abs(channel[i] - expectedDepth[eye][i]) < .0001f,
                                $"Native mask changed the wrong sample in eye {eye}, pixel {i}.");
                            // Encode the uniform local depth in Meta's preprocessed min/max format.
                            float encoded = 1 - channel[i];
                            filtered[i] = new Color(encoded, encoded, 0, 0);
                        }
                        soft.SetPixels(filtered, eye);
                    }
                    soft.Apply(false);
                    var actual = read();
                    var counts = new int[4];
                    int legGap = 0, previousBin = 0;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        if (!IsBrick(reference[i])) continue;
                        float u = ((i % imageSize) + .5f) / imageSize, v = ((i / imageSize) + .5f) / imageSize;
                        var shape = ObjectAt(u, v, moved);
                        // Boundary pixels are softened by the sensor and can belong to either sample.
                        if (!HasUniformNeighborhood(u, v, 2f / size, moved, shape)) continue;
                        if (shape == DepthObject.Floor)
                        {
                            Require(Mathf.Abs(actual[i].r-reference[i].r) <= 3 && Mathf.Abs(actual[i].g-reference[i].g) <= 3,
                                $"Visible brick changed at UV ({u:F3}, {v:F3}): expected {reference[i]}, read {actual[i]}.");
                            if (u > .48f && u < .51f && Mathf.Abs(v-.5f) < .10f) legGap++;
                            if (moved && Mathf.Abs(u-.69f) < .02f && Mathf.Abs(v-.5f) < .02f) previousBin++;
                        }
                        else Require(!IsBrick(actual[i]), $"Brick covered {shape} at UV ({u:F3}, {v:F3}): read {actual[i]}.");
                        counts[(int)shape]++;
                    }
                    Require(counts.All(count => count >= 4) && legGap > 4 && (!moved || previousBin > 4),
                        "The floor fixture must exercise each object, the gap between chair legs and the moved bin.");
                    Record($"Two-eye native mask and brick GPU checks with the bin {(moved ? "moved" : "at its starting position")}: " +
                        $"{counts[0]} floor, {counts[1]} couch, {counts[2]} chair-leg and {counts[3]} bin pixels. " +
                        $"{legGap} pixels of brick remained visible between the chair legs.");
                }
            }
            finally
            {
                foreach (var renderer in renderers) renderer.sharedMaterial = brick;
                Object.DestroyImmediate(surface);
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(combined);
                Object.DestroyImmediate(soft);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(raw);
            }
        }
    }
}
