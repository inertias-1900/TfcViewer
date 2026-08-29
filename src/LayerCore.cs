using TfcViewer.Core;

namespace TfcViewer.World.Layer;

/// <summary>fastutil HashCommon（murmurHash3/mix/long2int）</summary>
public static class HashCommon
{
    public static long Mix(long x)
    {
        x ^= HashCommon.UnsignedRightShift(x, 33);
        x *= unchecked((long)0xff51afd7ed558ccdUL);
        x ^= HashCommon.UnsignedRightShift(x, 33);
        x *= unchecked((long)0xc4ceb9fe1a85ec53UL);
        x ^= HashCommon.UnsignedRightShift(x, 33);
        return x;
    }

    public static int Long2Int(long x) => (int)(x ^ (long)((ulong)x >> 32));

    public static long MurmurHash3(long x)
    {
        x ^= UnsignedRightShift(x, 33);
        x *= unchecked((long)0xff51afd7ed558ccdUL);
        x ^= UnsignedRightShift(x, 33);
        x *= unchecked((long)0xc4ceb9fe1a85ec53UL);
        x ^= UnsignedRightShift(x, 33);
        return x;
    }

    internal static long UnsignedRightShift(long x, int bits) => (long)((ulong)x >> bits);
}

/// <summary>AreaContext（层链随机上下文）</summary>
public sealed class AreaContext
{
    private readonly long seed;
    private readonly RandomSource random;

    public AreaContext(long seed)
    {
        this.seed = HashCommon.MurmurHash3(seed);
        this.random = new XoroshiroRandomSource(seed);
    }

    public RandomSource Random => random;

    public void SetSeed(long x, long z)
    {
        random.SetSeed(((x * 501125321L) ^ (z * 1136930381L) ^ seed) * unchecked((int)0x27d4eb2d));
    }

    public int Choose(int first, int second) => random.NextBoolean() ? first : second;

    public int Choose(int first, int second, int third, int fourth)
        => random.NextInt(4) switch { 0 => first, 1 => second, 2 => third, _ => fourth };
}

/// <summary>Area（确定性层缓存）</summary>
public sealed class Area
{
    public delegate int AreaSource(int x, int z);
    public delegate Area Factory();

    private readonly AreaSource source;
    private readonly long[] keys;
    private readonly int[] values;
    private readonly int mask;

    public Area(AreaSource source, int maxCacheSize)
    {
        maxCacheSize = Mth.SmallestEncompassingPowerOfTwo(maxCacheSize);
        this.source = source;
        this.keys = new long[maxCacheSize];
        this.values = new int[maxCacheSize];
        this.mask = maxCacheSize - 1;
        Array.Fill(keys, long.MinValue);
    }

    public int Get(int x, int z)
    {
        long key = (long)x & 0xFFFFFFFFL | ((long)z << 32); // ChunkPos.asLong 语义
        int index = (int)HashCommon.Mix(key) & mask;
        if (keys[index] == key)
        {
            return values[index];
        }
        else
        {
            int value = source(x, z);
            values[index] = value;
            keys[index] = key;
            return value;
        }
    }
}

/// <summary>UniformLayer（SourceLayer）</summary>
public sealed class UniformLayer
{
    public static Area.Factory Apply(long seed)
    {
        var context = new AreaContext(seed);
        return () => new Area((x, z) =>
        {
            context.SetSeed(x, z);
            return context.Random.NextInt();
        }, 1024);
    }
}

/// <summary>ZoomLayer（NORMAL）</summary>
public sealed class ZoomLayer
{
    public static Area.Factory ApplyNormal(long seed, Area.Factory prev)
    {
        var context = new AreaContext(seed);
        var prevArea = prev();
        return () => new Area((x, z) =>
        {
            int parentX = x >> 1, parentZ = z >> 1;
            int offsetX = x & 1, offsetZ = z & 1;
            int northWest = prevArea.Get(parentX, parentZ);
            context.SetSeed(parentX, parentZ);
            if (offsetX == 0 && offsetZ == 0) return northWest;
            if (offsetX == 0) return context.Choose(northWest, prevArea.Get(parentX, parentZ + 1));
            if (offsetZ == 0) return context.Choose(northWest, prevArea.Get(parentX + 1, parentZ));
            return Choose(context, northWest, prevArea.Get(parentX, parentZ + 1), prevArea.Get(parentX + 1, parentZ), prevArea.Get(parentX + 1, parentZ + 1));
        }, 1024);
    }

    private static int Choose(AreaContext context, int first, int second, int third, int fourth)
    {
        if (first == second)
        {
            return first == third || third != fourth ? first : context.Choose(first, third);
        }
        else if (first == third)
        {
            return second != fourth ? first : context.Choose(first, second);
        }
        else if (first == fourth)
        {
            return second != third ? first : context.Choose(first, second);
        }
        else if (second == third || second == fourth)
        {
            return second;
        }
        else if (third == fourth)
        {
            return third;
        }
        return context.Choose(first, second, third, fourth);
    }
}

/// <summary>SmoothLayer</summary>
public sealed class SmoothLayer
{
    public static Area.Factory Apply(long seed, Area.Factory prev)
    {
        var context = new AreaContext(seed);
        var prevArea = prev();
        return () => new Area((x, z) =>
        {
            context.SetSeed(x, z);
            int north = prevArea.Get(x, z - 1);
            int west = prevArea.Get(x - 1, z);
            int south = prevArea.Get(x, z + 1);
            int east = prevArea.Get(x + 1, z);
            int center = prevArea.Get(x, z);

            bool equalX = west == east, equalZ = north == south;
            if (equalX == equalZ)
            {
                return equalX ? context.Choose(east, north) : center;
            }
            return equalX ? east : north;
        }, 1024);
    }
}

/// <summary>Area 工厂委托（Supplier<Area> 语义）</summary>
public static partial class AreaFactoryExt
{
    public static Area Get(this Area.Factory factory) => factory();
}