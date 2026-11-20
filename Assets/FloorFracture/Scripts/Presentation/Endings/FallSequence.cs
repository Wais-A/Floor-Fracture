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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    enum PreviewEnding { Shuffle = -1, Water, Lava, Crystal, Space, Sand, Snow }
    [SerializeField] PreviewEnding developmentEnding = PreviewEnding.Shuffle;
#endif
    EndingShuffle shuffle = new EndingShuffle(Environment.TickCount);
    FloorFracture.GlassBoardView board;
    FloorFracture.RoomBoardPlacement room;
    FloorFracture.FallingArena arena;
    FallScenery scenery;
    GameSound sound;
    Transform head;
    Action finished;
    FloorEnding kind;
    int visiblePreview = -1;
    bool reduced, hasOpening, impactPlayed, chimePlayed, entryReported;
    Vector3 opening;
    float width, yaw;
    double startedAt;
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

    public void SetShuffleSeed(int seed) => shuffle = new EndingShuffle(seed);

    internal void SetBoard(FloorFracture.GlassBoardView nextBoard, FloorFracture.RoomBoardPlacement room)
    {
        if (visiblePreview >= 0) scenery.Hide();
        visiblePreview = -1;
        board = nextBoard;
        this.room = room;
        // First use loads the sample water and builds the shaft meshes. Do it while the
        // board is being set up so the first timed shatter can show its destination immediately.
        if (board) PrepareChamber();
    }

    int SelectEnding(bool consume)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (developmentEnding != PreviewEnding.Shuffle) return (int)developmentEnding;
#endif
        return consume ? shuffle.Next(enabledEndings) : shuffle.Peek(enabledEndings);
    }

    internal void ShowOpenPanes(Transform viewer)
    {
        if (IsPlaying) return;
        int selected = board && board.HasOpenPanes && viewer ? SelectEnding(false) : -1;
        if (selected == visiblePreview) return;
        visiblePreview = selected;
        if (selected < 0) { scenery?.Hide(); return; }
        OpenChamber(viewer, (FloorEnding)selected, board.transform.position, board.PaneWidth, board.transform.eulerAngles.y);
        scenery.Draw(0,0,false,0,out _);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        developmentEnding = index >= 0 && index < 6 ? (PreviewEnding)index : PreviewEnding.Shuffle;
#endif
    }

    public void Begin(Transform head, bool reducedMotion, Action finished)
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
        int selected = SelectEnding(true);
        bool emptyBag = selected < 0;
        kind = emptyBag ? FloorEnding.Water : (FloorEnding)selected;
        LastEndingName = emptyBag ? string.Empty : kind.ToString();
        this.finished = finished;
        reduced = reducedMotion;
        IsPlaying = true;
        startedAt = Time.unscaledTimeAsDouble;
        if (!supplied)
        {
            nextOpening = this.head.position - Vector3.up*1.55f;
            nextWidth = .5f; nextYaw = 0;
        }
        if (!emptyBag && board) arena = new FloorFracture.FallingArena(board,room,reduced,nextOpening);
        OpenChamber(this.head,kind,nextOpening,nextWidth,nextYaw);
        if (emptyBag) { Blackout(); return; }
        sound.BeginFall(kind);
        Draw(0f);
    }

    void Update()
    {
        if (!IsPlaying) return;
        if (!head) { Finish(); return; }
        if (blackFrame >= 0)
        {
            // The callback only runs after a fully opaque frame has been presented.
            if (Time.frameCount > blackFrame) Finish();
            return;
        }
        Draw((float)(Time.unscaledTimeAsDouble - startedAt));
    }

    void Draw(float age)
    {
        float contact = reduced ? .82f : RimHold + FreeFallTime;
        float p = Mathf.Clamp01((age - contact) / (reduced ? 3.11f : 5f));
        // Let the player register the broken rim, then accelerate the scenery past the tracked eye.
        float fallingTime = Mathf.Clamp(age - RimHold, 0f, FreeFallTime);
        float travel = age >= contact ? FallScenery.ShaftDepth : .5f * Gravity * fallingTime * fallingTime;
        float immersion = scenery.Draw(age, p, reduced, travel, out bool entered);
        arena?.Draw(travel,entered);
        if (!entryReported && entered)
        {
            entryReported = true;
            int currentGeneration = generation;
            ChamberEntered?.Invoke();
            if (generation != currentGeneration || !IsPlaying) return;
        }
        float fade = FallScenery.Ease(reduced ? .80f : .60f, 1f, p);
        // At constant acceleration, speed as a fraction of impact speed is t / tImpact.
        float air = reduced ? .25f : fallingTime / FreeFallTime;
        float ambiance = 0;
        float tint = 0;
        Color tintColor = Color.black;
        switch (kind)
        {
            case FloorEnding.Water:
                if (!impactPlayed && age >= contact)
                {
                    Vector3 point = scenery.SplashPosition();
                    scenery.Splash(point,reduced); sound.WaterImpact(point); impactPlayed = true;
                }
                air *= 1-Mathf.Max(immersion,.70f*FallScenery.Ease(0,.17f,p));
                ambiance = Mathf.Max(immersion,.18f*FallScenery.Ease(0,.20f,p));
                tintColor = new Color(.015f,.19f,.24f); tint = .48f*immersion;
                break;
            case FloorEnding.Lava:
                if (!impactPlayed && age >= contact)
                {
                    sound.LavaImpact(scenery.SplashPosition());
                    impactPlayed = true;
                }
                ambiance = FallScenery.Ease(0,.42f,p); air *= 1-FallScenery.Ease(0,.20f,p);
                break;
            case FloorEnding.Crystal:
                air *= 1-FallScenery.Ease(.03f,.45f,p);
                if (!chimePlayed && p >= .71f)
                {
                    sound.CrystalChime(head.position + Vector3.down*.6f); chimePlayed = true;
                }
                break;
            case FloorEnding.Space:
                air *= 1-FallScenery.Ease(.08f,.35f,p);
                break;
            case FloorEnding.Sand:
                ambiance = FallScenery.Ease(.05f,.38f,p)*(1-FallScenery.Ease(.72f,.90f,p));
                air *= 1-FallScenery.Ease(.12f,.40f,p);
                break;
            case FloorEnding.Snow:
                ambiance = FallScenery.Ease(.04f,.75f,p); air *= 1-ambiance;
                tintColor = new Color(.87f,.94f,.98f); tint = FallScenery.Ease(.50f,.78f,p);
                break;
        }
        if (reduced) air *= .33f;
        sound.MixFall(air*(1-fade),ambiance*(1-fade));
        if (reduced && age < .70f)
        {
            float cover = FallScenery.Ease(0,.31f,age)*(1-FallScenery.Ease(.40f,.70f,age));
            scenery.SetVeil(Color.black,cover);
        }
        else
        {
            // Compose the colored water / snow wash with black in one stereo draw.
            float alpha = fade + tint*(1-fade);
            float colorWeight = alpha > .001f ? tint*(1-fade)/alpha : 0;
            scenery.SetVeil(tintColor*colorWeight,alpha);
        }
        if (p >= 1) Blackout();
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
        generation++; IsPlaying = false; visiblePreview = -1; finished = null; head = null;
        blackFrame = -1; hasOpening = impactPlayed = chimePlayed = entryReported = false;
        arena?.Restore(); arena = null;
        if (sound) sound.StopFall();
        scenery?.Hide();
    }

    void OnDisable() => ResetSequence();
    void OnDestroy() { scenery?.Dispose(); scenery = null; }
}
