# Assets and credits

| Use in Floor Fracture | Creator, source and version | Project location and reuse |
|---|---|---|
| Quest camera rig, controller models, passthrough | Meta XR Core SDK 209.0.0, Unity Package Manager | Scene references the installed package prefabs. Recipients resolve the package using the lockfile. |
| Room floor, scene anchors and example room | Meta MR Utility Kit 209.0.0 | The room provider uses the installed SDK and its Editor fixture. |
| Hole mask | Meta Platforms, Inc. and affiliates, MRUK **Floor Zone**, `Assets/MRUKSamples/FloorZone/materials/Stencil.shader` | `Assets/FloorFracture/Resources/FloorFractureMask.shader` adapts the stencil technique for both eyes in URP. Wais approved reuse on November 19. The sample is licensed under MIT. [License](./Licenses/Meta-MRUK-Floor-Zone.txt) |
| Water surface and splash | Meta Platforms, Inc. and affiliates, same Floor Zone sample. The water material references its Shader Graph and surface texture. The splash prefab references its drop mesh, material and Shader Graph. Water impact uses an included audio clip. | Approved for reuse with MIT attribution. Ending resources live under `Assets/FloorFracture/Resources/Endings/MetaFloorZone/`. [License](./Licenses/Meta-MRUK-Floor-Zone.txt) |
| Lava fire candidate | Unity Technologies, Creative Core VFX course, `VFX/Samples/Fire/Fire_ParticleSystem_Prefab.prefab`. Its particle systems reference course materials and textures. | Wais approved game use where permitted. The local course asset contains no verified grant to redistribute its raw dependencies in the course repository. The game uses original flame geometry, embers and heat audio. |
| Smoky glass, fissures, shafts, shards, fog, UI, remaining endings and generated audio | Original project implementation by Wais Almakaleh with Codex assistance | `Assets/FloorFracture/`. Fissures and shards share a geometry definition. Game code creates original meshes and synthesizes sound where credited sample audio is unavailable. |

[Meta Floor Zone documentation](https://developers.meta.com/vr/documentation/unity/unity-sample-mruk-floor-zone/) · [Meta's source repository and license](https://github.com/oculus-samples/Unity-MRUtilityKitSample)
