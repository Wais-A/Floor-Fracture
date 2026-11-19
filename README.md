# Floor Fracture

A solo mixed-reality survival game for Meta Quest 3. Step off cracking glass before the countdown reaches zero. The real room stays visible around the board, and neighboring panes open into shafts below the floor.

[Main project document](./FLOOR_FRACTURE.md) · [Implementation checklist](<./Floor Fracture — Master Project Checklist.md>) · [Course design PDF](<./Docs/Floor Fracture Design Document.pdf>)

## Open the project

Install Unity `6000.6.9f1` with Android Build Support, the Android SDK and NDK, and OpenJDK. Clone with Git LFS enabled, run `git lfs pull`, and open this directory in Unity Hub. Unity resolves Meta XR `209.0.0` and Unity OpenXR `1.19.0` from the package lockfile.

The approved Android baseline at `3265e4b` builds the template scene as a signed ARM64 APK, package `com.wa003.floorfracture`. The implementation checklist records separate Editor, Android-build and physical-headset checks as the gameplay scene is integrated.

## Credits and AI usage

Wais Almakaleh is the solo student developer. Unity provides the engine and URP. Meta provides the Quest runtime, Meta XR SDK and MRUK. Codex assisted with SDK comparison, troubleshooting, design documentation, implementation and automated verification. The course design includes the original project pitch and a written first-minute walkthrough.
