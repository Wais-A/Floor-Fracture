using System.Collections.Generic;
using UnityEngine;

// All facets of a feature share one mesh. A cloud of embers costs one renderer.
internal static class FallMeshes
{
    static readonly Vector2[] Rim = {
        new Vector2(-.76f,-1), new Vector2(.76f,-1), new Vector2(1,-.76f), new Vector2(1,.76f),
        new Vector2(.76f,1), new Vector2(-.76f,1), new Vector2(-1,.76f), new Vector2(-1,-.76f)
    };

    static Vector3 RimPoint(int side, float y, float inset = 1f)
    {
        Vector2 point = Rim[side % Rim.Length] * inset;
        return new Vector3(point.x,y,point.y);
    }

    internal static Mesh Shaft(float depth)
    {
        var mesh = new Builder();
        // Include the change of slope so the walls and their ledges follow the same profile.
        float[] levels = { 0f, -.20f, -.45f, -.75f, -1.15f, -1.45f, -FallScenery.ThroatDepth, -depth - 1f };
        for (int i = 0; i < Rim.Length; i++)
        {
            Color face = Gray(.64f + .24f * Mathf.Abs(Mathf.Sin(i * 1.8f)));
            for (int j = 1; j < levels.Length; j++)
                mesh.Quad(RimPoint(i,levels[j]),RimPoint(i+1,levels[j]),
                    RimPoint(i+1,levels[j-1]),RimPoint(i,levels[j-1]),face,i);
        }
        return mesh.Build("Chamber walls");
    }

    internal static Mesh Landmarks(float depth)
    {
        var mesh = new Builder();
        float distance = .02f;
        for (int row = 0; distance <= depth; row++)
        {
            float y = -distance;
            bool wide = row % 3 == 0;
            // A narrow bevel at the mouth leaves the glass silhouette readable.
            float inset = Mathf.Lerp(.984f, wide ? .89f : .955f, Mathf.SmoothStep(0,1,Mathf.InverseLerp(1f,6f,distance)));
            float thickness = wide ? .16f : .065f;
            for (int side = 0; side < Rim.Length; side++)
            {
                // Each ledge has a cap for a downward glance and a front edge at eye level.
                mesh.Quad(RimPoint(side,y,.997f),RimPoint(side+1,y,.997f),
                    RimPoint(side+1,y,inset),RimPoint(side,y,inset),Gray(wide ? .92f : .65f),side);
                mesh.Quad(RimPoint(side,y-thickness,inset),RimPoint(side+1,y-thickness,inset),
                    RimPoint(side+1,y,inset),RimPoint(side,y,inset),Gray(wide ? .73f : .46f),side);
                mesh.Quad(RimPoint(side,y-thickness-.045f,.997f),RimPoint(side+1,y-thickness-.045f,.997f),
                    RimPoint(side+1,y-thickness,inset),RimPoint(side,y-thickness,inset),Gray(.19f),side);
            }
            // Keep the close lip ledges readable at release. Deeper, separate the bands as speed builds.
            distance += distance < 5f ? .54f : distance < 18f ? 1.55f : wide ? 3.35f : 2.45f;
        }
        return mesh.Build("Fixed ledges from rim to landing");
    }

    internal static Mesh Disk()
    {
        var mesh = new Builder();
        int mid = mesh.Vertex(Vector3.zero, Color.white, new Vector2(.5f,.5f), 0);
        const int sides = 40;
        for (int i = 0; i <= sides; i++)
        {
            var point = Ring(i * 2 * Mathf.PI / sides);
            int edge = mesh.Vertex(point, Color.white, new Vector2(.5f + .5f * point.x, .5f + .5f * point.z), 0);
            if (i > 0) mesh.Triangle(mid, edge, edge - 1);
        }
        return mesh.Build("Liquid or sand surface");
    }

    internal static Mesh Box()
    {
        var m = new Builder();
        Vector3[] a = {new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,-1,1),new Vector3(-1,-1,1)};
        for (int i = 0; i < 4; i++)
        {
            var v = a[i]; var w = a[(i+1) % 4];
            m.Quad(v, w, w + Vector3.up * 2, v + Vector3.up * 2, Color.white, 0);
        }
        m.Quad(a[0], a[1], a[2], a[3], Color.white, 0);
        m.Quad(a[0]+Vector3.up*2, a[3]+Vector3.up*2, a[2]+Vector3.up*2, a[1]+Vector3.up*2, Color.white, 0);
        return m.Build("View enclosure");
    }

    internal static Mesh Flames()
    {
        var m = new Builder();
        var random = new System.Random(417);
        const int count = 25;
        for (int i = 0; i < count; i++)
        {
            float phase = i / (float)count;
            float angle = (phase + .012f * Rand(random)) * Mathf.PI * 2;
            Vector3 center = Ring(angle) * (1.32f + .35f * Rand(random));
            float height = .65f + 1.05f * Rand(random);
            float width = .13f + Rand(random) * .15f;
            Vector3 tip = center + Vector3.up * height + Ring(angle + .65f) * width;
            const int sides = 4;
            Color baseColor = new Color(.9f,.19f,.015f);
            for (int j=0; j<sides; j++)
            {
                Vector3 p = center + Ring(angle + j * 2 * Mathf.PI/sides)*width;
                Vector3 q = center + Ring(angle + (j+1) * 2 * Mathf.PI/sides)*width;
                Vector3 shoulder = Vector3.up * height * .25f;
                Color facet = baseColor * (.53f + .43f * Rand(random)); facet.a = 1;
                m.Quad(p, q, q+shoulder, p+shoulder, facet, phase);
                int a = m.Vertex(p+shoulder, facet, new Vector2(0,.7f), phase);
                int b = m.Vertex(q+shoulder, facet, new Vector2(1,.7f), phase);
                Color tipColor = new Color(1,.68f,.09f);
                int c = m.Vertex(tip, tipColor, Vector2.up, phase);
                m.Triangle(a,b,c);
            }
        }
        return m.Build("Flame tongues");
    }

    internal static Mesh Motes(FloorEnding ending)
    {
        var m = new Builder();
        var random = new System.Random(110 + (int)ending);
        int count = ending == FloorEnding.Space ? 264 : (ending == FloorEnding.Snow ? 96 : 54);
        for (int i = 0; i < count; i++)
        {
            float phase = Rand(random), angle = Rand(random) * Mathf.PI * 2;
            float radius = .50f + 1.05f * Rand(random);
            float size = .012f + .024f * Rand(random);
            float y = (phase - .45f) * 4.3f + 1.4f;
            Color color = new Color(.59f,.87f,.94f);
            float elongation = 1;
            if (ending == FloorEnding.Space)
            {
                // Place stars throughout a vertical volume. Near stars subtend more angle
                // per metre of descent than the distant field.
                radius = 3f + 34f * Rand(random);
                y = (phase - .5f)*72f;
                size = radius * (.003f + .004f*Rand(random));
                if (i % 11 == 0) size *= 1.7f;
                color = Color.Lerp(new Color(.59f,.74f,1), new Color(1,.91f,.68f), Rand(random));
            }
            else if (ending == FloorEnding.Lava)
            {
                color = new Color(1, .31f + .38f*Rand(random), .04f);
                elongation = 1.8f;
            }
            else if (ending == FloorEnding.Sand)
            {
                radius = 1.42f + Rand(random)*.21f;
                color = new Color(.91f,.65f,.31f); elongation = 4;
            }
            else if (ending == FloorEnding.Snow)
                color = Color.Lerp(new Color(.59f,.76f,.87f), Color.white, phase);
            Speck(m, Ring(angle)*radius + Vector3.up*y, size, elongation, color, .82f, phase);
        }
        Mesh result = m.Build(ending + " particles");
        Bounds bounds = result.bounds;
        bounds.Expand(ending == FloorEnding.Space ? new Vector3(0,144,0) : new Vector3(9,9,5)); // Motion happens in the vertex shader.
        result.bounds = bounds;
        return result;
    }

    // Height of the still-air column the shader wraps around the eye (Chamber.shader, role 13).
    internal const float DustColumn = 8f;

    internal static Mesh Dust()
    {
        var m = new Builder();
        var random = new System.Random(379);
        for (int i = 0; i < 150; i++)
        {
            float phase = Rand(random), angle = Rand(random) * Mathf.PI * 2;
            // Beyond arm's reach so nothing crosses the face. Walls hide any speck past a narrow shaft.
            float radius = .6f + .8f * Rand(random);
            float size = .005f + .009f * Rand(random);
            Speck(m, Ring(angle)*radius + Vector3.up*(phase-.5f)*DustColumn, size, 1, Gray(.6f + .4f*Rand(random)), .5f, phase);
        }
        Mesh result = m.Build("Fall dust");
        Bounds bounds = result.bounds;
        bounds.Expand(new Vector3(0,DustColumn,0)); // Wrapping and streaks happen in the vertex shader.
        result.bounds = bounds;
        return result;
    }

    internal const float ColumnHalfWidth = .8f;

    // One square brick column from the bend into the landing. Its uv is in metres around and down
    // the column, so the shared running bond matches the walls. The top starts half a metre above
    // the bend, inside the wall, so no top face or seam can show.
    internal static Mesh Column()
    {
        var m = new Builder();
        const float h = ColumnHalfWidth, top = -FallScenery.ThroatDepth + .5f, bottom = -FallScenery.ShaftDepth - 1f;
        Vector3[] corners = { new Vector3(-h,0,-h), new Vector3(h,0,-h), new Vector3(h,0,h), new Vector3(-h,0,h) };
        for (int side = 0; side < 4; side++)
        {
            Vector3 a = corners[side], b = corners[(side+1)%4];
            float u = side * 2*h;
            // Alternate face shades read as corners under the unlit shader, as the wall facets do.
            Color shade = Gray(side % 2 == 0 ? .86f : .68f);
            int first = m.Vertex(a + Vector3.up*bottom, shade, new Vector2(u, -bottom), 0);
            m.Vertex(b + Vector3.up*bottom, shade, new Vector2(u + 2*h, -bottom), 0);
            m.Vertex(b + Vector3.up*top, shade, new Vector2(u + 2*h, -top), 0);
            m.Vertex(a + Vector3.up*top, shade, new Vector2(u, -top), 0);
            m.Triangle(first,first+1,first+2); m.Triangle(first,first+2,first+3);
        }
        return m.Build("Brick column");
    }

    // A small double pyramid. Its top vertex has uv.y = 1 and its bottom vertex uv = 0, which the shader can stretch.
    static void Speck(Builder m, Vector3 center, float size, float elongation, Color color, float bottomShade, float phase)
    {
        int top = m.Vertex(center+Vector3.up*size*elongation, color, Vector2.up, phase);
        int bottom = m.Vertex(center-Vector3.up*size*elongation, color*bottomShade, Vector2.zero, phase);
        int first = top+2;
        for (int j=0; j<4; j++)
            m.Vertex(center+Ring(j*Mathf.PI*.5f)*size,color,new Vector2(.5f,.5f),phase);
        for (int j=0; j<4; j++)
        {
            int a=first+j, b=first+(j+1)%4; m.Triangle(top,a,b); m.Triangle(bottom,b,a);
        }
    }

    static float Rand(System.Random random) => (float)random.NextDouble();
    static Vector3 Ring(float a) => new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
    static Color Gray(float x) => new Color(x,x,x,1);

    sealed class Builder
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>(), seeds = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        internal int Vertex(Vector3 p, Color color, Vector2 uv, float phase)
        {
            positions.Add(p); colors.Add(color); uvs.Add(uv); seeds.Add(new Vector2(phase,0));
            return positions.Count-1;
        }
        internal void Triangle(int a, int b, int c)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }
        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float seed)
        {
            int first = Vertex(a,color,Vector2.zero,seed);
            Vertex(b,color,Vector2.right,seed); Vertex(c,color,Vector2.one,seed); Vertex(d,color,Vector2.up,seed);
            Triangle(first,first+1,first+2); Triangle(first,first+2,first+3);
        }
        internal Mesh Build(string name)
        {
            var result=new Mesh {name = name};
            result.SetVertices(positions); result.SetColors(colors); result.SetUVs(0,uvs); result.SetUVs(1,seeds);
            result.SetTriangles(triangles,0); result.RecalculateNormals(); result.RecalculateBounds(); return result;
        }
    }
}
