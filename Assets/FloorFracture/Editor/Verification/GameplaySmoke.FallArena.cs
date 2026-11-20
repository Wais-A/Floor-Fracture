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
        private static IEnumerator VerifyOutsideBrickPreview()
        {
            Choose("Main menu"); Choose("Development");
            var board = game.BoardView.Board;
            var effect = game.Room.FloorEffect;
            var pitch = game.State.Pitch;
            Vector2? hit = null;
            for (int row = -1; row <= game.State.Height && !hit.HasValue; row++)
            for (int col = -1; col <= game.State.Width && !hit.HasValue; col++)
            {
                if (row >= 0 && row < game.State.Height && col >= 0 && col < game.State.Width) continue;
                var local = new Vector2(col+.62f-game.State.Width*.5f,row+.38f-game.State.Height*.5f)*pitch;
                Vector3 world = board.TransformPoint(GlassGeometry.Ground(local));
                if (effect.EffectMeshObjects.Keys.Any(anchor => anchor.IsPositionInBoundary(anchor.transform.InverseTransformPoint(world)))) hit = local;
            }
            Require(hit.HasValue,"No scanned brick outside the fitted grid was available for a preview.");
            StandAt(hit.Value);
            Pose initialEye = head;
            Choose("Preview loss"); yield return Stage(GameStage.Fall);
            var chamber = GameObject.Find("Fall chamber").transform;
            var surface = chamber.Find("Hazard surface").GetComponent<MeshRenderer>();
            var data = new MaterialPropertyBlock(); surface.GetPropertyBlock(data);
            var entry = data.GetVector("_Mouth");
            Require(Vector2.Distance(new Vector2(entry.x,entry.z),new Vector2(initialEye.position.x,initialEye.position.z)) < .002f,
                "The outside preview started at a different spot from the viewer.");
            CheckOutsideAperture(board,initialEye,surface,false);
            yield return Delay(.76);
            CheckOutsideAperture(board,initialEye,surface,true);
            game.Ending.Skip(); yield return Stage(GameStage.Results);
            Choose("Restart"); yield return Stage(GameStage.Ready);
            Require(effect.MeshMaterial.GetInt("_PortalComparison") == (int)CompareFunction.Always &&
                !game.BoardView.Board.Find("Opening stencil").GetComponent<MeshRenderer>().enabled,
                "The temporary brick opening remained after skipping and restarting.");
            Record("An off-grid preview opened the scanned brick at the exact tracked position during the hold and rise. Skip and Restart removed the temporary opening.");
        }

        private static void CheckOutsideAperture(Transform board, Pose eye, MeshRenderer surface, bool lifting)
        {
            const int size = 64, layer = 30;
            var effect = game.Room.FloorEffect;
            var floor = lifting ? board.Find("Rising scanned floor").GetComponentsInChildren<MeshRenderer>() :
                effect.EffectMeshObjects.Values.Select(p => p.effectMeshGO.GetComponent<MeshRenderer>()).ToArray();
            var subjects = floor.Select(r => r.gameObject).Append(board.Find("Opening stencil").gameObject).Append(surface.gameObject).ToArray();
            var layers = subjects.Select(o => o.layer).ToArray();
            var material = floor[0].sharedMaterial;
            string property = lifting ? "_OutsideOpening" : "_PortalComparison";
            Vector4 opening = lifting ? material.GetVector(property) : Vector4.zero;
            int comparison = lifting ? 0 : material.GetInt(property);
            var holder = new GameObject("Outside brick opening camera");
            var camera = holder.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var prior = RenderTexture.active;
            var image = new Texture2D(size,size,TextureFormat.RGB24,false);
            try
            {
                foreach (var o in subjects) o.layer = layer;
                camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.cullingMask = 1<<layer; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta;
                camera.aspect = 1; camera.fieldOfView = 60; camera.nearClipPlane = .05f; camera.farClipPlane = 150;
                camera.transform.SetPositionAndRotation(eye.position,Quaternion.LookRotation(Vector3.down,board.forward));
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                Color32 Center()
                {
                    RenderPipeline.SubmitRenderRequest(camera,request); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0,0,size,size),0,0); image.Apply();
                    return image.GetPixel(size/2,size/2);
                }
                Color32 landing = Center();
                surface.enabled = false;
                Color32 empty = Center();
                if (lifting) material.SetVector(property,Vector4.zero);
                else material.SetInt(property,(int)CompareFunction.Always);
                Color32 closed = Center();
                Require(IsBrick(landing) && !IsBrick(empty) && closed.b > closed.g && closed.g > closed.r,
                    $"Outside preview {(lifting ? "in motion" : "at rest")} rendered {landing} through the aperture, {empty} without its landing and {closed} with closed brick.");
            }
            finally
            {
                if (lifting) material.SetVector(property,opening); else material.SetInt(property,comparison);
                surface.enabled = true;
                for (int i=0; i<subjects.Length; i++) subjects[i].layer=layers[i];
                RenderTexture.active = prior; RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image); Object.DestroyImmediate(holder);
            }
        }

        private sealed class FallArenaWitness
        {
            readonly Transform board;
            readonly Pose boardStart, initialEye;
            readonly bool reduced;
            readonly Transform[] anchors;
            readonly Pose[] anchorStarts, patchStarts;
            readonly MeshFilter[] originals;
            MeshFilter[] copies;
            bool overheadChecked;

            internal FallArenaWitness(Pose eye, bool reduced)
            {
                this.reduced = reduced;
                initialEye = eye;
                board = game.BoardView.Board;
                boardStart = new Pose(board.position, board.rotation);
                var patches = game.Room.FloorEffect.EffectMeshObjects.ToArray();
                anchors = patches.Select(p => p.Key.transform).ToArray();
                originals = patches.Select(p => p.Value.effectMeshGO.GetComponent<MeshFilter>()).ToArray();
                anchorStarts = anchors.Select(t => new Pose(t.position,t.rotation)).ToArray();
                patchStarts = originals.Select(p => new Pose(p.transform.position,p.transform.rotation)).ToArray();
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
                var rising = board.Find("Rising scanned floor");
                if (reduced) { Require(!rising,"Reduced motion created a moving floor."); return; }
                Require(rising && !rising.gameObject.activeInHierarchy && originals.All(p => p.GetComponent<MeshRenderer>().enabled),
                    "The native room floor was replaced before the stationary recognition beat.");
                var instances = rising.GetComponentsInChildren<MeshFilter>(true);
                Require(instances.Length == originals.Length && instances.Length > 0, "The fall lost scanned floor patches.");
                copies = originals.Select(original => instances.Single(copy => copy.sharedMesh == original.sharedMesh)).ToArray();
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
                    float displacement = Mathf.Max(Vector3.Distance(anchors[i].position,anchorStarts[i].position),
                        Vector3.Distance(originals[i].transform.position,patchStarts[i].position));
                    evidence.maxRoomAnchorDisplacement = Mathf.Max(evidence.maxRoomAnchorDisplacement,displacement);
                    Require(displacement < .002f && Quaternion.Angle(anchors[i].rotation,anchorStarts[i].rotation) < .02f &&
                        Quaternion.Angle(originals[i].transform.rotation,patchStarts[i].rotation) < .02f,
                        "A tracked floor anchor or its source EffectMesh moved with the game.");
                }
                if (reduced) return;
                Vector3 offset = Vector3.up * travel;
                float error = Vector3.Distance(board.position,boardStart.position + offset);
                Require(board.gameObject.activeInHierarchy && Quaternion.Angle(board.rotation,boardStart.rotation) < .02f,
                    "The falling grid disappeared or turned relative to its original board.");
                for (int i=0; i<copies.Length; i++)
                {
                    error = Mathf.Max(error,Vector3.Distance(copies[i].transform.position,patchStarts[i].position + offset));
                    Require(Quaternion.Angle(copies[i].transform.rotation,patchStarts[i].rotation) < .02f &&
                        copies[i].gameObject.activeInHierarchy == (travel>0f) && originals[i].GetComponent<MeshRenderer>().enabled == (travel<=0f),
                        "The rising room patch changed orientation or handed off on a different frame from the glass.");
                }
                var center = boardStart.position + boardStart.rotation * GlassGeometry.Ground(game.State.Center(CornerPane));
                evidence.offCenterMetres = Vector3.ProjectOnPlane(initialEye.position-center,Vector3.up).magnitude;
                evidence.maxArenaRegistrationError = Mathf.Max(evidence.maxArenaRegistrationError,error);
                Require(error < .002f && evidence.offCenterMetres > game.State.Pitch*.35f,
                    "The falling arena was recentered or lost alignment with the shaft rim.");
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
                var brick = copies.Select(copy => copy.GetComponent<MeshRenderer>()).ToArray();
                var glass = board.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && (r.name == "Smoky pane" || r.name == "Rim")).ToArray();
                try
                {
                    camera.CopyFrom(game.Rig.centerEyeAnchor.GetComponent<Camera>());
                    camera.enabled = false;
                    camera.stereoTargetEye = StereoTargetEyeMask.None;
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
                    var complete = Read();
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
                    evidence.overheadBrickPixels = Difference(brick);
                    evidence.overheadGlassPixels = Difference(glass);
                    Require(evidence.overheadBrickPixels > 100 && evidence.overheadGlassPixels > 100,
                        $"Looking back up, the scanned brick affected {evidence.overheadBrickPixels} pixels and the surviving glass {evidence.overheadGlassPixels}.");
                }
                finally
                {
                    foreach (var r in brick) r.enabled = true;
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
                    originals.All(p => !p.GetComponent<MeshRenderer>().enabled),
                    "Restart did not restore the glass and passthrough around it.");
            }
        }
    }
}
