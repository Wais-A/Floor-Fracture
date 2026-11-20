# Quest 3 performance capture

Measure Floor Fracture on the physical Quest 3 with OVR Metrics Tool. The course targets are at least 72 FPS and under 13.9 ms per frame. A development build also writes a local CSV with the scene, actual board size, gameplay events, viewing angle, and available Unity rendering counters. Record the APK filename, Git revision or APK SHA-256, and both CSV filenames with the [playtest record](<./Quest playtest.md>).

## Record a session

Two CSVs from Wais's first physical Quest 3 trial establish the original 3 × 4 baseline. With Vulkan at 72 Hz, the second session averaged 71.98 FPS during countdowns and 71.07 FPS during falls. The slowest fall frame took 113.52 ms. The device supplied no GPU timing samples for that run. The adaptive board needs its own gameplay and OVR Metrics captures. [Measured frame counts and timings](<./Verification/First Quest run metrics.json>)

1. Install the identified development APK and walk through the scene once to warm it up. In OVR Metrics Tool, turn on **Record all captured metrics to csv files** and choose **Basic**. Basic includes average FPS, stale frame count, CPU/GPU utilization and app GPU time. Keep the display refresh rate and capture settings consistent across comparisons. [Meta recording instructions](https://developers.meta.com/vr/documentation/unity/ts-ovrmetricstool/) · [Meta profiling guidance](https://developers.meta.com/vr/documentation/unity/ts-ovr-best-practices/)
2. Launch Floor Fracture and record the fitted rows, columns and centimetres per pane shown on Welcome. Run the scenarios below. Leave a few seconds in the menu between scenarios to make the boundaries visible in both recordings. The development menu can reproduce seed 379, level 1 or level 3, and can select a particular ending.
3. Quit from the results panel to finish the session. In OVR Metrics Tool, open **View Recorded Sessions** and select the Floor Fracture session. Use Meta Quest Developer Hub’s file manager or the read-only ADB pulls below to copy both captures. Turn off CSV recording when the required sessions are finished. [Meta recording instructions](https://developers.meta.com/vr/documentation/unity/ts-ovrmetricstool/)

| Capture | What to do | How to find it in the game CSV |
| --- | --- | --- |
| Full board | Face the first wave on the largest automatically fitted board. The expanded trial supports up to 8×8 at 67 cm, which requires 6.16 × 6.16 m of usable scan including the margin. Record the actual fit for each room and leave the maximum-size test open until observed. | Filter `stage=Countdown`, `columns`, `rows` and, in adaptive builds, `pane_pitch_m`. |
| Largest simultaneous shatter | Watch the breaking panes while standing safely. Keep the wave with the highest actual break count and repeat the same route. | Use the largest `event_count` on a `shatter` event. The following two seconds carry `shatter_count` and `shatter_wave_seed`. |
| Fog | Play a wave with fog crossing the visible board. | Filter `stage=Countdown` and `fog=1`. |
| Sliding glass | Watch or catch the moving pane, then watch it lock. | Filter `stage=Countdown` and `slider=active` or `locked`. |
| Six endings | Play Water, Lava, Crystal, Space, Sand and Snow to results. Use full motion for the first comparison. Repeat the busiest ending with reduced motion. | Filter `stage=Fall` and `ending`. The `reduced_motion` field identifies the selected setting. |

Run the pulls from the directory where you want to keep the evidence. Select the intended device with `adb -s SERIAL` if multiple devices are connected. The package below is the project’s Android identifier.

```sh
adb pull /sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics/ OVR-Metrics/
adb pull /sdcard/Android/data/com.wa003.floorfracture/files/Performance/ Floor-Fracture-Metrics/
```

## Read the measurements

Use OVR’s average frame rate, stale frames and app GPU time for the headset result. Its CSV timestamps are in milliseconds. Convert `app_gpu_time_microseconds` to milliseconds by dividing by 1,000 when the export uses that column. Compare OVR and Unity records from the same launch. The Unity file includes device UTC, Android elapsed time since boot when available, and Unity elapsed time. Match the OVR session and timestamp origin before lining up a shatter or ending. [Meta metric definitions](https://developers.meta.com/vr/documentation/unity/ts-ovrstats/) · [Meta CSV interpretation](https://developers.meta.com/vr/documentation/unity/ts-ovr-best-practices/)

Each Unity `window` row aggregates up to about one second of frame intervals and splits when the stage, board or effect changes. `unity_fps` is frames divided by elapsed time. `frame_ms_max` gives the slowest observed Unity frame interval, and `frames_at_or_above_13_9ms` counts intervals outside the course frame-time target. Combine windows using their frame counts and durations. Exclude menu, pause and setup rows when reporting gameplay.

Unity’s `main_thread_ms` is elapsed main-thread time and can include waiting. `timed_cpu_ms` and `timed_gpu_ms` come from Frame Timing Manager. `xr_app_gpu_ms` comes from the XR display provider. Draw calls and triangles are Unity’s per-frame rendering counters. Each counter has a sample count. A blank metric with zero samples means the device or build supplied no usable reading. Frame timing and renderer counters can arrive after their frame, so check the neighboring window around a short spike. [Unity frame timings](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html) · [Unity rendering counters](https://docs.unity3d.com/6000.0/Documentation/Manual/profiler-counters-reference.html)

Head yaw and pitch are relative to the board axes. Positive pitch looks up. `head_turn_deg_s` is angular travel per tracked second. Use those fields to repeat a comparable viewpoint. Startup, app suspension and resume break frame intervals. Device measurements and tester findings belong in sections 7 and 10 of the course document once a capture or observed playtest exists.

If a scenario misses target, change one measured expense such as shard count or overlapping transparent panes. Repeat the same build configuration, fitted dimensions, wave seed, viewpoint and settings, then identify the one change and the two captures together. A release APK can also be measured with OVR Metrics Tool. The in-app CSV runs only on Android development players.
