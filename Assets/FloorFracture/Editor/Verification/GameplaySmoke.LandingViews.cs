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

        // Sample the real stereo enclosure at a tilted pair of eyes. A magenta
        // clear makes both incomplete coverage and a reversed waterline measurable.
        private static void VerifyImmersionPixels(MeshRenderer interior, Transform chamber, string ending, bool reduced, bool line)
        {
            const int size = 72, layer = 30;
            var particles = chamber.Find("Particles").GetComponent<MeshRenderer>();
            var fade = interior.transform.parent.GetComponent<MeshRenderer>();
            var parts = line ? new[] { interior } : new[] { interior,particles,fade };
            var oldLayers = parts.Select(part => part.gameObject.layer).ToArray();
            bool showParticles = particles.enabled;
            var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(size,size,TextureFormat.RGB24,false);
            var probe = new GameObject("Immersion eye readback");
            var camera = probe.AddComponent<Camera>();
            try
            {
                foreach (var part in parts) part.gameObject.layer=layer;
                camera.enabled=false;
                camera.stereoTargetEye=StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.magenta;
                camera.cullingMask=line ? 1<<layer : game.Rig.centerEyeAnchor.GetComponent<Camera>().cullingMask | (1<<layer);
                camera.targetTexture=target;
                camera.nearClipPlane=.03f; camera.fieldOfView=80; camera.aspect=1; camera.allowHDR=false;
                Vector3 center=game.Rig.centerEyeAnchor.position;
                Quaternion roll=Quaternion.Euler(0,0,30);
                if (line)
                {
                    var block = new MaterialPropertyBlock(); interior.GetPropertyBlock(block);
                    float level=block.GetVector("_InteriorPlane").x;
                    foreach (float height in new[] { .24f,.07f,-.035f })
                    {
                        int leftCovered=0;
                        foreach (float offset in new[] { -.032f,.032f })
                        {
                            center.y=level+height;
                            probe.transform.SetPositionAndRotation(center+roll*Vector3.right*offset,roll);
                            int covered=Read().Count(pixel => !IsClear(pixel));
                            if (height>.2f) Require(covered==0,ending+" covered the approach too early.");
                            else if (height<0) Require(covered==size*size,ending+" exposed scenery just below the line.");
                            else
                            {
                                Require(covered>size && covered<size*(size-1),ending+" failed to split the view at the surface.");
                                if (offset<0) leftCovered=covered;
                                else Require(leftCovered-covered>size,ending+" did not account for the tilted eyes at the surface.");
                            }
                        }
                    }
                    return;
                }
                foreach (string name in new[] {"Hazard surface","Shaft walls","Shaft landmarks","Chamber darkness","Lava flames","Floor Zone water ripples"})
                {
                    var part=chamber.Find(name);
                    Require(!part || !part.GetComponent<MeshRenderer>().enabled,ending+" retained "+name+" after burial.");
                }
                Require(showParticles==(ending=="Water"),ending+" left the wrong particles visible underwater.");
                int bubblePixels=0;
                float upLight=0,downLight=0;
                foreach (Vector3 direction in new[] { Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back })
                foreach (float offset in new[] { -.032f,.032f })
                {
                    var rotation=Quaternion.FromToRotation(Vector3.forward,direction)*roll;
                    probe.transform.SetPositionAndRotation(center+rotation*Vector3.right*offset,rotation);
                    var view=Read();
                    Require(!view.Any(IsClear),ending+" leaked the room below the surface.");
                    if (ending=="Water")
                    {
                        particles.enabled=false; var water=Read(); particles.enabled=true;
                        for (int n=0;n<view.Length;n++)
                            if (Math.Abs(view[n].r-water[n].r)+Math.Abs(view[n].g-water[n].g)>10) bubblePixels++;
                        var middle=water[water.Length/2+size/2];
                        if (direction==Vector3.up) upLight=middle.g;
                        if (direction==Vector3.down) downLight=middle.g;
                        Require(middle.b>middle.r && middle.g>middle.r,"The underwater gradient lost its blue-green color.");
                    }
                    else
                    {
                        var color=view[0];
                        Require(view.All(pixel => pixel.Equals(color)),ending+" showed shapes through the opaque material.");
                        bool correct=ending=="Tar Pit" ? Math.Max(color.r,Math.Max(color.g,color.b))<=2 :
                            ending=="Lava" ? color.r>color.g*2 && color.r>24 : color.r>color.g && color.g>color.b && color.b>5;
                        Require(correct,$"{ending} had interior color {color}.");
                    }
                }
                if (ending=="Water") Require(upLight-downLight>12 && bubblePixels>4,
                    $"Open water overhead/below measured {upLight}/{downLight} with {bubblePixels} visible bubble pixels.");
                if (!reduced)
                {
                    var name="submerged-"+ending.ToLowerInvariant();
                    if (ending=="Water") Capture(name,SmokeView.CenterEye);
                    else
                    {
                        // The correct frame here is deliberately a single color.
                        var file=name+".png";
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Folder,file),pixels.EncodeToPNG());
                        report.images.Add(file);
                    }
                }
                string mode=reduced ? "Reduced" : "Full";
                string detail=ending=="Water" ? $" Overhead and downward green levels were {upLight} and {downLight}, with {bubblePixels} bubble pixels." : "";
                report.checks.Add($"{ending} ({mode}) covered both tilted eyes in all six directions.{detail}");
            }
            finally
            {
                for (int n=0;n<parts.Length;n++) parts[n].gameObject.layer=oldLayers[n];
                particles.enabled=showParticles;
                camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(pixels);
            }
            bool IsClear(Color32 pixel) => pixel.r>245 && pixel.b>245 && pixel.g<9;
            Color32[] Read()
            {
                camera.Render(); RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,size,size),0,0); pixels.Apply();
                return pixels.GetPixels32();
            }
        }

        private static float[] LandingViewTimes(string ending, bool reduced)
        {
            if (ending == "Space")
                return reduced ? new[] { .86f, .97f, 1.08f, 1.20f, 1.32f, 3.69f } : new[] { 4.49f, 4.66f, 4.83f, 5.02f, 5.14f, 11.73f };
            if (ending == "Snow") return new[] { reduced ? 2.61f : 7.43f };
            return ending == "Water" && !reduced ? new[] { 1.3f, 2.7f, 3.95f, 4.30f } : Array.Empty<float>();
        }

        // Pixel readbacks of the running chamber. Two different clear colors stand in for the
        // camera feed: a pixel that changes with the clear color has exposed the room underlay.
        private static void VerifyLandingViews(Transform chamber, string ending, bool reduced, float age, int sample)
        {
            const int size = 80, layer = 30;
            var renderers = chamber.GetComponentsInChildren<MeshRenderer>();
            bool snow = ending == "Snow";
            bool covered = snow || (ending == "Space" && sample == 5);
            if (covered)
                renderers = renderers.Append(game.Rig.centerEyeAnchor.Find("View fade").GetComponent<MeshRenderer>()).ToArray();
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
                if (covered) pitches = new[] { 0f, 90f, -90f };
                foreach (float pitch in pitches)
                foreach (float bearing in covered && pitch == 0 ? new[] { 0f,90f,180f,270f } : new[] { 0f })
                foreach (float offset in new[] { -.032f, .032f })
                {
                    var towardPool = Vector3.ProjectOnPlane(surface.transform.position - eyePose.position, Vector3.up);
                    float yaw = water && towardPool.sqrMagnitude > .01f
                        ? Quaternion.LookRotation(towardPool).eulerAngles.y : chamber.eulerAngles.y + 19;
                    Quaternion rotation = Quaternion.Euler(pitch,yaw+bearing,0);
                    probe.transform.SetPositionAndRotation(eyePose.position + rotation * Vector3.right * offset,rotation);
                    var first = Read(Color.magenta);
                    var view = new LandingView { ending=ending, reduced=reduced, seconds=age, pitchDegreesDown=pitch,
                        eye=offset < 0 ? "left" : "right" };
                    if (covered)
                    {
                        Require(first.All(pixel => snow ? Mathf.Min(pixel.r,Mathf.Min(pixel.g,pixel.b))>=248 :
                            Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b))<=3),
                            $"{ending} leaked scenery during its hold at pitch {pitch} and bearing {bearing} in the {view.eye} eye.");
                    }
                    else if (water)
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
                        Require(view.waterLuminanceRange >= 8, $"Water ripple contrast was {view.waterLuminanceRange} across {view.waterPixels} visible pixels at {age:F2} s, pitch {pitch:F0}.");
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
