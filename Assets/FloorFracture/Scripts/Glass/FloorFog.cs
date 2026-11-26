using System;
using UnityEngine;

namespace FloorFracture
{
    // Four stacked noise slices below knee height, plus up to three drifting patches that hide single panes.
    internal sealed class FloorFog : MonoBehaviour
    {
        static readonly float[] Heights = {.02f,.10f,.20f,.32f};
        static readonly int NoiseId = Shader.PropertyToID("_Noise"), ExtentId = Shader.PropertyToID("_Extent"),
            DriftId = Shader.PropertyToID("_Drift"), DensityId = Shader.PropertyToID("_Density"),
            FadeId = Shader.PropertyToID("_Fade"), TimeId = Shader.PropertyToID("_FogTime"), PatchesId = Shader.PropertyToID("_Patches"),
            BoardYId = Shader.PropertyToID("_BoardY");
        static readonly Vector4 NoPatch = new(0,0,1,0);
        readonly Vector4[] patches = {NoPatch,NoPatch,NoPatch};
        readonly Vector4[] paths = new Vector4[3]; // x period, z period, x phase, z phase
        readonly Vector2[] drawn = new Vector2[3];
        Vector2[] panes;
        double lastTick;
        bool placed;
        MeshRenderer[] slices;
        Material material;
        Mesh mesh;
        Texture2D noise;
        Vector2 reach;
        float radius, inset;
        WaveHazards hazards = WaveHazards.None;

        public void Build(BoardState board, Material fogMaterial)
        {
            material = fogMaterial;
            var half = new Vector2(board.Width*board.Pitch*.5f+.30f, board.Height*board.Pitch*.5f+.30f);
            // Patch centres stay over the outer pane centres.
            reach = new Vector2((board.Width-1)*board.Pitch*.5f, (board.Height-1)*board.Pitch*.5f);
            radius = .45f*board.Pitch;
            inset = .30f*board.Pitch;
            var centers = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < board.Count; ++i) if (board.Layout.Contains(i)) centers.Add(board.Center(i));
            panes = centers.ToArray();
            var b = new GlassGeometry.Builder();
            b.Quad(new(-half.x,0,-half.y),new(half.x,0,-half.y),new(half.x,0,half.y),new(-half.x,0,half.y),Color.white,Color.white);
            mesh = b.Create("Fog slice");
            noise = Noise(board.Seed);
            material.SetTexture(NoiseId, noise);
            material.SetVector(ExtentId, half);
            material.SetVectorArray(PatchesId, patches);
            slices = new MeshRenderer[Heights.Length];
            for (int i = 0; i < slices.Length; ++i)
            {
                slices[i] = GlassTile.Part(transform, "Floor fog "+i, mesh, material);
                slices[i].transform.localPosition = new Vector3(0, Heights[i], 0);
                slices[i].enabled = false;
            }
        }

        public void Show(WaveHazards shown)
        {
            hazards = shown;
            var random = new System.Random(shown.VisualSeed);
            float R() => (float)random.NextDouble();
            material.SetVector(DriftId, new Vector4(R(), R(), R(), R()));
            material.SetFloat(DensityId, shown.FogDensity);
            for (int k = 0; k < paths.Length; ++k)
                paths[k] = new Vector4(9+5*R(), 9+5*R(), 2*Mathf.PI*R(), 2*Mathf.PI*R());
            placed = false;
        }

        public void Tick(double now, float fade)
        {
            bool on = fade > 0 && (hazards.FogDensity > 0 || hazards.FogPatches > 0);
            foreach (var slice in slices) slice.enabled = on;
            if (!on) return;
            material.SetFloat(FadeId, fade);
            material.SetFloat(TimeId, (float)now);
            // The fall lifts the board, so the fog measures its height from the glass, not the room floor.
            material.SetFloat(BoardYId, transform.position.y);
            // Ease toward the target so a hop across a missing cell glides instead of jumping.
            float follow = placed ? 1-Mathf.Exp(-(float)Math.Min(.1, Math.Max(0, now-lastTick))/.5f) : 1;
            placed = true; lastTick = now;
            for (int k = 0; k < patches.Length; ++k)
            {
                if (k >= hazards.FogPatches) { patches[k] = NoPatch; continue; }
                var path = paths[k];
                var target = OverPanes(new Vector2(reach.x*(float)Math.Sin(2*Math.PI*now/path.x+path.z),
                    reach.y*(float)Math.Sin(2*Math.PI*now/path.y+path.w)));
                drawn[k] = Vector2.Lerp(drawn[k], target, follow);
                patches[k] = new Vector4(drawn[k].x, drawn[k].y, radius, .9f);
            }
            material.SetVectorArray(PatchesId, patches);
        }

        // Nearest point at least 0.2 pitch inside a pane, so a patch over a missing cell still hides glass.
        Vector2 OverPanes(Vector2 point)
        {
            float best = float.PositiveInfinity;
            var nearest = point;
            foreach (var pane in panes)
            {
                var offset = point-pane;
                var q = pane+new Vector2(Mathf.Clamp(offset.x,-inset,inset), Mathf.Clamp(offset.y,-inset,inset));
                float d = (q-point).sqrMagnitude;
                if (d < best) { best = d; nearest = q; }
            }
            return nearest;
        }

        // 128² value noise from two octaves on 8- and 16-cell lattices, wrapped so the texture tiles.
        static Texture2D Noise(int seed)
        {
            const int size = 128;
            var random = new System.Random(seed);
            float[] Lattice(int cells)
            {
                var values = new float[cells*cells];
                for (int i = 0; i < values.Length; ++i) values[i] = (float)random.NextDouble();
                return values;
            }
            float Sample(float[] lattice, int cells, int x, int y)
            {
                float fx = x*cells/(float)size, fy = y*cells/(float)size;
                int x0 = (int)fx, y0 = (int)fy;
                float tx = Mathf.SmoothStep(0,1,fx-x0), ty = Mathf.SmoothStep(0,1,fy-y0);
                float L(int i, int j) => lattice[j%cells*cells+i%cells];
                return Mathf.Lerp(Mathf.Lerp(L(x0,y0),L(x0+1,y0),tx), Mathf.Lerp(L(x0,y0+1),L(x0+1,y0+1),tx), ty);
            }
            var coarse = Lattice(8); var fine = Lattice(16);
            var pixels = new byte[size*size];
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x)
                    pixels[y*size+x] = (byte)(255*(.65f*Sample(coarse,8,x,y)+.35f*Sample(fine,16,x,y)));
            var texture = new Texture2D(size,size,TextureFormat.R8,false,true)
                {name="Fog noise",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.DontSave};
            texture.SetPixelData(pixels,0);
            texture.Apply(false,true);
            return texture;
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (noise) Destroy(noise);
        }
    }
}
