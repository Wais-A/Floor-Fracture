using UnityEngine;

/// <summary>Menu cues, pooled positional glass sounds, and the loss soundscape.</summary>
[DisallowMultipleComponent]
public sealed class GameSound : MonoBehaviour
{
    [SerializeField, Range(0, 1)] float volume = .75f;
    AudioSource ui, countdown, falling, ambience, accent, tarBed;
    AudioLowPassFilter tarFilter;
    readonly AudioSource[,] glass = new AudioSource[3, 3];
    readonly float[,] glassGains = new float[3, 3];
    int nextGlass, pendingCount;
    Vector3 weightedPosition;
    AudioClip click, heartbeat, urgentHeartbeat, impact, fragments, echo, clear, wind, submerged, heat, tarBubbles, tarImpact, tarPull, sand, snow, splash;
    float fallGain, ambienceGain, accentGain;
    public const string SpatialVoiceResource = "GlassSpatialVoice";
    GameObject spatialVoicePrefab;
#if UNITY_EDITOR
    public event System.Action<float> EditorMenuClick;
#endif

    void Awake()
    {
        spatialVoicePrefab = Resources.Load<GameObject>(SpatialVoiceResource);
        if (!spatialVoicePrefab)
            throw new System.InvalidOperationException("Prepare the game scene to create the Meta Spatial Audio prefab.");
        click = Clip("Click"); heartbeat = Clip("Heartbeat"); urgentHeartbeat = Clip("HeartbeatUrgent");
        impact = Clip("Impact"); fragments = Clip("Fragments"); echo = Clip("Echo"); clear = Clip("Clear");
        wind = Clip("Air"); submerged = Clip("Submerged"); heat = Clip("Heat");
        tarBubbles = Clip("TarBubbles"); tarImpact = Clip("TarImpact"); tarPull = Clip("TarPull");
        sand = Clip("Sand"); snow = Clip("Snow");
        splash = Resources.Load<AudioClip>("Endings/MetaFloorZone/Audio/WaterPerturbed2");
        if (splash != null && splash.loadState == AudioDataLoadState.Unloaded) splash.LoadAudioData();
        ui = Voice("Menu and countdown", false);
        countdown = Voice("Countdown heartbeat", false);
        falling = Voice("Shaft air", false);
        ambience = Voice("Ending ambience", false);
        accent = Voice("Ending impact", true);
        tarBed = Voice("Tar bubbling",true);
        tarBed.loop = true;
        tarFilter = tarBed.gameObject.AddComponent<AudioLowPassFilter>();
        tarFilter.lowpassResonanceQ = 1f;
        falling.loop = ambience.loop = true;
        for (int i = 0; i < glass.GetLength(0); i++)
        {
            glass[i, 0] = Voice("Pane impact " + i, true);
            glass[i, 1] = Voice("Glass fragments " + i, true);
            glass[i, 2] = Voice("Shaft echo " + i, true);
        }
        SetVolume(volume);
    }

    static AudioClip Clip(string name) => Resources.Load<AudioClip>("Endings/Audio/" + name);

    AudioSource Voice(string name, bool isSpatial)
    {
        if (isSpatial)
        {
            var emitter = Instantiate(spatialVoicePrefab, transform);
            emitter.name = name;
            return emitter.GetComponent<AudioSource>();
        }
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        return source;
    }

    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (!ui) return;
        ui.volume = volume;
        countdown.volume = .43f * volume;
        tarBed.volume = ambienceGain * volume;
        falling.volume = fallGain * volume;
        ambience.volume = ambienceGain * volume;
        accent.volume = accentGain * volume;
        for (int i = 0; i < glass.GetLength(0); i++)
            for (int j = 0; j < glass.GetLength(1); j++)
                glass[i, j].volume = glassGains[i, j] * volume;
    }

    public void Click() => Cue(click, .38f);
    public void Tick(int secondsRemaining)
    {
        if (!isActiveAndEnabled || secondsRemaining < 1) return;
        countdown.clip = secondsRemaining <= 3 ? urgentHeartbeat : heartbeat;
        countdown.Play();
    }

    public void StopCountdown() { if (countdown) countdown.Stop(); }
    public void Completion() => Cue(clear, .54f);

    void Cue(AudioClip clip, float gain)
    {
        if (!isActiveAndEnabled || !clip) return;
        ui.PlayOneShot(clip, gain);
#if UNITY_EDITOR
        if (clip == click) EditorMenuClick?.Invoke(ui.volume);
#endif
    }

    // Individual tile callers and a caller already supplying a wave centroid share one mix per frame.
    public void Break(Vector3 worldPoint, int count)
    {
        if (!isActiveAndEnabled || count <= 0) return;
        weightedPosition += worldPoint * count;
        pendingCount += count;
    }

    void LateUpdate()
    {
        if (pendingCount == 0) return;
        Vector3 position = weightedPosition / pendingCount;
        float gain = .55f + .13f * Mathf.Clamp01((pendingCount - 1) / 5f);
        double now = AudioSettings.dspTime + .02;
        PlayGlass(0, impact, position, now, gain);
        PlayGlass(1, fragments, position + Vector3.down * .22f, now + .055, gain * .41f);
        PlayGlass(2, echo, position + Vector3.down * 1.65f, now + .27, gain * .29f);
        nextGlass = (nextGlass + 1) % glass.GetLength(0);
        pendingCount = 0;
        weightedPosition = Vector3.zero;
    }

    void PlayGlass(int layer, AudioClip clip, Vector3 position, double when, float gain)
    {
        if (!clip) return;
        var voice = glass[nextGlass, layer];
        voice.Stop();
        voice.transform.position = position;
        voice.clip = clip;
        glassGains[nextGlass, layer] = gain;
        voice.volume = gain * volume;
        voice.PlayScheduled(when);
    }

    internal void BeginFall(FloorEnding ending)
    {
        StopFall();
        StopCountdown();
        falling.clip = wind;
        switch (ending)
        {
            case FloorEnding.Water: ambience.clip = submerged; break;
            case FloorEnding.Lava: ambience.clip = heat; break;
            case FloorEnding.Sand: ambience.clip = sand; break;
            case FloorEnding.Snow: ambience.clip = snow; break;
        }
        if (falling.clip) falling.Play();
        if (ambience.clip) ambience.Play();
    }

    internal void MixFall(float air, float environment)
    {
        fallGain = Mathf.Clamp01(air) * .48f;
        ambienceGain = Mathf.Clamp01(environment) * .55f;
        falling.volume = fallGain * volume;
        ambience.volume = ambienceGain * volume;
        tarBed.volume = ambienceGain * volume;
    }

    internal void WaterImpact(Vector3 position) => Accent(splash, position, .84f);
    internal void LavaImpact(Vector3 position) => Accent(echo, position, .92f, .60f);
    internal void TarImpact(Vector3 position)
    {
        tarBed.transform.position = position;
        tarFilter.cutoffFrequency = 3200;
        tarBed.clip = tarBubbles;
        tarBed.Play();
        Accent(tarImpact,position,.9f);
    }

    internal void TarSuction(Vector3 position, int pull) => Accent(tarPull,position,.76f,pull == 1 ? .94f : .78f);
    internal void MuffleTar(float immersion) => tarFilter.cutoffFrequency = Mathf.Lerp(3200,480,immersion);

    void Accent(AudioClip clip, Vector3 position, float gain, float pitch = 1f)
    {
        if (!clip) return;
        accent.transform.position = position;
        accent.clip = clip;
        accent.pitch = pitch;
        accentGain = gain;
        accent.volume = gain * volume;
        accent.Play();
    }

    internal void SetFallPaused(bool paused)
    {
        if (paused) { falling.Pause(); ambience.Pause(); accent.Pause(); tarBed.Pause(); }
        else { falling.UnPause(); ambience.UnPause(); accent.UnPause(); tarBed.UnPause(); }
    }

    internal void StopFall()
    {
        if (!falling) return;
        falling.Stop(); ambience.Stop(); accent.Stop(); tarBed.Stop();
        falling.clip = ambience.clip = accent.clip = tarBed.clip = null;
        fallGain = ambienceGain = accentGain = 0f;
        falling.volume = ambience.volume = accent.volume = tarBed.volume = 0f;
    }

    public void ResetSounds()
    {
        pendingCount = 0;
        weightedPosition = Vector3.zero;
        if (!ui) return;
        StopFall();
        ui.Stop(); StopCountdown();
        foreach (var voice in glass) if (voice) voice.Stop();
    }

    void OnDisable() => ResetSounds();
}
