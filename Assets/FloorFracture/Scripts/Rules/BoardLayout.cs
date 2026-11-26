using System;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture
{
    // Indices stay row-major even when an unplayable patch cuts into the board.
    public readonly struct BoardLayout : IEquatable<BoardLayout>
    {
        public readonly int Columns, Rows;
        private readonly ulong lowWord;
        private readonly bool[] occupied;
        public int CellCount { get; }
        public int PaneCount { get; }
        public bool IsRectangle => CellCount > 0 && PaneCount == CellCount;
        public bool IsPlayable { get; }

        public BoardLayout(int columns, int rows, ulong mask)
            : this(columns, rows, Indices(mask)) { }

        public BoardLayout(int columns, int rows, IEnumerable<int> occupiedIndices)
        {
            if (occupiedIndices == null) throw new ArgumentNullException(nameof(occupiedIndices));
            Columns = columns;
            Rows = rows;
            CellCount = Size(columns, rows);
            occupied = new bool[CellCount];
            int count = 0, first = -1;
            ulong lowWord = 0;
            bool inBounds = true;
            foreach (int tile in occupiedIndices)
            {
                if ((uint)tile >= (uint)CellCount) { inBounds = false; continue; }
                if (occupied[tile]) continue;
                occupied[tile] = true;
                if (first < 0) first = tile;
                ++count;
                if (tile < 64) lowWord |= 1UL << tile;
            }
            PaneCount = count;
            this.lowWord = lowWord;
            IsPlayable = false;
            if (IsRectangle) occupied = null; // Full rectangles need no lookup table.
            IsPlayable = inBounds && count >= 2 && (IsRectangle || IsConnected(first));
        }

        private BoardLayout(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
            CellCount = Size(columns, rows);
            PaneCount = CellCount;
            lowWord = CellCount >= 64 ? ulong.MaxValue : (1UL << CellCount) - 1;
            occupied = null;
            IsPlayable = CellCount >= 2;
        }

        public static BoardLayout Rectangle(int columns, int rows) => new BoardLayout(columns, rows);

        private static int Size(int columns, int rows) => columns > 0 && rows > 0 &&
            columns < int.MaxValue && rows < int.MaxValue && columns <= int.MaxValue / rows ? columns * rows : 0;

        private static IEnumerable<int> Indices(ulong mask)
        {
            for (int i = 0; mask != 0; ++i, mask >>= 1)
                if ((mask & 1) != 0) yield return i;
        }

        public bool HasPane(int column, int row) =>
            (uint)column < (uint)Columns && (uint)row < (uint)Rows && Contains(row * Columns + column);

        public bool Contains(int index) =>
            (uint)index < (uint)CellCount && (occupied == null || occupied[index]);

        public bool Equals(BoardLayout other)
        {
            if (Columns != other.Columns || Rows != other.Rows || PaneCount != other.PaneCount ||
                IsPlayable != other.IsPlayable || lowWord != other.lowWord) return false;
            if (ReferenceEquals(occupied, other.occupied)) return true;
            for (int i = 64; i < CellCount; ++i)
                if (Contains(i) != other.Contains(i)) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is BoardLayout other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (((Columns * 31 + Rows) * 31 + PaneCount) * 31 + lowWord.GetHashCode()) * 31 +
                    IsPlayable.GetHashCode();
                if (!IsRectangle)
                    for (int i = 64; i < CellCount; ++i)
                        if (Contains(i)) hash = hash * 31 + i;
                return hash;
            }
        }

        // In increasing row-major order: below, left, right, above. The source is an in-bounds pane.
        internal int Neighbor(int tile, int direction)
        {
            switch (direction)
            {
                case 0: return tile >= Columns ? tile - Columns : -1;
                case 1: return tile % Columns != 0 ? tile - 1 : -1;
                case 2: return tile % Columns < Columns - 1 ? tile + 1 : -1;
                default: return tile < CellCount - Columns ? tile + Columns : -1;
            }
        }

        private bool IsConnected(int first)
        {
            var seen = new bool[CellCount];
            var queue = new int[PaneCount];
            seen[first] = true;
            queue[0] = first;
            int tail = 1;
            for (int head = 0; head < tail; ++head)
                for (int d = 0; d < 4; ++d)
                {
                    int neighbor = Neighbor(queue[head], d);
                    if (!Contains(neighbor) || seen[neighbor]) continue;
                    seen[neighbor] = true;
                    queue[tail++] = neighbor;
                }
            return tail == PaneCount;
        }

        public Vector2 Corner(int column, int row, float pitch) =>
            new((column - Columns * .5f) * pitch, (row - Rows * .5f) * pitch);

        public IEnumerable<(Vector2 a, Vector2 b)> Perimeter(float pitch)
        {
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Columns; col++)
                {
                    if (!HasPane(col, row)) continue;
                    var lower = Corner(col, row, pitch);
                    var right = Corner(col + 1, row, pitch);
                    var upper = Corner(col + 1, row + 1, pitch);
                    var top = Corner(col, row + 1, pitch);
                    if (!HasPane(col, row - 1)) yield return (lower, right);
                    if (!HasPane(col + 1, row)) yield return (right, upper);
                    if (!HasPane(col, row + 1)) yield return (upper, top);
                    if (!HasPane(col - 1, row)) yield return (top, lower);
                }
        }
    }
}
