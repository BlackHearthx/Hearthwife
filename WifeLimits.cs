using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Hard caps so players cannot place an army of idols/wives.
    /// Default: 1 idol → 1 wife per world (SP homestead).
    /// </summary>
    internal static class WifeLimits
    {
        private static float _nextMaintainAt;

        internal static int MaxIdols =>
            PluginConfig.MaxIdols != null ? Mathf.Max(1, PluginConfig.MaxIdols.Value) : 1;

        internal static bool IsIdolPiece(Piece piece)
        {
            if (piece == null)
            {
                return false;
            }

            if (piece.GetComponent<WifeHome>() != null)
            {
                return true;
            }

            var n = piece.gameObject != null ? piece.gameObject.name : piece.name;
            if (string.IsNullOrEmpty(n))
            {
                return false;
            }

            return n.StartsWith(WifeIdol.PrefabName, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True for a real world idol (has a valid non-ghost ZDO).
        /// Hammer placement ghosts also carry WifeHome and must NOT count toward MaxIdols.
        /// </summary>
        internal static bool IsWorldIdol(WifeHome home)
        {
            if (home == null)
            {
                return false;
            }

            try
            {
                if (home.gameObject == null || !home.isActiveAndEnabled)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            var nview = home.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || nview.m_ghost)
            {
                return false;
            }

            var zdo = nview.GetZDO();
            return zdo != null && zdo.IsValid();
        }

        internal static int CountIdols(WifeHome except = null)
        {
            var n = 0;
            foreach (var home in Object.FindObjectsByType<WifeHome>(FindObjectsSortMode.None))
            {
                if (home == null || home == except || !IsWorldIdol(home))
                {
                    continue;
                }

                n++;
            }

            return n;
        }

        internal static bool CanPlaceAnotherIdol(WifeHome exceptGhost = null) =>
            CountIdols(exceptGhost) < MaxIdols;

        /// <summary>False for surplus idols left in old worlds after MaxIdols dropped to 1.</summary>
        internal static bool IsAllowedIdol(WifeHome home)
        {
            if (home == null)
            {
                return false;
            }

            if (CountIdols() <= MaxIdols)
            {
                return true;
            }

            return IsAmongAllowedIdols(home, MaxIdols);
        }

        /// <summary>True if this home may spawn/keep a wife under the global cap.</summary>
        internal static bool CanSpawnWifeFor(WifeHome home)
        {
            if (home == null || !IsAllowedIdol(home))
            {
                return false;
            }

            var max = MaxIdols;
            var others = 0;
            foreach (var wife in Object.FindObjectsByType<WifeAgent>(FindObjectsSortMode.None))
            {
                if (wife == null)
                {
                    continue;
                }

                if (wife.Home == home)
                {
                    continue;
                }

                others++;
                if (others >= max)
                {
                    return false;
                }
            }

            return true;
        }

        internal static WifeAgent FindExistingWifeFor(WifeHome home)
        {
            if (home == null)
            {
                return null;
            }

            foreach (var wife in Object.FindObjectsByType<WifeAgent>(FindObjectsSortMode.None))
            {
                if (wife == null)
                {
                    continue;
                }

                try
                {
                    if (wife.gameObject == null)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (wife.Home == home)
                {
                    return wife;
                }
            }

            return null;
        }

        private static bool IsAmongAllowedIdols(WifeHome home, int max)
        {
            if (!IsWorldIdol(home))
            {
                return false;
            }

            var all = Object.FindObjectsByType<WifeHome>(FindObjectsSortMode.None);
            var ranked = new System.Collections.Generic.List<WifeHome>(all.Length);
            foreach (var h in all)
            {
                if (IsWorldIdol(h))
                {
                    ranked.Add(h);
                }
            }

            ranked.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

            for (var i = 0; i < ranked.Count && i < max; i++)
            {
                if (ranked[i] == home)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Periodic mid-session cleanup (orphans + over-cap).</summary>
        internal static void TickMaintain()
        {
            if (Time.time < _nextMaintainAt)
            {
                return;
            }

            _nextMaintainAt = Time.time + 18f;
            MaintainNow();
        }

        internal static void MaintainNow()
        {
            PurgeOrphanWives();
            CullExtraWives();
        }

        /// <summary>Despawn wives with no live home (lost ref / destroyed idol).</summary>
        internal static void PurgeOrphanWives()
        {
            foreach (var wife in Object.FindObjectsByType<WifeAgent>(FindObjectsSortMode.None))
            {
                if (wife == null)
                {
                    continue;
                }

                var orphan = false;
                try
                {
                    if (wife.gameObject == null)
                    {
                        continue;
                    }

                    var home = wife.Home;
                    if (home == null || home.gameObject == null)
                    {
                        orphan = true;
                    }
                }
                catch
                {
                    orphan = true;
                }

                if (!orphan)
                {
                    continue;
                }

                try
                {
                    wife.Despawn();
                }
                catch
                {
                }
            }
        }

        /// <summary>Despawn wives that exceed the cap (keep at most MaxIdols).</summary>
        internal static void CullExtraWives(WifeHome preferKeep = null)
        {
            var max = MaxIdols;
            var wives = Object.FindObjectsByType<WifeAgent>(FindObjectsSortMode.None);
            if (wives == null || wives.Length <= max)
            {
                return;
            }

            System.Array.Sort(wives, (a, b) =>
            {
                var scoreA = WifeKeepScore(a, preferKeep);
                var scoreB = WifeKeepScore(b, preferKeep);
                return scoreB.CompareTo(scoreA);
            });

            for (var i = max; i < wives.Length; i++)
            {
                try
                {
                    wives[i]?.Despawn();
                }
                catch
                {
                }
            }
        }

        private static int WifeKeepScore(WifeAgent wife, WifeHome preferKeep)
        {
            if (wife == null)
            {
                return -100;
            }

            if (preferKeep != null && wife.Home == preferKeep)
            {
                return 100;
            }

            if (wife.Home != null)
            {
                return 50;
            }

            return 0;
        }

        internal static void NotifyIdolLimit()
        {
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                Localization.instance.Localize("$hearthwife_idol_limit"));
        }

        internal static void NotifyWifeLimit()
        {
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                Localization.instance.Localize("$hearthwife_wife_limit"));
        }
    }
}
