using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    // Indices stay row-major even when an unplayable patch cuts into the board.
    public readonly struct BoardLayout
    {
        public readonly int Columns, Rows;
        public readonly ulong Mask;
        private readonly ulong leftColumn, rightColumn;
        public int CellCount => Columns * Rows;
        public int PaneCount => WavePlanner.CountBits(Mask);
        public bool IsRectangle => Mask != 0 && Mask == BoundsMask(Columns, Rows);

        public BoardLayout(int columns, int rows, ulong mask)
        {
            Columns = columns;
            Rows = rows;
            Mask = mask;
            ulong left = 0;
            if (BoundsMask(columns, rows) != 0)
                for (int row = 0; row < rows; row++) left |= 1UL << (row * columns);
            leftColumn = left;
            rightColumn = left << (columns - 1);
        }

        public static BoardLayout Rectangle(int columns, int rows) =>
            new BoardLayout(columns, rows, BoundsMask(columns, rows));

        private static ulong BoundsMask(int columns, int rows) =>
            columns > 0 && rows > 0 && columns <= BoardState.MaxPanes / rows
                ? ulong.MaxValue >> (BoardState.MaxPanes - columns * rows) : 0;

        public bool HasPane(int column, int row) =>
            (uint)column < (uint)Columns && (uint)row < (uint)Rows && Contains(row * Columns + column);

        public bool Contains(int index) => (uint)index < BoardState.MaxPanes && (Mask & (1UL << index)) != 0;

        internal ulong Adjacent(ulong group)
        {
            group &= Mask;
            ulong horizontal = ((group & ~leftColumn) >> 1) | ((group & ~rightColumn) << 1);
            ulong vertical = Rows > 1 ? (group >> Columns) | (group << Columns) : 0;
            return (horizontal | vertical) & Mask;
        }

        public bool IsPlayable
        {
            get
            {
                if ((Mask & ~BoundsMask(Columns, Rows)) != 0 || (Mask & (Mask - 1)) == 0) return false;
                ulong reached = Mask & (~Mask + 1);
                while (true)
                {
                    ulong more = Adjacent(reached) & ~reached;
                    if (more == 0) return reached == Mask;
                    reached |= more;
                }
            }
        }

        public IEnumerable<(Vector2 a, Vector2 b)> Perimeter(float pitch)
        {
            float left = -Columns * pitch * .5f, bottom = -Rows * pitch * .5f;
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Columns; col++)
                {
                    if (!HasPane(col, row)) continue;
                    var lower = new Vector2(left + col * pitch, bottom + row * pitch);
                    var right = lower + Vector2.right * pitch;
                    var upper = lower + Vector2.one * pitch;
                    var top = lower + Vector2.up * pitch;
                    if (!HasPane(col, row - 1)) yield return (lower, right);
                    if (!HasPane(col + 1, row)) yield return (right, upper);
                    if (!HasPane(col, row + 1)) yield return (upper, top);
                    if (!HasPane(col - 1, row)) yield return (top, lower);
                }
        }
    }
}
