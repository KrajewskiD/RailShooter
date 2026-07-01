using UnityEngine;
using Unity.Collections;

[System.Serializable]
public struct RewriteFastNoiseLite
{
    public enum NoiseType : byte { Perlin = 0, SimplexNoise = 1, Worley = 2, Cellular = Worley }
    public enum FractalType : byte { None = 0, FBm = 1, Ridged = 2 }
    public enum CellularDistanceFunction : byte { Euclidean = 0, EuclideanSq = 1, Manhattan = 2, Hybrid = 3 }
    public enum CellularReturnType : byte { CellValue = 0, Distance = 1, Distance2 = 2, Distance2Add = 3, Distance2Sub = 4 }

    public NoiseType noiseType;
    public FractalType fractalType;
    public CellularDistanceFunction cellularDistance;
    public CellularReturnType cellularReturn;
    public NoiseHashMode hashMode;
    public int seed;
    public float frequency;
    [Range(1, 8)] public int octaves;
    public float lacunarity;
    public float gain;
    [Range(0f, 1f)] public float cellularJitter;

    public static RewriteFastNoiseLite Default(int seed = 1337)
    {
        return new RewriteFastNoiseLite
        {
            seed = seed,
            noiseType = NoiseType.Perlin,
            fractalType = FractalType.None,
            cellularDistance = CellularDistanceFunction.EuclideanSq,
            cellularReturn = CellularReturnType.Distance,
            frequency = 0.1f,
            octaves = 1,
            lacunarity = 2f,
            gain = 0.5f,
            cellularJitter = 0.45f
        };
    }

    public float GetNoise(float x, float y, float z)
    {
        return GetNoise(x, y, z, default);
    }

    public float GetNoise(float x, float y, float z, NativeArray<int> permutation)
    {
        float fx = x * frequency, fy = y * frequency, fz = z * frequency;
        switch (fractalType)
        {
            case FractalType.FBm:    return FBm(fx, fy, fz, permutation);
            case FractalType.Ridged: return Ridged(fx, fy, fz, permutation);
            default:                 return Single(seed, fx, fy, fz, permutation);
        }
    }

    public float GetNoise2D(float x, float y)
    {
        return GetNoise2D(x, y, default);
    }

    public float GetNoise2D(float x, float y, NativeArray<int> permutation)
    {
        float fx = x * frequency;
        float fy = y * frequency;
        switch (fractalType)
        {
            case FractalType.FBm:    return FBm2D(fx, fy, permutation);
            case FractalType.Ridged: return Ridged2D(fx, fy, permutation);
            default:                 return Single2D(seed, fx, fy, permutation);
        }
    }

    public float GetNoise(Vector3 p) => GetNoise(p.x, p.y, p.z);
    public float GetNoise(Vector3 p, NativeArray<int> permutation) => GetNoise(p.x, p.y, p.z, permutation);

    private float Single(int s, float x, float y, float z, NativeArray<int> permutation)
    {
        if (hashMode == NoiseHashMode.PermutationTable512 && permutation.IsCreated && permutation.Length >= 512)
        {
            switch (noiseType)
            {
                case NoiseType.Perlin:       return Perlin3DPerm(permutation, x, y, z);
                case NoiseType.SimplexNoise: return SimplexNoise.Noise3D(x, y, z, permutation);
                case NoiseType.Worley:       return Worley3D(s, x, y, z, cellularJitter);
                default: return 0f;
            }
        }

        switch (noiseType)
        {
            case NoiseType.Perlin:       return Perlin3D(s, x, y, z);
            case NoiseType.SimplexNoise: return SimplexNoise.Noise3D(s, x, y, z);
            case NoiseType.Worley:       return Worley3D(s, x, y, z, cellularJitter);
            default: return 0f;
        }
    }

    private float Single2D(int s, float x, float y, NativeArray<int> permutation)
    {
        if (hashMode == NoiseHashMode.PermutationTable512 && permutation.IsCreated && permutation.Length >= 512)
        {
            switch (noiseType)
            {
                case NoiseType.Perlin:       return Perlin2DPerm(permutation, x, y);
                case NoiseType.SimplexNoise: return SimplexNoise.Noise2D(x, y, permutation);
                case NoiseType.Worley:       return Worley2D(s, x, y, cellularJitter);
                default: return 0f;
            }
        }

        switch (noiseType)
        {
            case NoiseType.Perlin:       return Perlin2D(s, x, y);
            case NoiseType.SimplexNoise: return SimplexNoise.Noise2D(s, x, y);
            case NoiseType.Worley:       return Worley2D(s, x, y, cellularJitter);
            default: return 0f;
        }
    }

    private float FBm(float x, float y, float z, NativeArray<int> permutation)
    {
        float sum = 0f, amp = 1f, maxAmp = 0f;
        int s = seed;
        int oct = octaves < 1 ? 1 : octaves;
        for (int i = 0; i < oct; i++)
        {
            sum += Single(s, x, y, z, permutation) * amp;
            maxAmp += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity; z *= lacunarity;
            s = unchecked(s + 0x68E31DA4);
        }
        return sum / maxAmp;
    }

    private float FBm2D(float x, float y, NativeArray<int> permutation)
    {
        float sum = 0f, amp = 1f, maxAmp = 0f;
        int s = seed;
        int oct = octaves < 1 ? 1 : octaves;
        for (int i = 0; i < oct; i++)
        {
            sum += Single2D(s, x, y, permutation) * amp;
            maxAmp += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity;
            s = unchecked(s + 0x68E31DA4);
        }
        return sum / maxAmp;
    }

    private float Ridged(float x, float y, float z, NativeArray<int> permutation)
    {
        float sum = 0f, amp = 1f, maxAmp = 0f;
        int s = seed;
        int oct = octaves < 1 ? 1 : octaves;
        for (int i = 0; i < oct; i++)
        {
            float n = Single(s, x, y, z, permutation);
            float absN = n < 0f ? -n : n;
            sum += (1f - 2f * absN) * amp;
            maxAmp += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity; z *= lacunarity;
            s = unchecked(s + 0x68E31DA4);
        }
        float result = sum / maxAmp;
        return result;
    }

    private float Ridged2D(float x, float y, NativeArray<int> permutation)
    {
        float sum = 0f, amp = 1f, maxAmp = 0f;
        int s = seed;
        int oct = octaves < 1 ? 1 : octaves;
        for (int i = 0; i < oct; i++)
        {
            float n = Single2D(s, x, y, permutation);
            float absN = n < 0f ? -n : n;
            sum += (1f - 2f * absN) * amp;
            maxAmp += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity;
            s = unchecked(s + 0x68E31DA4);
        }
        return sum / maxAmp;
    }

    private const int PrimeX = 501125321;
    private const int PrimeY = 1136930381;
    private const int PrimeZ = 1720413743;

    private static int Hash(int s, int xPrimed, int yPrimed, int zPrimed)
    {
        int h = s ^ xPrimed ^ yPrimed ^ zPrimed;
        h *= unchecked((int)0x27d4eb2d);
        return h;
    }

    private static int Hash(int s, int xPrimed, int yPrimed)
    {
        int h = s ^ xPrimed ^ yPrimed;
        h *= unchecked((int)0x27d4eb2d);
        return h;
    }

    private static int FastFloor(float f)
    {
        int i = (int)f;
        return f < i ? i - 1 : i;
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Grad(int hash, float xd, float yd, float zd)
    {
        hash ^= hash >> 15;
        int h = hash & 15;
        float u = h < 8 ? xd : yd;
        float v = h < 4 ? yd : (h == 12 || h == 14 ? xd : zd);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static float Grad(int hash, float xd, float yd)
    {
        hash ^= hash >> 15;
        switch (hash & 7)
        {
            case 0: return xd + yd;
            case 1: return -xd + yd;
            case 2: return xd - yd;
            case 3: return -xd - yd;
            case 4: return xd;
            case 5: return -xd;
            case 6: return yd;
            default: return -yd;
        }
    }

    private static float Perlin2D(int seed, float x, float y)
    {
        int xi = FastFloor(x), yi = FastFloor(y);

        float xd0 = x - xi, yd0 = y - yi;
        float xd1 = xd0 - 1f, yd1 = yd0 - 1f;

        float xs = Fade(xd0), ys = Fade(yd0);

        int x0 = xi * PrimeX, x1 = x0 + PrimeX;
        int y0 = yi * PrimeY, y1 = y0 + PrimeY;

        float n00 = Grad(Hash(seed, x0, y0), xd0, yd0);
        float n10 = Grad(Hash(seed, x1, y0), xd1, yd0);
        float n01 = Grad(Hash(seed, x0, y1), xd0, yd1);
        float n11 = Grad(Hash(seed, x1, y1), xd1, yd1);

        return Lerp(Lerp(n00, n10, xs), Lerp(n01, n11, xs), ys) * 1.4247691f;
    }

    private static float Perlin2DPerm(NativeArray<int> perm, float x, float y)
    {
        int xi = FastFloor(x), yi = FastFloor(y);

        float xd0 = x - xi, yd0 = y - yi;
        float xd1 = xd0 - 1f, yd1 = yd0 - 1f;

        float xs = Fade(xd0), ys = Fade(yd0);

        int xi0 = xi & 255;
        int yi0 = yi & 255;

        int h00 = perm[xi0 + perm[yi0]];
        int h10 = perm[xi0 + 1 + perm[yi0]];
        int h01 = perm[xi0 + perm[yi0 + 1]];
        int h11 = perm[xi0 + 1 + perm[yi0 + 1]];

        float n00 = Grad(h00, xd0, yd0);
        float n10 = Grad(h10, xd1, yd0);
        float n01 = Grad(h01, xd0, yd1);
        float n11 = Grad(h11, xd1, yd1);

        return Lerp(Lerp(n00, n10, xs), Lerp(n01, n11, xs), ys) * 1.4247691f;
    }

    private static float Perlin3D(int seed, float x, float y, float z)
    {
        int xi = FastFloor(x), yi = FastFloor(y), zi = FastFloor(z);

        float xd0 = x - xi, yd0 = y - yi, zd0 = z - zi;
        float xd1 = xd0 - 1f, yd1 = yd0 - 1f, zd1 = zd0 - 1f;

        float xs = Fade(xd0), ys = Fade(yd0), zs = Fade(zd0);

        int x0 = xi * PrimeX, x1 = x0 + PrimeX;
        int y0 = yi * PrimeY, y1 = y0 + PrimeY;
        int z0 = zi * PrimeZ, z1 = z0 + PrimeZ;

        float n000 = Grad(Hash(seed, x0, y0, z0), xd0, yd0, zd0);
        float n100 = Grad(Hash(seed, x1, y0, z0), xd1, yd0, zd0);
        float n010 = Grad(Hash(seed, x0, y1, z0), xd0, yd1, zd0);
        float n110 = Grad(Hash(seed, x1, y1, z0), xd1, yd1, zd0);
        float n001 = Grad(Hash(seed, x0, y0, z1), xd0, yd0, zd1);
        float n101 = Grad(Hash(seed, x1, y0, z1), xd1, yd0, zd1);
        float n011 = Grad(Hash(seed, x0, y1, z1), xd0, yd1, zd1);
        float n111 = Grad(Hash(seed, x1, y1, z1), xd1, yd1, zd1);

        float a = Lerp(Lerp(n000, n100, xs), Lerp(n010, n110, xs), ys);
        float b = Lerp(Lerp(n001, n101, xs), Lerp(n011, n111, xs), ys);
        return Lerp(a, b, zs) * 0.96492142f;
    }

    private static float Perlin3DPerm(NativeArray<int> perm, float x, float y, float z)
    {
        int xi = FastFloor(x), yi = FastFloor(y), zi = FastFloor(z);

        float xd0 = x - xi, yd0 = y - yi, zd0 = z - zi;
        float xd1 = xd0 - 1f, yd1 = yd0 - 1f, zd1 = zd0 - 1f;

        float xs = Fade(xd0), ys = Fade(yd0), zs = Fade(zd0);

        int xi0 = xi & 255;
        int yi0 = yi & 255;
        int zi0 = zi & 255;

        int y0z0 = perm[yi0 + perm[zi0]];
        int y1z0 = perm[yi0 + 1 + perm[zi0]];
        int y0z1 = perm[yi0 + perm[zi0 + 1]];
        int y1z1 = perm[yi0 + 1 + perm[zi0 + 1]];

        float n000 = Grad(perm[xi0 + y0z0], xd0, yd0, zd0);
        float n100 = Grad(perm[xi0 + 1 + y0z0], xd1, yd0, zd0);
        float n010 = Grad(perm[xi0 + y1z0], xd0, yd1, zd0);
        float n110 = Grad(perm[xi0 + 1 + y1z0], xd1, yd1, zd0);
        float n001 = Grad(perm[xi0 + y0z1], xd0, yd0, zd1);
        float n101 = Grad(perm[xi0 + 1 + y0z1], xd1, yd0, zd1);
        float n011 = Grad(perm[xi0 + y1z1], xd0, yd1, zd1);
        float n111 = Grad(perm[xi0 + 1 + y1z1], xd1, yd1, zd1);

        float a = Lerp(Lerp(n000, n100, xs), Lerp(n010, n110, xs), ys);
        float b = Lerp(Lerp(n001, n101, xs), Lerp(n011, n111, xs), ys);
        return Lerp(a, b, zs) * 0.96492142f;
    }

    private float Worley3D(int seed, float x, float y, float z, float jitter)
    {
        int xr = FastFloor(x);
        int yr = FastFloor(y);
        int zr = FastFloor(z);
        float cellJitter = jitter * 0.39614353f;

        float minDist = float.MaxValue;
        float minDist2 = float.MaxValue;

        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = xr + dx, cy = yr + dy, cz = zr + dz;
            int h = Hash(seed, cx * PrimeX, cy * PrimeY, cz * PrimeZ);
            float jx = ((h         & 0x3FF) / 511.5f - 1f) * cellJitter;
            float jy = (((h >> 10) & 0x3FF) / 511.5f - 1f) * cellJitter;
            float jz = (((h >> 20) & 0x3FF) / 511.5f - 1f) * cellJitter;
            float vx = cx + 0.5f + jx - x;
            float vy = cy + 0.5f + jy - y;
            float vz = cz + 0.5f + jz - z;

            float d;
            switch (cellularDistance)
            {
                case CellularDistanceFunction.Manhattan:
                    d = (vx < 0 ? -vx : vx) + (vy < 0 ? -vy : vy) + (vz < 0 ? -vz : vz);
                    break;
                case CellularDistanceFunction.Hybrid:
                {
                    float ax = vx < 0 ? -vx : vx;
                    float ay = vy < 0 ? -vy : vy;
                    float az = vz < 0 ? -vz : vz;
                    d = (ax + ay + az) + (vx * vx + vy * vy + vz * vz);
                    break;
                }
                case CellularDistanceFunction.Euclidean:
                    d = (float)System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    break;
                case CellularDistanceFunction.EuclideanSq:
                default:
                    d = vx * vx + vy * vy + vz * vz;
                    break;
            }

            if (d < minDist)
            {
                minDist2 = minDist;
                minDist = d;
            }
            else if (d < minDist2)
            {
                minDist2 = d;
            }
        }

        return FinalizeWorleyReturn(minDist, minDist2);
    }

    private float Worley2D(int seed, float x, float y, float jitter)
    {
        int xr = FastFloor(x);
        int yr = FastFloor(y);
        float cellJitter = jitter * 0.43701595f;

        float minDist = float.MaxValue;
        float minDist2 = float.MaxValue;

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = xr + dx, cy = yr + dy;
            int h = Hash(seed, cx * PrimeX, cy * PrimeY);
            float jx = ((h & 0x3FF) / 511.5f - 1f) * cellJitter;
            float jy = (((h >> 10) & 0x3FF) / 511.5f - 1f) * cellJitter;
            float vx = cx + 0.5f + jx - x;
            float vy = cy + 0.5f + jy - y;

            float d;
            switch (cellularDistance)
            {
                case CellularDistanceFunction.Manhattan:
                    d = (vx < 0 ? -vx : vx) + (vy < 0 ? -vy : vy);
                    break;
                case CellularDistanceFunction.Hybrid:
                {
                    float ax = vx < 0 ? -vx : vx;
                    float ay = vy < 0 ? -vy : vy;
                    d = (ax + ay) + (vx * vx + vy * vy);
                    break;
                }
                case CellularDistanceFunction.Euclidean:
                    d = (float)System.Math.Sqrt(vx * vx + vy * vy);
                    break;
                case CellularDistanceFunction.EuclideanSq:
                default:
                    d = vx * vx + vy * vy;
                    break;
            }

            if (d < minDist)
            {
                minDist2 = minDist;
                minDist = d;
            }
            else if (d < minDist2)
            {
                minDist2 = d;
            }
        }

        return FinalizeWorleyReturn(minDist, minDist2);
    }

    private float FinalizeWorleyReturn(float minDist, float minDist2)
    {
        float f1 = WorleyOutputDistance(minDist);
        float f2 = WorleyOutputDistance(minDist2);

        switch (cellularReturn)
        {
            case CellularReturnType.CellValue:
                return f1 - 1f;
            case CellularReturnType.Distance2:
                return f2 - 1f;
            case CellularReturnType.Distance2Add:
                return (f2 + f1) * 0.5f - 1f;
            case CellularReturnType.Distance2Sub:
                return f2 - f1 - 1f;
            case CellularReturnType.Distance:
            default:
                return f1 - 1f;
        }
    }

    private float WorleyOutputDistance(float distance)
    {
        if (cellularDistance == CellularDistanceFunction.EuclideanSq)
            return (float)System.Math.Sqrt(distance);

        return distance;
    }
}
