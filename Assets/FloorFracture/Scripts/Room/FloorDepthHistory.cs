using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorFracture
{
    // The SDK exposes the current capture and its per-eye world reprojection as shader globals.
    // Retain one preceding soft capture so a fixed floor point can compare the two measurements.
    public sealed class FloorDepthHistory : IDisposable
    {
        private static readonly int Raw = Shader.PropertyToID("_EnvironmentDepthTexture");
        private static readonly int Soft = Shader.PropertyToID("_PreprocessedEnvironmentDepthTexture");
        private static readonly int Reprojection = Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
        private static readonly int ZParams = Shader.PropertyToID("_EnvironmentDepthZBufferParams");
        private static readonly int Previous = Shader.PropertyToID("_FloorPreviousDepth");
        private static readonly int PreviousReprojection = Shader.PropertyToID("_FloorPreviousReprojection");
        private static readonly int PreviousZ = Shader.PropertyToID("_FloorPreviousZParams");
        private static readonly int Weight = Shader.PropertyToID("_FloorPreviousWeight");
        private const double MaxAge = .1;
        private readonly Material floor;
        private readonly RenderTexture[] copies = new RenderTexture[2];
        private readonly List<Matrix4x4> matrices = new List<Matrix4x4>(2);
        private Texture lastFrame;
        private Vector4 lastZ;
        private double lastTime, previousTime;
        private int latest;

        public FloorDepthHistory(Material floor) => this.floor = floor;

        public void Update(double now)
        {
            var raw = Shader.GetGlobalTexture(Raw);
            var soft = Shader.GetGlobalTexture(Soft) as RenderTexture;
            if (!Shader.IsKeywordEnabled("SOFT_OCCLUSION") || !raw || !soft || soft.dimension != TextureDimension.Tex2DArray)
            {
                Clear();
                return;
            }
            // SDK 209's OpenXR provider rotates raw swapchain textures only on a new depth capture.
            if (raw == lastFrame)
            {
                if (now - previousTime > MaxAge) floor.SetFloat(Weight, 0);
                return;
            }
            var z = Shader.GetGlobalVector(ZParams);
            var old = copies[latest];
            if (lastFrame && Matches(old, soft) && now - lastTime <= MaxAge && z == lastZ)
            {
                floor.SetTexture(Previous, old);
                floor.SetVector(PreviousZ, lastZ);
                floor.SetMatrixArray(PreviousReprojection, matrices);
                floor.SetFloat(Weight, .5f);
                previousTime = lastTime;
            }
            else
            {
                floor.SetTexture(Previous, soft);
                floor.SetFloat(Weight, 0);
            }

            Shader.GetGlobalMatrixArray(Reprojection, matrices);
            if (matrices.Count != 2)
            {
                Clear();
                return;
            }
            latest = 1 - latest;
            var destination = copies[latest];
            if (!Matches(destination, soft))
            {
                if (destination) UnityEngine.Object.Destroy(destination);
                destination = new RenderTexture(soft.descriptor)
                {
                    name = "Previous soft floor depth", wrapMode = TextureWrapMode.Clamp, filterMode = soft.filterMode
                };
                copies[latest] = destination;
                if (!destination.Create())
                {
                    Clear();
                    return;
                }
            }
            Graphics.CopyTexture(soft, destination);
            lastFrame = raw;
            lastTime = now;
            lastZ = z;
        }

        private static bool Matches(RenderTexture copy, RenderTexture source) =>
            copy && copy.IsCreated() && copy.width == source.width && copy.height == source.height &&
            copy.volumeDepth == source.volumeDepth && copy.graphicsFormat == source.graphicsFormat;

        public void Clear()
        {
            if (!ReferenceEquals(lastFrame, null)) floor.SetFloat(Weight, 0);
            lastFrame = null;
        }

        public void Dispose()
        {
            Clear();
            floor.SetTexture(Previous, null);
            foreach (var copy in copies)
                if (copy) UnityEngine.Object.Destroy(copy);
        }
    }
}
