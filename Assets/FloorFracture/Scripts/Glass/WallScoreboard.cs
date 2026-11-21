using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using Labels = Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels;

namespace FloorFracture
{
    // A fixture in room space. Its pose is chosen when the board is confirmed.
    internal sealed class WallScoreboard : MonoBehaviour
    {
        private const float Width = 1.16f, Height = .56f, Depth = .055f;
        private const float MountMargin = .035f;
        // A square at this radius contains the cabinet and base under any yaw.
        private const float StandRadius = .62f;
        private const Labels Wall = Labels.WALL_FACE | Labels.INNER_WALL_FACE;
        private const Labels Opening = Labels.DOOR_FRAME | Labels.WINDOW_FRAME | Labels.WALL_ART;
        private Transform cabinet;
        private Mesh housing;
        private Material caseMaterial, fontMaterial;
        private MeshRenderer letters;
        internal TextMesh Display { get; private set; }

        private readonly struct MountPoint
        {
            internal readonly Pose Pose;
            internal readonly MRUKAnchor Wall;
            internal readonly float Score;
            internal MountPoint(Pose pose, MRUKAnchor wall, float score)
            { Pose = pose; Wall = wall; Score = score; }
        }

        internal void Mount(MRUKRoom room, BoardPlacement fit, Transform head, Func<Vector3, float, bool> clearStand)
        {
            if (cabinet || !room || !head || !fit.Layout.IsPlayable) return;
            Vector3 eye = head.position;
            var playCenter = CentralPane(fit, new Vector2(eye.x, eye.z));
            Vector3 boardEye = new Vector3(playCenter.x, Mathf.Clamp(eye.y, fit.FloorHeight + 1.0f, fit.FloorHeight + 1.85f), playCenter.y);
            Vector3 gaze = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            gaze = gaze.sqrMagnitude > .01f ? gaze.normalized : fit.Pose.rotation * Vector3.forward;

            var choices = new List<MountPoint>();
            foreach (var wall in room.WallAnchors)
            {
                if (!wall || !wall.HasAnyLabel(Wall) || wall.HasAnyLabel(Labels.INVISIBLE_WALL_FACE | Opening) ||
                    !wall.PlaneRect.HasValue || wall.PlaneBoundary2D == null || wall.PlaneBoundary2D.Count < 3 ||
                    Mathf.Abs(wall.transform.forward.y) > .1f || Mathf.Abs(wall.transform.up.y) < .98f) continue;
                Rect rect = wall.PlaneRect.Value;
                float left = rect.xMin + Width * .5f + MountMargin, right = rect.xMax - Width * .5f - MountMargin;
                if (right < left) continue;
                int steps = Mathf.Max(1, Mathf.CeilToInt((right - left) / .24f));
                float localEye = wall.transform.InverseTransformPoint(boardEye).y;
                Quaternion rotation = Quaternion.LookRotation(-wall.transform.forward, Vector3.up);
                for (int i = 0; i <= steps; i++)
                for (int row = -1; row <= 1; row++)
                {
                    var onWall = wall.transform.TransformPoint(Mathf.Lerp(left, right, (float)i / steps), localEye + row * .20f, 0);
                    var pose = new Pose(onWall + wall.transform.forward * .009f, rotation);
                    AddCandidate(wall, pose);
                }
            }

            var anchor = room.GlobalMeshAnchor;
            var mesh = anchor ? anchor.GlobalMesh : null;
            var scan = mesh && mesh.isReadable && mesh.vertexCount >= 3 ? new ScanGeometry(mesh, anchor.transform) : null;
            if (TryMount()) return;
            if (clearStand == null) return;
            choices.Clear();
            var visited = new HashSet<Vector2>();
            foreach (var edge in fit.Layout.Perimeter(fit.Pitch))
            {
                var tangent = (edge.b - edge.a).normalized;
                var outward = new Vector2(tangent.y, -tangent.x);
                int steps = Mathf.Max(2, Mathf.CeilToInt(fit.Pitch / .45f));
                for (int i = 0; i <= steps; i++)
                for (int lane = 0; lane < 2; lane++)
                {
                    var local = Vector2.Lerp(edge.a, edge.b, (float)i / steps) + outward * (StandRadius + .12f + lane * .35f);
                    if (!visited.Add(local) || OverlapsPane(fit, local)) continue;
                    var point = fit.ToWorldXZ(local.x, local.y);
                    var center = new Vector3(point.x, boardEye.y, point.y);
                    AddCandidate(null, new Pose(center, Quaternion.LookRotation(center - boardEye, Vector3.up)));
                }
            }
            TryMount();

            void AddCandidate(MRUKAnchor wall, Pose pose)
            {
                var toSign = Vector3.ProjectOnPlane(pose.position - eye, Vector3.up);
                float distance = toSign.magnitude;
                if (distance < .65f) return;
                float ahead = Vector3.Dot(gaze, toSign / distance);
                var intoWall = pose.rotation * Vector3.forward;
                float facing = Vector3.Dot(intoWall, (pose.position - boardEye).normalized);
                float eyeFacing = Vector3.Dot(intoWall, toSign / distance);
                if (facing < .28f || eyeFacing < .28f) return;
                float score = ahead * 3 + facing + eyeFacing * .55f - distance * .12f - Mathf.Abs(pose.position.y - boardEye.y);
                choices.Add(new MountPoint(pose, wall, score));
            }

            bool TryMount()
            {
                choices.Sort((a, b) => b.Score.CompareTo(a.Score));
                foreach (var candidate in choices)
                {
                    var pose = candidate.Pose;
                    if (candidate.Wall)
                    {
                        if (!FitsWall(candidate.Wall, pose)) continue;
                    }
                    else if (!clearStand(new Vector3(pose.position.x, fit.FloorHeight, pose.position.z), StandRadius)) continue;
                    if (!ClearCabinet(room, candidate)) continue;
                    if (!Visible(room, scan, pose, eye) ||
                        (eye - boardEye).sqrMagnitude > .0001f && !Visible(room, scan, pose, boardEye)) continue;
                    transform.SetPositionAndRotation(pose.position, pose.rotation);
                    BuildCabinet(candidate.Wall ? 0 : pose.position.y - fit.FloorHeight);
                    return true;
                }
                return false;
            }
        }

        private static Vector2 CentralPane(BoardPlacement fit, Vector2 wearer)
        {
            // The bounding centre may be occupied by furniture in an L-shaped board.
            Vector2 best = default;
            long bestGridDistance = long.MaxValue;
            float bestWearerDistance = float.PositiveInfinity;
            for (int i = 0; i < fit.Layout.CellCount; i++)
            {
                if (!fit.Layout.Contains(i)) continue;
                long x = 2L * (i % fit.Columns) + 1 - fit.Columns;
                long z = 2L * (i / fit.Columns) + 1 - fit.Rows;
                long distance = x * x + z * z;
                if (distance > bestGridDistance) continue;
                var point = fit.ToWorldXZ(x * (.5f * fit.Pitch), z * (.5f * fit.Pitch));
                float wearerDistance = (point - wearer).sqrMagnitude;
                if (distance == bestGridDistance && wearerDistance >= bestWearerDistance) continue;
                best = point;
                bestGridDistance = distance;
                bestWearerDistance = wearerDistance;
            }
            return best;
        }

        private static bool OverlapsPane(BoardPlacement fit, Vector2 center)
        {
            // The circle encloses the full cabinet at every yaw. Only tiles inside its
            // local bounding box can touch it, even on very large or rotated boards.
            int xMin = Mathf.Max(0, Mathf.FloorToInt((center.x - StandRadius) / fit.Pitch + fit.Columns * .5f));
            int xMax = Mathf.Min(fit.Columns - 1, Mathf.FloorToInt((center.x + StandRadius) / fit.Pitch + fit.Columns * .5f));
            int zMin = Mathf.Max(0, Mathf.FloorToInt((center.y - StandRadius) / fit.Pitch + fit.Rows * .5f));
            int zMax = Mathf.Min(fit.Rows - 1, Mathf.FloorToInt((center.y + StandRadius) / fit.Pitch + fit.Rows * .5f));
            for (int z = zMin; z <= zMax; z++)
            for (int x = xMin; x <= xMax; x++)
            {
                if (!fit.Layout.HasPane(x, z)) continue;
                var corner = fit.Layout.Corner(x, z, fit.Pitch);
                var near = new Vector2(Mathf.Clamp(center.x, corner.x, corner.x + fit.Pitch),
                    Mathf.Clamp(center.y, corner.y, corner.y + fit.Pitch));
                if ((near - center).sqrMagnitude <= StandRadius * StandRadius) return true;
            }
            return false;
        }

        private static bool FitsWall(MRUKAnchor wall, Pose pose)
        {
            // A simple polygon contains the full rectangle if it contains its centre
            // and none of its edges cross the rectangle. This catches inward notches.
            if (!wall.IsPositionInBoundary(wall.transform.InverseTransformPoint(pose.position))) return false;
            var bounds = new Bounds(Vector3.zero, new Vector3(Width + 2 * MountMargin - .001f,
                Height + 2 * MountMargin - .001f, .2f));
            var toPanel = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse * wall.transform.localToWorldMatrix;
            var polygon = wall.PlaneBoundary2D;
            var a = toPanel.MultiplyPoint3x4(polygon[polygon.Count - 1]);
            foreach (var vertex in polygon)
            {
                var b = toPanel.MultiplyPoint3x4(vertex);
                if (bounds.Contains(a) || bounds.IntersectRay(new Ray(a, b - a), out float d) && d <= (b - a).magnitude) return false;
                a = b;
            }
            return true;
        }

        private static bool ClearCabinet(MRUKRoom room, MountPoint candidate)
        {
            var pose = candidate.Pose;
            var toPanel = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one).inverse;
            var reserved = new Bounds(new Vector3(0, 0, -.04f), new Vector3(Width + .06f, Height + .06f, .24f));
            foreach (var anchor in room.Anchors)
            {
                if (!anchor || anchor == candidate.Wall || anchor.HasAnyLabel(Labels.GLOBAL_MESH | Labels.FLOOR | Labels.CEILING)) continue;
                var local = toPanel * anchor.transform.localToWorldMatrix;
                if (anchor.VolumeBounds is Bounds volume && reserved.Intersects(InPanelSpace(volume, local))) return false;
                if (anchor.PlaneRect is Rect rect)
                {
                    var plane = new Bounds(rect.center, new Vector3(rect.width, rect.height, .01f));
                    if (reserved.Intersects(InPanelSpace(plane, local))) return false;
                }
            }
            return true;
        }

        private static Bounds InPanelSpace(Bounds box, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(box.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(matrix.MultiplyPoint3x4(box.center + Vector3.Scale(box.extents, new Vector3(x, y, z))));
            return result;
        }

        private static bool Visible(MRUKRoom room, ScanGeometry scan, Pose pose, Vector3 eye)
        {
            var front = pose.position - pose.rotation * Vector3.forward * Depth;
            // Check the middle and all four reading corners, from both the head and board.
            for (int point = 0; point < 5; point++)
            {
                float x = point == 0 ? 0 : (point % 2 == 0 ? 1 : -1) * Width * .48f;
                float y = point == 0 ? 0 : (point > 2 ? 1 : -1) * Height * .48f;
                var delta = front + pose.rotation * new Vector3(x, y, 0) - eye;
                var ray = new Ray(eye, delta);
                float distance = delta.magnitude;
                if (room.Raycast(ray, distance - .02f, out _)) return false;
            }
            return scan == null || !scan.BlocksView(eye, pose);
        }

        // Clip saved triangles against the viewing pyramid enclosing the entire cabinet.
        // Open, back-facing and very thin surfaces all obstruct a view. No mesh cooking or
        // per-frame physics queries are needed. Spatially adjacent index batches reject cheaply.
        private sealed class ScanGeometry
        {
            private const int Batch = 32;
            private readonly Vector3[] vertices;
            private readonly int[] indices;
            private readonly Bounds[] batches;
            private readonly Plane[] planes = new Plane[5];
            private readonly Vector3[] corners = new Vector3[4], clipped = new Vector3[10], scratch = new Vector3[10];

            internal ScanGeometry(Mesh mesh, Transform anchor)
            {
                vertices = mesh.vertices;
                var toWorld = anchor.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
                indices = mesh.triangles;
                batches = new Bounds[(indices.Length / 3 + Batch - 1) / Batch];
                for (int batch = 0; batch < batches.Length; batch++)
                {
                    int first = batch * Batch * 3, end = Mathf.Min(first + Batch * 3, indices.Length);
                    var bounds = new Bounds(vertices[indices[first]], Vector3.zero);
                    for (int i = first + 1; i < end; i++) bounds.Encapsulate(vertices[indices[i]]);
                    batches[batch] = bounds;
                }
            }

            internal bool BlocksView(Vector3 eye, Pose pose)
            {
                var forward = pose.rotation * Vector3.forward;
                // The back of the test volume is 4 cm ahead of the mapped wall so
                // reconstruction noise on the mounting surface doesn't hide a valid sign.
                var face = pose.position - forward * .035f;
                var right = pose.rotation * Vector3.right * (Width * .5f + .02f);
                var up = pose.rotation * Vector3.up * (Height * .5f + .02f);
                corners[0] = face - right - up;
                corners[1] = face + right - up;
                corners[2] = face + right + up;
                corners[3] = face - right + up;
                var volume = new Bounds(eye, Vector3.zero);
                for (int i = 0; i < 4; i++)
                {
                    volume.Encapsulate(corners[i]);
                    var side = new Plane(eye, corners[i], corners[(i + 1) % 4]);
                    planes[i] = side.GetDistanceToPoint(face) >= 0 ? side : side.flipped;
                }
                planes[4] = new Plane(-forward, face);
                for (int batch = 0; batch < batches.Length; batch++)
                {
                    if (!batches[batch].Intersects(volume)) continue;
                    int first = batch * Batch * 3, end = Mathf.Min(first + Batch * 3, indices.Length);
                    for (int i = first; i < end; i += 3)
                    {
                        var a = vertices[indices[i]];
                        var b = vertices[indices[i + 1]];
                        var c = vertices[indices[i + 2]];
                        var bounds = new Bounds(a, Vector3.zero);
                        bounds.Encapsulate(b); bounds.Encapsulate(c);
                        if (bounds.Intersects(volume) && IntersectsPlanes(a, b, c)) return true;
                    }
                }
                return false;
            }

            private bool IntersectsPlanes(Vector3 a, Vector3 b, Vector3 c)
            {
                var input = clipped;
                var output = scratch;
                input[0] = a; input[1] = b; input[2] = c;
                int count = 3;
                foreach (var plane in planes)
                {
                    var previous = input[count - 1];
                    float from = plane.GetDistanceToPoint(previous);
                    int remaining = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var next = input[i];
                        float to = plane.GetDistanceToPoint(next);
                        if (from < 0 && to > 0 || from > 0 && to < 0)
                            output[remaining++] = previous + (next - previous) * (from / (from - to));
                        if (to >= 0) output[remaining++] = next;
                        previous = next; from = to;
                    }
                    if (remaining == 0) return false;
                    (input, output) = (output, input);
                    count = remaining;
                }
                return true;
            }
        }

        private void BuildCabinet(float standHeight)
        {
            cabinet = new GameObject("Scoreboard cabinet").transform;
            cabinet.SetParent(transform, false);
            var mesh = new GlassGeometry.Builder();
            var edge = new Color(.16f, .20f, .22f);
            var dark = new Color(.048f, .065f, .075f);
            var face = new Color(.009f, .017f, .022f);
            if (standHeight > 0)
            {
                Box(0, -standHeight + .016f, 0, .45f, .032f, .27f, dark, edge);
                Box(0, -standHeight * .5f, 0, .055f, standHeight - .035f, .055f, edge, dark);
            }
            Box(0, 0, -Depth * .5f, Width, Height, Depth, dark, edge);
            Box(0, 0, -Depth, Width - .034f, Height - .034f, .008f, edge, edge);
            Box(0, 0, -Depth - .006f, Width - .060f, Height - .060f, .006f, face, dark);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                Box(x * (Width * .5f - .018f), y * (Height * .5f - .018f), -Depth - .005f,
                    .010f, .010f, .003f, new Color(.36f, .41f, .42f), dark);
            housing = mesh.Create(standHeight > 0 ? "Freestanding scoreboard" : "Wall mounted scoreboard");
            caseMaterial = GlassTile.MakeMaterial("Scoreboard");
            GlassTile.Part(cabinet, "Housing", housing, caseMaterial).sortingOrder = 1;

            var text = new GameObject("Countdown and score");
            text.transform.SetParent(cabinet, false);
            Display = text.AddComponent<TextMesh>();
            Display.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Display.fontSize = 54;
            Display.fontStyle = FontStyle.Bold;
            Display.characterSize = .02f;
            Display.richText = true;
            Display.anchor = TextAnchor.MiddleCenter;
            Display.alignment = TextAlignment.Center;
            Display.color = new Color(1f, .80f, .38f);
            letters = text.GetComponent<MeshRenderer>();
            fontMaterial = new Material(caseMaterial) { name = "Scoreboard lettering", hideFlags = HideFlags.DontSave };
            fontMaterial.SetFloat("_ZWrite", 0);
            RefreshFontAtlas(Display.font);
            Font.textureRebuilt += RefreshFontAtlas;
            letters.sharedMaterial = fontMaterial;
            letters.sortingOrder = 2;
            SetReadout(string.Empty);

            void Box(float x, float y, float z, float w, float h, float depth, Color surface, Color side)
            {
                float l = x - w / 2, r = x + w / 2, b = y - h / 2, t = y + h / 2;
                float f = z - depth / 2, k = z + depth / 2;
                mesh.Quad(new(l,b,k), new(r,b,k), new(r,t,k), new(l,t,k), side, side);
                mesh.Quad(new(l,b,k), new(l,b,f), new(l,t,f), new(l,t,k), side, side);
                mesh.Quad(new(r,b,f), new(r,b,k), new(r,t,k), new(r,t,f), side, side);
                mesh.Quad(new(l,t,f), new(r,t,f), new(r,t,k), new(l,t,k), side, side);
                mesh.Quad(new(l,b,k), new(r,b,k), new(r,b,f), new(l,b,f), side, side);
                mesh.Quad(new(l,b,f), new(r,b,f), new(r,t,f), new(l,t,f), surface, surface);
            }
        }

        internal void SetReadout(string text)
        {
            if (!Display) return;
            bool visible = !string.IsNullOrEmpty(text);
            if (cabinet.gameObject.activeSelf != visible) cabinet.gameObject.SetActive(visible);
            if (Display.text == text) return;
            Display.text = text ?? string.Empty;
            if (!visible) return;
            Bounds glyphs = letters.localBounds;
            if (glyphs.size.x <= .0001f || glyphs.size.y <= .0001f) return;
            float scale = Mathf.Min((Width - .13f) / glyphs.size.x, (Height - .105f) / glyphs.size.y);
            Display.transform.localScale = Vector3.one * scale;
            Display.transform.localPosition = new Vector3(0, 0, -Depth - .013f) - glyphs.center * scale;
        }

        private void RefreshFontAtlas(Font font)
        {
            if (Display && fontMaterial && font == Display.font) fontMaterial.mainTexture = font.material.mainTexture;
        }

        private void OnDestroy()
        {
            Font.textureRebuilt -= RefreshFontAtlas;
            if (housing) Destroy(housing);
            if (caseMaterial) Destroy(caseMaterial);
            if (fontMaterial) Destroy(fontMaterial);
        }
    }
}
