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
                meshes.All(o => !registered.Contains(o.effectMeshGO.GetComponent<MeshFilter>())),
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
                material.shader.name == "Floor Fracture/Surrounding Brick" && material.HasProperty("_RequireEnvironmentDepth"),
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
                    colors.Count(c => c.b > c.g && c.g > c.r) > colors.Length * .95f,
                    "Expected blue shaft stone with distinguishable recessed joints.");
                VerifyMasonryMatch(material, camera, Read);
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

        // Compare the actual shaders at identical metre coordinates and with neutral light.
        private static void VerifyMasonryMatch(Material floorMaterial, Camera camera, System.Func<Color32[]> read)
        {
            const int layer = 31;
            var rotation = camera.transform.rotation;
            var position = camera.transform.position;
            var size = camera.orthographicSize;
            int mask = camera.cullingMask;
            var sample = new GameObject("Masonry verification swatch");
            var stencil = new GameObject("Verification aperture");
            var mesh = new Mesh();
            var aperture = new Material(Resources.Load<Shader>("FloorFractureMask"));
            var neighbor = new Material(Resources.Load<Shader>("FloorFractureDepth"));
            var chamber = new Material(Resources.Load<Shader>("Endings/Chamber"));
            try
            {
                var vertices = new[] {new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0)};
                mesh.vertices = vertices;
                mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
                mesh.uv = vertices.Select(v => new Vector2(v.x, -v.y)).ToArray();
                // FallMeshes supplies a face number in TEXCOORD1 on every shaft vertex.
                mesh.uv2 = new Vector2[4];
                mesh.triangles = new[] {0,1,2,0,2,3};
                foreach (var obj in new[] {sample, stencil})
                {
                    obj.layer = layer;
                    obj.transform.rotation = Quaternion.Euler(-90,0,0);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                }
                stencil.AddComponent<MeshRenderer>().sharedMaterial = aperture;
                var surface = sample.AddComponent<MeshRenderer>();
                surface.sharedMaterial = floorMaterial;
                camera.cullingMask = 1 << layer;
                camera.orthographicSize = .8f;
                camera.transform.SetPositionAndRotation(Vector3.up * 2, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var floor = read();
                Require(floor.All(IsBrick), "The masonry swatch must fill the test camera.");
                neighbor.SetFloat("_Courses", 1);
                chamber.SetFloat("_Role", 1);
                chamber.SetFloat("_Theme", 2);
                foreach (var material in new[] {neighbor, chamber})
                {
                    surface.sharedMaterial = material;
                    var pixels = read();
                    int matched = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        var a = pixels[i]; var b = floor[i];
                        if (Mathf.Abs(a.r - b.r) <= 2 && Mathf.Abs(a.g - b.g) <= 2 && Mathf.Abs(a.b - b.b) <= 2) matched++;
                    }
                    Require(matched >= floor.Length * .98f,
                        $"{material.shader.name} matches the floor's masonry at {matched}/{floor.Length} pixels.");
                }
                Record("The exterior floor, neighboring hole and falling shaft rendered matching blue brick faces and mortar at the same physical scale.");
            }
            finally
            {
                camera.cullingMask = mask;
                camera.orthographicSize = size;
                camera.transform.SetPositionAndRotation(position, rotation);
                foreach (Object item in new Object[] {sample, stencil, mesh, aperture, neighbor, chamber})
                    Object.DestroyImmediate(item);
            }
        }

        // Magenta is the sentinel for passthrough. No virtual geometry was drawn there.
        private static bool IsBrick(Color32 pixel) => !(pixel.r > 245 && pixel.g < 8 && pixel.b > 245);


        private enum DepthObject { Floor, Couch, ChairLeg, Bin }

        private static DepthObject ObjectAt(Vector2 p, bool moved)
        {
            float y = Mathf.Abs(p.y - .5f);
            if (p.x > .14f && p.x < .33f && y < .23f) return DepthObject.Couch;
            if (y < .15f && (Mathf.Abs(p.x - .435f) < .022f || Mathf.Abs(p.x - .555f) < .022f))
                return DepthObject.ChairLeg;
            return new Vector2(p.x - (moved ? .81f : .69f), y).sqrMagnitude < .053f * .053f
                ? DepthObject.Bin : DepthObject.Floor;
        }

        private static void VerifyFloorDepth(MeshRenderer[] renderers, Material brick, Camera camera,
            int imageSize, System.Func<Color32[]> read, Color32[] reference)
        {
            VerifyFloorVisibility(true);
            const int sensor = 128;
            const float near = .1f, distance = 3f;
            float width = 2 * camera.orthographicSize;
            var projection = Matrix4x4.Perspective(2 * Mathf.Atan(camera.orthographicSize / distance) * Mathf.Rad2Deg, 1, near, 100);
            projection.m22 = -1; // Infinite far plane, matching the synthetic Z parameters.
            projection.m23 = -2 * near;
            Matrix4x4 Pose(float x) => projection * Matrix4x4.TRS(camera.transform.position + Vector3.right * x,
                camera.transform.rotation, new Vector3(1,1,-1)).inverse;
            Vector2 Pixel(int i, int size) => new Vector2((i % size) + .5f, (i / size) + .5f) / size;

            using var fixture = new FloorDepthReadback(brick, renderers, sensor, near, distance);
            var depths = new[] {new float[sensor*sensor], new float[sensor*sensor]};
            var poses = new Matrix4x4[2];
            double now = 1;
            foreach (bool moved in new[] {false, true})
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    poses[eye] = Pose(0);
                    for (int i = 0; i < depths[eye].Length; i++)
                    {
                        float metres = ObjectAt(Pixel(i, sensor), moved) switch
                        {
                            DepthObject.Couch => 1.50f + eye * .05f,
                            DepthObject.ChairLeg => 2.40f + eye * .03f,
                            DepthObject.Bin => 2.85f + eye * .01f,
                            _ => i % 2 == 0 ? 2.96f : 3.04f
                        };
                        depths[eye][i] = 1 - near / metres;
                    }
                }
                fixture.Capture(depths, poses, now += 1d / 30);
                for (int eye = 0; eye < 2; eye++)
                {
                    var actual = fixture.ReadEye(eye, false, read);
                    int[] counts = new int[4];
                    int legGap = 0, oldBin = 0;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        if (!IsBrick(reference[i])) continue;
                        var uv = Pixel(i, imageSize);
                        var shape = ObjectAt(uv, moved);
                        const float radius = 2.5f / sensor;
                        if (ObjectAt(uv+new Vector2(-radius,-radius), moved) != shape ||
                            ObjectAt(uv+new Vector2(-radius, radius), moved) != shape ||
                            ObjectAt(uv+new Vector2( radius,-radius), moved) != shape ||
                            ObjectAt(uv+new Vector2( radius, radius), moved) != shape) continue;
                        if (shape == DepthObject.Floor)
                        {
                            Require(Mathf.Abs(actual[i].r - reference[i].r) <= 3 && Mathf.Abs(actual[i].g - reference[i].g) <= 3,
                                $"Eye {eye} lost bare floor at {uv}: {actual[i]} vs {reference[i]}.");
                            if (uv.x > .48f && uv.x < .51f && Mathf.Abs(uv.y - .5f) < .1f) legGap++;
                            if (moved && Mathf.Abs(uv.x - .69f) < .02f && Mathf.Abs(uv.y - .5f) < .02f) oldBin++;
                        }
                        else Require(!IsBrick(actual[i]), $"Eye {eye} painted {shape} at {uv}: {actual[i]}.");
                        counts[(int)shape]++;
                    }
                    Require(counts.All(n => n >= 4) && legGap > 4 && (!moved || oldBin > 4),
                        "The fixture must exercise the couch, separate chair legs, bin and vacated floor.");
                    Record($"Native soft GPU readback, eye {eye}, bin {(moved ? "moved" : "in place")}: " +
                        $"{counts[1]} couch, {counts[2]} chair-leg and {counts[3]} bin pixels in passthrough; " +
                        $"{legGap} visible brick pixels between the legs, {oldBin} at the vacated bin center.");
                }
            }

            // A tapered furniture foot meets the floor at a fixed world X. Intersect each moving
            // depth-camera ray with that slope; compare only identical floor points in the final images.
            const float slope = .45f;
            float edge = width * .04f;
            float[] headX = {-.042f, -.021f, .010f, .045f, .022f, -.009f, -.038f};
            foreach (var error in new[] {new float[7], new[] {.018f,-.018f,.018f,-.018f,.018f,-.018f,.018f},
                         new[] {.014f,-.006f,-.015f,.004f,.018f,-.009f,.002f}})
            {
                fixture.History.Clear();
                var sum = new float[2];
                var peak = new float[2];
                var edgeDrift = new float[2];
                var lastEdge = new float[2,2];
                var previous = new Color32[2,2][];
                int pairs = 0, edgePairs = 0;
                for (int frame = 0; frame < headX.Length; frame++)
                {
                    for (int eye = 0; eye < 2; eye++)
                    {
                        float x = headX[frame] + (eye == 0 ? -.032f : .032f);
                        poses[eye] = Pose(x);
                        for (int i = 0; i < depths[eye].Length; i++)
                        {
                            float rayX = (Pixel(i, sensor).x - .5f) * width / distance;
                            float range = Mathf.Clamp((distance - slope * (edge - x)) / (1 - slope * rayX), distance - .30f, distance);
                            range += error[frame] + (error[0] == 0 ? 0 : .004f * Mathf.Sin((i / sensor) * .22f + frame));
                            depths[eye][i] = 1 - near / range;
                        }
                    }
                    fixture.Capture(depths, poses, now += 1d / 30);
                    for (int mode = 0; mode < 2; mode++)
                    for (int eye = 0; eye < 2; eye++)
                    {
                        var actual = fixture.ReadEye(eye, mode == 0, read);
                        float edgeSum = 0;
                        int rows = 0;
                        float Contrast(int i) => (255f - actual[i].r) / (255f - reference[i].r);
                        for (int i = 1; i < actual.Length; i++)
                        {
                            if (!IsBrick(reference[i]) || !IsBrick(reference[i-1])) continue;
                            float floorX = (Pixel(i, imageSize).x - .5f) * width;
                            float height = slope * (edge - floorX);
                            if (height < .012f || height > .15f) continue;
                            if (Contrast(i) >= .5f && Contrast(i-1) < .5f)
                            {
                                edgeSum += floorX - width/imageSize * (1 - Mathf.InverseLerp(Contrast(i-1), Contrast(i), .5f));
                                rows++;
                            }
                            if (frame == 0) continue;
                            float change = Mathf.Abs(actual[i].r - previous[mode,eye][i].r) / (255f - reference[i].r);
                            sum[mode] += change;
                            peak[mode] = Mathf.Max(peak[mode], change);
                            if (mode == 0) pairs++;
                        }
                        Require(rows >= 8, "The contact readback must contain a measurable half-contrast contour.");
                        float contour = edgeSum / rows;
                        if (frame > 0)
                        {
                            edgeDrift[mode] += Mathf.Abs(contour - lastEdge[mode,eye]);
                            if (mode == 0) edgePairs++;
                        }
                        lastEdge[mode,eye] = contour;
                        previous[mode,eye] = actual;
                    }
                }
                string sequence = error[0] == 0 ? "lateral motion" : error[0] == .018f ? "alternating range error" : "irregular range error";
                Record($"Furniture contact, {sequence}, {pairs} matched floor-pixel pairs across both eyes: " +
                    $"mean contrast change {sum[0]/pairs:F4} to {sum[1]/pairs:F4}, peak {peak[0]:F4} to {peak[1]:F4}, " +
                    $"mean half-contrast edge travel {1000*edgeDrift[0]/edgePairs:F1} to {1000*edgeDrift[1]/edgePairs:F1} mm.");
                Require(pairs > 100 && sum[1] < sum[0] * .85f && peak[1] < peak[0] * .85f && edgeDrift[1] < edgeDrift[0] * .85f,
                    "The furniture contact must improve on the released mask at matched world points for " + sequence);
            }
            fixture.History.Update(now + .15);
            Require(fixture.Surface.GetFloat("_FloorPreviousWeight") == 0, "Old depth history survived a sensor pause.");
        }

        // Runs the same GPU passes and packed stereo textures as the SDK. The old mask is the A/B control.
        private sealed class FloorDepthReadback : System.IDisposable
        {
            internal readonly Material Surface;
            internal readonly FloorDepthHistory History;
            private readonly MeshRenderer[] renderers;
            private readonly Material brick, mask, preprocess;
            private readonly Texture2DArray[] raw = new Texture2DArray[2];
            private readonly Texture2DArray floor;
            private readonly RenderTexture masked, legacy, current, mono, previousMono;
            private readonly CommandBuffer commands = new CommandBuffer();
            private readonly Vector4 texel;
            private readonly float bias;
            private readonly bool asyncWasAllowed = ShaderUtil.allowAsyncCompilation;
            private readonly bool softWasEnabled = Shader.IsKeywordEnabled("SOFT_OCCLUSION");
            private readonly Texture oldRaw = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
            private readonly Texture oldSoft = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture");
            private readonly Vector4 oldZ = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
            private readonly Matrix4x4[] oldMatrices = Shader.GetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices");
            private Matrix4x4[] poses, previousPoses;
            private Texture previous;
            private float weight;
            private int frame;

            internal FloorDepthReadback(Material brick, MeshRenderer[] renderers, int size, float near, float distance)
            {
                ShaderUtil.allowAsyncCompilation = false;
                this.brick = brick;
                this.renderers = renderers;
                texel = new Vector4(1f/size, 1f/size, size, size);
                Surface = new Material(brick);
                bias = Surface.GetFloat("_EnvironmentDepthBias");
                History = new FloorDepthHistory(Surface);
                foreach (var renderer in renderers) renderer.sharedMaterial = Surface;
                for (int i = 0; i < 2; i++) raw[i] = Raw();
                floor = Raw();
                var scanned = Enumerable.Repeat(1 - near / (distance * .98f), size * size).ToArray();
                for (int eye = 0; eye < 2; eye++) floor.SetPixelData(scanned, 0, eye);
                floor.Apply(false);
                masked = Target(RenderTextureFormat.RFloat);
                legacy = Target(RenderTextureFormat.ARGBHalf);
                current = Target(RenderTextureFormat.ARGBHalf);
                mono = Target(RenderTextureFormat.ARGBHalf);
                previousMono = Target(RenderTextureFormat.ARGBHalf);
                mask = new Material(Shader.Find("Meta/EnvironmentDepth/DepthMask"));
                mask.SetTexture("_MaskTexture", floor);
                preprocess = new Material(Shader.Find("Meta/EnvironmentDepth/Preprocessing"));
                preprocess.SetVector("_EnvironmentDepthTexture_TexelSize", texel);
                Shader.EnableKeyword("SOFT_OCCLUSION");
                Surface.EnableKeyword("SOFT_OCCLUSION");
                Surface.SetTexture("_PreprocessedEnvironmentDepthTexture", mono);
                Surface.SetVector("_PreprocessedEnvironmentDepthTexture_TexelSize", texel);
                var z = new Vector4(-2 * near, -1, 0, 0);
                Surface.SetVector("_EnvironmentDepthZBufferParams", z);
                Shader.SetGlobalVector("_EnvironmentDepthZBufferParams", z);

                Texture2DArray Raw() => new Texture2DArray(size, size, 2, TextureFormat.RFloat, false, true)
                    {filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp};
                RenderTexture Target(RenderTextureFormat format)
                {
                    var target = new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear)
                        {dimension = TextureDimension.Tex2DArray, volumeDepth = 2, wrapMode = TextureWrapMode.Clamp};
                    target.Create();
                    return target;
                }
            }

            internal void Capture(float[][] samples, Matrix4x4[] matrices, double now)
            {
                var capture = raw[frame++ % raw.Length];
                for (int eye = 0; eye < 2; eye++) capture.SetPixelData(samples[eye], 0, eye);
                capture.Apply(false);
                mask.SetTexture("_EnvironmentDepthTexture", capture);
                Draw(mask, 1, masked);
                preprocess.SetTexture("_EnvironmentDepthTexture", masked);
                Draw(preprocess, 0, legacy);
                preprocess.SetTexture("_EnvironmentDepthTexture", capture);
                Draw(preprocess, 0, current);
                Shader.SetGlobalTexture("_EnvironmentDepthTexture", capture);
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", current);
                Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices", matrices);
                History.Update(now);
                poses = matrices;
                previous = Surface.GetTexture("_FloorPreviousDepth");
                previousPoses = Surface.GetMatrixArray("_FloorPreviousReprojection");
                weight = Surface.GetFloat("_FloorPreviousWeight");
                History.Update(now + .005);
                Require(Surface.GetTexture("_FloorPreviousDepth") == previous && Surface.GetFloat("_FloorPreviousWeight") == weight,
                    "Rendering another display frame advanced the depth history without a new sensor capture.");
            }

            private void Draw(Material material, int pass, RenderTexture target)
            {
                ShaderUtil.SetAsyncCompilation(commands, false);
                commands.SetRenderTarget(new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, -1));
                commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 2);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
            }

            internal Color32[] ReadEye(int eye, bool released, System.Func<Color32[]> read)
            {
                // A mono Editor camera reads slice zero. Present each actual stereo slice and its own pose there.
                Graphics.CopyTexture(released ? legacy : current, eye, 0, mono, 0, 0);
                Surface.SetMatrixArray("_EnvironmentDepthReprojectionMatrices", new[] {poses[eye], poses[eye]});
                Surface.SetFloat("_EnvironmentDepthBias", released ? 0 : bias);
                Surface.SetFloat("_FloorPreviousWeight", released ? 0 : weight);
                if (weight > 0)
                {
                    Graphics.CopyTexture(previous, eye, 0, previousMono, 0, 0);
                    Surface.SetTexture("_FloorPreviousDepth", previousMono);
                    Surface.SetMatrixArray("_FloorPreviousReprojection", new[] {previousPoses[eye], previousPoses[eye]});
                }
                // The preceding check rendered with depth unavailable. Settle that first variant switch
                // once; each subsequent read is the first draw of its new capture, camera pose or eye.
                if (frame == 1 && eye == 0 && !released) read();
                var pixels = read();
                Surface.SetTexture("_FloorPreviousDepth", previous);
                Surface.SetFloat("_FloorPreviousWeight", weight);
                return pixels;
            }

            public void Dispose()
            {
                ShaderUtil.allowAsyncCompilation = asyncWasAllowed;
                History.Dispose();
                foreach (var renderer in renderers) renderer.sharedMaterial = brick;
                Shader.SetGlobalTexture("_EnvironmentDepthTexture", oldRaw);
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", oldSoft);
                Shader.SetGlobalVector("_EnvironmentDepthZBufferParams", oldZ);
                Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices",
                    oldMatrices != null && oldMatrices.Length > 0 ? oldMatrices : new[] {Matrix4x4.identity, Matrix4x4.identity});
                if (!softWasEnabled) Shader.DisableKeyword("SOFT_OCCLUSION");
                commands.Dispose();
                foreach (Object item in new Object[] {Surface, mask, preprocess, raw[0], raw[1], floor, masked, legacy, current, mono, previousMono})
                    Object.DestroyImmediate(item);
            }
        }
    }
}
