using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Centered start cues and shaft audio, with positional glass and landing cues.</summary>
[DisallowMultipleComponent]
public sealed class GameSound : MonoBehaviour
{
    [SerializeField, Range(0, 1), Tooltip("Replaced when the game starts by the player's Volume setting in the Settings menu.")]
    float volume = .75f;

    // The game sets each sound source's own Volume while it plays: one of these levels times the player's Volume setting.
    [Header("Sound levels, before the player's Volume setting")]
    [SerializeField, Range(0, 1)] float menuClick = .38f;
    [SerializeField, Range(0, 1)] float levelComplete = .54f;
    [SerializeField, Range(0, 1)] float countdownTick = .66f;
    [SerializeField, Range(0, 1)] float countdownGo = .84f;
    [SerializeField, Range(0, 1)] float music = .12f;
    [SerializeField, Range(0, 1), Tooltip("A fully lit lamp, heard standing in its spotlight. It fades with the lamp and with distance from the spot.")] float lampHum = 1f;
    [SerializeField, Range(0, 1), Tooltip("The air rising from an open hole the player stands near.")] float openingDraft = .40f;
    [SerializeField, Range(0, 1)] float thunder = 1f;
    [SerializeField, Range(0, 1), Tooltip("One pane breaking.")] float paneImpact = .55f;
    [SerializeField, Range(0, 1), Tooltip("Six or more panes breaking at once.")] float paneImpactMany = .68f;
    [SerializeField, Range(0, 1), Tooltip("Times the pane impact.")] float glassFragments = .41f;
    [SerializeField, Range(0, 1), Tooltip("Times the pane impact.")] float shaftEcho = .29f;
    [SerializeField, Range(0, 1), Tooltip("A warned pane straining. Divided by the square root of the panes warned together, up to 16.")]
    float glassStrain = .74f;
    [SerializeField, Range(0, 1), Tooltip("At full fall speed.")] float fallAir = .88f;
    [SerializeField, Range(0, 1), Tooltip("At full fall speed. It rises with the square of the speed.")] float fallRoar = .68f;
    [SerializeField, Range(0, 1), Tooltip("The settling sound after a landing, and the tar bubbling.")] float endingAmbience = .55f;
    [SerializeField, Range(0, 1), Tooltip("Landing in water, lava or tar.")] float landingImpact = .96f;
    [SerializeField, Range(0, 1), Tooltip("Landing in sand or snow.")] float softLandingImpact = .94f;
    [SerializeField, Range(0, 1)] float tarSuction = .48f;

    AudioSource ui, falling, roar, ambience, accent, tarBed;
    AudioSource musicVoice;
    float musicFade = 1f, musicTarget = 1f;
    const float MusicFadeSeconds = .30f;
    AudioSource holeDraft;
    float draftFade;
    readonly List<AudioSource> lampVoices = new();
    readonly List<float> lampLevels = new();
    // Two strikes per wave at most, each 7 s of thunder, plus one spare so a new wave's strike never cuts one off.
    readonly AudioSource[] thunderVoices = new AudioSource[3];
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
        // Saved in the scene, this wakes long before GameFlow.Start. A muted player must hear no music meanwhile.
        volume = PlayerPrefs.GetInt("ff.volume", 3) / 4f;
        Build();
    }

    /// <summary>Adopts the sound sources saved under this object and creates any that are missing.</summary>
    public void Build()
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
        // A saved source keeps its own clip and 3D settings; these apply only to one created here.
        ui = Voice("Menu and countdown", false, out _);
        musicVoice = Voice("Background music", false, out bool made);
        if (made)
        {
            musicVoice.clip = Clip("BackgroundMusic");
            musicVoice.loop = true;
        }
        holeDraft = Voice("Opening draft", true, out made);
        if (made)
        {
            holeDraft.clip = Clip("HoleDraft");
            holeDraft.loop = true;
            holeDraft.minDistance = 1.6f;
        }
        holeDraft.volume = 0f;
        falling = Voice("Shaft air", false, out made);
        if (made) falling.priority = 64;
        roar = Voice("Shaft roar", false, out made);
        if (made) roar.priority = 64;
        ambience = Voice("Ending ambience", false, out _);
        accent = Voice("Ending impact", true, out made);
        if (made)
        {
            // Contact is at the feet, even when the player is standing at full height.
            accent.minDistance = 2.6f;
            // The positional impact needs to cut through the head-relative rush at contact.
            accent.GetComponent<MetaXRAudioSource>().GainBoostDb = 7f;
            accent.priority = 32;
        }
        tarBed = Voice("Tar bubbling", true, out made);
        tarFilter = FloorFracture.SceneParts.Require<AudioLowPassFilter>(tarBed.gameObject);
        if (made)
        {
            tarBed.minDistance = 2.6f;
            tarFilter.lowpassResonanceQ = 1f;
        }
        for (int i = 0; i < glass.GetLength(0); i++)
        {
            glass[i, 0] = Voice("Pane impact " + i, true, out _);
            glass[i, 1] = Voice("Glass fragments " + i, true, out _);
            glass[i, 2] = Voice("Shaft echo " + i, true, out _);
        }
        var thunderClip = Clip("Thunder");
        for (int i = 0; i < thunderVoices.Length; i++)
        {
            thunderVoices[i] = Voice("Thunder " + i, true, out made);
            if (!made) continue;
            thunderVoices[i].clip = thunderClip;
            thunderVoices[i].minDistance = 12f; // strikes are 9 m from the board centre: full level anywhere on the board
            thunderVoices[i].maxDistance = 40f;
        }
        SetVolume(volume);
    }

    static AudioClip Clip(string name) => Resources.Load<AudioClip>("Endings/Audio/" + name);

    AudioSource Voice(string name, bool isSpatial, out bool created)
    {
        if (isSpatial)
            return FloorFracture.SceneParts.Spawn(spatialVoicePrefab, transform, name, out created).GetComponent<AudioSource>();
        var child = FloorFracture.SceneParts.Child(transform, name, out created);
        var source = FloorFracture.SceneParts.Require<AudioSource>(child.gameObject);
        if (created) source.playOnAwake = false;
        return source;
    }

    public float MasterVolume => volume;
    public float GlassStrainLevel => glassStrain;

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

    AudioListener listener;
    const float LampHumAboveHead = .3f;

    // Over the middle of the lamp's spotlight, just above the player's head, so the hum is loudest in the lit spot.
    public void PlaceLampVoice(AudioSource voice, Vector3 pool)
    {
        if (!listener) listener = FindAnyObjectByType<AudioListener>();
        float ear = listener ? listener.transform.position.y : pool.y + 1.6f;
        voice.transform.position = new Vector3(pool.x, ear + LampHumAboveHead, pool.z);
    }

    // Saved under its lamp; PlaceLampVoice moves it over the lamp's spotlight. Silent until SetLampLevel.
    public AudioSource CreateLampVoice(Transform lamp, float poolRadius)
    {
        for (int i = lampVoices.Count - 1; i >= 0; --i)
            if (!lampVoices[i]) { lampVoices.RemoveAt(i); lampLevels.RemoveAt(i); }
        var voice = FloorFracture.SceneParts.Spawn(spatialVoicePrefab, lamp, "Lamp hum", out bool made).GetComponent<AudioSource>();
        if (made)
        {
            voice.clip = beamHum;
            voice.loop = true;
        }
        // Full volume anywhere in the lit spot, fading beyond it. The spot's size follows the panes, so this is set on every fit.
        voice.minDistance = Mathf.Sqrt(poolRadius * poolRadius + LampHumAboveHead * LampHumAboveHead);
        voice.Stop();
        voice.volume = 0f;
        // A board fitted again hands back the lamp it already has.
        int known = lampVoices.IndexOf(voice);
        if (known >= 0) lampLevels[known] = 0f;
        else { lampVoices.Add(voice); lampLevels.Add(0f); }
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
        voice.volume = lampHum * lampLevels[index] * volume;
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
        foreach (var voice in thunderVoices) voice.volume = thunder * volume;
    }

    // A pooled spatial one-shot from the strike's direction, `delaySeconds` after the flash.
    public void PlayThunder(Vector3 worldPosition, double delaySeconds, float pitch)
    {
        if (!isActiveAndEnabled || volume == 0f) return;
        var voice = thunderVoices[nextThunder];
        nextThunder = (nextThunder + 1) % thunderVoices.Length;
        if (!voice.clip) return;
        voice.Stop();
        voice.transform.position = worldPosition;
        voice.pitch = pitch;
        voice.volume = thunder * volume;
        voice.PlayScheduled(AudioSettings.dspTime + delaySeconds);
#if UNITY_EDITOR
        ++EditorThunderCues;
        EditorLastThunder = voice;
#endif
    }

    // Stopping also cancels thunder that is scheduled but not yet audible.
    void StopThunder()
    {
        foreach (var voice in thunderVoices) if (voice) voice.Stop();
    }

    public void Click() => Cue(click, menuClick);
    public void Completion() => Cue(clear, levelComplete);
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

    void ApplyMusicVolume() => musicVoice.volume = music * Mathf.SmoothStep(0f, 1f, musicFade) * volume;

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
        holeDraft.volume = openingDraft * volume * Mathf.SmoothStep(0f, 1f, draftFade);
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
        if (volume > 0f) Cue(go ? startGo : startKnock, go ? countdownGo : countdownTick);
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
        float gain = Mathf.Lerp(paneImpact, paneImpactMany, Mathf.Clamp01((pendingCount - 1) / 5f));
        double now = AudioSettings.dspTime + .02;
        PlayGlass(0, impact, position, now, gain);
        PlayGlass(1, fragments, position + Vector3.down * .22f, now + .055, gain * glassFragments);
        PlayGlass(2, echo, position + Vector3.down * 1.65f, now + .27, gain * shaftEcho);
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
        fallGain = fallAir * speed;
        roarGain = fallRoar * speed * speed;
        ambienceGain = Mathf.Clamp01(environment) * endingAmbience;
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
            case FloorEnding.Water: Accent(splash, position, landingImpact); ambience.clip = submerged; break;
            case FloorEnding.Lava: Accent(lavaImpact, position, landingImpact); ambience.clip = heat; break;
            case FloorEnding.Sand: Accent(sandImpact, position, softLandingImpact); ambience.clip = sand; break;
            case FloorEnding.Snow: Accent(snowImpact, position, softLandingImpact); ambience.clip = snow; break;
            case FloorEnding.TarPit:
                Accent(tarImpact, position, landingImpact);
                tarBed.transform.position = position;
                tarFilter.cutoffFrequency = 3200;
                tarBed.clip = tarBubbles;
                tarBed.Play();
                return;
            default: return;
        }
        ambience.Play();
    }

    internal void TarSuction(Vector3 position, int pull) => Accent(tarPull, position, tarSuction, pull == 1 ? .94f : .78f);
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

#if UNITY_EDITOR
    // A level changed in the Inspector during Play is heard at once. Unity puts Play Mode edits back when Play stops.
    void OnValidate() { if (Application.isPlaying && ui) SetVolume(volume); }
#endif

    void OnEnable()
    {
        if (!musicVoice || !musicVoice.clip) return;
        musicVoice.UnPause();
        if (!musicVoice.isPlaying) musicVoice.Play();
    }

    void OnDisable()
    {
        ResetSounds();
        if (musicVoice) musicVoice.Pause();
    }
}
