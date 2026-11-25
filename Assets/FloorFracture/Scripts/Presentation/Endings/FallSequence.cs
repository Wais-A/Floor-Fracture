using System;
using UnityEngine;

/// <summary>Plays one confirmed loss while the XR rig continues to own head tracking.</summary>
[DisallowMultipleComponent]
public sealed class FallSequence : MonoBehaviour
{
    const float Gravity = 9.8f;
    const float RimHold = .40f;
    static readonly float FreeFallTime = Mathf.Sqrt(2f * FallScenery.ShaftDepth / Gravity);
    [SerializeField] FloorEndingSet enabledEndings = FloorEndingSet.All;
    [SerializeField] Material waterSurfaceOverride = null;
    [SerializeField] GameObject splashPrefabOverride = null;
    [SerializeField] GameObject lavaFlamesOverride = null;
#if UNITY_EDITOR || DEBUG
    enum PreviewEnding { Shuffle = -1, Water, Lava, TarPit, Space, Sand, Snow }
    [SerializeField] PreviewEnding developmentEnding = PreviewEnding.Shuffle;
#endif
    EndingShuffle shuffle;
    string shufflePreference;
    int selectedLevel;
    public int CurrentLevelEnding { get; private set; } = -1;
#if UNITY_EDITOR || DEBUG
    readonly EndingShuffle developmentShuffle = new EndingShuffle(Guid.NewGuid().GetHashCode());
#endif
    FloorFracture.GlassBoardView board;
    FloorFracture.FallingArena arena;
    FallScenery scenery;
    GameSound sound;
    Transform head;
    Action finished;
    FloorEnding kind;
    int visiblePreview = -1;
    bool reduced, hasOpening, impactPlayed, entryReported, releasePulsed;
    FloorFracture.PanelController leftHand, rightHand;
    int tarPulls;
    Vector3 opening;
    float width, yaw;
    double startedAt;
    double? pausedAt;
    int blackFrame = -1, generation;

    public bool IsPlaying { get; private set; }
    public string LastEndingName { get; private set; } = string.Empty;
    public bool LastWasSkipped { get; private set; }
    public event Action ChamberEntered;

    /// <summary>Sets the next entry to the tracked head's vertical projection on the board.</summary>
    public void SetOpening(Vector3 pointOnFloor, float tileWidth, float yawDegrees = 0f)
    {
        opening = pointOnFloor; width = Mathf.Max(.1f,tileWidth); yaw = yawDegrees; hasOpening = true;
    }

    void Awake() => LoadShuffle("ff.waveEndingBag.v1", Guid.NewGuid().GetHashCode());

    void LoadShuffle(string preference, int seed)
    {
        shufflePreference = preference;
        shuffle = new EndingShuffle(seed, PlayerPrefs.GetString(preference));
    }

    public void SetShuffleSeed(int seed) => shuffle = new EndingShuffle(seed);

#if UNITY_EDITOR
    public void EditorUseShufflePreferences(string preference, int seed) => LoadShuffle(preference,seed);
    public int EditorPeekNextLevelEnding => shuffle.Peek(enabledEndings);
#endif

    internal void SelectLevel(int level)
    {
        // Every wave and tracking retry in this level shares the same chamber.
        if (selectedLevel == level) return;
        selectedLevel = level;
        CurrentLevelEnding = shuffle.Next(enabledEndings);
        if (CurrentLevelEnding < 0) return;
        PlayerPrefs.SetString(shufflePreference, shuffle.Save());
        PlayerPrefs.Save();
    }

    internal void SetBoard(FloorFracture.GlassBoardView nextBoard)
    {
        if (visiblePreview >= 0) scenery.Hide();
        visiblePreview = -1;
        selectedLevel = 0;
        CurrentLevelEnding = -1;
        board = nextBoard;
        // First use loads the sample water and builds the shaft meshes. Do it while the
        // board is being set up so the first timed shatter can show its destination immediately.
        if (board) PrepareChamber();
    }

    int PresentationEnding(bool developmentPreview, bool beginFall = false)
    {
#if UNITY_EDITOR || DEBUG
        if (developmentPreview && (Application.isEditor || Debug.isDebugBuild))
        {
            if (developmentEnding != PreviewEnding.Shuffle) return (int)developmentEnding;
            return beginFall ? developmentShuffle.Next(enabledEndings) : developmentShuffle.Peek(enabledEndings);
        }
#endif
        // A tutorial can open a pane before the first real wave. Preview it without drawing.
        return selectedLevel > 0 ? CurrentLevelEnding : shuffle.Peek(enabledEndings);
    }

    internal void ShowOpenPanes(Transform viewer, bool developmentPreview = false)
    {
        if (IsPlaying) return;
        int selected = board && board.HasOpenPanes && viewer ? PresentationEnding(developmentPreview) : -1;
        if (selected == visiblePreview) return;
        visiblePreview = selected;
        if (selected < 0) { scenery?.Hide(); return; }
        OpenChamber(viewer, (FloorEnding)selected, board.transform.position, board.PaneWidth, board.transform.eulerAngles.y);
        scenery.Draw(0,0,0,false,0,out _,preview:true);
    }

    void OpenChamber(Transform viewer, FloorEnding destination, Vector3 entry, float tileWidth, float yawDegrees)
    {
        PrepareChamber();
        Vector3 center = board ? board.transform.position : entry;
        float radius = board ? board.ChamberRadius : Mathf.Max(1.48f, tileWidth*.5f+.65f);
        scenery.Begin(viewer,destination,center,entry,tileWidth,yawDegrees,radius,board && board.HasOpenPanes);
    }

    void PrepareChamber()
    {
        scenery ??= new FallScenery(waterSurfaceOverride,splashPrefabOverride,lavaFlamesOverride);
    }

    /// <summary>Water through snow are indices zero through five. A negative index restores the bag.</summary>
    public void SetDevelopmentEnding(int index)
    {
#if UNITY_EDITOR || DEBUG
        if (Application.isEditor || Debug.isDebugBuild)
            developmentEnding = index >= 0 && index < 6 ? (PreviewEnding)index : PreviewEnding.Shuffle;
#endif
    }

    public void Begin(Transform head, bool reducedMotion, Action finished, bool developmentPreview = false)
    {
        // SetOpening can be called immediately before replacing an in-flight preview.
        Vector3 nextOpening = opening;
        bool supplied = hasOpening;
        float nextWidth = width, nextYaw = yaw;
        ResetSequence();
        LastWasSkipped = false;
        this.head = head ? head : Camera.main ? Camera.main.transform : null;
        if (!this.head || !isActiveAndEnabled)
        {
            finished?.Invoke();
            return;
        }
        if (!sound) sound = GetComponent<GameSound>();
        if (!sound) sound = FindAnyObjectByType<GameSound>();
        if (!sound) sound = gameObject.AddComponent<GameSound>();
        int selected = PresentationEnding(developmentPreview, true);
        bool emptyBag = selected < 0;
        kind = emptyBag ? FloorEnding.Water : (FloorEnding)selected;
        LastEndingName = emptyBag ? string.Empty : kind == FloorEnding.TarPit ? "Tar Pit" : kind.ToString();
        this.finished = finished;
        reduced = reducedMotion;
        IsPlaying = true;
        startedAt = Time.unscaledTimeAsDouble;
        if (!supplied)
        {
            nextOpening = this.head.position - Vector3.up*1.55f;
            nextWidth = .5f; nextYaw = 0;
        }
        if (!emptyBag && board) arena = new FloorFracture.FallingArena(board,reduced);
        OpenChamber(this.head,kind,nextOpening,nextWidth,nextYaw);
        if (emptyBag) { Blackout(); return; }
        sound.BeginFall(kind, ContactTime);
        Draw(0f);
    }

    internal void SetTrackingPaused(bool paused)
    {
        if (!IsPlaying || paused == pausedAt.HasValue) return;
        double now = Time.unscaledTimeAsDouble;
        if (paused) pausedAt = now;
        else
        {
            startedAt += now - pausedAt.Value;
            pausedAt = null;
        }
        scenery.SetTrackingPaused(paused);
        if (paused) StopHaptics();
        sound.SetFallPaused(paused, ContactTime - (float)((pausedAt ?? now) - startedAt));
        if (!paused && blackFrame >= 0) blackFrame = Time.frameCount;
    }

    void Update()
    {
        if (!IsPlaying || pausedAt.HasValue) return;
        if (!head) { Finish(); return; }
        if (blackFrame >= 0)
        {
            // The callback only runs after a fully opaque frame has been presented.
            if (Time.frameCount > blackFrame) Finish();
            return;
        }
        Draw((float)(Time.unscaledTimeAsDouble - startedAt));
    }

    float ContactTime => reduced ? .82f : RimHold + FreeFallTime;

    void Draw(float age)
    {
        float contact = ContactTime;
        float duration = kind == FloorEnding.Space ? 8f : kind == FloorEnding.Snow ? 5.2f : 5f;
        float p = Mathf.Clamp01((age - contact) / (reduced ? 3.11f : duration));
        float seconds = p * duration;
        // Let the player register the broken rim, then accelerate the scenery past the tracked eye.
        float fallingTime = Mathf.Clamp(age - RimHold, 0f, FreeFallTime);
        float travel = age >= contact ? FallScenery.ShaftDepth : .5f * Gravity * fallingTime * fallingTime;
        if (!reduced && age >= contact)
            travel += kind == FloorEnding.Space ? Gravity * FreeFallTime * seconds : scenery.EyeHeight * Burial(kind,seconds);
        // Free-fall speed past the shaft's still air. Space keeps falling after contact, so fade it out there too.
        float dustSpeed = reduced ? 0 : Gravity * fallingTime * (1 - FallScenery.Ease(0, .25f, age - contact));
        float immersion = scenery.Draw(age, p, seconds, reduced, travel, out bool entered, fallSpeed: dustSpeed);
        arena?.Draw(travel,entered);
        float fade = FallScenery.Ease(3f,5f,seconds);
        // At constant acceleration, speed as a fraction of impact speed is t / tImpact.
        float air = reduced ? .34f * Mathf.Clamp01(age / contact) : fallingTime / FreeFallTime;
        Haptics(age, contact, air);
        if (age >= contact && kind != FloorEnding.Space)
        {
            if (!impactPlayed)
            {
                impactPlayed = true;
                Vector3 point = scenery.SplashPosition();
                if (kind == FloorEnding.Water) scenery.Splash(point,reduced);
                sound.Land(kind,point);
                // Snow is a soft landing; every other surface hits hard.
                Pulse((kind == FloorEnding.Snow ? .45f : 1f) * (reduced ? .5f : 1f), .22f);
            }
            air *= 1 - FallScenery.Ease(0, .08f, age - contact);
        }
        float ambiance = 0;
        float tint = 0;
        Color tintColor = Color.black;
        switch (kind)
        {
            case FloorEnding.Water:
                ambiance = Mathf.Max(immersion,.18f*FallScenery.Ease(0,.20f,p));
                break;
            case FloorEnding.Lava:
                ambiance = FallScenery.Ease(0,.45f,seconds);
                break;
            case FloorEnding.TarPit:
                if (age >= contact)
                {
                    int due = seconds >= SecondPull ? 2 : seconds >= FirstPull ? 1 : 0;
                    if (due > tarPulls)
                    {
                        tarPulls = due;
                        sound.TarSuction(scenery.SplashPosition(),due);
                    }
                    ambiance = .75f + .25f*immersion;
                    sound.MuffleTar(immersion);
                }
                fade = FallScenery.Ease(3.3f,4.2f,seconds);
                break;
            case FloorEnding.Space:
                // Passing the broken rim enters the chamber; vacuum begins at the far end of the 80 m shaft.
                if (age >= contact) sound.SilenceSpace();
                fade = FallScenery.Ease(4.8f,7f,seconds);
                break;
            case FloorEnding.Sand:
                ambiance = FallScenery.Ease(0,.55f,seconds)*(1-FallScenery.Ease(3.8f,5f,seconds));
                break;
            case FloorEnding.Snow:
                ambiance = FallScenery.Ease(.16f,.80f,seconds);
                tintColor = Color.white; tint = FallScenery.Ease(0,2.8f,seconds);
                fade = FallScenery.Ease(3.2f,5.2f,seconds);
                break;
        }
        float ambienceFade = kind == FloorEnding.TarPit ? FallScenery.Ease(4.2f,5f,seconds) : fade;
        sound.MixFall(air*(1-fade),ambiance*(1-ambienceFade));
        if (reduced && age < .70f)
        {
            float cover = FallScenery.Ease(0,.31f,age)*(1-FallScenery.Ease(.40f,.70f,age));
            scenery.SetVeil(Color.black,cover);
        }
        else
        {
            // The final fade also covers underwater bubbles and the snow whiteout.
            float alpha = fade + tint*(1-fade);
            float colorWeight = alpha > .001f ? tint*(1-fade)/alpha : 0;
            scenery.SetVeil(tintColor*colorWeight,alpha);
        }
        if (!entryReported && entered)
        {
            // Even a late frame that skips the fade plateau must cover the tableau swap.
            if (reduced) scenery.SetVeil(Color.black,1);
            entryReported = true;
            int currentGeneration = generation;
            ChamberEntered?.Invoke();
            if (generation != currentGeneration || !IsPlaying) return;
        }
        if (p >= 1) Blackout();
    }

    void Haptics(float age, float contact, float air)
    {
        leftHand ??= new FloorFracture.PanelController(transform, true);
        rightHand ??= new FloorFracture.PanelController(transform, false);
        leftHand.EndPulse(); rightHand.EndPulse();
        float strength = reduced ? .5f : 1f;
        if (!releasePulsed && age >= RimHold)
        {
            // The glass gives way under the feet.
            releasePulsed = true;
            Pulse(.55f * strength, .07f);
        }
        // A rumble that builds with airspeed until the impact pulse takes over.
        else if (age > RimHold + .07f && age < contact)
            Pulse((.06f + .34f * air * air) * strength, .1f);
    }

    void Pulse(float amplitude, float duration)
    {
        leftHand?.Pulse(amplitude, duration);
        rightHand?.Pulse(amplitude, duration);
    }

    void StopHaptics()
    {
        leftHand?.EndPulse(true);
        rightHand?.EndPulse(true);
    }

    internal const float FirstPull = .47f, SecondPull = 1.57f;

    // Surface displacement in standing eye heights. A short initial compression gives
    // way to a positive sinking speed, including after the surface passes the eye.
    internal static float Burial(FloorEnding ending, float t)
    {
        float plunge = .13f * (1-Mathf.Exp(-t/.075f));
        switch (ending)
        {
            case FloorEnding.Water: return plunge + .87f*t/1.2f;
            case FloorEnding.Lava: return plunge + .87f*t/1.8f;
            case FloorEnding.Sand: return plunge + .87f*t/2.3f;
            case FloorEnding.TarPit:
                return plunge + .53f*t/2.5f + .17f*FallScenery.Ease(FirstPull,FirstPull+.42f,t)
                    + .17f*FallScenery.Ease(SecondPull,SecondPull+.48f,t);
            case FloorEnding.Snow:
                float remaining = 1-Mathf.Clamp01(t/.7f);
                return .5f*(1-remaining*remaining);
            default: return 0;
        }
    }

    public void Skip()
    {
        if (IsPlaying && blackFrame < 0)
        {
            LastWasSkipped = true;
            Blackout();
        }
    }

    void Blackout()
    {
        sound.StopFall();
        StopHaptics();
        scenery.SetVeil(Color.black,1);
        blackFrame = Time.frameCount;
    }

    void Finish()
    {
        var callback = finished;
        ResetSequence();
        callback?.Invoke();
    }

    public void ResetSequence()
    {
        pausedAt = null;
        generation++; IsPlaying = false; visiblePreview = -1; finished = null; head = null;
        blackFrame = -1; tarPulls = 0; hasOpening = impactPlayed = entryReported = releasePulsed = false;
        StopHaptics();
        arena?.Restore(); arena = null;
        if (sound) sound.StopFall();
        scenery?.Hide();
    }

    void OnDisable() => ResetSequence();
    void OnDestroy() { scenery?.Dispose(); scenery = null; }
}
