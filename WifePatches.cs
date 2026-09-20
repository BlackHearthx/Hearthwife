using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Hearthwife
{
    [HarmonyPatch]
    internal static class WifePatches
    {
        private static bool _purgedThisSession;

        internal static void Apply(Harmony harmony)
        {
            harmony.PatchAll(typeof(WifePatches));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static bool ContainerInteractPrefix(
            Container __instance,
            Humanoid character,
            bool hold,
            bool alt,
            ref bool __result)
        {
            var home = __instance.GetComponent<WifeHome>();
            if (home == null)
            {
                return true;
            }

            __result = home.Interact(character, hold, alt);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static bool ContainerHoverTextPrefix(Container __instance, ref string __result)
        {
            var home = __instance.GetComponent<WifeHome>();
            if (home == null)
            {
                return true;
            }

            __result = home.GetHoverText();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverName))]
        private static bool ContainerHoverNamePrefix(Container __instance, ref string __result)
        {
            var home = __instance.GetComponent<WifeHome>();
            if (home == null)
            {
                return true;
            }

            __result = home.GetHoverName();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static bool TryPlacePiecePrefix(Piece piece, ref bool __result)
        {
            if (!WifeLimits.IsIdolPiece(piece))
            {
                return true;
            }

            if (WifeLimits.CanPlaceAnotherIdol())
            {
                return true;
            }

            WifeLimits.NotifyIdolLimit();
            __result = false;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static void ZNetSceneAwakePostfix()
        {
            WifeNpcPrefab.Register();
            _purgedThisSession = false;
        }

        // Do NOT intercept CreateObject for the wife prefab while she is alive —
        // that was destroying runtime spawns. Orphans are purged only on world load.

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNet), "LoadWorld")]
        private static void ZNetLoadWorldPostfix()
        {
            PurgeSavedWives("LoadWorld");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        private static void ZoneSystemStartPostfix()
        {
            PurgeSavedWives("ZoneSystem.Start");
        }

        private static void PurgeSavedWives(string reason)
        {
            if (_purgedThisSession || ZDOMan.instance == null)
            {
                return;
            }

            try
            {
                var found = new List<ZDO>();
                var index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(
                           WifeNpcPrefab.PrefabName,
                           found,
                           ref index))
                {
                    // Iterative scan until complete.
                }

                var n = 0;
                foreach (var zdo in found)
                {
                    if (zdo == null || !zdo.IsValid())
                    {
                        continue;
                    }

                    ZDOMan.instance.DestroyZDO(zdo);
                    n++;
                }

                _purgedThisSession = true;
                if (n > 0)
                {
                    Jotunn.Logger.LogInfo(
                        "Hearthwife: purged " + n + " saved wife ZDO(s) (" + reason + ")");
                }

                WifeLimits.CullExtraWives();
                WifeLimits.PurgeOrphanWives();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning("Hearthwife: wife ZDO purge failed: " + ex.Message);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
        private static bool BedInteractPrefix(Bed __instance, Humanoid human, bool repeat, bool alt, ref bool __result)
        {
            if (!repeat && WifeHome.TryAssignBedFromLink(__instance))
            {
                __result = true;
                return false;
            }

            // Soft lock only while she is asleep — player keeps the bed otherwise.
            if (!repeat && human is Player && WifeAgent.IsWifeSleepingOnBed(__instance))
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    Localization.instance.Localize("$hearthwife_bed_wife_resting"));
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
        private static bool BedHoverTextPrefix(Bed __instance, ref string __result)
        {
            if (WifeHome.BedLinkHome != null && Time.time <= WifeHome.BedLinkUntil)
            {
                __result = Localization.instance.Localize("$piece_bed")
                           + "\n[<color=yellow><b>E</b></color>] "
                           + Localization.instance.Localize("$hearthwife_bed_assign_hint");
                return false;
            }

            if (WifeAgent.IsWifeSleepingOnBed(__instance))
            {
                __result = Localization.instance.Localize("$piece_bed")
                           + "\n"
                           + Localization.instance.Localize("$hearthwife_bed_wife_resting");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Skip MonsterAI brain (IdleMovement → RandomMovement → StopMoving every frame)
        /// which fought WifeAgent.MoveTo/SetMoveDir (whole-body robot shake).
        /// Keep MonsterAI.enabled=true — Character.UpdateWalking requires m_baseAI present
        /// and enabled=false previously killed all walking.
        /// Do NOT patch BaseAI.UpdateAI to return false: that aborted MonsterAI before
        /// ownership checks in a way that left some spawns unable to path.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static bool MonsterAIUpdateAIPrefix(MonsterAI __instance, ref bool __result)
        {
            if (__instance == null || __instance.GetComponent<WifeAgent>() == null)
            {
                return true;
            }

            __result = true;
            return false;
        }

        /// <summary>
        /// Wife at home raises player comfort (RuneboundRest pattern: SE_Rested.CalculateComfortLevel).
        /// +N while local player and wife are both inside the idol ward.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(Player))]
        private static void SE_RestedCalculateComfortLevelPostfix(Player player, ref int __result)
        {
            var add = WifeHome.GetWifeHomeComfortBonus(player);
            if (add > 0)
            {
                __result += add;
            }
        }

        /// <summary>
        /// Character steals hover from WifeAgent — route through Tameable (or Character) to talk.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        private static bool CharacterGetHoverTextPrefix(Character __instance, ref string __result)
        {
            var wife = __instance.GetComponent<WifeAgent>();
            if (wife == null)
            {
                return true;
            }

            __result = wife.GetHoverText();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static bool CharacterGetHoverNamePrefix(Character __instance, ref string __result)
        {
            var wife = __instance.GetComponent<WifeAgent>();
            if (wife == null)
            {
                return true;
            }

            __result = wife.GetHoverName();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        private static bool TameableGetHoverTextPrefix(Tameable __instance, ref string __result)
        {
            var wife = __instance.GetComponent<WifeAgent>();
            if (wife == null)
            {
                return true;
            }

            __result = wife.GetHoverText();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
        private static bool TameableInteractPrefix(
            Tameable __instance,
            Humanoid user,
            bool hold,
            bool alt,
            ref bool __result)
        {
            var wife = __instance.GetComponent<WifeAgent>();
            if (wife == null)
            {
                return true;
            }

            __result = wife.Interact(user, hold, alt);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
        private static bool TameableUseItemPrefix(
            Tameable __instance,
            Humanoid user,
            ItemDrop.ItemData item,
            ref bool __result)
        {
            if (__instance.GetComponent<WifeAgent>() == null)
            {
                return true;
            }

            __result = false;
            return false;
        }

        /// <summary>
        /// Player-clone NPCs crash EnemyHud every frame — never register them.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
        private static bool EnemyHudShowHudPrefix(Character c)
        {
            return c == null || c.GetComponent<WifeAgent>() == null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
        private static void EnemyHudUpdateHudsPrefix(EnemyHud __instance)
        {
            // Drop any wife already queued so LateUpdate doesn't NRE forever.
            try
            {
                var field = AccessTools.Field(typeof(EnemyHud), "m_huds");
                if (field == null)
                {
                    return;
                }

                var huds = field.GetValue(__instance) as System.Collections.IDictionary;
                if (huds == null)
                {
                    return;
                }

                Character remove = null;
                foreach (System.Collections.DictionaryEntry entry in huds)
                {
                    if (entry.Key is Character ch && ch != null && ch.GetComponent<WifeAgent>() != null)
                    {
                        remove = ch;
                        break;
                    }
                }

                if (remove != null)
                {
                    huds.Remove(remove);
                }
            }
            catch
            {
                // Best-effort; ShowHud prefix is the main fix.
            }
        }
    }
}
