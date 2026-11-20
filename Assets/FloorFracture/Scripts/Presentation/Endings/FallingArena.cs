using UnityEngine;

namespace FloorFracture
{
    // The board is already game-owned. For the surrounding room, retain the exact
    // Meta floor meshes and their starting poses without moving the tracked anchors.
    internal sealed class FallingArena
    {
        readonly GlassBoardView board;
        readonly RoomBoardPlacement room;
        readonly Vector3 boardStart;
        readonly Transform brickFloor;
        readonly Material brick;
        readonly Material anchoredBrick;
        readonly int previousComparison;
        readonly bool reduced;
        bool replacedRoom, belowRim;

        internal FallingArena(GlassBoardView board, RoomBoardPlacement room, bool reduced, Vector3 entry)
        {
            this.board = board;
            this.room = room;
            this.reduced = reduced;
            boardStart = board.transform.position;
            Vector3? outside = board.OpenOutsidePane(entry);
            if (!room || !room.FloorEffect || !room.FloorEffect.MeshMaterial) return;

            room.SetPlayAreaVisible(true, showFallFloor: true);
            var effect = room.FloorEffect;
            if (outside.HasValue)
            {
                anchoredBrick = effect.MeshMaterial;
                previousComparison = anchoredBrick.GetInt("_PortalComparison");
                anchoredBrick.SetInt("_PortalComparison", (int)UnityEngine.Rendering.CompareFunction.NotEqual);
            }
            if (reduced) return;
            brickFloor = new GameObject("Rising scanned floor").transform;
            brickFloor.SetParent(board.transform, false);
            brickFloor.gameObject.SetActive(false);
            brick = GlassTile.MakeMaterial("Endings/FallFloor");
            brick.SetVector("_BoardFrame", effect.MeshMaterial.GetVector("_BoardFrame"));
            brick.SetVector("_BoardHalfSize", effect.MeshMaterial.GetVector("_BoardHalfSize"));
            brick.SetInteger("_BoardMaskLow", effect.MeshMaterial.GetInteger("_BoardMaskLow"));
            brick.SetInteger("_BoardMaskHigh", effect.MeshMaterial.GetInteger("_BoardMaskHigh"));
            if (outside is Vector3 center)
                brick.SetVector("_OutsideOpening", new Vector4(center.x,center.z,board.PaneWidth*.5f,0));
            foreach (var patch in effect.EffectMeshObjects.Values)
            {
                if (!patch.mesh || !patch.effectMeshGO) continue;
                var source = patch.effectMeshGO.transform;
                var copy = GlassTile.Part(source, "Brick " + patch.mesh.name, patch.mesh, brick);
                copy.gameObject.layer = patch.effectMeshGO.layer;
                copy.transform.SetParent(brickFloor, true);
            }
        }

        internal void Draw(float travel, bool entered)
        {
            if (!board || (reduced ? !entered : travel <= 0f)) return;
            if (!replacedRoom)
            {
                if (room) room.SetPlayAreaVisible(false);
                if (brickFloor) brickFloor.gameObject.SetActive(true);
                if (reduced) board.gameObject.SetActive(false);
                replacedRoom = true;
            }
            if (reduced) return;
            board.transform.position = boardStart + Vector3.up * travel;
            if (brick) brick.SetFloat("_Travel", travel);
            if (belowRim != entered)
            {
                belowRim = entered;
                board.ShowShardsBelowRim(entered);
            }
        }

        internal void Restore()
        {
            if (brickFloor)
            {
                brickFloor.gameObject.SetActive(false);
                Object.Destroy(brickFloor.gameObject);
            }
            if (brick) Object.Destroy(brick);
            if (anchoredBrick) anchoredBrick.SetInt("_PortalComparison",previousComparison);
            if (board)
            {
                board.transform.position = boardStart;
                board.CloseOutsidePane();
                if (belowRim) board.ShowShardsBelowRim(false);
                if (reduced && replacedRoom) board.gameObject.SetActive(true);
            }
            if (room && room.isActiveAndEnabled) room.SetPlayAreaVisible(true);
        }
    }
}
