#ifndef FLOOR_FRACTURE_GLASS_FLOOR_OCCLUSION
#define FLOOR_FRACTURE_GLASS_FLOOR_OCCLUSION

#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

half GlassFloorVisibility(float3 world, float floorY)
{
#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
    float height = world.y - floorY;
    if (abs(height) > .20)
        return META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, .02);

    // Depth outside the native capture would otherwise repeat its last row or column.
    float4 capture = mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(world, 1));
    if (capture.w <= 0) return 1;
    float edge = max(abs(capture.x), abs(capture.y)) / capture.w;
    if (edge >= 1) return 1;

    // Express 6 cm above the mapped floor as a fraction of this ray. Passing a bias
    // keeps Meta's sample on its original ray as the display and depth poses diverge.
    float eyeToGlassY = max(.20, GetCameraPositionWS().y - world.y);
    float bias = (.06 - height) / eyeToGlassY;
    #if defined(SOFT_OCCLUSION)
        // Meta's soft comparison spans a 4% range before reaching full coverage.
        bias = (bias + .04) / 1.04;
    #endif
    bias = clamp(bias, .02, .30);
    half visibility = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(world, bias);
    return lerp(visibility, 1, smoothstep(.97, 1, edge));
#else
    return 1;
#endif
}

#endif
