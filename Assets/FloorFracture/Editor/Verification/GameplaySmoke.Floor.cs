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
        private static void VerifyOccupancyRingMesh()
        {
            var ring = game.BoardView.Board.Find("Occupied square");
            var renderer = ring.GetComponent<MeshRenderer>();
            var mesh = ring.GetComponent<MeshFilter>().sharedMesh;
            Require(ring.GetComponentsInChildren<Renderer>(true).Length == 1 && mesh.subMeshCount == 1 &&
                renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial == game.BoardView.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Smoky pane").sharedMaterial,
                "The occupied pane should use one mesh draw with the shared glass material.");
            var vertices = mesh.vertices;
            var colors = mesh.colors;
            int bright = 0, dark = 0;
            float outer = 0f;
            for (int i = 0; i < vertices.Length; ++i)
            {
                float radius = new Vector2(vertices[i].x,vertices[i].z).magnitude;
                outer = Mathf.Max(outer,radius);
                if (colors[i] == Color.white) { ++bright; continue; }
                Require(colors[i].maxColorComponent <= 1f && Mathf.Max(colors[i].r,colors[i].g,colors[i].b) < .05f &&
                    colors[i].a == 1f && radius >= GlassGeometry.RingRadius + GlassGeometry.RingWidth*.5f - .00001f,
                    "The dark keyline must sit outside the original bright stroke.");
                ++dark;
            }
            Require(bright > 0 && dark > 0 && Mathf.Abs(outer - GlassGeometry.RingOuterRadius) < .00001f,
                "The occupied ring must retain its bright stroke and fit the exact radius reserved on supported glass.");
        }

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

        private static void VerifyGlassOpenings()
        {
            var probe = new GameObject("Broken glass opening verification");
            try
            {
                var board = probe.AddComponent<GlassBoardView>();
                var state = new BoardState(9, 9, 379);
                board.Build(state, Pose.identity, null, WaveHazards.LampHeight(0, null));
                var mesh = probe.transform.Find("Opening stencil").GetComponent<MeshFilter>().sharedMesh;
                var fragments = probe.transform.Find("Falling fragments");
                foreach (var cells in new[] { new[] { 0 }, new[] { 0, 1 }, new[] { 0, 10 },
                    new[] { 0, 1, 9 }, new[] { 0, 1, 2, 9, 11, 18, 19, 20 }, new[] { 0, 64, 80 } })
                {
                    board.Draw(null, default, Now, false);
                    foreach (int cell in cells) board.RevealLossAperture(cell);
                    var v = mesh.vertices;
                    Require(v.Length == 4 * cells.Length && v.All(p => Mathf.Abs(p.y) < .0001f),
                        "Each broken tile must contribute one flat aperture.");
                    for (int i = 0; i < cells.Length; i++)
                        Require(Vector3.Distance((v[4*i] + v[4*i+2]) * .5f,
                            GlassGeometry.Ground(state.Center(cells[i]))) < .001f, "The aperture moved off its broken tile.");
                    var lowerMeshes = board.GetComponentsInChildren<MeshFilter>()
                        .Where(f => f.transform.parent == board.transform && f.transform != fragments &&
                            f.sharedMesh && f.sharedMesh.vertexCount > 0 && f.sharedMesh.bounds.min.y < -.001f);
                    Require(!lowerMeshes.Any(), "Breaking glass added a lining beneath the opening.");
                }
                var sides = probe.transform.Find("Glass 1, 1/Glass sides and bottom");
                var tile = sides.GetComponent<MeshFilter>().sharedMesh;
                Require(tile.vertexCount == 20 && tile.triangles.Length == 30 && tile.normals
                    .Select(n => Vector3Int.RoundToInt(n)).Distinct().Count() == 5 &&
                    tile.normals.All(n => Vector3.Dot(n,Vector3.up) < .01f) &&
                    Mathf.Approximately(sides.GetComponent<Renderer>().bounds.size.x,sides.GetComponent<Renderer>().bounds.size.y) &&
                    probe.transform.Find("Glass 8, 8/Glass sides and bottom").GetComponent<MeshFilter>().sharedMesh == tile,
                    "Fitted glass must have four outward sides and a bottom at one block width.");
                var wave=state.BeginWave(80,1,1,10);
                var danger=probe.transform.Find("Glass 8, 8");
                var safe=probe.transform.Find($"Glass {wave.GuaranteedTile%9}, {wave.GuaranteedTile/9}");
                var block=new MaterialPropertyBlock();
                Color Surface(Transform tile)
                {
                    tile.Find("Smoky pane").GetComponent<Renderer>().GetPropertyBlock(block);
                    return block.GetColor("_Color");
                }
                foreach (bool gentle in new[] {false,true})
                {
                    board.Draw(wave,default,10,gentle);
                    Color start=Surface(danger);
                    board.Draw(wave,default,10.18,gentle);
                    Color peak=Surface(danger);
                    Require(peak.r>start.r+.08f && Surface(safe)==GlassTile.Safe,"The single amber pulse missed its target pane.");
                    if (gentle) Require(peak.r<.60f,"Gentle warning went as bright as the standard pulse.");
                    board.Draw(wave,default,10.70,gentle);
                    Require(Surface(danger)==start && danger.Find("Fissures").GetComponent<Renderer>().enabled,
                        "The pulse continued after its initial flash or cracks disappeared early.");
                    board.Draw(wave,default,wave.RevealEndsAt,gentle);
                    Require(Surface(danger)==Surface(safe) && !danger.Find("Fissures").GetComponent<Renderer>().enabled,
                        "The end of the reveal left a color or crack clue on the dangerous pane.");
                }
                Record("A 9 × 9 board rendered distinct apertures at positions 0, 64 and 80, plus adjoining, diagonal, L-shaped and ring-shaped breaks.");
            }
            finally { Object.Destroy(probe); }
        }

        private static void VerifyGlassAndPassthrough()
        {
            float floorY = game.Room.Placement.FloorHeight;
            var panes = game.BoardView.GetComponentsInChildren<GlassTile>();
            Require(panes.Length > 0 && panes.All(pane =>
                Mathf.Abs(pane.transform.Find("Smoky pane").GetComponent<Renderer>().bounds.center.y - floorY) < .0001f),
                "A glass face is above or below the fitted floor.");
            foreach (var mesh in game.BoardView.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.name == "Rim" || mesh.name == "Fissures" || mesh.name.StartsWith("Floor fog") ||
                    mesh.name == "Occupied square" || mesh.name == "Perimeter")
                    foreach (var vertex in mesh.sharedMesh.vertices)
                        Require(Mathf.Abs(mesh.transform.TransformPoint(vertex).y - floorY) <= (mesh.name.StartsWith("Floor fog") ? .35f : .0021f),
                            "A floor marking is floating above the glass: " + mesh.name);
            Record($"All {panes.Length} glass faces are flush with the fitted room floor; rim, cracks and stance ring are within 2 mm, and fog stays below 35 cm.");
            VerifyGlassOpenings();
            VerifyFloorVisibility(false);
            Require(FloorSurface.EffectMeshObjects.Count == 0, "The room created decorative floor patches.");
            const int size = 128;
            var go = new GameObject("Glass verification camera");
            var camera = go.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var image = new Texture2D(size,size,TextureFormat.RGB24,false);
            var old = RenderTexture.active;
            try
            {
                camera.enabled=false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.magenta;
                camera.aspect=1;
                camera.targetTexture=target;
                var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
                Color32[] Read()
                {
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                    return image.GetPixels32();
                }
                VerifyBlockFaces(camera,size,Read);
                using var fixture=new FloorDepthReadback(size,.05f);
                VerifyMovingPaneDepth(fixture,camera,size,size,.05f,Read);
            }
            finally
            {
                RenderTexture.active=old;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void VerifyBlockFaces(Camera camera,int size,System.Func<Color32[]> read)
        {
            var top=game.BoardView.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Smoky pane");
            var body=top.transform.parent.Find("Glass sides and bottom").GetComponent<MeshRenderer>();
            var material=body.sharedMaterial;
            int comparison=material.GetInt("_PortalComparison");
            int topLayer=top.gameObject.layer,bodyLayer=body.gameObject.layer;
            try
            {
                var center=body.bounds.center;
                camera.cullingMask=1<<30;
                camera.nearClipPlane=.03f;
                material.SetInt("_PortalComparison",(int)CompareFunction.Always);
                foreach (var side in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back})
                {
                    var dir=game.BoardView.Board.TransformDirection(side);
                    top.gameObject.layer=side==Vector3.up?30:topLayer;
                    body.gameObject.layer=side==Vector3.up?bodyLayer:30;
                    var rotation=Quaternion.LookRotation(-dir,Mathf.Abs(dir.y)>.9f?game.BoardView.Board.forward:Vector3.up);
                    foreach (float eye in new[]{-.032f,.032f})
                    {
                        camera.transform.SetPositionAndRotation(center+dir*.95f+rotation*Vector3.right*eye,rotation);
                        var pixel=read()[size/2*size+size/2];
                        Require(pixel.g>30 && pixel.r<225,"Missing glass at face "+side+" and eye offset "+eye);
                    }
                }
                Record("Both eye offsets rendered the glass top, four sides and bottom of the fitted block.");
            }
            finally
            {
                material.SetInt("_PortalComparison",comparison);
                top.gameObject.layer=topLayer;
                body.gameObject.layer=bodyLayer;
            }
        }

        private static void VerifyMovingPaneDepth(FloorDepthReadback fixture, Camera camera, int imageSize,
            int sensor, float near, System.Func<Color32[]> read)
        {
            var pane = game.BoardView.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Smoky pane");
            ShaderUtil.CompilePass(pane.sharedMaterial, 0, true);
            var center = pane.bounds.center;
            var oldPose = new Pose(camera.transform.position, camera.transform.rotation);
            int oldMask = camera.cullingMask, oldLayer = pane.gameObject.layer;
            float oldFov = camera.fieldOfView;
            bool wasOrtho = camera.orthographic;
            const float fov = 56;
            var projection = Matrix4x4.Perspective(fov, 1, near, 100);
            projection.m22 = -1;
            projection.m23 = -2 * near;
            var depths = new[] { new float[sensor * sensor], new float[sensor * sensor] };
            var poses = new Matrix4x4[2];
            var heights = new[] { -.25f, .045f, -.015f, .05f, .16f, .03f, .16f };
            float lowestFloor = 1, mostThroughObject = 0, outsideCoverage = 1;
            try
            {
                camera.orthographic = false;
                camera.fieldOfView = fov;
                camera.cullingMask = 1 << 30;
                pane.gameObject.layer = 30;
                foreach (float eyeHeight in new[] { .85f, 1.60f })
                for (int eye = 0; eye < 2; eye++)
                {
                    float reference = 0;
                    for (int frame = 0; frame < heights.Length; frame++)
                    {
                        bool outside = frame == heights.Length - 1;
                        var position = center + new Vector3((eye == 0 ? -.032f : .032f) + .035f * Mathf.Sin(frame), eyeHeight, -.62f);
                        var gaze = Quaternion.LookRotation(center - position);
                        camera.transform.SetPositionAndRotation(position, gaze);
                        // Give the earlier depth capture its own pose, as on a moving headset.
                        var sensorPos = position - Vector3.right * .018f;
                        var rotation = gaze * Quaternion.Euler(0, outside ? 75f : 1.5f, 0);
                        poses[eye] = projection * Matrix4x4.TRS(sensorPos, rotation, new Vector3(1,1,-1)).inverse;
                        float tangent = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
                        for (int i = 0; i < depths[eye].Length; i++)
                        {
                            float y = (2 * (i / sensor + .5f) / sensor - 1) * tangent;
                            float x = (2 * (i % sensor + .5f) / sensor - 1) * tangent;
                            float down = -(rotation * new Vector3(x,y,1)).y;
                            float z = outside ? .35f : (sensorPos.y - game.Room.Placement.FloorHeight - heights[frame]) / Mathf.Max(.01f, down);
                            depths[eye][i] = 1 - near / z;
                        }
                        poses[1-eye] = poses[eye];
                        System.Array.Copy(depths[eye], depths[1-eye], depths[eye].Length);
                        fixture.Capture(depths, poses);
                        var pixels = fixture.ReadGlassEye(eye, read);
                        // Measure opacity against magenta over a small patch inside the intact pane.
                        float signal = 0;
                        for (int y=-2; y<=2; y++)
                        for (int x=-2; x<=2; x++) signal += 255 - pixels[(imageSize/2+y)*imageSize+imageSize/2+x].r;
                        if (frame == 0)
                        {
                            reference = signal;
                            Require(reference > 25*40, "The reference camera missed the intact glass.");
                        }
                        else if (outside) outsideCoverage = Mathf.Min(outsideCoverage,signal/reference);
                        else if (heights[frame] == .16f) mostThroughObject = Mathf.Max(mostThroughObject,signal/reference);
                        else lowestFloor = Mathf.Min(lowestFloor,signal/reference);
                    }
                }
                Record($"Moving pane, standing and crouched stereo views: minimum floor coverage {lowestFloor:P1}, " +
                    $"coverage behind a 16 cm obstacle {mostThroughObject:P1}, outside the depth view {outsideCoverage:P1}.");
                Require(lowestFloor >= .96f && mostThroughObject <= .04f && outsideCoverage >= .96f,
                    "A moving pane lost clear floor or painted through a measured obstacle.");
            }
            finally
            {
                pane.gameObject.layer = oldLayer;
                camera.cullingMask = oldMask;
                camera.orthographic = wasOrtho;
                camera.fieldOfView = oldFov;
                camera.transform.SetPositionAndRotation(oldPose.position,oldPose.rotation);
            }
        }

        private sealed class FloorDepthReadback : System.IDisposable
        {
            private readonly Material preprocess;
            private readonly Texture2DArray raw;
            private readonly RenderTexture current, mono;
            private readonly CommandBuffer commands = new CommandBuffer();
            private readonly Vector4 texel;
            private readonly bool asyncWasAllowed = ShaderUtil.allowAsyncCompilation;
            private readonly bool softWasEnabled = Shader.IsKeywordEnabled("SOFT_OCCLUSION");
            private readonly Texture oldRaw = Shader.GetGlobalTexture("_EnvironmentDepthTexture");
            private readonly Texture oldSoft = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture");
            private readonly Vector4 oldZ = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
            private readonly Matrix4x4[] oldMatrices = Shader.GetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices");
            private Matrix4x4[] poses;

            internal FloorDepthReadback(int size, float near)
            {
                ShaderUtil.allowAsyncCompilation=false;
                texel=new Vector4(1f/size,1f/size,size,size);
                raw=new Texture2DArray(size,size,2,TextureFormat.RFloat,false,true)
                    {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                current=Target(RenderTextureFormat.ARGBHalf);
                mono=Target(RenderTextureFormat.ARGBHalf);
                preprocess=new Material(Shader.Find("Meta/EnvironmentDepth/Preprocessing"));
                preprocess.SetVector("_EnvironmentDepthTexture_TexelSize",texel);
                Shader.EnableKeyword("SOFT_OCCLUSION");
                Shader.SetGlobalVector("_EnvironmentDepthZBufferParams",new Vector4(-2*near,-1,0,0));

                RenderTexture Target(RenderTextureFormat format)
                {
                    var target = new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear)
                        {dimension = TextureDimension.Tex2DArray, volumeDepth = 2, wrapMode = TextureWrapMode.Clamp};
                    target.Create();
                    return target;
                }
            }

            internal void Capture(float[][] samples, Matrix4x4[] matrices)
            {
                for (int eye=0; eye<2; eye++) raw.SetPixelData(samples[eye],0,eye);
                raw.Apply(false);
                preprocess.SetTexture("_EnvironmentDepthTexture",raw);
                Draw(preprocess,0,current);
                Shader.SetGlobalTexture("_EnvironmentDepthTexture",raw);
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture",current);
                Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices",matrices);
                poses=matrices;
            }

            private void Draw(Material material, int pass, RenderTexture target)
            {
                ShaderUtil.SetAsyncCompilation(commands, false);
                commands.SetRenderTarget(new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, -1));
                commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3, 2);
                ShaderUtil.RestoreAsyncCompilation(commands);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
            }

            internal Color32[] ReadGlassEye(int eye, System.Func<Color32[]> read)
            {
                Graphics.CopyTexture(current, eye, 0, mono, 0, 0);
                var oldTexel = Shader.GetGlobalVector("_PreprocessedEnvironmentDepthTexture_TexelSize");
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", mono);
                Shader.SetGlobalVector("_PreprocessedEnvironmentDepthTexture_TexelSize", texel);
                Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices", new[] {poses[eye], poses[eye]});
                try { return read(); }
                finally
                {
                    Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", current);
                    Shader.SetGlobalVector("_PreprocessedEnvironmentDepthTexture_TexelSize", oldTexel);
                    Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices", poses);
                }
            }

            public void Dispose()
            {
                ShaderUtil.allowAsyncCompilation = asyncWasAllowed;
                Shader.SetGlobalTexture("_EnvironmentDepthTexture", oldRaw);
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", oldSoft);
                Shader.SetGlobalVector("_EnvironmentDepthZBufferParams", oldZ);
                Shader.SetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices",
                    oldMatrices != null && oldMatrices.Length > 0 ? oldMatrices : new[] {Matrix4x4.identity, Matrix4x4.identity});
                if (!softWasEnabled) Shader.DisableKeyword("SOFT_OCCLUSION");
                commands.Dispose();
                foreach (Object item in new Object[] {preprocess, raw, current, mono})
                    Object.DestroyImmediate(item);
            }
        }
    }
}
