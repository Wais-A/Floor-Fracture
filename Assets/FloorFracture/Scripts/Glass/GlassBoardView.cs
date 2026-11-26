using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FloorFracture
{
    public sealed class GlassBoardView : MonoBehaviour
    {
        BoardState model;
        GameSound sound;
        GlassTile[] tiles;
        IReadOnlyList<SlidingPane> displayedSliders;
        MeshRenderer occupancy, holes;
        FloorFog fog;
        OverheadLights lights;
        Lightning lightning;
        WaveHazards hazards;
        double hazardsShownAt, hazardsHiddenAt;
        ShardPool shards;
        Mesh cutouts, boundary;
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
        public bool ReducedMotion { get; set; }

        internal bool TryGetViewedOpening(Transform head, out Vector3 source)
        {
            source = default;
            if (!HasOpenPanes || !head) return false;
            var eye = transform.InverseTransformPoint(head.position);
            var direction = transform.InverseTransformDirection(head.forward);
            if (eye.y <= 0f || direction.y >= -.01f) return false;
            var toFloor = direction * (-eye.y / direction.y);
            var hit = new Vector2(eye.x + toFloor.x, eye.z + toFloor.z);
            int cell = model.IndexAt(hit);
            if (cell < 0 || !openPanes[cell]) return false;
            for (int i = 0; i < model.Sliders.Count; ++i)
            {
                var pane = model.Sliders[i];
                var center = tiles[pane.Tile].transform.localPosition;
                float half = pane.HalfSize;
                if (Mathf.Abs(hit.x - center.x) <= half && Mathf.Abs(hit.y - center.z) <= half) return false;
            }
            source = transform.TransformPoint(GlassGeometry.Ground(model.Center(cell), -.16f));
            return true;
        }

        public void Build(BoardState board,Pose pose,GameSound gameSound,float lampHeight)
        {
            sound=gameSound;
            model=board; transform.SetPositionAndRotation(pose.position,pose.rotation);
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
            // The cracks share the pane material, so the fog draws after them and dims them.
            fogMaterial.renderQueue=paneMaterial.renderQueue+1;
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
            occupancy=GlassTile.Part(transform,"Occupied square",GlassGeometry.Ring,paneMaterial);
            occupancy.transform.localScale=fittedScale;
            GlassTile.Tint(occupancy,new Color(.90f,1f,.73f)); occupancy.enabled=false;
            shardMaterial=GlassTile.MakeMaterial("FloorFractureDepth");
            cutouts=new Mesh{name="Open floor apertures"};
            holes=GlassTile.Part(transform,"Opening stencil",cutouts,maskMaterial);
            shards=gameObject.AddComponent<ShardPool>(); shards.Initialize(shardMaterial,paneScale);
            var b=new GlassGeometry.Builder();
            foreach (var edge in board.Layout.Perimeter(board.Pitch)) b.Stroke(edge.a,edge.b,.012f,GlassGeometry.MarkingHeight);
            boundary=b.Create("Board perimeter"); var border=GlassTile.Part(transform,"Perimeter",boundary,paneMaterial);
            GlassTile.Tint(border,new Color(.61f,.82f,.85f,.96f));
            fog=new GameObject("Floor fog").AddComponent<FloorFog>();
            fog.transform.SetParent(transform,false);
            fog.Build(board,fogMaterial);
            lights=new GameObject("Overhead lights").AddComponent<OverheadLights>();
            lights.transform.SetParent(transform,false);
            lights.Build(board,lampHeight-pose.position.y,sound,fogMaterial.renderQueue+1);
            lightning=new GameObject("Lightning").AddComponent<Lightning>();
            lightning.Build(transform,sound,lampHeight-pose.position.y);
            holes.enabled=false;
        }

        public void Draw(WavePlan wave,TileSample sample,double now,bool gentle)
        {
            if(tiles==null) return;
            bool openingsChanged=false;
            var sliders = model.Sliders;
            if (displayedSliders != sliders && displayedSliders != null)
                for (int i = 0; i < displayedSliders.Count; ++i)
                {
                    int tile = displayedSliders[i].Tile;
                    tiles[tile].transform.localPosition = GlassGeometry.Ground(model.Center(tile));
                }
            for(int i=0;i<tiles.Length;i++)
            {
                if (!tiles[i]) continue;
                bool open=!model.IsIntact(i) || i==practiceHole;
                if (openPanes[i]!=open)
                {
                    openPanes[i]=open;
                    openingsChanged=true;
                }
                var pane = model.SliderFor(i);
                bool visible = !open || pane != null;
                if(tiles[i].gameObject.activeSelf!=visible) tiles[i].gameObject.SetActive(visible);
                if (visible)
                {
                    if (pane != null) tiles[i].transform.localPosition = GlassGeometry.Ground(pane.CenterAt(now));
                    DrawWarning(tiles[i],i);
                }
            }
            if(openingsChanged) RebuildHoles();
            if (sliders.Count != 0 || displayedSliders != sliders)
            {
                for (int i = 0; i < CoveredPanes.Length; ++i)
                    maskMaterial.SetVector(CoveredPanes[i], CoveredPane(i,now));
            }
            displayedSliders = sliders;
            var slide = sample.IsSlider ? model.SliderFor(sample.Index) : null;
            occupancy.enabled=sample.Kind==SupportKind.Glass && sample.Index>=0 && sample.Index!=practiceHole &&
                (sample.IsSlider ? slide!=null : model.IsIntact(sample.Index));
            if(occupancy.enabled)
            {
                var center=sample.IsSlider&&slide!=null?slide.CenterAt(now):model.Center(sample.Index);
                // Keep the whole ring on the supporting glass when a foot overlaps a seam.
                float inset=(GlassGeometry.Half-GlassGeometry.RingOuterRadius)*occupancy.transform.localScale.x;
                Vector2 contact=sample.Stance-center;
                contact.x=Mathf.Clamp(contact.x,-inset,inset);
                contact.y=Mathf.Clamp(contact.y,-inset,inset);
                occupancy.transform.localPosition=GlassGeometry.Ground(center+contact);
            }
            void DrawWarning(GlassTile tile,int cell)
            {
                var group=wave?.BreakGroupFor(cell);
                bool warned=group!=null && group.ShowsWarning(now);
                tile.ShowWarning(warned, warned ? now-group.RevealStartsAt : 0, gentle,
                    warned ? group.RevealEndsAt-group.RevealStartsAt : .36);
                if (sound) tile.SoundWarning(sound, warned, cell,
                    warned ? group.Tiles.Count : 1, warned ? now-group.RevealStartsAt : 0,
                    warned ? group.RevealEndsAt-now : double.PositiveInfinity);
            }
        }
        // Last wave's sliders (up to 3) plus this wave's offers (up to 3).
        static readonly int[] CoveredPanes = Enumerable.Range(0,6)
            .Select(i => Shader.PropertyToID("_CoveredPane"+i)).ToArray();
        Vector4 CoveredPane(int index, double now)
        {
            if (index >= model.Sliders.Count) return Vector4.zero;
            var pane = model.Sliders[index];
            var center = pane.CenterAt(now);
            return new Vector4(center.x, center.y, pane.HalfSize, 0);
        }

        // A set with any hazard replaces the shown one and fades in. An empty set fades the shown one out.
        public void SetHazards(WaveHazards next, double now)
        {
            if (next.Any)
            {
                hazards=next; hazardsShownAt=now; hazardsHiddenAt=double.PositiveInfinity;
                fog.Show(next);
                lights.Show(next);
                lightning.Show(next);
            }
            else if (hazards!=null && double.IsPositiveInfinity(hazardsHiddenAt)) hazardsHiddenAt=now;
            DrawHazards(now);
        }

        // Runs every frame on the gameplay clock, including stages that do not Draw the board.
        public void DrawHazards(double now)
        {
            if (hazards==null) return;
            float fade=WaveHazards.Fade(now,hazardsShownAt,hazardsHiddenAt);
            fog.Tick(now,fade);
            lights.Tick(now,fade);
            // Once withdrawn, nothing flashes: strikes that come due during the fade-out pass silently.
            lightning.Tick(now,double.IsPositiveInfinity(hazardsHiddenAt) ? fade : 0,ReducedMotion);
            if (fade<=0 && !double.IsPositiveInfinity(hazardsHiddenAt)) hazards=null;
        }

        public void Shatter(int index)
        {
            if (model.Layout.Contains(index)) Shatter(index,model.Center(index));
        }
        internal Vector3 Shatter(int index, Vector2 center, double secondsSinceBreak=0)
        {
            var local = GlassGeometry.Ground(center);
            shards.Drop(local,index,Time.unscaledTime-(float)secondsSinceBreak);
            return transform.TransformPoint(local);
        }
        public void PracticeBreak(int index) { if (model.Layout.Contains(index)) { practiceHole=index; Shatter(index); } }
        public void ClearTransient() { practiceHole=-1; if(shards) shards.Clear(); }
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
            foreach(Object asset in new Object[]{cutouts,boundary,bodyMaterial,paneMaterial,maskMaterial,shardMaterial,fogMaterial})
                if(asset) Destroy(asset);
        }
    }
}
