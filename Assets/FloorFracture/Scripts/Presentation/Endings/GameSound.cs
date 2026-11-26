using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Centered start cues and shaft audio, with positional glass and landing cues.</summary>
[DisallowMultipleComponent]
public sealed class GameSound : MonoBehaviour
{
    [SerializeField, Range(0, 1)] float volume = .75f;
    AudioSource ui, falling, roar, ambience, accent, tarBed;
    AudioSource music;
    float musicFade = 1f, musicTarget = 1f;
    const float MusicGain = .12f;
    const float MusicFadeSeconds = .30f;
    AudioSource holeDraft;
    const float DraftGain = .40f;
    float draftFade;
    const float LampGain = .18f;
    readonly List<AudioSource> lampVoices = new();
    readonly List<float> lampLevels = new();
    // Two strikes per wave at most, each 7 s of thunder.
    readonly AudioSource[] thunder = new AudioSource[2];
    int nextThunder;
    AudioLowPassFilter tarFilter;
    readonly AudioSource[,] glass = new AudioSource[3, 3];
    readonly float[,] glassGains = new float[3, 3];
    int nextGlass, pendingCount;
    Vector3 weightedPosition;
    AudioClip paneStress, click, startKnock, startGo, impact, fragments, echo, clear, wind, lowWind;
    AudioClip beamHum, submerged, heat, tarBubbles, tarImpact, tarPull, sand, snow, splash, lavaImpact, sandImpact, snowImpact;
    float fallGain, roarGain, ambienceGain, accentGain;
    double spaceExitDsp = double.PositiveInfinity;
    bool inSpace, spaceSilent;
    public const string SpatialVoiceResource = "GlassSpatialVoice";
    GameObject spatialVoicePrefab;
#if UNITY_EDITOR
    public event Action<float> EditorMenuClick;
    public int EditorStartKnocks { get; private set; }
    public int EditorGoCues { get; private set; }
    public int EditorLandingCueCount { get; private set; }
    public int EditorThunderCues { get; private set; }
    public AudioSource EditorLastThunder { get; private set; }
#endif

    void Awake()
    {
        spatialVoicePrefab = Resources.Load<GameObject>(SpatialVoiceResource);
        if (!spatialVoicePrefab)
            throw new InvalidOperationException("Prepare the game scene to create the Meta Spatial Audio prefab.");
        click = Clip("Click"); startKnock = Clip("ClockTick"); startGo = Clip("StartGo");
        paneStress = Clip("PaneStress"); beamHum = Clip("BeamHum");
        impact = Clip("Impact"); fragments = Clip("Fragments"); echo = Clip("Echo"); clear = Clip("Clear");
        wind = Clip("Air"); lowWind = Clip("ShaftRoar"); submerged = Clip("Submerged"); heat = Clip("Heat");
        tarBubbles = Clip("TarBubbles"); tarImpact = Clip("TarImpact"); tarPull = Clip("TarPull");
        sand = Clip("Sand"); snow = Clip("Snow");
        lavaImpact = Clip("LavaImpact"); sandImpact = Clip("SandImpact"); snowImpact = Clip("SnowImpact");
        splash = Clip("WaterImpact");
        ui = Voice("Menu and countdown", false);
        music = Voice("Background music", false);
        music.clip = Clip("BackgroundMusic");
        music.loop = true;
        holeDraft = Voice("Opening draft", true);
        holeDraft.clip = Clip("HoleDraft");
        holeDraft.loop = true;
        holeDraft.volume = 0f;
        holeDraft.minDistance = 1.6f;
        falling = Voice("Shaft air", false);
        roar = Voice("Shaft roar", false);
        ambience = Voice("Ending ambience", false);
        accent = Voice("Ending impact", true);
        tarBed = Voice("Tar bubbling", true);
        // Contact is at the feet, even when the player is standing at full height.
        accent.minDistance = tarBed.minDistance = 2.6f;
        // The positional impact needs to cut through the head-relative rush at contact.
        accent.GetComponent<MetaXRAudioSource>().GainBoostDb = 7f;
        accent.priority = 32;
        falling.priority = roar.priority = 64;
        tarFilter = tarBed.gameObject.AddComponent<AudioLowPassFilter>();
        tarFilter.lowpassResonanceQ = 1f;
        for (int i = 0; i < glass.GetLength(0); i++)
        {
            glass[i, 0] = Voice("Pane impact " + i, true);
            glass[i, 1] = Voice("Glass fragments " + i, true);
            glass[i, 2] = Voice("Shaft echo " + i, true);
        }
        var thunderClip = Clip("Thunder");
        for (int i = 0; i < thunder.Length; i++)
        {
            thunder[i] = Voice("Thunder " + i, true);
            thunder[i].clip = thunderClip;
            thunder[i].minDistance = 6f;
            thunder[i].maxDistance = 40f;
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

    public float MasterVolume => volume;

    internal AudioSource CreateCrackVoice(Transform pane, int cell)
    {
        var emitter = Instantiate(spatialVoicePrefab, pane, false);
        emitter.name = "Glass strain";
        var voice = emitter.GetComponent<AudioSource>();
        voice.clip = paneStress;
        voice.pitch = .94f + .025f * (cell % 5);
        voice.priority = 160;
        return voice;
    }

    // Parented to the lamp, so the hum comes from its direction. Silent until SetLampLevel.
    public AudioSource CreateLampVoice(Transform lamp)
    {
        for (int i = lampVoices.Count - 1; i >= 0; --i)
            if (!lampVoices[i]) { lampVoices.RemoveAt(i); lampLevels.RemoveAt(i); }
        var emitter = Instantiate(spatialVoicePrefab, lamp, false);
        emitter.name = "Lamp hum";
        var voice = emitter.GetComponent<AudioSource>();
        voice.clip = beamHum;
        voice.loop = true;
        voice.volume = 0f;
        lampVoices.Add(voice);
        lampLevels.Add(0f);
        return voice;
    }

    public void SetLampLevel(AudioSource voice, float level)
    {
        int index = lampVoices.IndexOf(voice);
        if (index < 0) return;
        lampLevels[index] = level;
        ApplyLamp(index);
    }

    void ApplyLamp(int index)
    {
        var voice = lampVoices[index];
        if (!voice) return;
        voice.volume = LampGain * lampLevels[index] * volume;
        if (voice.volume <= 0f) { if (voice.isPlaying) voice.Stop(); }
        else if (!voice.isPlaying && voice.isActiveAndEnabled && voice.clip)
        {
            // Lamps start at different points in the loop so two hums never sound in unison.
            voice.time = index * 6.7f % voice.clip.length;
            voice.Play();
        }
    }

    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (!ui) return;
        if (volume == 0f)
        {
            StopGlass();
            ui.Stop();
            accent.Stop();
            ClearHoleDraft();
            StopThunder();
        }
        ui.volume = volume;
        ApplyMusicVolume();
        tarBed.volume = ambienceGain * volume;
        falling.volume = fallGain * volume;
        roar.volume = roarGain * volume;
        ambience.volume = ambienceGain * volume;
        accent.volume = accentGain * volume;
        for (int i = 0; i < glass.GetLength(0); i++)
            for (int j = 0; j < glass.GetLength(1); j++)
                glass[i, j].volume = glassGains[i, j] * volume;
        for (int i = 0; i < lampVoices.Count; i++) ApplyLamp(i);
        foreach (var voice in thunder) voice.volume = volume; // thunder plays at gain 1
    }

    // A pooled spatial one-shot from the strike's direction, `delaySeconds` after the flash.
    public void PlayThunder(Vector3 worldPosition, double delaySeconds, float pitch)
    {
        if (!isActiveAndEnabled || volume == 0f) return;
        var voice = thunder[nextThunder];
        nextThunder = (nextThunder + 1) % thunder.Length;
        if (!voice.clip) return;
        voice.Stop();
        voice.transform.position = worldPosition;
        voice.pitch = pitch;
        voice.volume = volume;
        voice.PlayScheduled(AudioSettings.dspTime + delaySeconds);
#if UNITY_EDITOR
        ++EditorThunderCues;
        EditorLastThunder = voice;
#endif
    }

    // Stopping also cancels thunder that is scheduled but not yet audible.
    void StopThunder()
    {
        foreach (var voice in thunder) if (voice) voice.Stop();
    }

    public void Click() => Cue(click, .38f);
    public void Completion() => Cue(clear, .54f);
    // Let the fall effects take over without seeking or restarting the score.
    internal void SetMusicAudible(bool audible)
    {
        musicTarget = audible ? 1f : 0f;
        if (!audible && spaceSilent)
        {
            musicFade = 0f;
            ApplyMusicVolume();
        }
    }

    void ApplyMusicVolume() => music.volume = MusicGain * Mathf.SmoothStep(0f, 1f, musicFade) * volume;

    internal void UpdateHoleDraft(Vector3? opening)
    {
        if (volume == 0f) return;
        float dt = Time.unscaledDeltaTime;
        if (opening.HasValue)
        {
            if (!holeDraft.isPlaying)
            {
                holeDraft.transform.position = opening.Value;
                holeDraft.Play();
            }
            else
                holeDraft.transform.position = Vector3.Lerp(holeDraft.transform.position, opening.Value,
                    1f - Mathf.Exp(-22f * dt));
        }
        else if (!holeDraft.isPlaying) return;

        draftFade = opening.HasValue ? Mathf.Min(1f, draftFade + dt / .32f) :
            Mathf.Max(0f, draftFade - dt / .45f);
        holeDraft.volume = DraftGain * volume * Mathf.SmoothStep(0f, 1f, draftFade);
        if (draftFade == 0f && !opening.HasValue) holeDraft.Stop();
    }

    internal void ClearHoleDraft()
    {
        if (holeDraft.isPlaying) holeDraft.Stop();
        draftFade = holeDraft.volume = 0f;
    }

    void Update()
    {
        if (musicFade == musicTarget) return;
        musicFade = Mathf.MoveTowards(musicFade, musicTarget, Time.unscaledDeltaTime / MusicFadeSeconds);
        ApplyMusicVolume();
    }

    public void StartCountCue(bool go)
    {
        if (volume > 0f) Cue(go ? startGo : startKnock, go ? .84f : .66f);
    }

    void Cue(AudioClip clip, float gain)
    {
        if (!isActiveAndEnabled || !clip) return;
        ui.PlayOneShot(clip, gain);
#if UNITY_EDITOR
        if (clip == click) EditorMenuClick?.Invoke(ui.volume);
        if (clip == startKnock) ++EditorStartKnocks;
        if (clip == startGo) ++EditorGoCues;
#endif
    }

    // Individual tile callers and a caller already supplying a wave centroid share one mix per frame.
    public void Break(Vector3 worldPoint, int count)
    {
        if (!isActiveAndEnabled || count <= 0 || volume == 0 || spaceSilent) return;
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
        if (!clip || when >= spaceExitDsp) return;
        var voice = glass[nextGlass, layer];
        voice.Stop();
        voice.transform.position = position;
        voice.clip = clip;
        glassGains[nextGlass, layer] = gain;
        voice.volume = gain * volume;
        voice.PlayScheduled(when);
        if (when + clip.length > spaceExitDsp) voice.SetScheduledEndTime(spaceExitDsp);
    }

    void StopGlass()
    {
        pendingCount = 0;
        weightedPosition = Vector3.zero;
        foreach (var voice in glass) if (voice) { voice.Stop(); voice.clip = null; }
    }

    internal void BeginFall(FloorEnding ending, float timeToContact)
    {
        StopFall();
#if UNITY_EDITOR
        EditorLandingCueCount = 0;
#endif
        inSpace = ending == FloorEnding.Space;
        falling.clip = wind;
        roar.clip = lowWind;
        if (inSpace) ScheduleSpaceExit(timeToContact);
        else { falling.Play(); roar.Play(); }
    }

    void ScheduleSpaceExit(float remaining)
    {
        const double lead = .015;
        double dsp = AudioSettings.dspTime;
        spaceExitDsp = dsp + remaining;
        if (remaining <= lead) { SilenceSpace(); return; }
        // Play the final portion of each dry recording. Its last four milliseconds
        // taper to digital zero and its last sample coincides with the shaft exit.
        StartWindTail(falling, remaining - lead, dsp + lead);
        StartWindTail(roar, remaining - lead, dsp + lead);
        foreach (var voice in glass)
            if (voice.clip && voice.isPlaying) voice.SetScheduledEndTime(spaceExitDsp);
    }

    void StartWindTail(AudioSource source, double seconds, double dsp)
    {
        source.Stop();
        var clip = source.clip;
        source.timeSamples = Mathf.Clamp(clip.samples - Mathf.RoundToInt((float)(seconds * clip.frequency)), 0, clip.samples - 1);
        source.PlayScheduled(dsp);
        source.SetScheduledEndTime(spaceExitDsp);
    }

    internal void SilenceSpace()
    {
        if (spaceSilent) return;
        spaceSilent = true;
        falling.Stop(); roar.Stop(); StopGlass();
        fallGain = roarGain = falling.volume = roar.volume = 0f;
    }

    internal void MixFall(float air, float environment)
    {
        float speed = spaceSilent ? 0f : Mathf.Clamp01(air);
        fallGain = .88f * speed;
        roarGain = .68f * speed * speed;
        ambienceGain = Mathf.Clamp01(environment) * .55f;
        falling.volume = fallGain * volume;
        roar.volume = roarGain * volume;
        ambience.volume = ambienceGain * volume;
        tarBed.volume = ambienceGain * volume;
    }

    internal void Land(FloorEnding ending, Vector3 position)
    {
        // Start each recording on contact so the impact leads into its own settling sound.
        switch (ending)
        {
            case FloorEnding.Water: Accent(splash, position, .96f); ambience.clip = submerged; break;
            case FloorEnding.Lava: Accent(lavaImpact, position, .96f); ambience.clip = heat; break;
            case FloorEnding.Sand: Accent(sandImpact, position, .94f); ambience.clip = sand; break;
            case FloorEnding.Snow: Accent(snowImpact, position, .94f); ambience.clip = snow; break;
            case FloorEnding.TarPit:
                Accent(tarImpact, position, .96f);
                tarBed.transform.position = position;
                tarFilter.cutoffFrequency = 3200;
                tarBed.clip = tarBubbles;
                tarBed.Play();
                return;
            default: return;
        }
        ambience.Play();
    }

    internal void TarSuction(Vector3 position, int pull) => Accent(tarPull, position, .48f, pull == 1 ? .94f : .78f);
    internal void MuffleTar(float immersion) => tarFilter.cutoffFrequency = Mathf.Lerp(3200, 480, immersion);

    void Accent(AudioClip clip, Vector3 position, float gain, float pitch = 1f)
    {
        if (!clip || volume == 0f) return;
        accent.transform.position = position;
        accent.clip = clip;
        accent.pitch = pitch;
        accentGain = gain;
        accent.volume = gain * volume;
        accent.Play();
#if UNITY_EDITOR
        ++EditorLandingCueCount;
#endif
    }

    internal void SetFallPaused(bool paused, float timeToContact)
    {
        if (paused)
        {
            falling.Pause(); roar.Pause(); ambience.Pause(); accent.Pause(); tarBed.Pause();
            StopGlass();
        }
        else
        {
            if (inSpace && !spaceSilent) ScheduleSpaceExit(timeToContact);
            else if (!inSpace) { falling.UnPause(); roar.UnPause(); }
            ambience.UnPause(); accent.UnPause(); tarBed.UnPause();
        }
    }

    internal void StopFall()
    {
        if (!falling) return;
        if (falling.clip) StopGlass();
        inSpace = spaceSilent = false;
        spaceExitDsp = double.PositiveInfinity;
        falling.Stop(); roar.Stop(); ambience.Stop(); accent.Stop(); tarBed.Stop();
        falling.clip = roar.clip = ambience.clip = accent.clip = tarBed.clip = null;
        fallGain = roarGain = ambienceGain = accentGain = 0f;
        falling.volume = roar.volume = ambience.volume = accent.volume = tarBed.volume = 0f;
    }

    public void ResetSounds()
    {
        if (!ui) return;
        ClearHoleDraft();
        StopFall(); StopGlass();
        ui.Stop();
        foreach (var lamp in lampVoices) if (lamp) lamp.Stop();
        StopThunder();
    }

    void OnEnable()
    {
        if (!music || !music.clip) return;
        music.UnPause();
        if (!music.isPlaying) music.Play();
    }

    void OnDisable()
    {
        ResetSounds();
        if (music) music.Pause();
    }
}
