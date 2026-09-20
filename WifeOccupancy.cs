using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Fires OccupancyManager-lite: one wife (or player) per chair/bed/fire/station.
    /// </summary>
    internal static class WifeOccupancy
    {
        private static readonly Dictionary<int, float> LockedUntil = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> LockedBy = new Dictionary<int, int>();

        internal static bool TryOccupy(Object target, Object owner, float seconds)
        {
            if (target == null || owner == null)
            {
                return false;
            }

            var id = target.GetInstanceID();
            var oid = owner.GetInstanceID();
            Cleanup();

            if (LockedUntil.TryGetValue(id, out var until) && until > Time.time)
            {
                if (LockedBy.TryGetValue(id, out var by) && by == oid)
                {
                    LockedUntil[id] = Time.time + Mathf.Max(1f, seconds);
                    return true;
                }

                return false;
            }

            LockedUntil[id] = Time.time + Mathf.Max(1f, seconds);
            LockedBy[id] = oid;
            return true;
        }

        internal static void Release(Object target, Object owner)
        {
            if (target == null)
            {
                return;
            }

            var id = target.GetInstanceID();
            if (owner != null && LockedBy.TryGetValue(id, out var by) && by != owner.GetInstanceID())
            {
                return;
            }

            LockedUntil.Remove(id);
            LockedBy.Remove(id);
        }

        internal static bool IsFree(Object target, Object owner = null)
        {
            if (target == null)
            {
                return false;
            }

            Cleanup();
            var id = target.GetInstanceID();
            if (!LockedUntil.TryGetValue(id, out var until) || until <= Time.time)
            {
                return true;
            }

            if (owner != null && LockedBy.TryGetValue(id, out var by) && by == owner.GetInstanceID())
            {
                return true;
            }

            return false;
        }

        private static void Cleanup()
        {
            if (LockedUntil.Count == 0)
            {
                return;
            }

            var dead = new List<int>();
            foreach (var kv in LockedUntil)
            {
                if (kv.Value <= Time.time)
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var id in dead)
            {
                LockedUntil.Remove(id);
                LockedBy.Remove(id);
            }
        }
    }
}
