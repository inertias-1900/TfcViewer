using System;

namespace TfcViewer.Core;

/// <summary>Mth 数学函数（与 MC 1.21 语义对齐：sin 查表 10430.378F）</summary>
public static class Mth
{
    public const float PI = (float)Math.PI;
    public const float TWO_PI = (float)(Math.PI * 2.0);

    private static readonly float[] SIN = BuildSinTable();

    private static float[] BuildSinTable()
    {
        var t = new float[65536];
        for (int i = 0; i < 65536; i++)
            t[i] = (float)Math.Sin(i * Math.PI * 2.0 / 65536.0);
        return t;
    }

    public static float Sin(float v) => SIN[(int)(v * 10430.378F) & 65535];
    public static float Cos(float v) => SIN[(int)(v * 10430.378F + 16384.0F) & 65535];
    public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    public static double Atan2(double y, double x) => Math.Atan2(y, x);

    public static float Map(float value, float inMin, float inMax, float outMin, float outMax)
        => outMin + (outMax - outMin) * (value - inMin) / (inMax - inMin);
    public static double Map(double value, double inMin, double inMax, double outMin, double outMax)
        => outMin + (outMax - outMin) * (value - inMin) / (inMax - inMin);

    public static float ClampedMap(float value, float inMin, float inMax, float outMin, float outMax)
        => Clamp(Map(value, inMin, inMax, outMin, outMax), Math.Min(outMin, outMax), Math.Max(outMin, outMax));
    public static double ClampedMap(double value, double inMin, double inMax, double outMin, double outMax)
        => Clamp(Map(value, inMin, inMax, outMin, outMax), Math.Min(outMin, outMax), Math.Max(outMin, outMax));

    public static float ClampedLerp(float v, float from, float to) => Lerp(Clamp(v), from, to);
    public static double ClampedLerp(double v, double from, double to) => Lerp(Clamp(v), from, to);

    public static float Lerp(float delta, float start, float end) => start + delta * (end - start);
    public static double Lerp(double delta, double start, double end) => start + delta * (end - start);

    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
    public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    public static long Clamp(long value, long min, long max) => Math.Clamp(value, min, max);
    public static float Clamp(float value) => Clamp(value, 0f, 1f);
    public static double Clamp(double value) => Clamp(value, 0.0, 1.0);

    public static int Floor(double value) => (int)Math.Floor(value);
    public static int Floor(float value) => (int)Math.Floor(value);
    public static int Ceil(double value) => (int)Math.Ceiling(value);
    public static int Ceil(float value) => (int)Math.Ceiling(value);
    public static long Floor(long value) => value; // 占位避免重载歧义

    public static int FloorMod(int x, int y) => (int)FloorModLong(x, y);
    public static long FloorMod(long x, long y) => FloorModLong(x, y);
    public static int FloorDiv(int x, int y) => (int)FloorDivLong(x, y);
    public static long FloorDiv(long x, long y) => FloorDivLong(x, y);

    private static long FloorDivLong(long x, long y)
    {
        long q = x / y, r = x % y;
        if (r != 0 && ((r < 0) != (y < 0))) q--;
        return q;
    }

    private static long FloorModLong(long x, long y)
    {
        long r = x % y;
        if (r != 0 && ((r < 0) != (y < 0))) r += y;
        return r;
    }

    public static float Abs(float v) => Math.Abs(v);
    public static double Abs(double v) => Math.Abs(v);
    public static int Abs(int v) => Math.Abs(v);

    public static int SmallestEncompassingPowerOfTwo(int value)
    {
        int i = value - 1;
        i |= i >> 1;
        i |= i >> 2;
        i |= i >> 4;
        i |= i >> 8;
        i |= i >> 16;
        return i + 1;
    }

    public static float SmallestEncompassingPowerOfTwoF(int value) => SmallestEncompassingPowerOfTwo(value);

    public static int CeilLog2(int value)
    {
        int i = 0;
        while ((1 << i) < value) i++;
        return i;
    }
}

/// <summary>MC RandomSource 接口（1.21 语义）</summary>
public interface RandomSource
{
    void SetSeed(long seed);
    int NextInt();
    int NextInt(int bound);
    long NextLong();
    bool NextBoolean();
    float NextFloat();
    double NextDouble();
}

/// <summary>MC 1.21.1 XoroshiroRandomSource：内部 Xoroshiro128PlusPlus（128 位）</summary>
public sealed class XoroshiroRandomSource : RandomSource
{
    private const long XH = unchecked((long)0x6a09e667f3bcc909UL);
    private const long XL = unchecked((long)0x9e3779b97f4a7c15UL);
    private const long A = unchecked((long)0xbf58476d1ce4e5b9UL);
    private const long B = unchecked((long)0x94d049bb133111ebUL);
    private const long ALL_ZERO_LO = unchecked((long)0x9e3779b97f4a7c15UL);
    private const long ALL_ZERO_HI = unchecked((long)0x6a09e667f3bcc909UL);

    private long stateLo, stateHi;

    public XoroshiroRandomSource(long seed)
    {
        (long l, long h) = RandomSupport.UpgradeSeedTo128bit(seed);
        Init(l, h);
    }

    public XoroshiroRandomSource(long lo, long hi) => Init(lo, hi);

    private void Init(long lo, long hi)
    {
        stateLo = lo;
        stateHi = hi;
        if ((stateLo | stateHi) == 0)
        {
            stateLo = ALL_ZERO_LO;
            stateHi = ALL_ZERO_HI;
        }
    }

    public void SetSeed(long seed)
    {
        (long l, long h) = RandomSupport.UpgradeSeedTo128bit(seed);
        stateLo = l;
        stateHi = h;
    }

    /// <summary>读取状态（debug/快照用）</summary>
    public static (long lo, long hi) PeekState(XoroshiroRandomSource r) => (r.stateLo, r.stateHi);

    /// <summary>Xoroshiro128PlusPlus.nextLong()：单次状态步进，64 位全量输出</summary>
    private long NextLongInternal()
    {
        long l = stateLo;
        long h = stateHi;
        long result = unchecked(LongRotateLeft(l + h, 17) + l);
        h ^= l;
        stateLo = LongRotateLeft(l, 49) ^ h ^ (h << 21);
        stateHi = LongRotateLeft(h, 28);
        return result;
    }

    private static long LongRotateLeft(long x, int k) => (long)(((ulong)x << k) | ((ulong)x >> (64 - k)));

    public long NextLong() => NextLongInternal();

    /// <summary>MC 1.21：nextInt() = 低 32 位</summary>
    public int NextInt() => (int)NextLongInternal();

    /// <summary>MC 1.21：Lemire 乘法取模（无符号比较拒绝采样）</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentException("Bound must be positive");
        long v = (uint)NextInt();
        long product = v * (long)bound;
        if ((product & 0xFFFFFFFFL) < bound)
        {
            long rem = (uint)(~bound + 1) % (uint)bound; // Integer.remainderUnsigned
            while ((product & 0xFFFFFFFFL) < rem)
            {
                v = (uint)NextInt();
                product = v * (long)bound;
            }
        }
        return (int)(product >> 32);
    }

    public bool NextBoolean() => (NextLongInternal() & 1L) != 0L;

    public float NextFloat() => (float)NextBits(24) * 5.9604645E-8F;

    public double NextDouble() => (double)NextBits(53) * 1.1102230246251565E-16;

    private long NextBits(int bits) => (long)((ulong)NextLongInternal() >> (64 - bits));
}

/// <summary>MC RandomSupport：upgradeSeedTo128bit = Unmixed + mixStafford13</summary>
public static class RandomSupport
{
    public static (long lo, long hi) UpgradeSeedTo128bit(long seed)
    {
        long l = seed ^ unchecked((long)0x6a09e667f3bcc909UL);
        long h = unchecked(l + unchecked((long)0x9e3779b97f4a7c15UL));
        l = MixStafford13(l);
        h = MixStafford13(h);
        return (l, h);
    }

    public static long MixStafford13(long z)
    {
        z = unchecked((z ^ (long)((ulong)z >> 30)) * (long)0xbf58476d1ce4e5b9UL);
        z = unchecked((z ^ (long)((ulong)z >> 27)) * (long)0x94d049bb133111ebUL);
        return z ^ (long)((ulong)z >> 31);
    }
}

/// <summary>TFC Seed：levelSeed + 顺序派生（next() = XoroshiroRandomSource(levelSeed).NextLong()）</summary>
public sealed class Seed
{
    public static Seed Of(long levelSeed) => new(levelSeed, new XoroshiroRandomSource(levelSeed));
    public static Seed UnsafeOf(long levelSeed) => new(levelSeed, null);

    private readonly long seed;
    private readonly XoroshiroRandomSource? next;

    private Seed(long seed, XoroshiroRandomSource? next)
    {
        this.seed = seed;
        this.next = next;
    }

    public long SeedValue => seed;
    public long Next()
    {
        if (next == null) throw new InvalidOperationException("Unsafe to use Next() in this context");
        long v = next.NextLong();
        if (Environment.GetEnvironmentVariable("TFC_SEED_LOG") != null)
            Console.Error.WriteLine($"[SEED] -> {v}");
        return v;
    }
    public RandomSource Fork() => new XoroshiroRandomSource(Next(), Next());
    public Seed ForkStable() => Of(seed);
}

public static class RandomSourceDump
{
    /// <summary>克隆随机源（debug 用）：读取 XoroshiroRandomSource 状态</summary>
    public static RandomSource? TryClone(RandomSource r)
    {
        if (r is XoroshiroRandomSource x)
        {
            var (lo, hi) = XoroshiroRandomSource.PeekState(x);
            return new XoroshiroRandomSource(lo, hi);
        }
        return null;
    }
}

/// <summary>简化 Holder（只存值）</summary>
public sealed class Holder<T>(T value)
{
    public T Value { get; } = value;
    public static Holder<T> Direct(T value) => new(value);
}

/// <summary>简化 ResourceLocation（namespace:path 字符串）</summary>
public sealed class ResourceLocation
{
    private readonly string full;

    public ResourceLocation(string full) => this.full = full;
    public ResourceLocation(string ns, string path) => this.full = ns + ":" + path;

    public string Namespace => full.Contains(':') ? full.Split(':')[0] : "minecraft";
    public string Path => full.Contains(':') ? full.Split(':', 2)[1] : full;
    public string Full => full;
    public override string ToString() => full;
}