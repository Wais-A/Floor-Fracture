# Quest 3 performance capture

Measure Floor Fracture on the physical Quest 3 with OVR Metrics Tool. The course targets are at least 72 FPS and under 13.9 ms per frame. A development build also writes a local CSV with the scene, actual board size, gameplay events, viewing angle, and available Unity rendering counters. Record the APK filename, Git revision or APK SHA-256, and both CSV filenames with the [playtest record](<./Quest playtest.md>).

## November 20 development build

The earlier `54b54ee` player logged 13 room-setup windows using Vulkan and Mobile quality. The headset was asleep during this launch. For the next gameplay measurement, use the largest clear room available and record the fitted board dimensions. The Editor replay also counted live scene objects at Welcome after every one of 96 restarts. Meshes, materials, audio sources, board and chamber counts remained constant. [Earlier Quest startup](<./Verification/Overnight Quest build.json>) · [Editor replay](<./Verification/Overnight replay checks.json>)

## Shared chamber on Quest 3

Wais played nine runs on 5 × 3 panes at 53.9 cm in development build `4be44192652c4aa284ed355d31c245a0`. The headset reported 72 Hz with Vulkan. Across Countdown, BetweenWaves and Fall, Unity recorded 19,399 frames in 269.500 s, or **71.981 FPS**. The mean frame interval was 13.892 ms. **9,653 frames reached or exceeded 13.9 ms.** GPU timing and compositor stale frames remain unmeasured. [Physical session, weighted timings and rendering counters](<./Verification/Shared chamber Quest session.json>)

| Measured scenario | Frames | Seconds | Unity FPS | Longest frame |
| --- | ---: | ---: | ---: | ---: |
| First wave on the fitted board | 3,878 | 53.887 | 71.965 | 36.408 ms |
| Tagged windows after five-pane shatters | 1,876 | 26.075 | 71.946 | 80.621 ms |
| Fog during countdown | 1,360 | 18.903 | 71.946 | 29.100 ms |
| Active slider during countdown | 1,000 | 13.891 | 71.989 | 17.023 ms |
| All nine Full falls | 6,129 | 85.123 | 72.002 | 18.025 ms |

Five panes broke together 13 times. The first such event occurred at 60.170 s. Its Countdown window peaked at 36.408 ms. The next window, 60.194–61.196 s, contained the 80.621 ms frame. The other 12 five-pane tails peaked at 17.773 ms. The tags extend into subsequent countdowns or falls. Fog and the slider shared 394 frames over 5.473 s. These rows overlap. The fitted board had 15 panes, and the capture contains no locked-slider windows. [Event windows and coverage](<./Verification/Shared chamber Quest session.json>)

Water had the longest fall frame at 18.025 ms. Space had the highest fall triangle count at 18,346. The November 20 bench below adds paired OVR and Unity recordings on 64 panes. [Ending measurements](<./Verification/Shared chamber Quest session.json>)

## Earlier builds

Two CSVs from Wais's first physical Quest 3 trial establish the original 3 × 4 baseline. With Vulkan at 72 Hz, the second session averaged 71.98 FPS during countdowns and 71.07 FPS during falls. The slowest fall frame took 113.52 ms. The device supplied no GPU timing samples for that run. [Measured frame counts and timings](<./Verification/First Quest run metrics.json>)

The expanded-room build first recorded 13 startup samples on Quest 3 with Vulkan. The resumed recording contains Welcome, timed waves, loss and Results on 5 × 3 panes at 53.9 cm. The build GUID is `7fdcaf92922947548c00e55b14ae6090`. Pair a subsequent run with OVR Metrics for the performance review. [Expanded-room Quest record](<./Verification/Expanded room Quest build.json>)

## First opening on the largest board in Unity

The first five-pane shatter in the 5 × 3 headset session reached 80.621 ms. A matched Editor experiment used an 8 × 8 board at 60 cm and the same seeded 21-pane break in six fresh processes. Moving construction of the chamber meshes and loading of the Meta water assets to board setup cut median synchronous first-opening work from 19.52 ms to 3.11 ms. Each run still emitted 336 shards. The subsequent Quest benchmark includes the first break on 64 panes and its paired compositor capture. [Matched 64-pane measurements](<./Verification/64 pane first opening.json>)

## 64 panes on physical Quest hardware

The separately installed rendering fixture ran the gameplay scene on Quest 3 with an 8 × 8 board at 60 cm per pane and a scripted step. Seed 4 broke 32 panes in fog, then produced moving glass. Over 740 gameplay frames, Unity averaged 72.04 FPS and recorded a longest frame of 18.82 ms, up to 116 draw calls and 3,982 triangles. Across that app launch, Meta recorded 5.04 ms peak application GPU time and one stale frame. Another 22-pane run reached 65.36 ms. These are measurements of the fixed synthetic board. A comparison in the wearer's scanned room remains open. [Device captures and measurement conditions](<./Verification/64 pane Quest measurements.json>)

On an unattended Quest, Unity can keep updating while the compositor receives no visible eye buffer. For these captures a rendering result requires positive draw calls and triangles in Unity and a nonzero submitted eye buffer in Meta’s recording. [Room and rendering evidence](<./Verification/64 pane Quest measurements.json>)

The repeat 32-pane shatter averaged 72.03 FPS across 774 gameplay frames. Its slowest frame was 21.00 ms. Meta Perfetto recorded 33 Unity player-loop samples around the break. The longest loop was 14.98 ms and the longest script-update marker was 5.79 ms. Neither capture reproduced the 65.36 ms frame from the earlier 22-pane run. Its cause remains open. [Traces, timestamp alignment and fixture inputs](<./Verification/64 pane Quest measurements.json>)

Across six matched Water runs, loading the splash prefab before descent moved the median slowest frame around impact from 15.60 ms to 15.42 ms. The individual measurements overlapped. The game retains the approved Meta splash at contact. An earlier build of the fixture recorded an 84.13 ms Water frame. It has a different build identifier and is excluded from the six-run comparison. [Per-run measurements](<./Verification/64 pane Quest measurements.json>)

| Full-motion landing | Rendered Unity frames measured | Average FPS | Slowest frame | Peak app GPU in this launch |
| --- | ---: | ---: | ---: | ---: |
| Water | 681 | 72.03 | 16.90 ms | 5.99 ms |
| Lava | 681 | 72.02 | 15.87 ms | 4.44 ms |
| Crystal (now Tar Pit) | 681 | 72.02 | 16.58 ms | 4.53 ms |
| Space | 681 | 72.01 | 16.58 ms | 4.44 ms |
| Sand | 681 | 72.03 | 17.61 ms | 4.19 ms |
| Snow | 681 | 72.03 | 16.83 ms | 4.47 ms |

The game logged Results 9.45–9.46 seconds after each Full loss began. Each table row covers a complete fall. Sand was repeated after its first CSV ended during the approach to Results. Water uses the first matched stock run from the splash comparison. GPU peaks come from Meta’s compositor samples across each app launch. [Corresponding files and hashes](<./Verification/64 pane Quest measurements.json>)

On another six launches, each landing completed in Reduced and reached Results in 3.94–3.95 seconds. Each Restart restored level one, zero score and a visible board. A second Full fall was skipped 0.60 seconds after it started. Results appeared at 0.616–0.623 seconds, and the game’s Quit action returned control to Android on all six launches. The off-head tracking guard rejected button presses, so the fixture called the buttons’ game handlers. The raw CSV records the saved motion preference. These tests set motion in memory, so the Reduced frame windows were identified from the game markers and the shorter timeline. [Six renderings, input conditions and Android exits](<./Verification/64 pane Quest measurements.json>)

The two 32-pane captures reached 18.82 and 21.00 ms across the countdown-to-break transition. Their next two seconds peaked at 17.32 and 16.43 ms. The moving-pane countdowns peaked at 16.34 and 16.28 ms. The fixture used scripted movement and muted audio. For the real-room comparison, use normal game volume and step onto a moving pane so the recording includes its catch and lock. [Measured fixture conditions](<./Verification/64 pane Quest measurements.json>)

## Catching a moving pane with audio on

On the physical Quest 3, the game ran the same seeded 64-pane board twice in one process at 75% game volume. The scripted stance caught pane 61 after it had traveled 18–19 cm. The pane stayed locked for 3.17–3.19 seconds and survived the countdown both times. The locked frames averaged 71.97 and 71.99 FPS. Their longest intervals were 15.99 and 15.68 ms. Three glass audio clips advanced during playback. Across both routes the compositor reported stereo submissions on all 29 recorded samples. This was an unattended synthetic room. [Fixture, results and capture hashes](<./Verification/Locked slider on Quest/locked-slider-device-review.json>)

The first 32-pane shatter in that run produced a **96.58 ms** interval beginning about 29 ms after the game recorded the break. The same break on repeat reached **14.73 ms** in the matched window. A separate run confirmed a 75.46 ms first-break interval. Four fresh launches compared the ordinary start with silently priming the existing spatial-audio voices. They measured 73.02 ms (ordinary), 17.18 and 15.52 ms (primed), then 15.39 ms (ordinary). That last control also ran quickly. These captures establish an intermittent first-break hitch. The audio experiment did not isolate its cause. [Matched timing and raw captures](<./Verification/Locked slider on Quest/locked-slider-ab/summary.json>)

### First-use Vulkan pipeline creation

A native Perfetto capture reproduced a 77.69 ms frame at the first 32-pane shatter. Across the opening frames, Unity’s render thread spent 14.48, 20.25 and 59.43 ms creating three Vulkan graphics pipelines, a total of 94.16 ms. The preceding 15 render-graph calls took 2.06–4.73 ms each. The game’s shatter loop took 0.11 ms, and the Unity main thread waited for graphics work. The capture identifies the render-thread calls; it does not label their individual materials. [Trace timing, control frames and device conditions](<./Verification/First shatter native trace.json>)

### Preparing the three graphics pipelines before Welcome

At game revision `1da914c`, the Meta Quest build profile prepares three recorded Unity graphics states at startup. They draw the hole mask, the shaft and falling glass, and the ending chamber. The capture came from the headset's Vulkan stereo rendering with four-sample antialiasing. No audio or visible pregame shatter is needed.

| First 32-pane opening on a fixed 64-pane Quest board | Longest frame near the break | Combined time in the three graphics pipeline creation calls |
| --- | ---: | ---: |
| Build profile with startup preparation off | 78.18 ms | 92.70 ms |
| Startup preparation on, first fresh installation | 19.42 ms | 1.44 ms |
| Startup preparation on, second fresh installation | 15.75 ms | 1.56 ms |

During a separate startup trace, Unity scheduled the warmup on the main thread in 2.55 ms. The three device compilations ran on a worker and finished 1.13 seconds before Welcome.

The three creation calls span opening frames, so their total can exceed one frame. In a separate same-executable comparison, two unwarmed controls reached 70.99 and 74.70 ms. Explicitly warming the captured states brought the corresponding peak to 17.01 ms. The native profile had prepared all three by Welcome. Both profile-on opening peaks remained above the 13.9 ms target. The worn-room test uses the same scan for a fresh run and a repeat. [Fixed-board trials and conditions](<./Verification/First opening warmup on Quest.json>)

A follow-up tested whether reusing the opening mesh buffers would improve the remaining frame time. The 32-pane rebuild took 0.659 ms in the control and 0.236 ms in the variant. Its 135 measured managed allocations fell to zero. The full sampled interval was 15.766 ms in the control and 16.016 ms in the variant. Both unattended runs recorded zero eye-buffer submissions. The installed release keeps the tested mesh implementation. [Mesh results, exact geometry comparison and candidate patch location](<./Verification/Opening mesh allocation comparison.json>)

## Record a session

1. Install the identified development APK. Record from a fresh launch before the first pane opens. In OVR Metrics Tool, turn on **Record all captured metrics to csv files** and choose **Basic**. Basic includes average FPS, stale frame count, CPU/GPU utilization and app GPU time. Keep the display refresh rate and capture settings consistent. Capture the first seeded shatter, then restart the same route in the same running app and capture it again. Label each recording with the seed, fitted board, number of shattered panes and whether the chamber had already appeared. [Meta recording instructions](https://developers.meta.com/vr/documentation/unity/ts-ovrmetricstool/) · [Meta profiling guidance](https://developers.meta.com/vr/documentation/unity/ts-ovr-best-practices/)
2. Launch Floor Fracture and record the fitted rows, columns and centimetres per pane shown on Welcome. Run the scenarios below. Leave a few seconds in the menu between scenarios to make the boundaries visible in both recordings. The development menu can reproduce seed 379, level 1 or level 3, and can select a particular ending.
3. Quit from the results panel to finish the session. In OVR Metrics Tool, open **View Recorded Sessions** and select the Floor Fracture session. Use Meta Quest Developer Hub’s file manager or the read-only ADB pulls below to copy both captures. Turn off CSV recording when the required sessions are finished. [Meta recording instructions](https://developers.meta.com/vr/documentation/unity/ts-ovrmetricstool/)

| Capture | What to do | How to find it in the game CSV |
| --- | --- | --- |
| Full board | Face the first wave on the largest automatically fitted board: choose **Play area → Board size → Entire connected floor**. That mode has no pane limit and uses 40–75 cm panes. For example, a clear 10 × 10 m floor fits 169 panes at 75 cm, spanning 9.75 × 9.75 m. The board can be long, square or shaped around furniture, and its edges can reach the clear scan boundary. Record the actual fitted dimensions. | Filter `stage=Countdown`, `columns`, `rows` and, in adaptive builds, `pane_pitch_m`. |
| Largest simultaneous shatter | Watch the breaking panes while standing safely. Keep the wave with the highest actual break count and repeat the same route. | Use the largest `event_count` on a `shatter` event. The following two seconds carry `shatter_count` and `shatter_wave_seed`. |
| Fog | Play a wave with fog crossing the visible board. | Filter `stage=Countdown` and `fog=1`. |
| Sliding glass | Watch or catch the moving pane, then watch it lock. | Filter `stage=Countdown` and `slider=active` or `locked`. |
| Six endings | Play Water, Lava, Tar Pit, Space, Sand and Snow to results. Use full motion for the first comparison. Repeat the busiest ending with reduced motion. | Filter `stage=Fall` and `ending`. The `reduced_motion` field identifies the selected setting. |

From this Mac the native Meta recorder also accepts Android broadcasts. Read the current setting with `LOG_STATE`, enable capture for the test with `ENABLE_CSV`, and restore its previous value afterward. Both actions target `com.oculus.ovrmonitormetricsservice/.SettingsBroadcastReceiver` with the action prefix `com.oculus.ovrmonitormetricsservice.`. This was verified with the paired Quest captures above. [Meta recording controls](https://developers.meta.com/vr/documentation/native/android/ts-ovrmetricstool/)

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
