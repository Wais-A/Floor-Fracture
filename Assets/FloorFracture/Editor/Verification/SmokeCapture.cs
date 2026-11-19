using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    internal static class SmokeCapture
    {
        public static string Write(string directory, string name, GameFlow game, bool showBoard)
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
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = 66f;
                camera.aspect = (float)width / height;
                camera.allowHDR = false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                if (showBoard)
                {
                    var pose = game.Room.Placement.Pose;
                    var offset = name == "room-preview" ? new Vector3(-0.5f, 2.7f, -2.8f) : new Vector3(-0.35f,1.7f,-1.75f);
                    var position = pose.position + pose.rotation * offset;
                    camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(pose.position - position, Vector3.up));
                }
                else
                {
                    var eye = game.Panel.Head;
                    camera.transform.SetPositionAndRotation(eye.position, eye.rotation * Quaternion.Euler(13f,0f,0f));
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
    }
}
