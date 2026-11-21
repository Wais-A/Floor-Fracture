#ifndef FLOOR_FRACTURE_GLASS_FLOOR_OCCLUSION
#define FLOOR_FRACTURE_GLASS_FLOOR_OCCLUSION

#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

half GlassFloorVisibility(float3 world, float floorY)
{
    half visibility = 1;
#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
    float height = world.y - floorY;
    if (abs(height) > .20)
    {
        visibility = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, .02);
    }
    else
    {
        // Depth outside the native capture would otherwise repeat its last row or column.
        float4 capture = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(world, 1));
        float extent = max(abs(capture.x), abs(capture.y));
        if (extent < capture.w)
        {
            // Express 6 cm above the mapped floor as a fraction of the original view ray.
            float eyeToGlassY = max(.20, GetCameraPositionWS().y - world.y);
            float bias = (.06 - height) / eyeToGlassY;
            #if defined(SOFT_OCCLUSION)
                // Meta's soft comparison spans a 4% range before reaching full coverage.
                bias = (bias + .04) / 1.04;
            #endif
            bias = clamp(bias, .02, .30);
            visibility = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, bias);
            visibility = lerp(visibility, 1, smoothstep(.97, 1, extent / capture.w));
        }
    }
#endif
    return visibility;
}

#endif
