using System;

[Flags]
public enum FloorEndingSet
{
    Water = 1, Lava = 2, Crystal = 4, Space = 8, Sand = 16, Snow = 32,
    All = Water | Lava | Crystal | Space | Sand | Snow
}

internal enum FloorEnding { Water, Lava, Crystal, Space, Sand, Snow }

// The bag belongs to the presentation. Restarting gameplay preserves its place.
internal sealed class EndingShuffle
{
    readonly Random random;
    readonly int[] bag = new int[6];
    int mask, count, next;
    int previous = -1;

    internal EndingShuffle(int seed) { random = new Random(seed); }

    internal int Next(FloorEndingSet enabled)
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
        previous = bag[next++];
        return previous;
    }

    void Swap(int a, int b)
    {
        int value = bag[a];
        bag[a] = bag[b];
        bag[b] = value;
    }
}
