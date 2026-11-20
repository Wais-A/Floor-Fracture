using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        [Serializable]
        private sealed class LandingView
        {
            public string ending, eye;
            public bool reduced;
            public float seconds, pitchDegreesDown;
            public int roomPixels, waterPixels, waterLuminanceRange;
        }

        private static float[] LandingViewTimes(string ending, bool reduced)
        {
            if (ending == "Space")
                return reduced ? new[] { .86f, .97f, 1.08f, 1.20f, 1.32f } : new[] { 4.49f, 4.66f, 4.83f, 5.02f, 5.14f };
            return ending == "Water" && !reduced ? new[] { 1.3f, 2.7f, 3.95f, 4.30f } : Array.Empty<float>();
        }

        // Pixel readbacks of the running chamber. Two different clear colors stand in for the
        // camera feed: a pixel that changes with the clear color has exposed the room underlay.
        private static void VerifyLandingViews(Transform chamber, string ending, bool reduced, float age, int sample)
        {
            const int size = 80, layer = 30;
            var renderers = chamber.GetComponentsInChildren<MeshRenderer>();
            var oldLayers = renderers.Select(part => part.gameObject.layer).ToArray();
            var surface = chamber.Find("Hazard surface").GetComponent<MeshRenderer>();
            bool surfaceWasEnabled = surface.enabled;
            bool water = ending == "Water";
            var probe = new GameObject("Ending eye readback");
            var camera = probe.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var image = new Texture2D(size,size,TextureFormat.RGBA32,false);
            var previous = RenderTexture.active;
            try
            {
                foreach (var renderer in renderers) renderer.gameObject.layer = layer;
                var eyePose = game.Rig.centerEyeAnchor;
                camera.CopyFrom(eyePose.GetComponent<Camera>());
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.allowHDR = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = 1 << layer;
                camera.aspect = 1;
                camera.fieldOfView = 76;
                camera.targetTexture = target;
                if (water)
                {
                    var detail = chamber.Find("Floor Zone water ripples");
                    Require(surfaceWasEnabled && (!detail || !detail.GetComponent<MeshRenderer>().enabled),
                        "The approach to Water switched from the rippled surface seen through the original opening.");
                }
                // Two Water losses on Quest included downward gazes near 41 and 21 degrees during the approach.
                float[] pitches = water ? new[] { sample < 2 ? 41f : 21f } : new[] { 0f, 62f, -60f };
                foreach (float pitch in pitches)
                foreach (float offset in new[] { -.032f, .032f })
                {
                    Quaternion rotation = Quaternion.Euler(pitch,chamber.eulerAngles.y + 19,0);
                    probe.transform.SetPositionAndRotation(eyePose.position + rotation * Vector3.right * offset,rotation);
                    var first = Read(Color.magenta);
                    var view = new LandingView { ending=ending, reduced=reduced, seconds=age, pitchDegreesDown=pitch,
                        eye=offset < 0 ? "left" : "right" };
                    if (water)
                    {
                        surface.enabled = false;
                        var withoutWater = Read(Color.magenta);
                        surface.enabled = true;
                        var brightness = new List<int>();
                        for (int i=0;i<first.Length;i++)
                        {
                            var a = first[i]; var b = withoutWater[i];
                            if (Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>20)
                                brightness.Add((21*a.r+72*a.g+7*a.b)/100);
                        }
                        brightness.Sort();
                        view.waterPixels = brightness.Count;
                        Require(brightness.Count > size*size/25, "Water disappeared from the sampled descent angle.");
                        view.waterLuminanceRange = brightness[brightness.Count*9/10]-brightness[brightness.Count/10];
                        Require(view.waterLuminanceRange >= 8, "The descending Water surface lost its visible ripples.");
                    }
                    else
                    {
                        var alternateRoom = Read(Color.green);
                        for (int i=0;i<first.Length;i++)
                            if (Math.Abs(first[i].g-alternateRoom[i].g)+Math.Abs(first[i].r-alternateRoom[i].r)>8)
                                view.roomPixels++;
                        Require(view.roomPixels == 0, $"Space exposed {view.roomPixels} room pixels at {age:F2} s in the {view.eye} eye.");
                    }
                    report.landingViews.Add(view);
                }
                Color32[] Read(Color clear)
                {
                    camera.backgroundColor = clear;
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0,0,size,size),0,0);
                    image.Apply();
                    return image.GetPixels32();
                }
            }
            finally
            {
                surface.enabled = surfaceWasEnabled;
                for (int i=0;i<renderers.Length;i++) renderers[i].gameObject.layer=oldLayers[i];
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }
}
