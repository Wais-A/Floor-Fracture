using System;
using UnityEngine;

namespace FloorFracture
{
    // Up to three lamps above the board's outer panes. Each beam sweeps a warm pool across the glass and each lamp hums.
    internal sealed class OverheadLights : MonoBehaviour
    {
        const int MaxLamps = 3, Segments = 24;
        const float PoolHeight = .004f;
        static readonly int FadeId = Shader.PropertyToID("_Fade"), PoolId = Shader.PropertyToID("_Pool"),
            MappedFloorId = Shader.PropertyToID("_MappedFloorY");
        readonly Transform[] lamps = new Transform[MaxLamps];
        readonly AudioSource[] hums = new AudioSource[MaxLamps];
        readonly MeshRenderer[] beams = new MeshRenderer[MaxLamps], pools = new MeshRenderer[MaxLamps];
        readonly Mesh[] cones = new Mesh[MaxLamps];
        readonly Vector4[] paths = new Vector4[MaxLamps]; // ω, x phase, z phase, unused
        readonly Vector3[] vertices = new Vector3[2*(Segments+1)], normals = new Vector3[2*(Segments+1)];
        Material beamMaterial, poolMaterial;
        Mesh poolMesh;
        GameSound sound;
        Vector2 extents;
        float radius;
        bool zIsLonger;
        // Each lamp fades on its own, so a lamp lit by two waves in a row stays lit. A lamp is lit while dimAt is +∞.
        readonly double[] litAt = new double[MaxLamps], dimAt = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        MaterialPropertyBlock block;

        // renderQueue is the fog's queue + 1, so the pools draw over the fog and the fissures.
        public void Build(BoardState board, float lampHeightLocal, GameSound gameSound, int renderQueue)
        {
            sound = gameSound;
            block = new MaterialPropertyBlock();
            // Pool centres stay over the outer pane centres.
            extents = new Vector2((board.Width-1)*board.Pitch*.5f, (board.Height-1)*board.Pitch*.5f);
            radius = .55f*board.Pitch;
            zIsLonger = board.Height > board.Width;
            beamMaterial = GlassTile.MakeMaterial("FloorFractureBeam");
            poolMaterial = new Material(beamMaterial) {name = "FloorFractureBeam pool", hideFlags = HideFlags.DontSave};
            poolMaterial.SetFloat(PoolId, 1);
            foreach (var material in new[] {beamMaterial, poolMaterial})
            {
                material.renderQueue = renderQueue;
                material.SetFloat(MappedFloorId, transform.position.y);
            }
            var b = new GlassGeometry.Builder();
            b.Quad(new(-radius,0,-radius),new(radius,0,-radius),new(radius,0,radius),new(-radius,0,radius),Color.white,Color.white);
            poolMesh = b.Create("Spotlight pool");
            var triangles = new int[3*Segments];
            for (int i = 0; i < Segments; ++i)
            {
                triangles[3*i] = i; triangles[3*i+1] = Segments+2+i; triangles[3*i+2] = Segments+1+i;
            }
            // Over the left, right, then far outer panes. The panes lie on the floor, so no lamp can hang inside a wall.
            var sides = new[] {new Vector2(-extents.x,0), new Vector2(extents.x,0), new Vector2(0,extents.y)};
            for (int k = 0; k < MaxLamps; ++k)
            {
                lamps[k] = new GameObject("Lamp "+k).transform;
                lamps[k].SetParent(transform,false);
                lamps[k].localPosition = GlassGeometry.Ground(sides[k], lampHeightLocal);
                if (sound) hums[k] = sound.CreateLampVoice(lamps[k]);
                lamps[k].gameObject.SetActive(false);
                cones[k] = new Mesh {name = "Spotlight beam "+k};
                cones[k].MarkDynamic();
                cones[k].vertices = vertices; cones[k].normals = normals;
                cones[k].SetTriangles(triangles,0);
                beams[k] = GlassTile.Part(transform, "Spotlight beam "+k, cones[k], beamMaterial);
                pools[k] = GlassTile.Part(transform, "Spotlight pool "+k, poolMesh, poolMaterial);
                beams[k].enabled = pools[k].enabled = false;
            }
        }

        // Lamps that stay lit keep their sweep, fade and hum. New lamps fade in, and lamps no longer wanted fade out.
        public void Show(WaveHazards shown, double now)
        {
            var random = new System.Random(unchecked(shown.VisualSeed*31+7)); // apart from the fog's stream
            float R() => (float)random.NextDouble();
            for (int k = 0; k < paths.Length; ++k)
            {
                // Half a period crosses the longer side in 3-5 s. That side moves at ω, or at 1.3ω along z.
                float omega = Mathf.PI/(3+2*R())/(zIsLonger ? 1.3f : 1f);
                var path = new Vector4(omega, 2*Mathf.PI*R(), 2*Mathf.PI*R(), 0);
                bool lit = double.IsPositiveInfinity(dimAt[k]);
                if (k >= shown.Spotlights) { if (lit) dimAt[k] = now; }
                else if (!lit) { paths[k] = path; litAt[k] = now; dimAt[k] = double.PositiveInfinity; }
            }
        }

        public void Tick(double now)
        {
            for (int k = 0; k < MaxLamps; ++k)
            {
                float fade = WaveHazards.Fade(now, litAt[k], dimAt[k]);
                bool on = fade > 0;
                if (lamps[k].gameObject.activeSelf != on) lamps[k].gameObject.SetActive(on);
                beams[k].enabled = pools[k].enabled = on;
                if (hums[k]) sound.SetLampLevel(hums[k], fade);
                if (!on) continue;
                block.SetFloat(FadeId, fade);
                beams[k].SetPropertyBlock(block);
                pools[k].SetPropertyBlock(block);
                var path = paths[k];
                var center = new Vector2(extents.x*(float)Math.Sin(path.x*now+path.y),
                    extents.y*(float)Math.Sin(1.3*path.x*now+path.z));
                pools[k].transform.localPosition = GlassGeometry.Ground(center, PoolHeight);
                Aim(k, center);
            }
        }

        // An open cone from the lamp to the pool's rim on the floor.
        void Aim(int k, Vector2 center)
        {
            var apex = lamps[k].localPosition;
            for (int i = 0; i <= Segments; ++i)
            {
                float angle = 2*Mathf.PI*i/Segments, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                var rim = new Vector3(center.x+radius*cos, PoolHeight, center.y+radius*sin);
                var normal = Vector3.Cross(rim-apex, new Vector3(-sin,0,cos)).normalized;
                vertices[i] = apex; vertices[Segments+1+i] = rim;
                normals[i] = normals[Segments+1+i] = normal;
            }
            cones[k].SetVertices(vertices);
            cones[k].SetNormals(normals);
            cones[k].RecalculateBounds();
        }

        void OnDestroy()
        {
            foreach (var cone in cones) if (cone) Destroy(cone);
            if (poolMesh) Destroy(poolMesh);
            if (beamMaterial) Destroy(beamMaterial);
            if (poolMaterial) Destroy(poolMaterial);
        }
    }
}
