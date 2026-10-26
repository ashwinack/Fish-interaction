using UnityEngine;

namespace Aquarium
{
    /// <summary>Small deterministic value-noise helpers for procedural geometry.</summary>
    public static class Noise
    {
        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        public static float Value3(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float fx = Smooth(p.x - xi), fy = Smooth(p.y - yi), fz = Smooth(p.z - zi);
            float x00 = Mathf.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), fx);
            float x10 = Mathf.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), fx);
            float x01 = Mathf.Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), fx);
            float x11 = Mathf.Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        public static float Fbm3(Vector3 p, int octaves = 4)
        {
            float v = 0f, a = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                v += a * Value3(p);
                norm += a;
                p = p * 2.03f + new Vector3(17.1f, 3.7f, 9.2f);
                a *= 0.5f;
            }
            return v / norm;
        }
    }

    /// <summary>
    /// Seabed height field shared by the terrain mesh and the fish (so fish never swim into the sand).
    /// </summary>
    public static class Seabed
    {
        static float s_BaseY = -6f;
        static float s_HalfWidth = 14f;
        static Vector2 s_Offset;

        public static void Configure(TankSettings tank, int seed)
        {
            s_BaseY = tank.seabedDepth;
            s_HalfWidth = tank.halfWidth;
            var rng = new System.Random(seed);
            s_Offset = new Vector2((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f);
        }

        public static float Height(float x, float z)
        {
            float h = s_BaseY;
            h += (Mathf.PerlinNoise(x * 0.07f + s_Offset.x, z * 0.07f + s_Offset.y) - 0.5f) * 1.8f;
            h += (Mathf.PerlinNoise(x * 0.25f + s_Offset.y, z * 0.25f + s_Offset.x) - 0.5f) * 0.45f;
            // The tank floor rises into rocky reef slopes behind the wall screens.
            float side = Mathf.InverseLerp(s_HalfWidth * 0.5f, s_HalfWidth + 2f, Mathf.Abs(x));
            h += side * side * 6f;
            return h;
        }
    }
}
