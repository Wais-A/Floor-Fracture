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
        Mouth = Shader.PropertyToID("_Mouth"), MouthRight = Shader.PropertyToID("_MouthRight");
    // Four seconds under gravity leaves more than three seconds of walls after a standing eye enters.
    internal const float ShaftDepth = 80f;
    // A player looking into the opening from an adjacent pane can see the distant landing.
    const float WideningPerMetre = .72f;
    static readonly Color[] wallTints = {
        new Color(.19f,.28f,.30f), new Color(.25f,.15f,.12f), new Color(.18f,.17f,.25f),
        new Color(.18f,.19f,.26f), new Color(.44f,.31f,.19f), new Color(.26f,.40f,.53f)
    };
    static readonly Color[] skyTints = {
        new Color(.018f,.049f,.060f), new Color(.040f,.019f,.014f), new Color(.027f,.019f,.048f),
        new Color(.009f,.012f,.030f), new Color(.061f,.039f,.021f), new Color(.16f,.25f,.36f)
    };

    readonly Transform world;
    readonly Material surfaceMaterial, fadeMaterial;
    readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    readonly MeshRenderer walls, landmarks, background, floor, nativeWater, pillars, motes, opening;
    MeshRenderer veil;
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly Mesh[] moteMeshes = new Mesh[6];
    readonly Mesh boxMesh, crystalMesh, flameMesh;
    readonly GameObject splashPrefab, replacementFire;
    GameObject splash;
    FloorEnding kind;
    Transform head;
    float eyeHeight, startRadius, radius;
    Vector3 mouthRight, entryWorld, entryLocal;
    bool hasFloorApertures, stencilEnabled;

    internal FallScenery(Material waterOverride, GameObject splashOverride, GameObject fireOverride)
    {
        world = new GameObject("Fall chamber").transform;
        surfaceMaterial = new Material(Resources.Load<Shader>("Endings/Chamber"));
        surfaceMaterial.SetFloat("_WideningPerMetre",WideningPerMetre);
        fadeMaterial = new Material(Resources.Load<Shader>("Endings/ViewFade"));
        boxMesh = Own(FallMeshes.Box());
        Mesh disk = Own(FallMeshes.Disk());
        crystalMesh = Own(FallMeshes.Spires(false)); flameMesh = Own(FallMeshes.Spires(true));
        walls = Part("Shaft walls", Own(FallMeshes.Shaft(ShaftDepth)), surfaceMaterial);
        landmarks = Part("Shaft landmarks", Own(FallMeshes.Landmarks(ShaftDepth)), surfaceMaterial);
        background = Part("Chamber darkness", boxMesh, surfaceMaterial);
        floor = Part("Hazard surface", disk, surfaceMaterial);
        var native = waterOverride ? waterOverride : Resources.Load<Material>("Endings/MetaFloorZone/materials/Water");
        if (native && native.HasProperty("_WaterNormal"))
            surfaceMaterial.SetTexture("_WaterNormal",native.GetTexture("_WaterNormal"));
        if (native && native.shader && native.shader.isSupported) nativeWater = Part("Floor Zone water ripples", disk, native);
        pillars = Part("Flames or crystals", crystalMesh, surfaceMaterial);
        motes = Part("Particles", null, surfaceMaterial);
        opening = Part("Last light overhead", disk, surfaceMaterial);
        veil = Part("View fade", boxMesh, fadeMaterial);
        splashPrefab = splashOverride ? splashOverride : Resources.Load<GameObject>("Endings/MetaFloorZone/prefabs/Splash");
        if (fireOverride)
        {
            replacementFire = Object.Instantiate(fireOverride, world);
            replacementFire.SetActive(false);
        }
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

    internal void Begin(Transform viewer, FloorEnding chosen, Vector3 center, Vector3 entry, float width,
        float yaw, float chamberRadius, bool throughOpenPanes)
    {
        head = viewer; kind = chosen;
        world.SetPositionAndRotation(center, Quaternion.Euler(0,yaw,0));
        entryWorld = entry;
        entryLocal = world.InverseTransformPoint(entry);
        hasFloorApertures = throughOpenPanes;
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
        pillars.GetComponent<MeshFilter>().sharedMesh = kind == FloorEnding.Lava ? flameMesh : crystalMesh;
        if (kind != FloorEnding.Crystal)
        {
            if (!moteMeshes[(int)kind]) moteMeshes[(int)kind] = Own(FallMeshes.Motes(kind));
            motes.GetComponent<MeshFilter>().sharedMesh = moteMeshes[(int)kind];
        }
        world.gameObject.SetActive(true);
        if (!veil) veil = Part("View fade",boxMesh,fadeMaterial);
        veil.transform.SetParent(head, false);
        veil.transform.localPosition = Vector3.zero;
        veil.transform.localRotation = Quaternion.identity;
        veil.transform.localScale = Vector3.one * 1.5f;
        SetVeil(Color.black, 0);
    }

    // Returns the submersion factor at the viewer's actual height.
    internal float Draw(float age, float progress, bool reduced, float travel, out bool entered)
    {
        bool visible = !reduced || age >= .32f;
        // The chamber and its destination are one moving world until the feet reach the surface.
        // Only the upper metre of the throat widens as the lip draws near.
        float top = reduced ? 5f : travel;
        // Above the board, all broken panes are windows into this world. Once the rim
        // moves, clip it to the falling player's moving entry until the eyes are inside.
        bool useStencil = hasFloorApertures && !reduced && top <= .001f;
        if (stencilEnabled != useStencil)
        {
            stencilEnabled = useStencil;
            surfaceMaterial.SetInt("_PortalComparison",(int)(useStencil ? CompareFunction.Equal : CompareFunction.Always));
        }
        float mouthRadius = Mathf.Lerp(startRadius,Mathf.Max(startRadius,Mathf.Min(radius,.77f)),Ease(0,1.35f,top));
        float surfaceY = reduced ? 0f : travel-ShaftDepth;
        Color wallColor = wallTints[(int)kind], sky = skyTints[(int)kind];
        float wallAlpha = 1, particleAlpha = Ease(0,.17f,progress);
        float floorAlpha = 1, pillarsAlpha = 0, light = 0;
        int floorRole = 0;
        Color surfaceColor = wallColor*.70f;
        float immersion = 0;
        switch (kind)
        {
            case FloorEnding.Water:
                if (!reduced)
                    surfaceY += (eyeHeight+.40f)*Ease(.07f,.79f,progress);
                immersion = reduced ? Ease(.50f,.73f,progress) :
                    Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.09f,.14f,world.position.y+surfaceY-head.position.y));
                floorRole = 2; surfaceColor = new Color(.035f,.20f,.22f);
                particleAlpha *= immersion;
                break;
            case FloorEnding.Lava:
                if (!reduced) surfaceY += Mathf.Max(0,eyeHeight-.75f)*Ease(.10f,.68f,progress);
                floorRole = 3;
                pillarsAlpha = Ease(0,.12f,progress);
                break;
            case FloorEnding.Crystal:
                floorRole = 10;
                pillarsAlpha = Ease(0,.10f,progress);
                break;
            case FloorEnding.Space:
                floorRole = 11;
                wallAlpha = floorAlpha = 1-Ease(0,.15f,progress);
                particleAlpha = Ease(0,.13f,progress)*(1-Ease(.78f,.97f,progress));
                break;
            case FloorEnding.Sand:
                if (!reduced) surfaceY += Mathf.Max(0,eyeHeight-.14f)*Ease(.06f,.79f,progress);
                float lastLight = Ease(.62f,.87f,progress);
                wallColor = Color.Lerp(wallColor, Color.black, lastLight);
                sky = Color.Lerp(sky,Color.black,lastLight);
                surfaceColor = Color.Lerp(new Color(.72f,.49f,.24f), Color.black,lastLight);
                particleAlpha *= 1-lastLight;
                floorRole = 4;
                light = Ease(.04f,.30f,progress)*(1-Ease(.84f,.99f,progress));
                break;
            case FloorEnding.Snow:
                floorRole = 12;
                sky = Color.Lerp(sky,new Color(.53f,.66f,.77f),Ease(.06f,.64f,progress));
                break;
        }
        float level = world.position.y + surfaceY;
        float motion = reduced ? .10f : 1;
        properties.Clear();
        properties.SetFloat(Theme,(int)kind); properties.SetFloat(Age,age);
        properties.SetFloat(Reveal,progress); properties.SetFloat(Level,level); properties.SetFloat(Motion,motion);
        float rimY = world.position.y+top;
        entered = reduced ? visible : rimY >= head.position.y;
        properties.SetVector(Mouth,new Vector4(entryWorld.x,rimY,entryWorld.z,mouthRadius));
        properties.SetVector(MouthRight,new Vector4(mouthRight.x,mouthRight.z,0,reduced || useStencil ? 0 : 1));
        // The shaft stays lit. Only the view past its walls gradually loses the room light.
        float overhead = reduced ? 1 : Ease(.15f,26f,rimY-head.position.y);
        Paint(background,kind == FloorEnding.Space ? 8 : 0,sky,visible ? overhead : 0);
        walls.transform.localPosition = landmarks.transform.localPosition = Vector3.up*top;
        Paint(walls,1,wallColor,visible ? wallAlpha : 0);
        Color ledgeColor = Color.Lerp(wallColor,new Color(.84f,.88f,.85f),.64f);
        Paint(landmarks,9,ledgeColor,visible ? wallAlpha : 0);
        floor.transform.localPosition = new Vector3(0,surfaceY,0);
        Paint(floor,floorRole,surfaceColor,visible ? floorAlpha : 0);
        if (nativeWater)
        {
            // The source graph uses transparency and cannot apply our per-eye opening mask.
            // Its overlay joins the already visible procedural water once both eyes are inside.
            nativeWater.enabled = visible && !useStencil && kind == FloorEnding.Water && (reduced || rimY > head.position.y+.12f);
            nativeWater.transform.localPosition = floor.transform.localPosition + Vector3.up*.009f;
        }
        pillars.transform.localPosition = entryLocal + Vector3.up*(kind == FloorEnding.Lava ? surfaceY-.11f : 0);
        Paint(pillars,kind == FloorEnding.Lava ? 7 : 5,Color.white, visible ? pillarsAlpha : 0);
        if (replacementFire)
        {
            bool lit = visible && kind == FloorEnding.Lava && pillarsAlpha > .20f;
            if (lit != replacementFire.activeSelf) replacementFire.SetActive(lit);
            replacementFire.transform.localPosition = entryLocal + new Vector3(1.25f,surfaceY,0);
        }
        Paint(motes,6,Color.white,visible && kind != FloorEnding.Crystal ? particleAlpha : 0);
        opening.transform.localPosition = entryLocal + new Vector3(0,eyeHeight+3.35f,0);
        opening.transform.localScale = Vector3.one * Mathf.Lerp(1.05f,.28f,Ease(.4f,.88f,progress));
        Paint(opening,0,new Color(1,.83f,.54f),visible ? light : 0);
        return immersion;
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

    internal void Hide()
    {
        if (veil) { veil.enabled = false; veil.transform.SetParent(world,false); }
        if (splash) { splash.SetActive(false); }
        if (replacementFire) replacementFire.SetActive(false);
        world.gameObject.SetActive(false); head = null;
    }

    internal void Dispose()
    {
        Hide();
        Object.Destroy(world.gameObject); Object.Destroy(surfaceMaterial); Object.Destroy(fadeMaterial);
        foreach (Mesh owned in ownedMeshes) Object.Destroy(owned);
    }

    internal static float Ease(float from, float to, float value) => Smooth(Mathf.InverseLerp(from,to,value));
    static float Smooth(float value) => Mathf.SmoothStep(0,1,value);
}
