using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;

namespace FloorFracture
{
    public sealed partial class RoomBoardPlacement
    {
        // Native depth is view-dependent. Keep the measurement local to the floor in view.
        internal const float DepthSpacing = .075f;

        internal static FloorMeshScan BuildDepthSurface(Vector3?[] first, Vector3?[] second, int width,
            float trackingY, float boardY, out int accepted, out float level, List<Vector2[]> liveObstacles)
        {
            accepted = 0;
            level = trackingY;
            var heights = new List<float>();
            var stable = new bool[first.Length];
            for (int i = 0; i < first.Length; i++)
            {
                if (!(first[i] is Vector3 a) || !(second[i] is Vector3 b)) continue;
                if ((a - b).sqrMagnitude > .045f * .045f || Mathf.Abs(a.y - b.y) > .018f) continue;
                float y = (a.y + b.y) * .5f;
                if (y < trackingY - .065f || y > trackingY + .03f || y < boardY - .075f || y > boardY + .035f) continue;
                stable[i] = true;
                heights.Add(y);
            }
            if (heights.Count < 32) return null;
            heights.Sort();
            level = Mathf.Min(trackingY + .01f, heights[heights.Count / 2]);
            var vertices = new Vector3[first.Length];
            for (int i = 0; i < stable.Length; i++)
            {
                if (!(first[i] is Vector3 a) || !(second[i] is Vector3 b)) continue;
                // A repeated change in elevation vetoes even previously clear saved floor.
                if ((a - b).sqrMagnitude <= .045f * .045f &&
                    (Mathf.Min(a.y, b.y) > Mathf.Max(level, boardY) + FloorSurvey.RaisedHeight || Mathf.Max(a.y, b.y) < Mathf.Min(level, boardY) - FloorSurvey.DropDepth))
                    liveObstacles.Add(new[] { new Vector2(b.x, b.z) });
                if (!stable[i]) continue;
                stable[i] = Mathf.Min(a.y, b.y) >= level - .027f && Mathf.Max(a.y, b.y) <= level + .015f;
                if (!stable[i]) continue;
                vertices[i] = (a + b) * .5f;
                accepted++;
            }
            var triangles = new List<int>();
            for (int i = 0; i + width + 1 < vertices.Length; i++)
            {
                if (i % width == width - 1 || !stable[i] || !stable[i + 1] ||
                    !stable[i + width] || !stable[i + width + 1]) continue;
                int right = i + 1, top = i + width, corner = top + 1;
                // Use measured XZ vertices. A grazing ray which lands far beyond its
                // neighbor must not stretch a triangle over an unobserved patch.
                if (!Adjacent(i, right) || !Adjacent(i, top) || !Adjacent(right, corner) || !Adjacent(top, corner)) continue;
                triangles.Add(i); triangles.Add(right); triangles.Add(corner);
                triangles.Add(i); triangles.Add(corner); triangles.Add(top);
            }
            FloorMeshScan.TryCreate(vertices, triangles.ToArray(), Matrix4x4.identity, out var surface);
            return surface;

            bool Adjacent(int a, int b) => (vertices[a] - vertices[b]).sqrMagnitude <= DepthSpacing * DepthSpacing * 2.25f;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool waitingForDepthView;
        private Vector3 previousDepthDirection, previousDepthPosition;

        private bool MovedToNewFloorView()
        {
            if (!waitingForDepthView || !HasPlacementFocus || !IsRoomLocalized) return false;
            var eye = rig.centerEyeAnchor;
            return eye.forward.y < -.18f && (Vector3.Angle(eye.forward, previousDepthDirection) > 12f ||
                (eye.position - previousDepthPosition).sqrMagnitude > .25f * .25f);
        }

        private async Task<FloorMeshScan> ReadDepthSurface(RoomFloor floor, List<Vector2[]> liveObstacles, CancellationToken cancel)
        {
            if (!liveManager || !EnvironmentRaycastManager.IsSupported) return null;
            SetState(RoomPlacementStatus.Loading, "Look down at the open floor. Measuring the saved patches with live depth…");
            liveManager.gameObject.SetActive(true);
            var eye = rig.centerEyeAnchor.GetComponent<Camera>();
            if (!eye || !liveManager.isActiveAndEnabled) return null;
            double end = Time.realtimeSinceStartupAsDouble + 3;
            while (eye.transform.forward.y > -.22f && Time.realtimeSinceStartupAsDouble < end)
                await Awaitable.NextFrameAsync(cancel);
            var gaze = eye.transform.forward;
            var eyePos = eye.transform.position;
            previousDepthDirection = gaze;
            previousDepthPosition = eyePos;
            float y = TrackingFloorAtEye(eyePos);
            if (!FloorGeometry.Finite(y)) return null;
            var forward = new Vector2(gaze.x, gaze.z);
            if (forward.sqrMagnitude < .01f)
            {
                var r = eye.transform.right;
                forward = new Vector2(-r.z, r.x);
            }
            forward.Normalize();
            var right = new Vector2(forward.y, -forward.x);
            float distance = Mathf.Clamp((eyePos.y - y) * new Vector2(gaze.x, gaze.z).magnitude / Mathf.Max(.1f, -gaze.y), .65f, 1.65f);
            var center = new Vector2(eyePos.x, eyePos.z) + forward * distance;
            const int width = 31;
            var first = new Vector3?[width * width];
            var second = new Vector3?[first.Length];
            int returns = 0;
            await Measure(first);
            double again = Time.realtimeSinceStartupAsDouble + .12;
            while (Time.realtimeSinceStartupAsDouble < again) await Awaitable.NextFrameAsync(cancel);
            await Measure(second);
            var surface = BuildDepthSurface(first, second, width, y, floor.Height, out int accepted, out float level, liveObstacles);
            if (Debug.isDebugBuild)
            {
                string line = $"[Floor Fracture depth] Floor patch: hits={returns}, repeatable floor samples={accepted}, " +
                    $"triangles={surface?.Indices.Length / 3 ?? 0}, exclusion samples={liveObstacles.Count}, " +
                    $"measured minus tracking={(level - y)*100:F1} cm.";
                Debug.Log(line, this);
                lastSurveyReport += line + "\n";
                SaveSurveyReport();
            }
            return surface;

            async Task Measure(Vector3?[] points)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    if (i % 16 == 0) await Awaitable.NextFrameAsync(cancel);
                    var p = center + right * ((i % width - width / 2) * DepthSpacing) +
                        forward * ((i / width - width / 2) * DepthSpacing);
                    var target = new Vector3(p.x, y, p.y);
                    var screen = eye.WorldToViewportPoint(target);
                    if (screen.z <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) continue;
                    var origin = eye.transform.position;
                    var delta = target - origin;
                    if (!liveManager.Raycast(new Ray(origin, delta), out var hit, delta.magnitude + .3f) ||
                        hit.status != EnvironmentRaycastHitStatus.Hit) continue;
                    returns++;
                    var displacement = new Vector2(hit.point.x - p.x, hit.point.z - p.y);
                    if (displacement.sqrMagnitude <= .10f * .10f) points[i] = hit.point;
                }
            }
        }
#endif
    }
}
