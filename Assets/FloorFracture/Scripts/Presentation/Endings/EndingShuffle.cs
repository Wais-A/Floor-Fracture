using System;

[Flags]
public enum FloorEndingSet
{
    Water = 1, Lava = 2, TarPit = 4, Space = 8, Sand = 16, Snow = 32,
    All = Water | Lava | TarPit | Space | Sand | Snow
}

internal enum FloorEnding { Water, Lava, TarPit, Space, Sand, Snow }

internal sealed class EndingShuffle
{
    readonly Random random;
    readonly int[] bag = new int[6];
    int mask, count, next;
    int previous = -1;

    internal EndingShuffle(int seed, string saved = null)
    {
        random = new Random(seed);
        if (string.IsNullOrEmpty(saved) || saved.Length > bag.Length + 1) return;
        int divider = saved.IndexOf('|');
        if (divider < 1) return;

        // Digits before the divider were drawn, in order. The last of those
        // also guards the boundary when a completed bag is loaded at launch.
        int seen = 0;
        for (int i = 0; i < saved.Length; i++)
        {
            if (i == divider) continue;
            int slot = saved[i] - '0';
            if (slot < 0 || slot >= bag.Length || (seen & (1 << slot)) != 0) return;
            bag[i < divider ? i : i - 1] = slot;
            seen |= 1 << slot;
        }
        mask = seen;
        count = saved.Length - 1;
        next = divider;
        previous = bag[next - 1];
    }

    internal string Save()
    {
        var slots = new char[count];
        for (int i = 0; i < count; i++) slots[i] = (char)('0' + bag[i]);
        return new string(slots).Insert(next, "|");
    }

    internal int Peek(FloorEndingSet enabled)
    {
        int selected = (int)(enabled & FloorEndingSet.All);
        if (selected == 0) return -1;
        if (selected != mask || next == count)
        {
            mask = selected;
            next = count = 0;
            for (int i = 0; i < bag.Length; i++)
                if ((mask & (1 << i)) != 0) bag[count++] = i;
            for (int i = count - 1; i > 0; i--)
                Swap(i, random.Next(i + 1));
            if (count > 1 && bag[0] == previous)
                Swap(0, random.Next(1, count));
        }
        return bag[next];
    }

    internal int Next(FloorEndingSet enabled)
    {
        int choice = Peek(enabled);
        if (choice < 0) return choice;
        next++;
        previous = choice;
        return previous;
    }

    void Swap(int a, int b)
    {
        int value = bag[a];
        bag[a] = bag[b];
        bag[b] = value;
    }
}
