using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR;
using static FloorFracture.QuestMetricWindow;

namespace FloorFracture
{
    /// <summary>Writes local profiling evidence on an Android development player.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9000)]
    public sealed class QuestRunMetrics : MonoBehaviour
    {
        const string Header = "kind,event,event_count,device_utc_ms,device_boot_ms,unity_start_s,unity_end_s,package,version,build_guid,unity_version,device,graphics,quality,scene,stage,columns,rows,run_seed,level,wave,wave_seed,mode,fog,slider,shatter_count,shatter_wave_seed,ending,reduced_motion,refresh_hz,frames,unity_fps,frame_ms_mean,frame_ms_max,frames_at_or_above_13_9ms,main_thread_ms,timed_cpu_ms,timed_gpu_ms,xr_app_gpu_ms,draw_calls_mean,draw_calls_max,triangles_mean,triangles_max,head_yaw_deg,head_pitch_deg,head_turn_deg_s,main_samples,timed_cpu_samples,timed_gpu_samples,xr_gpu_samples,draw_samples,triangle_samples,pane_pitch_m";
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>(1);
        readonly FrameTiming[] timing = new FrameTiming[1];
        ProfilerRecorder draws, triangles, mainThread;
        StreamWriter writer;
        GameFlow game;
        XRDisplaySubsystem display;
        QuestMetricWindow window;
        SampleContext context;
        Quaternion previousRotation;
        bool hasContext, hasRotation, hasPose, timingEnabled, paused, unfocused;
        float yaw, pitch, refresh;
        double previousTime, windowStart, nextFlush, nextDisplayCheck, shatterUntil;
        int shatterCount;
        int? shatterWaveSeed;
        ulong lastTimingStamp;
        long utcAnchor, bootAnchor = -1;
        double unityAnchor;
        string package, version, build, unity, device, graphics, scene;

        public string OutputPath { get; private set; }

        void OnEnable()
        {
            if (Application.platform != RuntimePlatform.Android || !Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }
            game = GetComponent<GameFlow>();
            if (!game) { enabled = false; return; }
            package = Application.identifier; version = Application.version; build = Application.buildGUID;
            unity = Application.unityVersion; device = SystemInfo.deviceModel; graphics = SystemInfo.graphicsDeviceType.ToString();
            scene = game.gameObject.scene.path;
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "Performance");
                Directory.CreateDirectory(folder);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
                OutputPath = Path.Combine(folder, stamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
                writer = new StreamWriter(new FileStream(OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false), 8192);
                writer.WriteLine(Header);
                writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                StopWriting(e); return;
            }
            draws = Counter(ProfilerCategory.Render, "Draw Calls Count");
            triangles = Counter(ProfilerCategory.Render, "Triangles Count");
            mainThread = Counter(ProfilerCategory.Internal, "Main Thread");
            timingEnabled = FrameTimingManager.IsFeatureEnabled();
            StartClock();
            Debug.Log("[Floor Fracture] Quest performance CSV " + OutputPath);
        }

        static ProfilerRecorder Counter(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch (ArgumentException) { return default; }
            catch (InvalidOperationException) { return default; }
        }

        void StartClock()
        {
            unityAnchor = Time.realtimeSinceStartupAsDouble;
            utcAnchor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bootAnchor = -1;
            try
            {
                using (var clock = new AndroidJavaClass("android.os.SystemClock"))
                    bootAnchor = clock.CallStatic<long>("elapsedRealtime");
            }
            catch (AndroidJavaException) { /* UTC and app-relative clocks remain available. */ }
            hasContext = hasRotation = hasPose = false;
            lastTimingStamp = 0;
            shatterUntil = 0;
            nextDisplayCheck = unityAnchor; nextFlush = unityAnchor + 4;
        }

        void LateUpdate()
        {
            if (writer == null || paused || unfocused) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextDisplayCheck)
            {
                if (display == null || !display.running)
                {
                    display = null;
                    SubsystemManager.GetSubsystems(displays);
                    foreach (var item in displays) if (item.running) { display = item; break; }
                }
                refresh = 0;
                if (display != null && display.TryGetDisplayRefreshRate(out float hz)) refresh = hz;
                nextDisplayCheck = now + 2;
            }
            SampleContext current = ReadContext(now);
            if (hasContext && now > previousTime)
            {
                double seconds = now - previousTime;
                window.Frame(seconds);
                ReadCounters();
                ReadPose(seconds);
                if (!current.Matches(context) || now - windowStart >= 1)
                    FinishWindow(now);
            }
            else if (!hasContext)
                windowStart = now;
            if (writer == null) return;
            hasContext = true;
            context = current; previousTime = now;
            if (timingEnabled) FrameTimingManager.CaptureFrameTimings();
        }

        void ReadCounters()
        {
            if (draws.Valid && draws.Count > 0) window.Draws.Add(draws.LastValue);
            if (triangles.Valid && triangles.Count > 0) window.Triangles.Add(triangles.LastValue);
            if (mainThread.Valid && mainThread.Count > 0 && mainThread.LastValue > 0)
                window.MainThreadMs.Add(mainThread.LastValue * .000001);
            if (display != null && display.running && display.TryGetAppGPUTimeLastFrame(out float gpu) && gpu > 0)
                window.XrGpuMs.Add(gpu * 1000.0);
            if (!timingEnabled || FrameTimingManager.GetLatestTimings(1, timing) == 0) return;
            FrameTiming frame = timing[0];
            if (frame.frameStartTimestamp != 0 && frame.frameStartTimestamp == lastTimingStamp) return;
            lastTimingStamp = frame.frameStartTimestamp;
            if (frame.cpuFrameTime > 0) window.TimedCpuMs.Add(frame.cpuFrameTime);
            if (frame.gpuFrameTime > 0) window.TimedGpuMs.Add(frame.gpuFrameTime);
        }

        void ReadPose(double seconds)
        {
            // Sample orientation only. The file contains no room mesh or absolute head position.
            var panel = game.Panel;
            if (!panel || !panel.Head || !panel.Tracked) { hasRotation = false; return; }
            Quaternion rotation = panel.Head.rotation;
            if (hasRotation)
            {
                window.TurnDegrees += Quaternion.Angle(previousRotation, rotation);
                window.TrackedSeconds += seconds;
            }
            previousRotation = rotation; hasRotation = true;
            if (!game.BoardView) return;
            Vector3 local = game.BoardView.Board.InverseTransformDirection(rotation * Vector3.forward);
            yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(local.y, -1, 1)) * Mathf.Rad2Deg;
            hasPose = true;
        }

        struct SampleContext
        {
            public GameStage Stage;
            public int Width, Height, Seed, Level, Wave, Shatter, Quality, Slider;
            public float PanePitch;
            public int? WaveSeed, ShatterSeed;
            public WaveMode? Mode;
            public bool Fog, Reduced;
            public string Ending;
            public bool Matches(SampleContext b) => Stage == b.Stage && Width == b.Width && Height == b.Height && PanePitch == b.PanePitch &&
                Seed == b.Seed && Level == b.Level && Wave == b.Wave && WaveSeed == b.WaveSeed && Mode == b.Mode &&
                Fog == b.Fog && Slider == b.Slider && Shatter == b.Shatter && ShatterSeed == b.ShatterSeed &&
                Ending == b.Ending && Reduced == b.Reduced && Quality == b.Quality;
        }

        SampleContext ReadContext(double now)
        {
            var state = game.State;
            var wave = state?.ActiveWave;
            var slider = state?.Slider;
            var value = new SampleContext {
                Stage = game.Stage, Width = state?.Width ?? 0, Height = state?.Height ?? 0,
                PanePitch = state?.Pitch ?? 0f,
                Seed = game.SelectedSeed, Level = game.Level, Wave = game.WaveNumber,
                WaveSeed = wave?.Seed, Mode = wave?.Mode, Fog = wave?.Fog ?? false,
                Slider = slider == null ? 0 : slider.Locked ? 2 : 1,
                Quality = QualitySettings.GetQualityLevel()
            };
            if (game.Stage == GameStage.Fall && game.Ending)
            {
                value.Ending = game.Ending.LastEndingName;
                value.Reduced = hasContext && context.Stage == GameStage.Fall ?
                    context.Reduced : PlayerPrefs.GetInt("ff.reduced", 0) != 0;
            }
            if (now < shatterUntil) { value.Shatter = shatterCount; value.ShatterSeed = shatterWaveSeed; }
            return value;
        }

        /// <summary>A shatter marker starts a two-second tail and preserves the resolving wave seed.</summary>
        public void Mark(string eventName, int count = 0)
        {
            if (writer == null || paused || unfocused || string.IsNullOrEmpty(eventName)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (eventName == "shatter" && count > 0)
            {
                shatterCount = count;
                shatterWaveSeed = game.State?.ActiveWave?.Seed ?? (hasContext ? context.WaveSeed : null);
                shatterUntil = now + 2;
            }
            WriteRow("event", eventName, count, now, now, hasContext ? context : ReadContext(now), default, false);
        }

        static string Value(int? v) => v.HasValue ? Integer(v.Value) : "";
        static string Flag(bool x) => x ? "1" : "0";

        void FinishWindow(double end)
        {
            if (window.FrameMs.Count > 0)
                WriteRow("window", "", 0, windowStart, end, context, window, true);
            window = default; windowStart = end; hasPose = false;
        }

        void WriteRow(string kind, string marker, int count, double start, double end, SampleContext c, QuestMetricWindow w, bool measured)
        {
            if (writer == null) return;
            bool board = c.Width > 0 && c.Height > 0;
            long offsetMs = (long)Math.Round((end-unityAnchor)*1000);
            string row = string.Join(",", new[] {
                kind,Text(marker),kind == "event" ? Integer(count) : "",
                Integer(utcAnchor + offsetMs),bootAnchor < 0 ? "" : Integer(bootAnchor + offsetMs),Number(start),Number(end),
                Text(package),Text(version),Text(build),Text(unity),Text(device),Text(graphics),Integer(c.Quality),Text(scene),Text(c.Stage.ToString()),
                board ? Integer(c.Width) : "",board ? Integer(c.Height) : "",board ? Integer(c.Seed) : "",
                board ? Integer(c.Level) : "",board ? Integer(c.Wave) : "",Value(c.WaveSeed),Text(c.Mode?.ToString()),
                c.Mode.HasValue ? Flag(c.Fog) : "",board ? (c.Slider == 0 ? "none" : c.Slider == 2 ? "locked" : "active") : "",
                c.Shatter > 0 ? Integer(c.Shatter) : "",Value(c.ShatterSeed),Text(c.Ending),c.Stage == GameStage.Fall ? Flag(c.Reduced) : "",
                refresh > 0 ? Number(refresh) : "",measured ? Integer(w.FrameMs.Count) : "",w.Fps,w.FrameMs.Mean,w.FrameMs.Peak,
                measured ? Integer(w.OverBudget) : "",w.MainThreadMs.Mean,w.TimedCpuMs.Mean,w.TimedGpuMs.Mean,w.XrGpuMs.Mean,
                w.Draws.Mean,w.Draws.Peak,w.Triangles.Mean,w.Triangles.Peak,
                measured && hasPose ? Number(yaw) : "",measured && hasPose ? Number(pitch) : "",w.TurnRate,
                measured ? Integer(w.MainThreadMs.Count) : "",measured ? Integer(w.TimedCpuMs.Count) : "",measured ? Integer(w.TimedGpuMs.Count) : "",
                measured ? Integer(w.XrGpuMs.Count) : "",measured ? Integer(w.Draws.Count) : "",measured ? Integer(w.Triangles.Count) : "",
                board ? Number(c.PanePitch) : ""
            });
            try
            {
                writer.WriteLine(row);
                if (end >= nextFlush) { writer.Flush(); nextFlush = end + 4; }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { StopWriting(e); }
        }

        void StopWriting(Exception error)
        {
            Debug.LogWarning("[Floor Fracture] Quest performance capture stopped. " + error.Message);
            CloseWriter();
            enabled = false;
        }

        void CloseWriter()
        {
            var file = writer; writer = null;
            try { file?.Dispose(); }
            catch (IOException) { }
        }

        void Suspend()
        {
            if (writer == null || !hasContext) return;
            FinishWindow(previousTime); hasContext = hasRotation = hasPose = false;
            try { writer?.Flush(); }
            catch (IOException e) { StopWriting(e); }
        }

        void OnApplicationPause(bool pause)
        {
            bool wasSuspended = paused || unfocused; paused = pause;
            UpdateSuspension(wasSuspended);
        }
        void OnApplicationFocus(bool focus)
        {
            bool wasSuspended = paused || unfocused; unfocused = !focus;
            UpdateSuspension(wasSuspended);
        }
        void UpdateSuspension(bool wasSuspended)
        {
            if (writer == null || wasSuspended == (paused || unfocused)) return;
            if (paused || unfocused) Suspend(); else StartClock();
        }
        void OnDisable()
        {
            Suspend(); CloseWriter();
            if (draws.Valid) draws.Dispose();
            if (triangles.Valid) triangles.Dispose();
            if (mainThread.Valid) mainThread.Dispose();
        }
    }
}
