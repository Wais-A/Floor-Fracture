using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>World-anchored presentation geometry and a separate, stereo view fade.</summary>
internal sealed class FallScenery
{
    static readonly int Tint = Shader.PropertyToID("_Tint"), Role = Shader.PropertyToID("_Role"),
        Theme = Shader.PropertyToID("_Theme"), Age = Shader.PropertyToID("_Age"),
        Reveal = Shader.PropertyToID("_Reveal"), Level = Shader.PropertyToID("_Level"),
        Motion = Shader.PropertyToID("_Motion"), Opacity = Shader.PropertyToID("_Opacity"),
        SurfaceClock = Shader.PropertyToID("_SurfaceClock"), PassDistance = Shader.PropertyToID("_PassDistance"),
        Mouth = Shader.PropertyToID("_Mouth"), MouthRight = Shader.PropertyToID("_MouthRight"),
        Travel = Shader.PropertyToID("_Travel"), FallSpeed = Shader.PropertyToID("_FallSpeed"),
        Impact = Shader.PropertyToID("_Impact");
    // Four seconds under gravity leaves more than three seconds of walls after a standing eye enters.
    internal const float ShaftDepth = 80f;
    // A near-vertical throat gives an oblique view of the wall from the next pane.
    // Below it the chamber opens out, preserving the view of the landing in flight and from the
    // next pane (a narrow .05 flare hid it). Two brick columns under the bend keep nearer surfaces passing.
    internal const float ThroatDepth = 18f;
    const float WideningPerMetre = .72f;
    static readonly Color SandColor = new Color(.72f,.49f,.24f);
    const float LineWidth = .14f;
    // Allow for both eye offsets and pose updates between Update and stereo rendering.
    const float EyeMargin = .08f;
    static readonly int InteriorPlane = Shader.PropertyToID("_InteriorPlane");
    static readonly Color[] wallTints = {
        new Color(.19f,.28f,.30f), new Color(.25f,.15f,.12f), new Color(.17f,.13f,.09f),
        new Color(.18f,.19f,.26f), new Color(.44f,.31f,.19f), new Color(.26f,.40f,.53f)
    };
    static readonly Color[] skyTints = {
        new Color(.018f,.049f,.060f), new Color(.040f,.019f,.014f), new Color(.027f,.021f,.014f),
        new Color(.009f,.012f,.030f), new Color(.061f,.039f,.021f), new Color(.16f,.25f,.36f)
    };

    readonly Transform world;
    readonly Material surfaceMaterial, backgroundMaterial, fadeMaterial, interiorMaterial, bubbleMaterial, dustMaterial;
    readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    readonly MaterialPropertyBlock waterProperties = new MaterialPropertyBlock();
    readonly MeshRenderer walls, landmarks, background, floor, nativeWater, pillars, motes, dust, leftColumn, rightColumn,
        spray, entryBubbles;
    MeshRenderer veil, interior;
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly Mesh[] moteMeshes = new Mesh[6];
    readonly Mesh boxMesh;
    readonly GameObject splashPrefab;
    readonly Color sourceWaterColor;
    GameObject splash;
    FloorEnding kind;
    Transform head;
    float eyeHeight, startRadius, radius, surfaceStartTime;
    internal float EyeHeight => eyeHeight;
    Vector3 mouthRight, entryWorld, entryLocal;
    bool hasFloorApertures, stencilEnabled, fullyUnder;
    float sandShade;

    internal FallScenery()
    {
        world = new GameObject("Fall chamber").transform;
        surfaceMaterial = new Material(Resources.Load<Shader>("Endings/Chamber"));
        surfaceMaterial.SetFloat("_WideningPerMetre",WideningPerMetre);
        surfaceMaterial.SetFloat("_ThroatDepth",ThroatDepth);
        // Establish the sky's projection alpha before fading walls or the landing can write depth.
        backgroundMaterial = new Material(surfaceMaterial) { renderQueue = surfaceMaterial.renderQueue - 1 };
        fadeMaterial = new Material(Resources.Load<Shader>("Endings/ViewFade"));
        interiorMaterial = new Material(fadeMaterial) { renderQueue = fadeMaterial.renderQueue - 2 };
        bubbleMaterial = new Material(surfaceMaterial) { renderQueue = fadeMaterial.renderQueue - 1 };
        bubbleMaterial.SetInt("_DepthTest",(int)CompareFunction.Always);
        bubbleMaterial.SetInt("_DepthWrite",0);
        boxMesh = Own(FallMeshes.Box());
        Mesh disk = Own(FallMeshes.Disk());
        walls = Part("Shaft walls", Own(FallMeshes.Shaft(ShaftDepth)), surfaceMaterial);
        landmarks = Part("Shaft landmarks", Own(FallMeshes.Landmarks(ShaftDepth)), surfaceMaterial);
        Mesh column = Own(FallMeshes.Column());
        leftColumn = Part("Left brick column", column, surfaceMaterial);
        rightColumn = Part("Right brick column", column, surfaceMaterial);
        background = Part("Chamber darkness", boxMesh, backgroundMaterial);
        floor = Part("Hazard surface", disk, surfaceMaterial);
        var native = Resources.Load<Material>("Endings/MetaFloorZone/materials/Water");
        if (native && native.HasProperty("_WaterNormal"))
            surfaceMaterial.SetTexture("_WaterNormal",native.GetTexture("_WaterNormal"));
        if (native && native.shader && native.shader.isSupported)
        {
            nativeWater = Part("Floor Zone water ripples", disk, native);
            if (native.HasProperty("_Color")) sourceWaterColor = native.GetColor("_Color");
        }
        pillars = Part("Lava flames", Own(FallMeshes.Flames()), surfaceMaterial);
        motes = Part("Particles", null, surfaceMaterial);
        spray = Part("Water spray", Own(FallMeshes.Spray()), surfaceMaterial);
        // Bubbles draw over the underwater view, as the Water particles do.
        entryBubbles = Part("Entry bubbles", Own(FallMeshes.EntryBubbles()), bubbleMaterial);
        // Drawn after the walls and without depth writes, so faint specks never punch holes in them.
        dustMaterial = new Material(surfaceMaterial) { renderQueue = surfaceMaterial.renderQueue + 1 };
        dustMaterial.SetInt("_DepthWrite",0);
        dust = Part("Fall dust", Own(FallMeshes.Dust()), dustMaterial);
        CreateView();
        splashPrefab = Resources.Load<GameObject>("Endings/MetaFloorZone/prefabs/Splash");
        // Even a straight-down ray must meet the destination before it meets the sky enclosure.
        background.transform.localScale = Vector3.one * (ShaftDepth + 8f);
        Hide();
    }

    Mesh Own(Mesh mesh) { ownedMeshes.Add(mesh); return mesh; }

    MeshRenderer Part(string name, Mesh mesh, Material material)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(world, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
        return renderer;
    }

    void CreateView()
    {
        veil = Part("View fade",boxMesh,fadeMaterial);
        interior = Part("Submerged view",boxMesh,interiorMaterial);
        interior.transform.SetParent(veil.transform,false);
        interior.enabled = false;
    }

    internal void Begin(Transform viewer, FloorEnding chosen, Vector3 center, Vector3 entry, float width,
        float yaw, float chamberRadius, bool throughOpenPanes)
    {
        head = viewer; kind = chosen; surfaceStartTime = Time.time;
        world.SetPositionAndRotation(center, Quaternion.Euler(0,yaw,0));
        entryWorld = entry;
        entryLocal = world.InverseTransformPoint(entry);
        hasFloorApertures = throughOpenPanes;
        fullyUnder = false;
        sandShade = 1;
        motes.sharedMaterial = kind == FloorEnding.Water ? bubbleMaterial : surfaceMaterial;
        eyeHeight = Mathf.Max(.25f, viewer.position.y-center.y);
        startRadius = Mathf.Max(.08f, width*.5f);
        radius = chamberRadius;
        mouthRight = world.right;
        walls.transform.localScale = landmarks.transform.localScale = new Vector3(radius,1,radius);
        float spread = ShaftDepth * WideningPerMetre;
        // Shader-expanded walls must remain inside Unity's renderer culling bounds.
        foreach (MeshRenderer part in new[] { walls, landmarks })
        {
            var bounds = part.GetComponent<MeshFilter>().sharedMesh.bounds;
            bounds.Expand(new Vector3(2*spread/radius,0,2*spread/radius));
            part.localBounds = bounds;
        }
        float surfaceRadius = (radius+spread)*1.45f;
        floor.transform.localScale = new Vector3(surfaceRadius,1,surfaceRadius);
        if (nativeWater) nativeWater.transform.localScale = floor.transform.localScale;
        float ringScale = Mathf.Clamp(radius*.55f,.78f,1.02f);
        pillars.transform.localScale = new Vector3(ringScale,1,ringScale);
        motes.transform.localScale = kind == FloorEnding.Sand ? pillars.transform.localScale : Vector3.one;
        motes.transform.localPosition = entryLocal;
        // The dust column hangs still around the starting eye; only the shader wraps it upward.
        dust.transform.localPosition = entryLocal + Vector3.up*eyeHeight;
        if (kind != FloorEnding.TarPit)
        {
            if (!moteMeshes[(int)kind]) moteMeshes[(int)kind] = Own(FallMeshes.Motes(kind));
            motes.GetComponent<MeshFilter>().sharedMesh = moteMeshes[(int)kind];
        }
        world.gameObject.SetActive(true);
        if (!veil) CreateView();
        interior.enabled = false;
        veil.transform.SetParent(head, false);
        veil.transform.localPosition = Vector3.zero;
        veil.transform.localRotation = Quaternion.identity;
        veil.transform.localScale = Vector3.one * 1.5f;
        SetVeil(Color.black, 0);
    }

    // Returns the submersion factor at the viewer's actual height.
    internal float Draw(float age, float progress, float seconds, bool reduced, float travel, out bool entered,
        bool preview = false, float fallSpeed = 0)
    {
        // In reduced motion, hold the preview in place until the comfort fade reaches black.
        bool tableau = reduced && age >= .32f;
        // The chamber and its destination are one moving world until the feet reach the surface.
        // Only the upper metre of the throat widens as the lip draws near.
        float top = tableau ? 5f : travel;
        float rimY = world.position.y + top;
        // Keep every real opening in place as the board rises. Release the stencil as its
        // plane crosses the near clip distance so it cannot clip the view from inside.
        bool useStencil = hasFloorApertures && !tableau && rimY < head.position.y - .05f;
        if (stencilEnabled != useStencil)
        {
            stencilEnabled = useStencil;
            surfaceMaterial.SetInt("_PortalComparison",(int)(useStencil ? CompareFunction.Equal : CompareFunction.Always));
            dustMaterial.SetInt("_PortalComparison",(int)(useStencil ? CompareFunction.Equal : CompareFunction.Always));
        }
        float mouthRadius = Mathf.Lerp(startRadius,Mathf.Max(startRadius,Mathf.Min(radius,.77f)),Ease(0,1.35f,top));
        float surfaceY = tableau ? 0f : travel-ShaftDepth;
        Color wallColor = wallTints[(int)kind], sky = skyTints[(int)kind];
        float wallAlpha = 1, particleAlpha = Ease(0,.17f,progress);
        float floorAlpha = 1, pillarsAlpha = 0;
        int floorRole = 0;
        Color surfaceColor = wallColor*.70f;
        float surfaceAboveEye = world.position.y + surfaceY - head.position.y;
        if (reduced) surfaceAboveEye = eyeHeight * (FallSequence.Burial(kind,seconds,true)-1);
        float immersion = Ease(-.09f,.14f,surfaceAboveEye);
        DrawInterior(age,seconds,surfaceAboveEye,reduced);
        switch (kind)
        {
            case FloorEnding.Water:
                floorRole = 2; surfaceColor = new Color(.035f,.20f,.22f);
                particleAlpha *= fullyUnder ? Ease(EyeMargin,EyeMargin+.18f,surfaceAboveEye) : 0;
                break;
            case FloorEnding.Lava:
                floorRole = 3;
                pillarsAlpha = seconds > 0 ? .52f+.48f*Ease(0,.18f,seconds) : 0;
                particleAlpha = pillarsAlpha;
                break;
            case FloorEnding.TarPit:
                floorRole = 10;
                break;
            case FloorEnding.Space:
                floorRole = 11;
                float exit = reduced ? Ease(0,.55f,seconds) : Ease(ShaftDepth-12,ShaftDepth,travel);
                floorAlpha = 1-exit;
                wallAlpha = 1-Ease(0,1.0f,seconds);
                particleAlpha = exit*(1-Ease(2.8f,6.95f,seconds));
                break;
            case FloorEnding.Sand:
                surfaceColor = SandColor;
                floorRole = 4;
                break;
            case FloorEnding.Snow:
                floorRole = 12;
                particleAlpha = seconds > 0 ? .32f+.68f*Ease(0,.8f,seconds) : 0;
                sky = Color.Lerp(sky,new Color(.73f,.83f,.93f),Ease(0,2.8f,seconds));
                break;
        }
        float level = world.position.y + surfaceY;
        float motion = reduced ? .10f : 1;
        properties.Clear();
        properties.SetFloat(SurfaceClock,preview ? -1 : surfaceStartTime+age);
        properties.SetFloat(PassDistance,reduced ? 0 : Mathf.Max(0,travel-ShaftDepth+12f));
        properties.SetFloat(Theme,(int)kind); properties.SetFloat(Age,age);
        properties.SetFloat(Reveal,progress); properties.SetFloat(Level,level); properties.SetFloat(Motion,motion);
        properties.SetFloat(Travel,travel); properties.SetFloat(FallSpeed,fallSpeed);
        entered = reduced ? tableau : rimY >= head.position.y;
        properties.SetVector(Mouth,new Vector4(entryWorld.x,rimY,entryWorld.z,mouthRadius));
        properties.SetVector(MouthRight,new Vector4(mouthRight.x,mouthRight.z,0,tableau || hasFloorApertures ? 0 : 1));
        // The shaft stays lit. Only the view past its walls gradually loses the room light.
        float overhead = tableau ? 1 : Ease(.15f,26f,rimY-head.position.y);
        Paint(background,kind == FloorEnding.Space ? 8 : 0,sky,fullyUnder ? 0 : overhead);
        walls.transform.localPosition = landmarks.transform.localPosition = Vector3.up*top;
        Paint(walls,1,wallColor,fullyUnder ? 0 : wallAlpha);
        Color ledgeColor = Color.Lerp(wallColor,new Color(.84f,.88f,.85f),.64f);
        Paint(landmarks,9,ledgeColor,fullyUnder ? 0 : wallAlpha);
        // The columns carry the narrow tunnel where it ends. Each inner face lines up with the flat
        // side wall at the bend (Chamber.shader widens .08 m per metre down to it), so the whole
        // column stays outside the tunnel's footprint: nothing stands under a hole, and the tunnel
        // wall hides the top from above. Below the bend the flare uncovers it. They rise with the walls.
        float columnX = radius + .08f*ThroatDepth + FallMeshes.ColumnHalfWidth + .01f;
        leftColumn.transform.localPosition = new Vector3(-columnX,top,0);
        rightColumn.transform.localPosition = new Vector3(columnX,top,0);
        Paint(leftColumn,14,Color.white,fullyUnder ? 0 : wallAlpha);
        Paint(rightColumn,14,Color.white,fullyUnder ? 0 : wallAlpha);
        floor.transform.localPosition = new Vector3(0,surfaceY,0);
        Paint(floor,floorRole,surfaceColor,fullyUnder ? 0 : floorAlpha);
        if (nativeWater)
        {
            // The same rippling surface stays visible all the way from the glass to impact.
            // The transparent Floor Zone layer adds close-up detail as the splash starts.
            nativeWater.enabled = kind == FloorEnding.Water && progress > 0 && !fullyUnder;
            nativeWater.transform.localPosition = floor.transform.localPosition + Vector3.up*.009f;
            if (nativeWater.enabled && nativeWater.sharedMaterial.HasProperty("_Color"))
            {
                Color tint = sourceWaterColor;
                tint.a *= Ease(0,.08f,progress);
                waterProperties.SetColor("_Color",tint);
                nativeWater.SetPropertyBlock(waterProperties);
            }
        }
        // A hard water entry throws a crown of spray around the fall line, then drags a column of
        // bubbles under with the player. Both sit on the surface, so they rise with it as the eye sinks.
        bool plunge = kind == FloorEnding.Water && !reduced && progress > 0;
        spray.transform.localPosition = entryBubbles.transform.localPosition = new Vector3(entryLocal.x,surfaceY,entryLocal.z);
        properties.SetFloat(Impact,seconds);
        Paint(spray,15,new Color(.86f,.96f,1f),plunge && !fullyUnder ? 1 : 0);
        Paint(entryBubbles,16,new Color(.72f,.93f,1f),plunge && fullyUnder ? .9f*(1-Ease(.9f,1.7f,seconds)) : 0);
        pillars.transform.localPosition = entryLocal + Vector3.up*(kind == FloorEnding.Lava ? surfaceY-.11f : 0);
        Paint(pillars,7,Color.white,fullyUnder ? 0 : pillarsAlpha);
        if (fullyUnder && splash && splash.activeSelf) splash.SetActive(false);
        if (kind == FloorEnding.TarPit || (fullyUnder && kind != FloorEnding.Water)) particleAlpha = 0;
        Paint(motes,6,Color.white,particleAlpha);
        // Near specks streaming past the periphery carry the sense of speed the flaring walls lose.
        Paint(dust,13,Color.Lerp(wallColor,Color.white,.55f),
            reduced || fullyUnder || preview ? 0 : .85f*Ease(1.5f,9f,fallSpeed));
        return immersion;
    }

    void DrawInterior(float age, float seconds, float surfaceAboveEye, bool reduced)
    {
        if (seconds <= 0 || kind == FloorEnding.Space || kind == FloorEnding.Snow) return;
        if (!fullyUnder && surfaceAboveEye < -LineWidth-EyeMargin)
        {
            interior.enabled = false;
            return;
        }
        if (!interior.enabled && kind == FloorEnding.Sand)
        {
            Vector3 point = head.position;
            sandShade = .77f + .23f*Mathf.Sin(point.x*13 + Mathf.Sin(point.z*3.1f)*2.2f);
        }
        fullyUnder |= surfaceAboveEye >= EyeMargin;
        Color color = Color.black;
        float brightness = 1;
        float depth = Mathf.Max(0,surfaceAboveEye)/eyeHeight;
        switch (kind)
        {
            case FloorEnding.Sand:
                color = SandColor;
                brightness = sandShade*Mathf.Exp(-1.4f*depth);
                break;
            case FloorEnding.Lava:
                color = new Color(.48f,.019f,.007f);
                brightness = Mathf.Exp(-.5f*depth);
                break;
            case FloorEnding.Water:
                // The plunge leaves white water that clears over the first 1.6 s below the surface.
                float foam = reduced ? 0 : .85f*(1-Ease(.3f,1.6f,seconds));
                color = Color.Lerp(new Color(.032f,.24f,.29f), new Color(.80f,.92f,.95f), foam);
                brightness = Mathf.Exp(-.22f*depth);
                break;
        }
        properties.Clear();
        properties.SetColor(Tint,color);
        properties.SetFloat(Age,age);
        properties.SetFloat("_Brightness",brightness);
        properties.SetVector(InteriorPlane,new Vector4(head.position.y+surfaceAboveEye,LineWidth,
            fullyUnder ? 1 : 0,kind == FloorEnding.Water ? 1 : 0));
        interior.SetPropertyBlock(properties);
        interior.enabled = true;
    }

    void Paint(MeshRenderer renderer, int role, Color tint, float opacity)
    {
        renderer.enabled = opacity > .001f;
        if (!renderer.enabled) return;
        properties.SetFloat(Role,role); properties.SetColor(Tint,tint);
        properties.SetFloat(Opacity,Mathf.Clamp01(opacity)); renderer.SetPropertyBlock(properties);
    }

    internal Vector3 SplashPosition()
    {
        Vector3 direction = Vector3.ProjectOnPlane(head.forward,Vector3.up);
        if (direction.sqrMagnitude < .01f) direction = world.forward;
        Vector3 center = head.position + direction.normalized*.72f;
        center.y = floor.transform.position.y;
        return center;
    }

    internal void Splash(Vector3 point, bool reduced)
    {
        if (!splashPrefab) return;
        if (!splash)
        {
            splash = Object.Instantiate(splashPrefab,world);
            splash.name = "Water contact splash";
        }
        splash.transform.SetPositionAndRotation(point,world.rotation*splashPrefab.transform.localRotation);
        splash.transform.localScale = splashPrefab.transform.localScale*(reduced ? .5f : 1);
        splash.SetActive(true);
        var system = splash.GetComponent<ParticleSystem>();
        if (system)
        {
            system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
        }
    }

    internal void SetVeil(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        veil.enabled = color.a > .001f;
        if (!veil.enabled) return;
        properties.Clear(); properties.SetColor(Tint,color); veil.SetPropertyBlock(properties);
    }

    internal void SetTrackingPaused(bool paused)
    {
        world.gameObject.SetActive(!paused);
        if (veil) veil.gameObject.SetActive(!paused);
    }

    internal void Hide()
    {
        if (veil)
        {
            veil.gameObject.SetActive(true);
            veil.enabled = false;
            if (world) veil.transform.SetParent(world,false);
        }
        if (interior) interior.enabled = false;
        if (splash) { splash.SetActive(false); }
        if (world) world.gameObject.SetActive(false);
        head = null;
    }

    internal void Dispose()
    {
        Hide();
        // During scene unload Unity may have destroyed the chamber before the sequence.
        if (world) Object.Destroy(world.gameObject);
        else if (veil) Object.Destroy(veil.gameObject);
        Object.Destroy(surfaceMaterial); Object.Destroy(backgroundMaterial); Object.Destroy(fadeMaterial);
        Object.Destroy(interiorMaterial); Object.Destroy(bubbleMaterial); Object.Destroy(dustMaterial);
        foreach (Mesh owned in ownedMeshes) Object.Destroy(owned);
    }

    internal static float Ease(float from, float to, float value) => Smooth(Mathf.InverseLerp(from,to,value));
    static float Smooth(float value) => Mathf.SmoothStep(0,1,value);
}
