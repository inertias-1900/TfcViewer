using TfcViewer.Core;

namespace TfcViewer.Noise;

/// <summary>Noise2D（等价 TFC Noise2D：抽象类 + 链式方法；lambda 用 Wrapper 包装）</summary>
public abstract class Noise2D
{
    public abstract double Noise(double x, double z);

    private sealed class Wrapper : Noise2D
    {
        private readonly Func<double, double, double> fn;
        public Wrapper(Func<double, double, double> f) => fn = f;
        public override double Noise(double x, double z) => fn(x, z);
    }

    public static Noise2D Fn(Func<double, double, double> f) => new Wrapper(f);

    public Noise2D Octaves(int octaves)
    {
        var frequency = new double[octaves];
        var amplitude = new double[octaves];
        for (int i = 0; i < octaves; i++)
        {
            frequency[i] = 1 << i;
            amplitude[i] = Math.Pow(0.5f, octaves - i);
        }
        return Fn((x, y) =>
        {
            double value = 0;
            for (int i = 0; i < octaves; i++)
                value += Noise(x / frequency[i], y / frequency[i]) * amplitude[i];
            return value;
        });
    }

    public Noise2D Ridged() => Fn((x, y) => { double v = Noise(x, y); return 1f - 2f * (v < 0 ? -v : v); });

    public Noise2D Abs() => Fn((x, y) => Math.Abs(Noise(x, y)));

    public Noise2D Spread(double scaleFactor) => Fn((x, y) => Noise(x * scaleFactor, y * scaleFactor));

    public Noise2D Scaled(double min, double max) => Scaled(-1, 1, min, max);

    public Noise2D Scaled(double oldMin, double oldMax, double min, double max)
    {
        double scale = (max - min) / (oldMax - oldMin);
        double shift = min - oldMin * scale;
        return Affine(scale, shift);
    }

    public Noise2D Affine(double scale, double shift) => Fn((x, y) => Noise(x, y) * scale + shift);

    public Noise2D ClampedScaled(double min, double max) => ClampedScaled(-1, 1, min, max);

    public Noise2D ClampedScaled(double oldMin, double oldMax, double min, double max)
    {
        double scale = (max - min) / (oldMax - oldMin);
        double shift = min - oldMin * scale;
        return Fn((x, y) => Math.Clamp(Noise(x, y) * scale + shift, min, max));
    }

    public Noise2D Clamped(double min, double max) => Fn((x, y) => Math.Clamp(Noise(x, y), min, max));

    public Noise2D Add(Noise2D other) => Fn((x, y) => Noise(x, y) + other.Noise(x, y));

    public Noise2D AddConstant(double constant) => Fn((x, y) => Noise(x, y) + constant);

    public Noise2D Min(Noise2D other) => Fn((x, y) => Math.Min(Noise(x, y), other.Noise(x, y)));

    public Noise2D Max(Noise2D other) => Fn((x, y) => Math.Max(Noise(x, y), other.Noise(x, y)));

    /// <summary>lazyProduct：第一个为 0 时短路（语义与原版一致）</summary>
    public Noise2D LazyProduct(Noise2D other) => Fn((x, y) => { double v = Noise(x, y); return v == 0 ? 0 : v * other.Noise(x, y); });

    public Noise2D Map(Func<double, double> mappingFunction) => Fn((x, y) => mappingFunction(Noise(x, y)));

    public Noise2D EaseIn(double start, double end, double minScale, double maxScale, Noise2D easingNoise)
        => Fn((x, z) => Mth.ClampedMap(easingNoise.Noise(x, z), start, end, minScale, maxScale) * Noise(x, z));

    public Noise2D StretchZ(double stretch) => Fn((x, z) => Noise(x, z / stretch));

    public Noise2D StretchX(double stretch) => Fn((x, z) => Noise(x / stretch, z));
}

/// <summary>FastNoiseLite 子集（OpenSimplex2S 专用 + 静态工具）</summary>
public partial class FNLite
{
    public const int PrimeX = 501125321;
    public const int PrimeY = 1136930381;

    public static float FastMin(float a, float b) => a < b ? a : b;
    public static double FastMin(double a, double b) => a < b ? a : b;
    public static float FastMax(float a, float b) => a > b ? a : b;
    public static double FastMax(double a, double b) => a > b ? a : b;
    public static int FastFloor(double f) => f >= 0 ? (int)f : (int)f - 1;
    public static int FastRound(double f) => f >= 0 ? (int)(f + 0.5f) : (int)(f - 0.5f);
    public static float Lerp(float a, float b, float t) => a + t * (b - a);

    public static int Hash(int seed, int xPrimed, int yPrimed)
    {
        int hash = seed ^ xPrimed ^ yPrimed;
        hash *= unchecked((int)0x27d4eb2d);
        return hash;
    }

    /// <summary>（hash 高位归一化，TFC 版特有）</summary>
    public static float ValCoord(int seed, int xPrimed, int yPrimed)
    {
        int hash = Hash(seed, xPrimed, yPrimed);
        hash ^= hash << 19;
        return hash * (1 / 2147483648.0f);
    }

    public static float GradCoord(int seed, int xPrimed, int yPrimed, float xd, float yd)
    {
        int hash = Hash(seed, xPrimed, yPrimed);
        hash ^= hash >> 15;
        hash &= 127 << 1;
        float xg = Gradients2D[hash];
        float yg = Gradients2D[hash | 1];
        return xd * xg + yd * yg;
    }

    // ---- OpenSimplex2S 实例部分（TFC 裁剪版） ----
    public enum NoiseType { OpenSimplex2, OpenSimplex2S }
    public enum FractalType { None, FBm, Ridged, PingPong }

    private int mSeed = 1337;
    private float mFrequency = 0.01f;
    private NoiseType mNoiseType = NoiseType.OpenSimplex2;
    private FractalType mFractalType = FractalType.None;
    private int mOctaves = 3;
    private float mLacunarity = 2.0f;
    private float mGain = 0.5f;
    private float mWeightedStrength = 0.0f;
    private float mFractalBounding = 1 / 1.75f;

    public FNLite() { }
    public FNLite(int seed) { SetSeed(seed); }

    public void SetSeed(int seed) => mSeed = seed;
    public void SetFrequency(float frequency) => mFrequency = frequency;
    public void SetNoiseType(NoiseType t) => mNoiseType = t;
    public void SetFractalType(FractalType t) => mFractalType = t;
    public void SetFractalOctaves(int octaves)
    {
        mOctaves = octaves;
        CalculateFractalBounding();
    }

    private void CalculateFractalBounding()
    {
        float gain = FastAbs(mGain);
        float amp = gain;
        float ampFractal = 1.0f;
        for (int i = 1; i < mOctaves; i++)
        {
            ampFractal += amp;
            amp *= gain;
        }
        mFractalBounding = 1 / ampFractal;
    }

    private static float FastAbs(float f) => Math.Abs(f);

    public float GetNoise(double x, double y)
    {
        x *= mFrequency;
        y *= mFrequency;

        // OpenSimplex2/2S 的斜变（Skew）
        const double SQRT3 = 1.7320508075688772935274463415059;
        const double F2 = 0.5f * (SQRT3 - 1);
        double t = (x + y) * F2;
        x += t;
        y += t;

        return mFractalType switch
        {
            FractalType.FBm => GenFractalFBm(x, y),
            _ => GenNoiseSingle(mSeed, x, y),
        };
    }

    private float GenNoiseSingle(int seed, double x, double y)
        => SingleOpenSimplex2S(seed, x, y); // TFC 构造即设 2S，OpenSimplex2 原始路径不使用

    private float GenFractalFBm(double x, double y)
    {
        int seed = mSeed;
        float sum = 0;
        float amp = mFractalBounding;
        for (int i = 0; i < mOctaves; i++)
        {
            float noise = GenNoiseSingle(seed++, x, y);
            sum += noise * amp;
            amp *= Lerp(1.0f, FastMin(noise + 1, 2) * 0.5f, mWeightedStrength);
            x *= mLacunarity;
            y *= mLacunarity;
            amp *= mGain;
        }
        return sum;
    }

    private static float SingleOpenSimplex2S(int seed, double x, double y)
    {
        // 2D OpenSimplex2S：修改版 2D 单纯形噪声（skew 已在 GetNoise 完成）
        const double SQRT3 = 1.7320508075688772935274463415059;
        const double G2 = (3 - SQRT3) / 6;

        int i = FastFloor(x);
        int j = FastFloor(y);
        float xi = (float)(x - i);
        float yi = (float)(y - j);

        i *= PrimeX;
        j *= PrimeY;
        int i1 = i + PrimeX;
        int j1 = j + PrimeY;

        float t = (xi + yi) * (float)G2;
        float x0 = xi - t;
        float y0 = yi - t;

        float a0 = (2.0f / 3.0f) - x0 * x0 - y0 * y0;
        float value = (a0 * a0) * (a0 * a0) * GradCoord(seed, i, j, x0, y0);

        float a1 = (float)(2 * (1 - 2 * G2) * (1 / G2 - 2)) * t + ((float)(-2 * (1 - 2 * G2) * (1 - 2 * G2)) + a0);
        float x1 = x0 - (float)(1 - 2 * G2);
        float y1 = y0 - (float)(1 - 2 * G2);
        value += (a1 * a1) * (a1 * a1) * GradCoord(seed, i1, j1, x1, y1);

        float xmyi = xi - yi;
        if (t > G2)
        {
            if (xi + xmyi > 1)
            {
                float x2 = x0 + (float)(3 * G2 - 2);
                float y2 = y0 + (float)(3 * G2 - 1);
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i + (PrimeX << 1), j + PrimeY, x2, y2);
            }
            else
            {
                float x2 = x0 + (float)G2;
                float y2 = y0 + (float)(G2 - 1);
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i, j + PrimeY, x2, y2);
            }

            if (yi - xmyi > 1)
            {
                float x3 = x0 + (float)(3 * G2 - 1);
                float y3 = y0 + (float)(3 * G2 - 2);
                float a3 = (2.0f / 3.0f) - x3 * x3 - y3 * y3;
                if (a3 > 0)
                    value += (a3 * a3) * (a3 * a3) * GradCoord(seed, i + PrimeX, j + (PrimeY << 1), x3, y3);
            }
            else
            {
                float x3 = x0 + (float)(G2 - 1);
                float y3 = y0 + (float)G2;
                float a3 = (2.0f / 3.0f) - x3 * x3 - y3 * y3;
                if (a3 > 0)
                    value += (a3 * a3) * (a3 * a3) * GradCoord(seed, i + PrimeX, j, x3, y3);
            }
        }
        else
        {
            if (xi + xmyi < 0)
            {
                float x2 = x0 + (float)(1 - G2);
                float y2 = y0 - (float)G2;
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i - PrimeX, j, x2, y2);
            }
            else
            {
                float x2 = x0 + (float)(G2 - 1);
                float y2 = y0 + (float)G2;
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i + PrimeX, j, x2, y2);
            }

            if (yi < xmyi)
            {
                float x2 = x0 - (float)G2;
                float y2 = y0 - (float)(G2 - 1);
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i, j - PrimeY, x2, y2);
            }
            else
            {
                float x2 = x0 + (float)G2;
                float y2 = y0 + (float)(G2 - 1);
                float a2 = (2.0f / 3.0f) - x2 * x2 - y2 * y2;
                if (a2 > 0)
                    value += (a2 * a2) * (a2 * a2) * GradCoord(seed, i, j + PrimeY, x2, y2);
            }
        }

        return value * 18.24196194486065f;
    }
}

/// <summary>OpenSimplex2D（TFC 版：FNL OpenSimplex2S 包装）</summary>
public sealed class OpenSimplex2D : Noise2D
{
    internal readonly FNLite fnl;
    private double frequency;
    private double midpoint, amplitude;

    public OpenSimplex2D(long seed)
        : this((int)(seed ^ (seed >> 32))) { }

    public OpenSimplex2D(int seed)
    {
        fnl = new FNLite(seed);
        fnl.SetFrequency(1f);
        fnl.SetNoiseType(FNLite.NoiseType.OpenSimplex2S);
        fnl.SetFractalOctaves(1);
        frequency = 1;
        midpoint = 0;
        amplitude = 1;
    }

    public override double Noise(double x, double z) => midpoint + fnl.GetNoise(x, z) * amplitude;

    public new OpenSimplex2D Octaves(int octaves)
    {
        fnl.SetFractalOctaves(octaves);
        fnl.SetFractalType(FNLite.FractalType.FBm);
        return Spread(1f / (1 << (octaves - 1))); // 历史原因：多数调用方期待这个缩放
    }

    public new OpenSimplex2D Spread(double scaleFactor)
    {
        frequency *= scaleFactor;
        fnl.SetFrequency((float)frequency);
        return this;
    }

    public new OpenSimplex2D Scaled(double min, double max) => Scaled(-1, 1, min, max);

    public new OpenSimplex2D Scaled(double oldMin, double oldMax, double min, double max)
    {
        midpoint = (max + min) / 2;
        amplitude = (max - min) / 2;
        return this;
    }

    internal double GetAmplitude() => amplitude;
}

/// <summary>Cellular2D（TFC 修改版 FastNoiseLite Worley）</summary>
public sealed class Cellular2D : Noise2D
{
    public record Cell(double x, double y, int cx, int cy, double nx, double ny, double f1, double f2, double noise, double angle);

    private readonly double jitter;
    private readonly int seed;
    private readonly int sample;
    private double frequency;

    public Cellular2D(long seed) : this(seed, 0.43701595f, 1) { }
    public Cellular2D(long seed, int sample) : this(seed, 0.43701595f, sample) { }
    public Cellular2D(long seed, float jitter, int sample)
    {
        // HashCommon.long2int
        this.seed = (int)(seed ^ (seed >> 32));
        this.frequency = 1;
        this.jitter = jitter;
        this.sample = sample;
    }

    public override double Noise(double x, double y) => GetCell(x, y).noise;

    public new Cellular2D Spread(double scaleFactor)
    {
        frequency *= scaleFactor;
        return this;
    }

    public Noise2D Then(Func<Cell, double> f)
        => Fn((x, y) => f(GetCell(x, y)));

    public Cell GetCell(double x, double y)
    {
        x *= frequency;
        y *= frequency;

        const int primeX = 501125321;
        const int primeY = 1136930381;

        int xr = FNLite.FastFloor(x);
        int yr = FNLite.FastFloor(y);

        double distance0 = double.MaxValue;
        double distance1 = double.MaxValue;
        double angle0 = -1;
        double thisCenterX = 0;
        double thisCenterY = 0;
        double neighborCenterX = 0;
        double neighborCenterY = 0;
        int closestHash = 0;

        int xPrimed = (xr - sample) * primeX;
        int yPrimedBase = (yr - sample) * primeY;

        for (int xi = xr - sample; xi <= xr + sample; xi++)
        {
            int yPrimed = yPrimedBase;
            for (int yi = yr - sample; yi <= yr + sample; yi++)
            {
                int hash = FNLite.Hash(seed, xPrimed, yPrimed);
                int idx = hash & (255 << 1);

                double vecX = xi + FNLite.RandVecs2D[idx] * jitter;
                double vecY = yi + FNLite.RandVecs2D[idx | 1] * jitter;

                double newDistanceX = vecX - x;
                double newDistanceY = vecY - y;
                double newAngle = Helpers.DiamondAngle(newDistanceX, newDistanceY);
                double newDistance = newDistanceX * newDistanceX + newDistanceY * newDistanceY;

                double oldDist1 = distance1;
                distance1 = FNLite.FastMax(FNLite.FastMin(distance1, newDistance), distance0);
                if (newDistance < distance0)
                {
                    distance0 = newDistance;
                    angle0 = newAngle;
                    closestHash = hash;
                    neighborCenterX = thisCenterX;
                    neighborCenterY = thisCenterY;
                    thisCenterX = vecX;
                    thisCenterY = vecY;
                }
                else if (distance1 != oldDist1)
                {
                    neighborCenterX = vecX;
                    neighborCenterY = vecY;
                }
                yPrimed += primeY;
            }
            xPrimed += primeX;
        }

        // 关键：noise 的 float 语义（int * (1f/2^31) → float → double）
        float noiseF = closestHash * (1f / 2147483648f);
        return new Cell(
            thisCenterX / frequency, thisCenterY / frequency,
            0, 0, // cx/cy 未使用（java 版有记录但任务未用）
            neighborCenterX / frequency, neighborCenterY / frequency,
            distance0, distance1,
            noiseF, angle0);
    }
}

/// <summary>TFC Helpers 数学工具（diamondAngle 等）</summary>
public static class Helpers
{
    public static float Lerp(float delta, float start, float end) => start + delta * (end - start);
    public static double Lerp(double delta, double start, double end) => start + delta * (end - start);

    public static double DiamondAngle(double x, double y)
    {
        if (y >= 0)
            return (x >= 0 ? y / (x + y) : 1 - x / (-x + y));
        else
            return (x < 0 ? 2 - y / (-x - y) : 3 + x / (x - y));
    }

    public static double HashDouble(double input, int index)
    {
        long inputBits = BitConverter.DoubleToInt64Bits(input);
        long x = Mix64(inputBits + index);
        return (long)((ulong)x >> 11) * 1.1102230246251565E-16;
    }

    public static long Mix64(long x)
    {
        x ^= (long)((ulong)x >> 33);
        x *= unchecked((long)0xff51afd7ed558ccdUL);
        x ^= (long)((ulong)x >> 33);
        x *= unchecked((long)0xc4ceb9fe1a85ec53UL);
        x ^= (long)((ulong)x >> 33);
        return x;
    }
}