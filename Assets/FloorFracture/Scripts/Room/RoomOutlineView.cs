using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorFracture
{
    internal sealed class RoomOutlineView
    {
        private readonly Transform root;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> indices = new List<int>();

        internal RoomOutlineView(Transform parent, Shader shader)
        {
            var holder = new GameObject("Placement outlines");
            root = holder.transform;
            root.SetParent(parent, false);
            mesh = new Mesh { name = "Playable glass perimeter" };
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (shader) material = new Material(shader) { name = "Room outlines" };
            renderer.sharedMaterial = material;
            holder.SetActive(false);
        }

        internal void Show(bool visible, BoardPlacement fit)
        {
            root.gameObject.SetActive(visible);
            if (!visible) return;
            vertices.Clear();
            colors.Clear();
            indices.Clear();
            float y = fit.FloorHeight + 0.02f;
            Loop(fit.FootprintOutline(), y, 0.012f, new Color32(255, 213, 123, 219));
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
        }

        private void Loop(Vector2[] points, float y, float width, Color32 color)
        {
            if (points.Length == 1)
            {
                Line(points[0] - Vector2.right * width, points[0] + Vector2.right * width, y, width, color);
                return;
            }
            int count = points.Length == 2 ? 1 : points.Length;
            for (int i = 0; i < count; i++) Line(points[i], points[(i + 1) % points.Length], y, width, color);
        }

        private void Line(Vector2 a, Vector2 b, float y, float width, Color32 color)
        {
            var delta = b - a;
            if (delta.sqrMagnitude < 0.00000001f) return;
            var perpendicular = new Vector2(-delta.y, delta.x).normalized * (width / 2f);
            int first = vertices.Count;
            Add(a + perpendicular);
            Add(b + perpendicular);
            Add(b - perpendicular);
            Add(a - perpendicular);
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first);
            indices.Add(first + 2);
            indices.Add(first + 3);

            void Add(Vector2 p)
            {
                vertices.Add(root.InverseTransformPoint(new Vector3(p.x, y, p.y)));
                colors.Add(color);
            }
        }

        internal void Dispose()
        {
            Object.Destroy(mesh);
            if (material) Object.Destroy(material);
            if (root) Object.Destroy(root.gameObject);
        }
    }
}
