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

        internal RoomOutlineView(Transform parent, Shader shader, string name = "Placement outlines")
        {
            var holder = new GameObject(name);
            root = holder.transform;
            root.SetParent(parent, false);
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (shader) material = new Material(shader) { name = "Room outlines" };
            renderer.sharedMaterial = material;
            holder.SetActive(false);
        }

        private static Color32 ReasonColor(TileClearance reason) => reason switch
        {
            TileClearance.OutsideFloor => new Color32(96, 168, 255, 225),
            TileClearance.Furniture => new Color32(255, 72, 80, 230),
            TileClearance.MeshObstacle => new Color32(255, 156, 55, 235),
            TileClearance.Unobserved => new Color32(182, 112, 250, 225),
            TileClearance.LiveObstacle => new Color32(255, 231, 54, 235),
            TileClearance.LiveUnknown => new Color32(255, 117, 208, 235),
            TileClearance.Declined => new Color32(170, 144, 125, 215),
            _ => new Color32(156, 170, 170, 155)
        };

        internal void ShowSurvey(List<FloorSurvey> surveys, BoardPlacement? accepted)
        {
            root.gameObject.SetActive(surveys != null);
            if (surveys == null)
                return;
            vertices.Clear();
            colors.Clear();
            indices.Clear();
            foreach (var survey in surveys)
            {
                float y = survey.Floor.Height + .025f;
                Loop(survey.Floor.Boundary, y, .014f, ReasonColor(TileClearance.OutsideFloor));
                for (int z = 0; z < survey.Height; z++)
                    for (int x = 0; x < survey.Width; x++)
                    {
                        var reason = survey.Cells[z * survey.Width + x];
                        if (reason == TileClearance.Clear || reason == TileClearance.OutsideFloor) continue;
                        int i = z * survey.Width + x;
                        bool bottom = z == 0 || survey.Cells[i - survey.Width] != reason;
                        bool right = x + 1 == survey.Width || survey.Cells[i + 1] != reason;
                        bool top = z + 1 == survey.Height || survey.Cells[i + survey.Width] != reason;
                        bool left = x == 0 || survey.Cells[i - 1] != reason;
                        if (!(bottom || right || top || left)) continue;
                        var cell = survey.Rect(x, z, 1, 1);
                        var color = ReasonColor(reason);
                        if (bottom) Line(cell[0], cell[1], y, .008f, color);
                        if (right) Line(cell[1], cell[2], y, .008f, color);
                        if (top) Line(cell[2], cell[3], y, .008f, color);
                        if (left) Line(cell[3], cell[0], y, .008f, color);
                    }
            }
            if (accepted is BoardPlacement fit)
                for (int z = 0; z < fit.Rows; z++)
                    for (int x = 0; x < fit.Columns; x++)
                        if (fit.Layout.HasPane(x, z))
                            Loop(fit.TileOutline(x, z, -.016f), fit.FloorHeight + .037f, .014f, new Color32(64, 255, 143, 255));

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
        }

        internal void Show(bool visible, BoardPlacement fit)
        {
            root.gameObject.SetActive(visible);
            if (!visible) return;
            vertices.Clear();
            colors.Clear();
            indices.Clear();
            float y = fit.FloorHeight + 0.02f;
            foreach (var edge in fit.Layout.Perimeter(fit.Pitch))
                Line(fit.ToWorldXZ(edge.a.x, edge.a.y), fit.ToWorldXZ(edge.b.x, edge.b.y), y, .012f, new Color32(255, 213, 123, 219));
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
