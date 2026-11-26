using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace FloorFracture
{
    // Copy Unity-owned buffers on the main thread. The fitter can inspect the snapshot off-thread.
    internal sealed class FloorMeshScan
    {
        internal readonly Vector3[] Vertices;
        internal readonly int[] Indices;
        // Each heading survey of a fit asks for the same faces; the answer depends only on the two heights.
        private readonly object raisedLock = new object();
        private float raisedCutoff, raisedHeadHigh;
        private bool[] raisedFaces;
        private FloorMeshScan(Vector3[] vertices, int[] indices)
        {
            Vertices = vertices;
            Indices = indices;
        }

        internal bool Matches(FloorMeshScan other)
        {
            if (other == null || other.Vertices.Length != Vertices.Length || other.Indices.Length != Indices.Length)
                return false;
            for (int i = 0; i < Vertices.Length; i++)
                if (!Vertices[i].Equals(other.Vertices[i]))
                    return false;
            for (int i = 0; i < Indices.Length; i++)
                if (Indices[i] != other.Indices[i])
                    return false;
            return true;
        }

        // A reconstructed bump can have many faces around just one noisy vertex.
        // Group faces only through measured vertices above the cutoff. Shared floor
        // vertices and duplicate faces must not turn separate speckles into an object.
        internal bool[] FindRaisedFaces(float cutoff, float headHigh, CancellationToken cancel)
        {
            lock (raisedLock)
                if (raisedFaces != null && raisedCutoff == cutoff && raisedHeadHigh == headHigh)
                    return raisedFaces;
            int count = Indices.Length / 3;
            var parent = new int[count];
            var firstFace = new Dictionary<Vector3, int>();
            var baseY = new float[count];
            for (int face = 0; face < count; face++)
            {
                parent[face] = -1;
                if ((face & 255) == 0) cancel.ThrowIfCancellationRequested();
                var a = Vertices[Indices[3 * face]];
                var b = Vertices[Indices[3 * face + 1]];
                var c = Vertices[Indices[3 * face + 2]];
                float low = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                if (low > headHigh || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) <= cutoff ||
                    Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-16f) continue;
                parent[face] = face;
                baseY[face] = Mathf.Max(cutoff, low);
                for (int j = 0; j < 3; j++)
                {
                    var v = Vertices[Indices[3 * face + j]];
                    if (v.y <= cutoff) continue;
                    if (firstFace.TryGetValue(v, out int neighbor)) parent[Root(face)] = Root(neighbor);
                    else firstFace.Add(v, face);
                }
            }

            var bounds = new Bounds[count];
            var samples = new int[count];
            foreach (var pair in firstFace)
            {
                int root = Root(pair.Value);
                if (samples[root]++ == 0) bounds[root] = new Bounds(pair.Key, Vector3.zero);
                else bounds[root].Encapsulate(pair.Key);
            }
            for (int i = 0; i < count; i++)
                if (parent[i] >= 0)
                {
                    int root = Root(i);
                    baseY[root] = Mathf.Min(baseY[root], baseY[i]);
                }
            var result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (parent[i] < 0) continue;
                int root = Root(i);
                var size = bounds[root].size;
                // Accept a coherent patch about a hand-width across, or a narrow upright
                // structure such as a chair leg with at least two distinct raised returns.
                result[i] = samples[root] >= 2 && (Mathf.Max(size.x, size.z) >= .08f ||
                    Mathf.Min(headHigh, bounds[root].max.y) - baseY[root] >= .15f);
            }
            lock (raisedLock)
                (raisedCutoff, raisedHeadHigh, raisedFaces) = (cutoff, headHigh, result);
            return result;

            int Root(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
        }

        // Compare distinct physical locations so overlapping triangles or a densely
        // reconstructed chair cannot set the floor level for the whole room.
        internal const float HeightSearchRange = .15f;
        internal RoomFloor AlignFloor(RoomFloor floor, float trackingHeight)
        {
            FloorGeometry.Bounds(floor.Boundary, out var min, out var max);
            float step = Mathf.Max(.20f, Mathf.Max(max.x - min.x, max.y - min.y) / 96);
            int width = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / step));
            int depth = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / step));
            var heights = new float[width * depth];
            System.Array.Fill(heights, float.NegativeInfinity);
            float label = floor.LabelHeight;
            for (int i = 0; i < Indices.Length; i += 3)
            {
                var a = Vertices[Indices[i]];
                var b = Vertices[Indices[i + 1]];
                var c = Vertices[Indices[i + 2]];
                if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > label + HeightSearchRange ||
                    Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < label - HeightSearchRange) continue;
                var ab = b - a;
                var ac = c - a;
                var normal = Vector3.Cross(ab, ac);
                if (Mathf.Abs(normal.y) < 1e-8f || normal.y * normal.y < .96f * normal.sqrMagnitude) continue;
                int x0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - min.x) / step - .5f));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - min.x) / step - .5f));
                int z0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - min.y) / step - .5f));
                int z1 = Mathf.Min(depth - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - min.y) / step - .5f));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = min.x + (x + .5f) * step - a.x;
                        float dz = min.y + (z + .5f) * step - a.z;
                        float u = (dz * ac.x - dx * ac.z) / normal.y;
                        float v = (dx * ab.z - dz * ab.x) / normal.y;
                        if (u < -1e-5f || v < -1e-5f || u + v > 1.00001f) continue;
                        float height = a.y + u * ab.y + v * ac.y;
                        int index = z * width + x;
                        heights[index] = Mathf.Max(heights[index], height);
                    }
            }

            const int rangeCm = 15;
            var bins = new int[2 * rangeCm + 1];
            var sums = new float[bins.Length];
            int observed = 0, openLocations = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                float offset = heights[i] - label;
                var point = min + new Vector2(i % width + .5f, i / width + .5f) * step;
                if (!FloorGeometry.Contains(floor.Boundary, point) || InsideObstacle(point)) continue;
                openLocations++;
                if (Mathf.Abs(offset) > HeightSearchRange) continue;
                int bin = rangeCm + Mathf.RoundToInt(offset * 100);
                bins[bin]++;
                sums[bin] += offset;
                observed++;
            }

            bool InsideObstacle(Vector2 point)
            {
                foreach (var p in floor.Obstacles)
                    if (p.Length >= 3 && FloorGeometry.Contains(p, point)) return true;
                return false;
            }
            int Count(int center) => bins[center - 1] + bins[center] + bins[center + 1];
            int best = rangeCm;
            // Only lower the baseline. A raised flat surface can be a real obstacle.
            for (int i = rangeCm - 1; i >= 1; i--)
                if (Count(i) > Count(best)) best = i;
            int votes = Count(best);
            if (votes * step * step < .8f || votes < observed * .35f) return floor;
            float measured = label + (sums[best - 1] + sums[best] + sums[best + 1]) / votes;
            if (measured >= label - .002f) return floor;
            // A lower shelf or a small reconstructed patch cannot set the room height.
            // A consistent level across most of the open room can establish it even
            // when the tracking origin was calibrated at the labelled height.
            bool dominant = votes >= observed * .70f && votes >= openLocations * .50f;
            bool tracked = Mathf.Abs(trackingHeight - measured) <= .035f &&
                Mathf.Abs(trackingHeight - measured) < Mathf.Abs(trackingHeight - label);
            if (!dominant && votes <= Count(rangeCm) * 1.25f) return floor;
            float board = dominant || tracked ? measured : label;
            return new RoomFloor(floor, measured, board);
        }

        internal static bool TryRead(MRUKRoom room, out FloorMeshScan scan)
        {
            scan = null;
            var anchor = room ? room.GlobalMeshAnchor : null;
            if (!anchor || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.GLOBAL_MESH))
                return false;
            var mesh = anchor.GlobalMesh;
            Mesh retry = null;
            try
            {
                // SDK 209 caches an empty Mesh if its native triangle data is late.
                if ((!mesh || mesh.vertexCount < 3) && anchor.HasValidHandle)
                    mesh = retry = anchor.LoadGlobalMeshTriangles();
                if (!mesh || !mesh.isReadable || mesh.vertexCount < 3 || mesh.subMeshCount == 0)
                    return false;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    if (mesh.GetTopology(s) != MeshTopology.Triangles)
                        return false;
                return TryCreate(mesh.vertices, mesh.triangles, anchor.transform.localToWorldMatrix, out scan);
            }
            finally
            {
                if (retry)
                    Object.Destroy(retry);
            }
        }

        internal static bool TryCreate(Vector3[] vertices, int[] indices, Matrix4x4 localToWorld, out FloorMeshScan scan)
        {
            scan = null;
            if (vertices == null || indices == null || vertices.Length < 3 || indices.Length < 3 || indices.Length % 3 != 0)
                return false;
            var world = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = localToWorld.MultiplyPoint3x4(vertices[i]);
                if (!FloorGeometry.Finite(v.x) || !FloorGeometry.Finite(v.y) || !FloorGeometry.Finite(v.z))
                    return false;
                world[i] = v;
            }

            foreach (int i in indices)
                if ((uint)i >= world.Length)
                    return false;
            scan = new FloorMeshScan(world, indices);
            return true;
        }
    }
}
