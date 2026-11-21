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
        Mesh cutouts, fogPlane, boundary;
        Material bodyMaterial, paneMaterial, maskMaterial, fogMaterial, shardMaterial;
        bool[] openPanes;
        int practiceHole=-1;
        public Transform Board => transform;
        internal bool HasOpenPanes => isActiveAndEnabled && holes && holes.enabled;
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
            openPanes=new bool[board.Count];
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
            holes.enabled=false;
        }

        public void Draw(WavePlan wave,TileSample sample,double now,bool gentle,int practiceWarning=-1)
        {
            if(tiles==null) return;
            bool openingsChanged=false;
            bool shown=wave!=null&&wave.ShowsWarnings(now);
            double revealAge=wave!=null ? now-wave.StartsAt : 0;
            for(int i=0;i<tiles.Length;i++)
            {
                if (!tiles[i]) continue;
                bool open=!model.IsIntact(i) || i==practiceHole;
                if (openPanes[i]!=open)
                {
                    openPanes[i]=open;
                    openingsChanged=true;
                }
                bool visible=!open;
                if(tiles[i].gameObject.activeSelf!=visible) tiles[i].gameObject.SetActive(visible);
                if(visible) tiles[i].ShowWarning((shown&&wave.IsBreaking(i))||i==practiceWarning,revealAge,gentle);
            }
            if(openingsChanged) RebuildHoles();
            var slide=model.Slider;
            moving.gameObject.SetActive(slide!=null);
            if(slide!=null)
            {
                moving.transform.localPosition=GlassGeometry.Ground(slide.CenterAt(now));
                moving.ShowWarning(shown&&wave.IsBreaking(slide.Tile),revealAge,gentle);
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
            openPanes[cell]=true;
            RebuildHoles();
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
        internal void SetFullFall(bool falling) => shards.FullFall = falling;

        internal void ShowShardsBelowRim(bool belowRim)
        {
            var comparison = belowRim ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Equal;
            // Shards are visible through the aperture until the viewer passes below the rim.
            shardMaterial.SetInt("_PortalComparison", (int)comparison);
        }
        void RebuildHoles()
        {
            var maskGeometry = new GlassGeometry.Builder();
            var layout=model.Layout;
            float pitch=model.Pitch;
            for (int i=0;i<openPanes.Length;i++)
            {
                if (!openPanes[i]) continue;
                int col=i%layout.Columns, row=i/layout.Columns;
                maskGeometry.Quad(GlassGeometry.Ground(layout.Corner(col,row,pitch)),
                    GlassGeometry.Ground(layout.Corner(col+1,row,pitch)),
                    GlassGeometry.Ground(layout.Corner(col+1,row+1,pitch)),
                    GlassGeometry.Ground(layout.Corner(col,row+1,pitch)), Color.white, Color.white);
            }
            maskGeometry.Write(cutouts);
            holes.enabled=maskGeometry.Vertices.Count>0;
        }
        void OnDestroy()
        {
            foreach(Object asset in new Object[]{cutouts,fogPlane,boundary,bodyMaterial,paneMaterial,maskMaterial,shardMaterial,fogMaterial})
                if(asset) Destroy(asset);
        }
    }
}
