using BedrockForge.Rendering.Blocks;
using UnityEngine;

namespace BedrockForge.Rendering.World
{
    public struct VoxelHit
    {
        public Vector3Int Block;
        /// <summary>The empty cell the ray came from: where a placed block goes.</summary>
        public Vector3Int Before;
        public float Distance;
    }

    /// <summary>
    /// Grid traversal (Amanatides &amp; Woo) through the decoded world, in Minecraft coordinates.
    /// Use <see cref="ToMinecraft(Ray)"/> to convert a Unity camera ray (Unity Z = -Minecraft Z).
    /// </summary>
    public static class VoxelRaycast
    {
        public static Ray ToMinecraft(Ray unityRay) =>
            new Ray(ToMinecraft(unityRay.origin), new Vector3(unityRay.direction.x, unityRay.direction.y, -unityRay.direction.z));

        public static Vector3 ToMinecraft(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z);

        public static Vector3 ToUnity(Vector3 minecraft) => new Vector3(minecraft.x, minecraft.y, -minecraft.z);

        public static bool Cast(WorldData world, Ray ray, float maxDistance, out VoxelHit hit, bool hitLiquids = false)
        {
            hit = default;
            var visuals = world.Registry.Snapshot();
            var dir = ray.direction.normalized;
            var cell = Vector3Int.FloorToInt(ray.origin);
            var before = cell;

            var step = new Vector3Int(dir.x > 0 ? 1 : -1, dir.y > 0 ? 1 : -1, dir.z > 0 ? 1 : -1);
            var tDelta = new Vector3(Inv(dir.x), Inv(dir.y), Inv(dir.z));
            var tMax = new Vector3(
                Boundary(ray.origin.x, cell.x, dir.x) * tDelta.x,
                Boundary(ray.origin.y, cell.y, dir.y) * tDelta.y,
                Boundary(ray.origin.z, cell.z, dir.z) * tDelta.z);

            float t = 0f;
            while (t <= maxDistance)
            {
                ushort id = world.GetBlock(cell.x, cell.y, cell.z);
                if (id != BlockRegistry.AirId && id < visuals.Length)
                {
                    var v = visuals[id];
                    if (!v.IsAir && (hitLiquids || v.Shape != BlockShape.Liquid))
                    {
                        hit = new VoxelHit { Block = cell, Before = before, Distance = t };
                        return true;
                    }
                }

                before = cell;
                if (tMax.x < tMax.y && tMax.x < tMax.z)
                {
                    t = tMax.x;
                    tMax.x += tDelta.x;
                    cell.x += step.x;
                }
                else if (tMax.y < tMax.z)
                {
                    t = tMax.y;
                    tMax.y += tDelta.y;
                    cell.y += step.y;
                }
                else
                {
                    t = tMax.z;
                    tMax.z += tDelta.z;
                    cell.z += step.z;
                }
            }
            return false;
        }

        private static float Inv(float d) => Mathf.Abs(d) < 1e-8f ? float.PositiveInfinity : Mathf.Abs(1f / d);

        private static float Boundary(float origin, int cell, float dir) => dir > 0 ? cell + 1 - origin : origin - cell;

        /// <summary>Ray against an axis-aligned box; returns the entry distance or null.</summary>
        public static float? IntersectBox(Ray ray, Vector3 min, Vector3 max)
        {
            float tMin = 0f, tMax = float.MaxValue;
            for (int a = 0; a < 3; a++)
            {
                float o = ray.origin[a], d = ray.direction[a];
                if (Mathf.Abs(d) < 1e-8f)
                {
                    if (o < min[a] || o > max[a]) return null;
                    continue;
                }
                float t1 = (min[a] - o) / d, t2 = (max[a] - o) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return null;
            }
            return tMin;
        }
    }
}
