using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator VerifyOutsideBoardPreview()
        {
            Choose("Main menu"); Choose("Development");
            var state=game.State;
            var board=game.BoardView.Board;
            StandAt(state.Center(CornerPane)-new Vector2(state.Width+2,0)*state.Pitch);
            var point=PlayerTileTracker.Project(board,head.position);
            int nearest=PlayablePanes.OrderBy(i => (state.Center(i)-point).sqrMagnitude).First();
            Choose("Preview loss"); yield return Stage(GameStage.Fall);
            var mask=board.Find("Opening stencil").GetComponent<MeshFilter>().sharedMesh;
            Require(mask.vertexCount==4 && Vector3.Distance(mask.bounds.center,GlassGeometry.Ground(state.Center(nearest)))<.002f,
                "An off-board preview must open the nearest playable cell.");
            VerifyFloorVisibility(false);
            var position=board.position;
            var stateAtEntry=game.State;
            game.gameObject.SendMessage("OnApplicationPause",true);
            yield return Stage(GameStage.Paused);
            yield return Delay(.65);
            Require(board.position==position && !board.gameObject.activeSelf && !GameObject.Find("Fall chamber"),
                "The ending advanced or left scenery visible while tracking was paused.");
            game.gameObject.SendMessage("OnApplicationPause",false);
            Choose("Show board"); yield return Stage(GameStage.Fall);
            Require(game.State==stateAtEntry && game.Ending.IsPlaying,"Tracking recovery abandoned the running ending.");
            game.Ending.Skip(); yield return Stage(GameStage.Results);
            Choose("Retry Level"); yield return Stage(GameStage.Ready);
            Require(!game.BoardView.Board.Find("Opening stencil").GetComponent<Renderer>().enabled,"The preview opening survived Restart.");
            Record("The off-board preview opened the nearest fitted tile and cleared on Restart.");
        }

        private sealed class FallArenaWitness
        {
            readonly Transform board;
            readonly Pose boardStart, initialEye;
            readonly bool reduced;
            readonly Transform[] anchors;
            readonly Pose[] anchorStarts;
            bool overheadChecked;

            internal FallArenaWitness(Pose eye, bool reduced)
            {
                this.reduced = reduced;
                initialEye = eye;
                board = game.BoardView.Board;
                boardStart = new Pose(board.position, board.rotation);
                anchors=Meta.XR.MRUtilityKit.MRUK.Instance.Rooms.Single().FloorAnchors.Select(a=>a.transform).ToArray();
                anchorStarts=anchors.Select(t=>new Pose(t.position,t.rotation)).ToArray();

            }

            internal void VerifyEntry(MeshRenderer walls)
            {
                var data = new MaterialPropertyBlock();
                walls.GetPropertyBlock(data);
                Vector4 entry = data.GetVector("_Mouth");
                Vector4 right = data.GetVector("_MouthRight");
                Require(Vector2.Distance(new Vector2(entry.x,entry.z),new Vector2(initialEye.position.x,initialEye.position.z)) < .002f &&
                    Vector2.Distance(new Vector2(right.x,right.y),new Vector2(board.right.x,board.right.z)) < .002f &&
                    Vector3.Distance(walls.transform.parent.position,boardStart.position) < .002f,
                    "Entry moved away from the tracked position or changed the chamber's established board frame.");
                VerifyFloorVisibility(false);
                Require(!board.Find("Rising scanned floor"),"The fall created a duplicate scanned floor.");
                if (reduced) return;
                var vertices = board.Find("Opening stencil").GetComponent<MeshFilter>().sharedMesh.vertices;
                var centers = Enumerable.Range(0,vertices.Length/4).Select(i => (vertices[4*i]+vertices[4*i+2])*.5f).ToArray();
                foreach (int cell in new[] { CornerPane, AdjacentPane(CornerPane) })
                    Require(centers.Any(c => Vector3.Distance(c,GlassGeometry.Ground(game.State.Center(cell))) < .002f),
                        "The connected openings at the corner changed shape on entry.");
            }

            internal void Observe(FallEvidence evidence, float travel, MeshRenderer walls)
            {
                for (int i=0; i<anchors.Length; i++)
                {
                    float displacement=Vector3.Distance(anchors[i].position,anchorStarts[i].position);
                    evidence.maxRoomAnchorDisplacement=Mathf.Max(evidence.maxRoomAnchorDisplacement,displacement);
                    Require(displacement<.002f && Quaternion.Angle(anchors[i].rotation,anchorStarts[i].rotation)<.02f,
                        "A tracked floor anchor moved during the fall.");
                }
                VerifyFloorVisibility(false);
                if (reduced) return;
                Vector3 offset = Vector3.up * travel;
                float error = Vector3.Distance(board.position,boardStart.position + offset);
                Require(board.gameObject.activeInHierarchy && Quaternion.Angle(board.rotation,boardStart.rotation) < .02f,
                    "The falling grid disappeared or turned relative to its original board.");
                var center = boardStart.position + boardStart.rotation * GlassGeometry.Ground(game.State.Center(CornerPane));
                evidence.offCenterMetres = Vector3.ProjectOnPlane(initialEye.position-center,Vector3.up).magnitude;
                evidence.maxArenaRegistrationError = Mathf.Max(evidence.maxArenaRegistrationError,error);
                Require(error < .002f,
                    "The falling board lost alignment with its captured pose.");
                if (walls.transform.position.y < initialEye.position.y - .07f)
                    Require(walls.sharedMaterial.GetInt("_PortalComparison") == (int)CompareFunction.Equal,
                        "Before its rim arrived, the shaft stopped using the actual rising, connected pane openings.");
                if (!overheadChecked && walls.transform.position.y > initialEye.position.y + 3f)
                {
                    overheadChecked = true;
                    ReadOverhead(evidence);
                }
            }

            // Render the complete production scene from the tracked eye, then omit one
            // set of surfaces at a time to count its actual contribution to the upward view.
            void ReadOverhead(FallEvidence evidence)
            {
                const int size = 320;
                var holder = new GameObject("Overhead fall camera");
                var camera = holder.AddComponent<Camera>();
                var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
                var image = new Texture2D(size,size,TextureFormat.RGB24,false);
                var priorTarget = RenderTexture.active;

                var glass = board.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.name == "Glass sides and bottom").ToArray();
                try
                {
                    camera.CopyFrom(game.Rig.centerEyeAnchor.GetComponent<Camera>());
                    camera.enabled = false;
                    camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.aspect = 1; camera.fieldOfView = 94f;
                    camera.transform.SetPositionAndRotation(initialEye.position,Quaternion.LookRotation(Vector3.up,board.forward));
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    Color32[] Read()
                    {
                        RenderPipeline.SubmitRenderRequest(camera,request);
                        RenderTexture.active = target;
                        image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                        return image.GetPixels32();
                    }
                    Color32[] complete = Read();
                    string photo = "fall-" + evidence.ending.ToLowerInvariant() + "-full-overhead.png";
                    File.WriteAllBytes(Path.Combine(Folder,photo),image.EncodeToPNG()); report.images.Add(photo);
                    int Difference(MeshRenderer[] surfaces)
                    {
                        foreach (var surface in surfaces) surface.enabled = false;
                        var without = Read();
                        foreach (var surface in surfaces) surface.enabled = true;
                        int count = 0;
                        for (int i=0; i<complete.Length; i++)
                            if (Math.Abs(complete[i].r-without[i].r)+Math.Abs(complete[i].g-without[i].g)+Math.Abs(complete[i].b-without[i].b) > 12) count++;
                        return count;
                    }
                    var origin=camera.transform.position;
                    foreach (float eye in new[]{-.032f,.032f})
                    {
                        camera.transform.position=origin+board.right*eye;
                        complete=Read();
                        int glassPixels=Difference(glass);
                        Require(glassPixels>100,$"Only {glassPixels} glass-body pixels visible looking up from eye {eye}.");
                        evidence.overheadGlassPixels=glassPixels;
                    }
                    Require(FloorSurface.EffectMeshObjects.Count==0 && !board.Find("Rising scanned floor"),
                        "A scanned-floor renderer was present overhead.");

                }
                finally
                {
                    foreach (var r in glass) r.enabled = true;
                    RenderTexture.active = priorTarget;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(image); Object.DestroyImmediate(holder);
                }
            }

            internal void VerifyReset()
            {
                Require((reduced || overheadChecked) && !GameObject.Find("Rising scanned floor") &&
                    Vector3.Distance(game.BoardView.Board.position,boardStart.position) < .002f &&
                    Quaternion.Angle(game.BoardView.Board.rotation,boardStart.rotation) < .02f &&
                    FloorSurface.HideMesh && FloorSurface.EffectMeshObjects.Count==0,
                    "Restart did not restore the glass and passthrough around it.");
            }
        }
    }
}
