using System.Runtime.CompilerServices;
using Unity.Collections;

public static class SimplexNoise
{
    private const MethodImplOptions Inline = MethodImplOptions.AggressiveInlining;

    [MethodImpl(Inline)]
    public static float Noise2D(int seed, float x, float y)
    {
        return Noise2DSimplexReference(seed, x, y);
    }

    [MethodImpl(Inline)]
    public static float Noise2D(float x, float y, NativeArray<int> perm)
    {
        return Noise2DSimplexPermutation(x, y, perm);
    }

    [MethodImpl(Inline)]
    public static float Noise3D(int seed, float x, float y, float z)
    {
        ApplySeedOffset3D(seed, ref x, ref y, ref z);
        return Noise3DSimplexReference(x, y, z);
    }

    [MethodImpl(Inline)]
    public static float Noise3D(float x, float y, float z, NativeArray<int> perm)
    {
        return Noise3DSimplexPermutation(x, y, z, perm);
    }

    [MethodImpl(Inline)]
    private static float Noise3DSimplexReference(float xIn, float yIn, float zIn)
    {
        double x = xIn;
        double y = yIn;
        double z = zIn;

        double skew = (x + y + z) / 3.0;
        int i = FastFloor(x + skew);
        int j = FastFloor(y + skew);
        int k = FastFloor(z + skew);

        double unskew = (i + j + k) / 6.0;
        double u = x - i + unskew;
        double v = y - j + unskew;
        double w = z - k + unskew;

        int a0 = 0;
        int a1 = 0;
        int a2 = 0;

        int hi = u >= w ? (u >= v ? 0 : 1) : (v >= w ? 1 : 2);
        int lo = u < w ? (u < v ? 0 : 1) : (v < w ? 1 : 2);

        double value =
            Kernel(hi, ref a0, ref a1, ref a2, i, j, k, u, v, w) +
            Kernel(3 - hi - lo, ref a0, ref a1, ref a2, i, j, k, u, v, w) +
            Kernel(lo, ref a0, ref a1, ref a2, i, j, k, u, v, w) +
            Kernel(0, ref a0, ref a1, ref a2, i, j, k, u, v, w);

        return (float)value;
    }

    [MethodImpl(Inline)]
    private static float Noise3DSimplexPermutation(float xIn, float yIn, float zIn, NativeArray<int> perm)
    {
        double x = xIn;
        double y = yIn;
        double z = zIn;

        double skew = (x + y + z) / 3.0;
        int i = FastFloor(x + skew);
        int j = FastFloor(y + skew);
        int k = FastFloor(z + skew);

        double unskew = (i + j + k) / 6.0;
        double u = x - i + unskew;
        double v = y - j + unskew;
        double w = z - k + unskew;

        int a0 = 0;
        int a1 = 0;
        int a2 = 0;

        int hi = u >= w ? (u >= v ? 0 : 1) : (v >= w ? 1 : 2);
        int lo = u < w ? (u < v ? 0 : 1) : (v < w ? 1 : 2);

        double value =
            KernelPerm(hi, ref a0, ref a1, ref a2, i, j, k, u, v, w, perm) +
            KernelPerm(3 - hi - lo, ref a0, ref a1, ref a2, i, j, k, u, v, w, perm) +
            KernelPerm(lo, ref a0, ref a1, ref a2, i, j, k, u, v, w, perm) +
            KernelPerm(0, ref a0, ref a1, ref a2, i, j, k, u, v, w, perm);

        return (float)value;
    }

    [MethodImpl(Inline)]
    private static float Noise2DSimplexReference(int seed, float x, float y)
    {
        const float F2 = 0.36602540378443864676372317075294f;
        const float G2 = 0.21132486540518711774542560974902f;

        float skew = (x + y) * F2;
        int i = FastFloor(x + skew);
        int j = FastFloor(y + skew);

        float unskew = (i + j) * G2;
        float x0 = x - (i - unskew);
        float y0 = y - (j - unskew);

        int i1;
        int j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        float value =
            Kernel2D(seed, i, j, x0, y0) +
            Kernel2D(seed, i + i1, j + j1, x1, y1) +
            Kernel2D(seed, i + 1, j + 1, x2, y2);

        value *= 70f;
        if (value > 1f) return 1f;
        if (value < -1f) return -1f;
        return value;
    }

    [MethodImpl(Inline)]
    private static float Kernel2D(int seed, int i, int j, float x, float y)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f) return 0f;

        t *= t;
        return t * t * Grad2D(Hash2D(seed, i, j), x, y);
    }

    [MethodImpl(Inline)]
    private static float Noise2DSimplexPermutation(float x, float y, NativeArray<int> perm)
    {
        const float F2 = 0.36602540378443864676372317075294f;
        const float G2 = 0.21132486540518711774542560974902f;

        float skew = (x + y) * F2;
        int i = FastFloor(x + skew);
        int j = FastFloor(y + skew);

        float unskew = (i + j) * G2;
        float x0 = x - (i - unskew);
        float y0 = y - (j - unskew);

        int i1;
        int j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        int ii = i & 255;
        int jj = j & 255;

        int g0 = perm[ii + perm[jj]];
        int g1 = perm[ii + i1 + perm[jj + j1]];
        int g2 = perm[ii + 1 + perm[jj + 1]];

        float value =
            Kernel2DPerm(g0, x0, y0) +
            Kernel2DPerm(g1, x1, y1) +
            Kernel2DPerm(g2, x2, y2);

        value *= 70f;
        if (value > 1f) return 1f;
        if (value < -1f) return -1f;
        return value;
    }

    [MethodImpl(Inline)]
    private static float Kernel2DPerm(int hash, float x, float y)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f) return 0f;

        t *= t;
        return t * t * Grad2D(hash, x, y);
    }

    [MethodImpl(Inline)]
    private static int Hash2D(int seed, int i, int j)
    {
        unchecked
        {
            int hash = seed;
            hash ^= i * 501125321;
            hash ^= j * 1136930381;
            hash *= (int)0x27d4eb2d;
            hash ^= hash >> 15;
            return hash;
        }
    }

    [MethodImpl(Inline)]
    private static float Grad2D(int hash, float x, float y)
    {
        switch (hash & 7)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x;
            case 5: return -x;
            case 6: return y;
            default: return -y;
        }
    }

    [MethodImpl(Inline)]
    private static double Kernel(
        int a,
        ref int a0,
        ref int a1,
        ref int a2,
        int i,
        int j,
        int k,
        double u,
        double v,
        double w)
    {
        double skew = (a0 + a1 + a2) / 6.0;
        double x = u - a0 + skew;
        double y = v - a1 + skew;
        double z = w - a2 + skew;
        double t = 0.6 - x * x - y * y - z * z;

        int h = Shuffle(i + a0, j + a1, k + a2);
        Increment(ref a0, ref a1, ref a2, a);

        if (t < 0.0) return 0.0;

        int b5 = (h >> 5) & 1;
        int b4 = (h >> 4) & 1;
        int b3 = (h >> 3) & 1;
        int b2 = (h >> 2) & 1;
        int b = h & 3;

        double p = b == 1 ? x : b == 2 ? y : z;
        double q = b == 1 ? y : b == 2 ? z : x;
        double r = b == 1 ? z : b == 2 ? x : y;

        p = b5 == b3 ? -p : p;
        q = b5 == b4 ? -q : q;
        r = b5 != (b4 ^ b3) ? -r : r;

        t *= t;
        return 8.0 * t * t * (p + (b == 0 ? q + r : b2 == 0 ? q : r));
    }

    [MethodImpl(Inline)]
    private static double KernelPerm(
        int a,
        ref int a0,
        ref int a1,
        ref int a2,
        int i,
        int j,
        int k,
        double u,
        double v,
        double w,
        NativeArray<int> perm)
    {
        double skew = (a0 + a1 + a2) / 6.0;
        double x = u - a0 + skew;
        double y = v - a1 + skew;
        double z = w - a2 + skew;
        double t = 0.6 - x * x - y * y - z * z;

        int h = PermHash(i + a0, j + a1, k + a2, perm);
        Increment(ref a0, ref a1, ref a2, a);

        if (t < 0.0) return 0.0;

        int b5 = (h >> 5) & 1;
        int b4 = (h >> 4) & 1;
        int b3 = (h >> 3) & 1;
        int b2 = (h >> 2) & 1;
        int b = h & 3;

        double p = b == 1 ? x : b == 2 ? y : z;
        double q = b == 1 ? y : b == 2 ? z : x;
        double r = b == 1 ? z : b == 2 ? x : y;

        p = b5 == b3 ? -p : p;
        q = b5 == b4 ? -q : q;
        r = b5 != (b4 ^ b3) ? -r : r;

        t *= t;
        return 8.0 * t * t * (p + (b == 0 ? q + r : b2 == 0 ? q : r));
    }

    [MethodImpl(Inline)]
    private static int PermHash(int i, int j, int k, NativeArray<int> perm)
    {
        int ii = i & 255;
        int jj = j & 255;
        int kk = k & 255;
        return perm[ii + perm[jj + perm[kk]]];
    }

    [MethodImpl(Inline)]
    private static void Increment(ref int a0, ref int a1, ref int a2, int axis)
    {
        if (axis == 0) a0++;
        else if (axis == 1) a1++;
        else a2++;
    }

    [MethodImpl(Inline)]
    private static int Shuffle(int i, int j, int k)
    {
        return
            BitShuffle(i, j, k, 0) +
            BitShuffle(j, k, i, 1) +
            BitShuffle(k, i, j, 2) +
            BitShuffle(i, j, k, 3) +
            BitShuffle(j, k, i, 4) +
            BitShuffle(k, i, j, 5) +
            BitShuffle(i, j, k, 6) +
            BitShuffle(j, k, i, 7);
    }

    [MethodImpl(Inline)]
    private static int BitShuffle(int i, int j, int k, int bit)
    {
        return Table((Bit(i, bit) << 2) | (Bit(j, bit) << 1) | Bit(k, bit));
    }

    [MethodImpl(Inline)]
    private static int Bit(int value, int bit)
    {
        return (value >> bit) & 1;
    }

    [MethodImpl(Inline)]
    private static int Table(int index)
    {
        switch (index & 7)
        {
            case 0: return 0x15;
            case 1: return 0x38;
            case 2: return 0x32;
            case 3: return 0x2c;
            case 4: return 0x0d;
            case 5: return 0x13;
            case 6: return 0x07;
            default: return 0x2a;
        }
    }

    [MethodImpl(Inline)]
    private static int FastFloor(float value)
    {
        int integer = (int)value;
        return value < integer ? integer - 1 : integer;
    }

    [MethodImpl(Inline)]
    private static int FastFloor(double value)
    {
        int integer = (int)value;
        return value < integer ? integer - 1 : integer;
    }

    [MethodImpl(Inline)]
    private static void ApplySeedOffset3D(int seed, ref float x, ref float y, ref float z)
    {
        if (seed == 0) return;

        x += seed & 255;
        y += (seed >> 8) & 255;
        z += (seed >> 16) & 255;
    }
}
