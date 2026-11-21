using UnityEngine;

namespace FloorFracture
{
    // Only the game-owned blocks move. The tracked room stays in passthrough.
    internal sealed class FallingArena
    {
        readonly GlassBoardView board;
        readonly Vector3 start;
        readonly bool reduced;
        bool rising, belowRim;

        internal FallingArena(GlassBoardView board, bool reduced)
        {
            this.board = board;
            this.reduced = reduced;
            start = board.transform.position;
        }

        internal void Draw(float travel, bool entered)
        {
            if (!board) return;
            if (reduced)
            {
                if (entered) board.gameObject.SetActive(false);
                return;
            }
            board.transform.position = start + Vector3.up * travel;
            bool lifted = travel > 0;
            if (lifted == rising && entered == belowRim) return;
            rising = lifted;
            belowRim = entered;
            board.ShowLiftedBlocks(lifted,entered);
            board.ShowShardsBelowRim(entered);
        }

        internal void Restore()
        {
            if (!board) return;
            board.transform.position = start;
            board.ShowLiftedBlocks(false);
            board.ShowShardsBelowRim(false);
            if (reduced) board.gameObject.SetActive(true);
        }
    }
}
