using System;
using UnityEngine;

/// <summary>Plays one confirmed loss while the XR rig continues to own head tracking.</summary>
[DisallowMultipleComponent]
public sealed class FallSequence : MonoBehaviour
{
    [SerializeField] FloorEndingSet enabledEndings = FloorEndingSet.All;
    [SerializeField] Material waterSurfaceOverride = null;
    [SerializeField] GameObject splashPrefabOverride = null;
    [SerializeField] GameObject lavaFlamesOverride = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    enum PreviewEnding { Shuffle = -1, Water, Lava, Crystal, Space, Sand, Snow }
    [SerializeField] PreviewEnding developmentEnding = PreviewEnding.Shuffle;
#endif
    EndingShuffle shuffle = new EndingShuffle(Environment.TickCount);
    FallScenery scenery;
    GameSound sound;
    Transform head;
    Action finished;
    FloorEnding kind;
    bool reduced, hasOpening, impactPlayed, chimePlayed, entryReported;
    Vector3 opening;
    float width, yaw, age;
    int blackFrame = -1, generation;

    public bool IsPlaying { get; private set; }
    public string LastEndingName { get; private set; } = string.Empty;
    public event Action ChamberEntered;

    /// <summary>Sets the next entry aperture to the actual tile center and rotation.</summary>
    public void SetOpening(Vector3 centerOnFloor, float tileWidth, float yawDegrees = 0f)
    {
        opening = centerOnFloor; width = Mathf.Max(.1f,tileWidth); yaw = yawDegrees; hasOpening = true;
    }

    public void SetShuffleSeed(int seed) => shuffle = new EndingShuffle(seed);

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
        this.head = head ? head : Camera.main ? Camera.main.transform : null;
        if (!this.head || !isActiveAndEnabled)
        {
            finished?.Invoke();
            return;
        }
        if (!sound) sound = GetComponent<GameSound>();
        if (!sound) sound = FindAnyObjectByType<GameSound>();
        if (!sound) sound = gameObject.AddComponent<GameSound>();
        if (scenery == null) scenery = new FallScenery(waterSurfaceOverride,splashPrefabOverride,lavaFlamesOverride);
        int selected;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (developmentEnding != PreviewEnding.Shuffle) selected = (int)developmentEnding;
        else
#endif
            selected = shuffle.Next(enabledEndings);
        bool emptyBag = selected < 0;
        kind = emptyBag ? FloorEnding.Water : (FloorEnding)selected;
        LastEndingName = emptyBag ? string.Empty : kind.ToString();
        this.finished = finished;
        reduced = reducedMotion;
        IsPlaying = true;
        if (!supplied)
        {
            nextOpening = this.head.position - Vector3.up*1.55f;
            nextWidth = .5f; nextYaw = 0;
        }
        scenery.Begin(this.head,kind,nextOpening,nextWidth,nextYaw);
        if (emptyBag) { Blackout(); return; }
        sound.BeginFall(kind);
        Draw();
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
        age += Mathf.Max(0,Time.unscaledDeltaTime);
        Draw();
    }

    void Draw()
    {
        float contact = reduced ? .82f : 3.15f;
        float end = reduced ? 3.93f : 6.72f;
        float p = Mathf.InverseLerp(contact,end,age);
        float descent = reduced ? 1f : FallScenery.Ease(.15f,contact,age);
        float immersion = scenery.Draw(age,p,reduced,FallScenery.ShaftDepth*descent);
        if (!entryReported && age >= (reduced ? .32f : .83f))
        {
            entryReported = true;
            int currentGeneration = generation;
            ChamberEntered?.Invoke();
            if (generation != currentGeneration || !IsPlaying) return;
        }
        float fade = FallScenery.Ease(.80f,1,p);
        float air = reduced ? .25f : Mathf.Lerp(.12f,1,FallScenery.Ease(.15f,2.65f,age));
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
        if (IsPlaying && blackFrame < 0) Blackout();
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
        int finishingGeneration = generation;
        finished = null; IsPlaying = false;
        sound.StopFall();
        try { callback?.Invoke(); }
        finally
        {
            // A callback may already have restarted or opened the next preview.
            if (generation == finishingGeneration) ResetSequence();
        }
    }

    public void ResetSequence()
    {
        generation++; IsPlaying = false; finished = null; head = null;
        age = 0; blackFrame = -1; hasOpening = impactPlayed = chimePlayed = entryReported = false;
        if (sound) sound.StopFall();
        scenery?.Hide();
    }

    void OnDisable() => ResetSequence();
    void OnDestroy() { scenery?.Dispose(); scenery = null; }
}
