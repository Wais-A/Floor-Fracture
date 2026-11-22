# Quest release warning review

Unity 6000.6.9f1 reported 24 warnings and zero errors in Android build `build_b603034f2cda`. The numbers below follow that report's order. Several entries repeat a compiler diagnostic for a different instruction or graphics API. The Quest release uses Vulkan and the mobile forward renderer.

| Original entries | Build warning | Quest impact and disposition |
| --- | --- | --- |
| 1 | Screen Space Ambient Occlusion can be expensive on Quest. | The PC renderer has this feature. Both quality levels available in the Quest build omit it, so Quest spends no GPU time on this pass. |
| 2 | Thin Link Time Optimization was disabled. | Enabled the optimized Unity engine in the Quest release profile and disabled engine-code stripping as required by Unity. The ARM64 engine in the APK matches Unity's release ThinLTO library byte for byte. |
| 3 | Meta XR requests OpenXR 1.1.54. Unity recommends 1.1.62. | Unity ignores the older patch request. The preceding release logged a successful OpenXR 1.1.62 instance on this Quest with the same SDK versions. |
| 4 | Inference Engine circular padding uses signed remainder on Vulkan. | A performance hint at line 52 of `Pad.shader`. The game has no active inference worker or model. |
| 5–10 | Inference Engine transposed convolution uses signed remainder on Vulkan. | Six operations in `ConvTranspose.shader` at lines 215, 218, 256, 259, 296 and 299. Gameplay never dispatches these kernels. |
| 11–12 | The water shader reports a potentially uninitialized lighting value on Metal and Vulkan. | Both warnings refer to Unity's shared lighting function. See the shader checks below. |
| 13–15 | Inference Engine slice assignment uses signed division. | `SliceSet.shader` line 55 on Vulkan and line 69 on Metal and Vulkan. Gameplay never dispatches this kernel. |
| 16–17 | Meta's environment-depth lit shader reports the same lighting value on Metal and Vulkan. | Both variants compiled. The Quest GPU accepted the Vulkan shader modules. |
| 18–19 | Clustered deferred and stencil deferred lighting each report the same value on Metal. | These are desktop deferred passes. The Quest profile uses Vulkan forward rendering. |
| 20–21 | Unity's standard URP Lit shader reports the same lighting value on Metal and Vulkan. | Both variants compiled. The Quest GPU accepted the Vulkan shader modules. |
| 22–24 | IL2CPP split three large TextMeshPro methods into individual C++ files. | These messages describe native compilation. They cover the text-parser static constructor, world-space text mesh generation and canvas text mesh generation. |

## Release and shader checks

- Build `build_abcba0950e4f` succeeded with zero errors. It reproduced 23 of the original warnings. The 24th message in the new report comes from Android manifest generation. A voice SDK references the microphone and the game explicitly removes the recording permission. The final APK has no `RECORD_AUDIO` permission. The old and new APKs declare the same permissions and device requirements.
- The APK grew from 115,936,809 to 118,385,681 bytes (2.34 MiB). ThinLTO changes the native engine used by every session. A frame-rate improvement during play would require a matched headset performance comparison.
- The shared URP function initializes the reflected light before returning it and explicitly returns zero when a Quest surface faces away from the light. Recompiled both shader stages for all eight reported lighting variants using their recorded pass and keywords. All 16 compiled. All six Vulkan modules passed SPIR-V validation, and the connected Quest 3's Adreno 740 driver accepted each one. Module creation checks driver compatibility. Visible lighting and room occlusion also depend on scene and tracking data.
- Installed the update in place on Quest 3 and verified its on-device SHA-256 against the built APK (`7841a2c60e7d59f060f8d6407d99400edd21b5acf3b65a6d71fe2e3265a62812`).
