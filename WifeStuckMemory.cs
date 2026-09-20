using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Avoid re-pathing into the same stuck cell (OfflineCompanions blacklist idea).
    /// </summary>
    internal static class WifeStuckMemory
    {
        private static readonly Dictionary<long, float> BlacklistUntil = new Dictionary<long, float>();

        internal static long CellKey(Vector3 world, float cellSize = 2.5f)
        {
            var x = Mathf.FloorToInt(world.x / cellSize);
            var z = Mathf.FloorToInt(world.z / cellSize);
            return ((long)x << 32) ^ (uint)z;
        }

        internal static void Mark(Vector3 world, float seconds = 25f)
        {
            BlacklistUntil[CellKey(world)] = Time.time + Mathf.Max(5f, seconds);
            Cleanup();
        }

        internal static bool IsBlocked(Vector3 world)
        {
            Cleanup();
            return BlacklistUntil.TryGetValue(CellKey(world), out var until) && until > Time.time;
        }

        internal static bool TryPickUnblocked(WifeHome home, float minFrac, float maxFrac, out Vector3 point)
        {
            point = home.HomePosition;
            for (var i = 0; i < 10; i++)
            {
                if (!home.TryPickPointInRadius(minFrac, maxFrac, out var p))
                {
                    continue;
                }

                if (!IsBlocked(p))
                {
                    point = p;
                    return true;
                }
            }

            return home.TryPickPointInRadius(minFrac, maxFrac, out point);
        }

        private static void Cleanup()
        {
            if (BlacklistUntil.Count < 24)
            {
                return;
            }

            var dead = new List<long>();
            foreach (var kv in BlacklistUntil)
            {
                if (kv.Value <= Time.time)
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var k in dead)
            {
                BlacklistUntil.Remove(k);
            }
        }
    }
}
