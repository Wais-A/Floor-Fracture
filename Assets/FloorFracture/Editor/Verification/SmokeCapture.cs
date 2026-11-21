using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    internal enum SmokeView { BoardObserver, RoomOverview, FloorEdge, CenterEye, Ending, FallRim, FallDown, FallForward }

    internal static class SmokeCapture
    {
        public static string Write(string directory, string name, GameFlow game, SmokeView view, Bounds? floorBounds)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "The Editor process has no graphics device.";
            if (!game.Rig || !game.Rig.centerEyeAnchor) return "The scene camera was unavailable.";
            const int width = 960, height = 720;
            var holder = new GameObject("Editor verification camera") { hideFlags = HideFlags.HideAndDontSave };
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D frame = null;
            var previous = RenderTexture.active;
            try
            {
                var camera = holder.AddComponent<Camera>();
                camera.CopyFrom(game.Rig.centerEyeAnchor.GetComponent<Camera>());
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = view == SmokeView.CenterEye ? 78f : 66f;
                camera.aspect = (float)width / height;
                camera.allowHDR = false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                if (view == SmokeView.BoardObserver)
                {
                    var fit = game.Room.Placement;
                    var pose = fit.Pose;
                    bool welcome = name == "room-preview";
                    Vector3 direction = pose.rotation * new Vector3(-0.16f, 1f, -1.05f).normalized;
                    var rotation = Quaternion.LookRotation(-direction, Vector3.up);
                    float tanVertical = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    float distance = 0;
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = pose.rotation * new Vector3(x * fit.Columns * fit.Pitch * 0.5f,
                                0f, z * fit.Rows * fit.Pitch * 0.5f);
                            Vector3 projected = Quaternion.Inverse(rotation) * corner;
                            float edge = Mathf.Max(Mathf.Abs(projected.y) / tanVertical, Mathf.Abs(projected.x) / (tanVertical * camera.aspect));
                            distance = Mathf.Max(distance, edge - projected.z);
                        }
                    camera.transform.SetPositionAndRotation(pose.position + direction * distance * (welcome ? 1.3f : 1.10f), rotation);
                }
                else if (view == SmokeView.RoomOverview && floorBounds.HasValue)
                {
                    var bounds = floorBounds.Value;
                    camera.orthographic = true;
                    camera.orthographicSize = Mathf.Max(bounds.extents.z, bounds.extents.x / camera.aspect) * 1.08f;
                    camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * (bounds.extents.y + 5f),
                        Quaternion.LookRotation(Vector3.down, Vector3.forward));
                }
                else if (view == SmokeView.FloorEdge)
                {
                    var fit = game.Room.Placement;
                    var pose = fit.Pose;
                    float halfWidth = fit.Columns * fit.Pitch * .5f;
                    float halfDepth = fit.Rows * fit.Pitch * .5f;
                    var eye = pose.position + pose.rotation * new Vector3(-halfWidth * .5f, 1.5f, halfDepth - .6f);
                    var floorEdge = pose.position + pose.rotation * new Vector3(0f, 0f, halfDepth + .3f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(floorEdge - eye, Vector3.up));
                }
                else
                {
                    var eye = game.Rig.centerEyeAnchor;
                    var rotation = view == SmokeView.Ending ? eye.rotation * Quaternion.Euler(13f,0f,0f) : eye.rotation;
                    if (view == SmokeView.FallRim || view == SmokeView.FallDown || view == SmokeView.FallForward)
                    {
                        Vector3 heading = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
                        if (heading.sqrMagnitude < 0.1f) heading = Vector3.forward;
                        float pitch = view == SmokeView.FallRim ? 74f : view == SmokeView.FallDown ? 36f : 0f;
                        rotation = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
                    }
                    camera.transform.SetPositionAndRotation(eye.position, rotation);
                }

                if (name == "classic-player-view")
                {
                    CheckFraming(camera, game.BoardView.Board.Find("Occupied square").GetComponent<MeshRenderer>());
                }

                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) return "The current render pipeline does not support capture.";
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                frame = new Texture2D(width, height, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0,0,width,height),0,0);
                frame.Apply(false);
                var pixels = frame.GetPixels32();
                int low = 765, high = 0, visible = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    int sum = pixel.r + pixel.g + pixel.b;
                    low = Math.Min(low, sum); high = Math.Max(high, sum);
                    if (sum > 24) visible++;
                }
                if (high-low < 12 || visible < 24) return "The graphics readback contained no distinguishable scene geometry.";
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), frame.EncodeToPNG());
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (frame) Object.DestroyImmediate(frame);
                Object.DestroyImmediate(holder);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static void CheckFraming(Camera camera, MeshRenderer renderer)
        {
            if (!renderer.enabled) throw new InvalidOperationException("Missing player-view subject: " + renderer.name);
            Bounds bounds = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                var point = camera.WorldToViewportPoint(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z));
                if (point.z <= 0f || point.x < 0.01f || point.x > 0.99f || point.y < 0.01f || point.y > 0.99f)
                    throw new InvalidOperationException($"The player-view photo clipped {renderer.name} at viewport {point:F3}.");
            }
        }
    }
}
