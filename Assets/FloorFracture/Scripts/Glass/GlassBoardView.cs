using UnityEngine;

namespace FloorFracture
{
    public sealed class GlassBoardView : MonoBehaviour
    {
        BoardState model;
        GlassTile[] tiles;
        GlassTile moving;
        MeshRenderer occupancy, fog, holes;
        ShardPool shards;
        Mesh cutouts, fogPlane, boundary, labelBackground;
        Material bodyMaterial, paneMaterial, maskMaterial, fogMaterial, shardMaterial;
        TextMesh readout;
        Transform labelPlate;
        bool clockPositionSet;
        float readoutWidth=.68f, readoutHeight=.23f;
        ulong holeBits;
        int practiceHole=-1;
        string lastText;
        public Transform Board => transform;
        internal bool HasOpenPanes => isActiveAndEnabled && holeBits != 0;
        internal float ChamberRadius
        {
            get
            {
                float extent = Mathf.Max(model.Width,model.Height)*model.Pitch*.5f;
                return Mathf.Max(1.48f, extent/.76f+.4f);
            }
        }
        internal float PaneWidth => model.Pitch;

        public void Build(BoardState board,Pose pose)
        {
            model=board; transform.SetPositionAndRotation(pose.position+Vector3.up*.009f,pose.rotation);
            var template=Resources.Load<GameObject>("GlassTile");
            var prefab=template?template.GetComponent<GlassTile>():null;
            if(!prefab) throw new System.InvalidOperationException("Missing Resources/GlassTile prefab");
            paneMaterial=GlassTile.MakeMaterial("FloorFractureSurface");
            bodyMaterial = new Material(paneMaterial);
            bodyMaterial.SetFloat("_BlockBody",1);
            bodyMaterial.SetInt("_Cull",(int)UnityEngine.Rendering.CullMode.Back);
            ShowLiftedBlocks(false);
            maskMaterial=GlassTile.MakeMaterial("FloorFractureMask");
            fogMaterial=GlassTile.MakeMaterial("FloorFractureFog");
            foreach (var material in new[] {paneMaterial, bodyMaterial, maskMaterial, fogMaterial})
                material.SetFloat("_MappedFloorY", pose.position.y);
            tiles=new GlassTile[board.Count];
            float paneScale=board.Pitch/GlassGeometry.AuthoredPitch;
            var fittedScale=new Vector3(paneScale,1,paneScale);
            for(int i=0;i<tiles.Length;i++)
            {
                if (!board.Layout.Contains(i)) continue;
                var t=Instantiate(prefab,transform); t.name="Glass "+(i%board.Width)+", "+(i/board.Width);
                t.transform.localPosition=GlassGeometry.Ground(board.Center(i));
                t.transform.localScale=fittedScale; t.Initialize(paneMaterial,bodyMaterial,paneScale); tiles[i]=t;
            }
            moving=Instantiate(prefab,transform); moving.name="Sliding pane"; moving.transform.localScale=fittedScale;
            moving.Initialize(paneMaterial,bodyMaterial,paneScale); moving.gameObject.SetActive(false);
            occupancy=GlassTile.Part(transform,"Occupied square",GlassGeometry.Ring,paneMaterial);
            occupancy.transform.localScale=fittedScale;
            GlassTile.Tint(occupancy,new Color(.90f,1f,.73f)); occupancy.enabled=false;
            shardMaterial=GlassTile.MakeMaterial("FloorFractureDepth");
            cutouts=new Mesh{name="Open floor apertures"};
            holes=GlassTile.Part(transform,"Opening stencil",cutouts,maskMaterial);
            shards=gameObject.AddComponent<ShardPool>(); shards.Initialize(shardMaterial,paneScale);
            var b=new GlassGeometry.Builder();
            foreach (var edge in board.Layout.Perimeter(board.Pitch)) b.Stroke(edge.a,edge.b,.012f,.006f);
            boundary=b.Create("Board perimeter"); var border=GlassTile.Part(transform,"Perimeter",boundary,paneMaterial);
            GlassTile.Tint(border,new Color(.61f,.82f,.85f,.96f));
            b=new GlassGeometry.Builder();
            float half=board.Pitch*.5f;
            for (int i=0;i<tiles.Length;i++)
            {
                if (!tiles[i]) continue;
                var p=board.Center(i);
                b.Quad(new(p.x-half,.052f,p.y-half),new(p.x+half,.052f,p.y-half),
                    new(p.x+half,.052f,p.y+half),new(p.x-half,.052f,p.y+half),Color.white,Color.white);
                // Keep one continuous fog bank across the connected panes.
                for (int v=b.Uv.Count-4; v<b.Uv.Count; v++)
                    b.Uv[v]=new Vector2((i%board.Width+b.Uv[v].x)/board.Width,(i/board.Width+b.Uv[v].y)/board.Height);
            }
            fogPlane=b.Create("Fog bank"); fog=GlassTile.Part(transform,"Low fog",fogPlane,fogMaterial); fog.enabled=false;
            readout=CreateReadout(); ClearTransient(); RebuildHoles(0);
        }
        TextMesh CreateReadout()
        {
            var go=new GameObject("Countdown and score"); go.transform.SetParent(transform,false);
            var text=go.AddComponent<TextMesh>(); text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var letters=text.GetComponent<MeshRenderer>();
            letters.sharedMaterial=text.font.material;
            letters.sortingOrder=2;
            text.fontSize=96; text.characterSize=.026f; text.anchor=TextAnchor.LowerCenter; text.alignment=TextAlignment.Center;
            var b=new GlassGeometry.Builder();
            b.Quad(new(-.5f,-.5f,0),new(.5f,-.5f,0),new(.5f,.5f,0),new(-.5f,.5f,0),Color.white,Color.white);
            labelBackground=b.Create("Readout backing");
            var plate=GlassTile.Part(go.transform,"Contrast backing",labelBackground,GlassTile.Surface);
            // Both draws follow the transparent room floor. Drawing the backing first in
            // the scene lets the bricks paint over it and leaves white numbers on mortar.
            plate.sortingOrder=1; GlassTile.Tint(plate,new Color(.017f,.055f,.067f,.94f)); labelPlate=plate.transform;
            text.color=new Color(.91f,1,.94f); return text;
        }
        public void AnchorReadout(Transform head)
        {
            if(!head||!readout) return;
            var local=transform.InverseTransformPoint(head.position);
            Vector3 bearing=local.x*local.x+local.z*local.z<.04f?transform.InverseTransformDirection(head.forward):new Vector3(-local.x,0,-local.z);
            float x=model.Width*model.Pitch*.5f, z=model.Height*model.Pitch*.5f;
            var p=Mathf.Abs(bearing.x)/x>Mathf.Abs(bearing.z)/z?
                new Vector3(Mathf.Sign(bearing.x)*(x+.08f),.11f,0):new Vector3(0,.11f,Mathf.Sign(bearing.z)*(z+.08f));
            // On a long strip put the sign on the side ahead of the player. The far end
            // can be many metres away, even though the guaranteed move is a single step.
            var onFloor = new Vector2(local.x,local.z);
            if (Vector2.Distance(onFloor,new Vector2(p.x,p.z))>3.1f)
            {
                var gaze=transform.InverseTransformDirection(head.forward);
                if (x>=z)
                    p=new Vector3(Mathf.Clamp(local.x+Mathf.Sign(bearing.x)*1.15f,-x,x),.11f,
                        Mathf.Sign(Mathf.Abs(gaze.z)>.25f?gaze.z:local.z)*(z+.08f));
                else
                    p=new Vector3(Mathf.Sign(Mathf.Abs(gaze.x)>.25f?gaze.x:local.x)*(x+.08f),.11f,
                        Mathf.Clamp(local.z+Mathf.Sign(bearing.z)*1.15f,-z,z));
            }
            readout.transform.localPosition=p; clockPositionSet=true;
            float distance=Vector3.Distance(p,local);
            readoutWidth=Mathf.Clamp(distance*.30f,.63f,1.05f);
            readoutHeight=Mathf.Clamp(distance*.11f,.22f,.36f);
            lastText=null;
        }
        public void SetReadout(string text,Transform head)
        {
            if(!readout) return;
            if(!clockPositionSet && head && text.Length>0) AnchorReadout(head);
            if(text!=lastText)
            {
                readout.text=text; lastText=text;
                labelPlate.gameObject.SetActive(text.Length>0);
                if(text.Length>0)
                {
                    var bounds=readout.GetComponent<MeshRenderer>().localBounds;
                    if(bounds.size.x>.001f&&bounds.size.y>.001f)
                    {
                        float scale=Mathf.Min(readoutWidth/bounds.size.x,readoutHeight/bounds.size.y);
                        readout.transform.localScale=Vector3.one*scale;
                        labelPlate.localPosition=bounds.center+Vector3.forward*(.003f/scale);
                        labelPlate.localScale=new Vector3(bounds.size.x+.048f/scale,bounds.size.y+.025f/scale,1);
                    }
                }
            }
            if(!head||text.Length==0) return;
            Vector3 away=readout.transform.position-head.position;
            if(away.sqrMagnitude>.001f) readout.transform.rotation=Quaternion.LookRotation(away,Vector3.up);
        }
        public void Draw(WavePlan wave,TileSample sample,double now,bool gentle,int practiceWarning=-1)
        {
            if(tiles==null) return;
            ulong bits=practiceHole>=0?1UL<<practiceHole:0UL;
            bool shown=wave!=null&&wave.ShowsWarnings(now);
            for(int i=0;i<tiles.Length;i++)
            {
                if (!tiles[i]) continue;
                if(!model.IsIntact(i)) bits|=1UL<<i;
                bool visible=(bits&(1UL<<i))==0;
                if(tiles[i].gameObject.activeSelf!=visible) tiles[i].gameObject.SetActive(visible);
                if(visible) tiles[i].ShowWarning((shown&&wave.IsBreaking(i))||i==practiceWarning,now,gentle);
            }
            if(bits!=holeBits) RebuildHoles(bits);
            var slide=model.Slider;
            moving.gameObject.SetActive(slide!=null);
            if(slide!=null)
            {
                moving.transform.localPosition=GlassGeometry.Ground(slide.CenterAt(now));
                moving.ShowWarning(shown&&wave.IsBreaking(slide.Tile),now,gentle);
            }
            occupancy.enabled=sample.Kind==SupportKind.Glass && sample.Index>=0 && sample.Index!=practiceHole &&
                (sample.IsSlider ? slide!=null && slide.Tile==sample.Index : model.IsIntact(sample.Index));
            if(occupancy.enabled)
            {
                var center=sample.IsSlider&&slide!=null?slide.CenterAt(now):model.Center(sample.Index);
                // Keep the whole ring on the supporting glass when a foot overlaps a seam.
                float inset=(GlassGeometry.Half-GlassGeometry.RingOuterRadius)*occupancy.transform.localScale.x;
                Vector2 contact=sample.Stance-center;
                contact.x=Mathf.Clamp(contact.x,-inset,inset);
                contact.y=Mathf.Clamp(contact.y,-inset,inset);
                occupancy.transform.localPosition=GlassGeometry.Ground(center+contact,.014f);
            }
            fog.enabled=wave!=null&&wave.Fog;
        }
        public void Shatter(int index)
        {
            if (model.Layout.Contains(index)) Shatter(index,model.Center(index));
        }
        internal Vector3 Shatter(int index, Vector2 center)
        {
            var local = GlassGeometry.Ground(center);
            shards.Drop(local,index,Time.unscaledTime);
            return transform.TransformPoint(local);
        }
        public void PracticeBreak(int index) { if (model.Layout.Contains(index)) { practiceHole=index; Shatter(index); } }
        public void ClearTransient() { practiceHole=-1; if(shards) shards.Clear(); }
        public void RestorePracticePane() { practiceHole=-1; }
        public void RevealLossAperture(int cell)
        {
            occupancy.enabled=false;
            if (!model.Layout.Contains(cell)) return;
            tiles[cell].gameObject.SetActive(false);
            RebuildHoles(holeBits | (1UL << cell));
        }

        internal void ShowLiftedBlocks(bool lifted, bool belowRim = false)
        {
            // At rest the stencil carries Meta's floor-depth test. Only a broken
            // pane can reveal glass below the physical floor. Once lifted, the
            // blocks have their own visible silhouette above the real floor.
            bodyMaterial.SetFloat("_DepthTestBlock",lifted && !belowRim ? 1f : 0f);
            bodyMaterial.SetInt("_PortalComparison", (int)(lifted ?
                UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Equal));
        }
        internal void ShowShardsBelowRim(bool belowRim)
        {
            var comparison = belowRim ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Equal;
            // Shards are visible through the aperture until the viewer passes below the rim.
            shardMaterial.SetInt("_PortalComparison", (int)comparison);
        }
        void RebuildHoles(ulong bits)
        {
            holeBits = bits;
            var maskGeometry = new GlassGeometry.Builder();
            var openings = new BoardLayout(model.Width, model.Height, bits);
            float pitch = model.Pitch;
            for (int row = 0; row < openings.Rows; row++)
            for (int col = 0; col < openings.Columns; col++)
            {
                if (!openings.HasPane(col, row)) continue;
                maskGeometry.Quad(GlassGeometry.Ground(openings.Corner(col, row, pitch)),
                    GlassGeometry.Ground(openings.Corner(col + 1, row, pitch)),
                    GlassGeometry.Ground(openings.Corner(col + 1, row + 1, pitch)),
                    GlassGeometry.Ground(openings.Corner(col, row + 1, pitch)), Color.white, Color.white);
            }

            maskGeometry.Write(cutouts);
            holes.enabled = bits!=0;
        }
        void OnDestroy()
        {
            foreach(Object asset in new Object[]{cutouts,fogPlane,boundary,labelBackground,bodyMaterial,paneMaterial,maskMaterial,shardMaterial,fogMaterial})
                if(asset) Destroy(asset);
        }
    }
}
