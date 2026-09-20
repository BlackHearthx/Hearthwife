using System;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// VillageNPCs-style Player clone: keep CharacterController + MonsterAI so
    /// vanilla walk/idle animations run. Do not teleport the transform or drive Animator.
    /// </summary>
    internal static class WifeNpcPrefab
    {
        internal const string PrefabName = "Hearthwife_WifeNPC";

        private static GameObject _container;
        private static bool _registered;

        internal static void Register()
        {
            try
            {
                RegisterInternal();
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError("Hearthwife: WifeNpcPrefab.Register failed: " + ex);
            }
        }

        private static void RegisterInternal()
        {
            if (_registered)
            {
                return;
            }

            if (ZNetScene.instance == null)
            {
                Jotunn.Logger.LogWarning("Hearthwife: ZNetScene not ready — will register NPC on first spawn");
                return;
            }

            EnsureContainer();

            var player = ZNetScene.instance.GetPrefab("Player");
            if (player == null)
            {
                Jotunn.Logger.LogError("Hearthwife: Player prefab missing");
                return;
            }

            var existing = ZNetScene.instance.GetPrefab(PrefabName);
            if (existing != null)
            {
                ZNetScene.instance.m_prefabs.Remove(existing);
                ZNetScene.instance.m_namedPrefabs.Remove(PrefabName.GetStableHashCode());
            }

            var go = UnityEngine.Object.Instantiate(player, _container.transform, false);
            go.name = PrefabName;
            go.SetActive(false);

            // Same strip list as VillageNPCs (Player inherits Humanoid — re-add after).
            RemoveComponent(go, typeof(PlayerController));
            RemoveComponent(go, typeof(Player));
            RemoveComponent(go, typeof(Talker));
            RemoveComponent(go, typeof(Skills));

            var humanoid = go.AddComponent<Humanoid>();
            ConfigureHumanoid(humanoid, ZNetScene.instance);

            var ai = go.AddComponent<MonsterAI>();
            ConfigureMonsterAI(ai, ZNetScene.instance);

            // Tameable makes Character expose hover/interact (VillageNPCs). We hijack it for talk.
            var tame = go.AddComponent<Tameable>();
            tame.m_commandable = false;
            tame.m_tamingTime = 0.01f;
            tame.m_fedDuration = 99999f;

            // Leave CharacterController / Rigidbody alone — Player setup drives anims.

            var znv = go.GetComponent<ZNetView>();
            if (znv != null)
            {
                znv.m_persistent = false;
                znv.m_distant = false;
                znv.m_type = ZDO.ObjectType.Default;
                znv.m_syncInitialScale = false;
            }

            var zst = go.GetComponent<ZSyncTransform>();
            if (zst != null)
            {
                zst.m_syncPosition = true;
                zst.m_syncRotation = true;
                zst.m_syncScale = false;
                zst.m_syncBodyVelocity = false;
                zst.m_characterParentSync = false;
            }

            var zsa = go.GetComponent<ZSyncAnimation>();
            if (zsa != null)
            {
                zsa.m_smoothCharacterSpeeds = true;
            }

            if (go.GetComponent<WifeAgent>() == null)
            {
                go.AddComponent<WifeAgent>();
            }

            // Same idle component VillageNPCs adds (vanilla random idle triggers).
            if (go.GetComponent<RandomAnimation>() == null)
            {
                go.AddComponent<RandomAnimation>();
            }

            ZNetScene.instance.m_prefabs.Add(go);
            ZNetScene.instance.m_namedPrefabs[PrefabName.GetStableHashCode()] = go;
            _registered = true;
            Jotunn.Logger.LogInfo("Hearthwife: wife NPC prefab registered (MonsterAI + CC)");
        }

        internal static void ForceReregister()
        {
            _registered = false;
            Register();
        }

        internal static GameObject SpawnAt(Vector3 pos, Quaternion rot)
        {
            if (!_registered)
            {
                Register();
            }

            if (!_registered || ZNetScene.instance == null)
            {
                Jotunn.Logger.LogError("Hearthwife: cannot spawn — NPC prefab not registered");
                return null;
            }

            var prefab = ZNetScene.instance.GetPrefab(PrefabName);
            if (prefab == null)
            {
                _registered = false;
                Register();
                prefab = ZNetScene.instance.GetPrefab(PrefabName);
            }

            if (prefab == null)
            {
                Jotunn.Logger.LogError("Hearthwife: prefab missing from ZNetScene");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
            go.name = PrefabName;
            go.SetActive(true);

            var znv = go.GetComponent<ZNetView>();
            if (znv != null && znv.IsValid())
            {
                znv.ClaimOwnership();
                znv.GetZDO().SetPosition(pos);
                znv.GetZDO().SetRotation(rot);
                try
                {
                    znv.GetZDO().Set(ZDOVars.s_tamed, 1);
                }
                catch
                {
                    znv.GetZDO().Set("tamed", 1);
                }
            }

            var character = go.GetComponent<Character>();
            if (character != null)
            {
                try
                {
                    character.SetTamed(true);
                }
                catch
                {
                }
            }

            return go;
        }

        private static void EnsureContainer()
        {
            if (_container != null)
            {
                return;
            }

            _container = new GameObject("Hearthwife_Prefabs");
            _container.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_container);
        }

        private static void RemoveComponent(GameObject go, Type type)
        {
            var c = go.GetComponent(type);
            if (c != null)
            {
                UnityEngine.Object.Destroy(c);
            }
        }

        private static void ConfigureHumanoid(Humanoid h, ZNetScene scene)
        {
            h.m_group = "Hearthwife";
            h.m_name = "$hearthwife_wife_name";
            h.m_faction = Character.Faction.Players;
            h.m_walkSpeed = 2f;
            h.m_speed = 2f;
            h.m_runSpeed = 3.5f;
            h.m_turnSpeed = 300f;
            h.m_health = 100f;
            h.m_canSwim = false;
            h.m_tolerateWater = true;
            h.m_jumpForce = 0f;

            var eye = h.transform.Find("EyePos");
            if (eye != null)
            {
                h.m_eye = eye;
            }
        }

        /// <summary>
        /// Passiveive MonsterAI — WifeAgent drives MoveTo for path/stairs.
        /// Do not also drive CharacterController.Move (double-drive = jitter / roof climb).
        /// </summary>
        private static void ConfigureMonsterAI(MonsterAI ai, ZNetScene scene)
        {
            ai.m_alertRange = 0f;
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_fleeIfNotAlerted = false;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_circulateWhileCharging = false;
            ai.m_circulateWhileChargingFlying = false;
            ai.m_enableHuntPlayer = false;
            ai.m_attackPlayerObjects = false;
            ai.m_privateAreaTriggerTreshold = 999;
            ai.m_interceptTimeMax = 0f;
            ai.m_interceptTimeMin = 0f;
            ai.m_maxChaseDistance = 0f;
            ai.m_minAttackInterval = 999f;
            ai.m_circleTargetInterval = 999f;
            ai.m_circleTargetDuration = 0f;
            ai.m_circleTargetDistance = 0f;
            ai.m_consumeRange = 0f;
            ai.m_consumeSearchRange = 0f;
            ai.m_consumeSearchInterval = 999f;
            ai.m_randomMoveInterval = 99999f;
            ai.m_randomMoveRange = 0f;
            // VillageNPCs: Humanoid path so MoveTo uses stairs / indoor nav.
            // m_moveMinAngle 90 = walk while turning (default 10 = spin-in-place).
            ((BaseAI)ai).m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ((BaseAI)ai).m_smoothMovement = true;
            ((BaseAI)ai).m_moveMinAngle = 90f;
            ((BaseAI)ai).m_avoidWater = true;

            if (ai.m_consumeItems != null)
            {
                ai.m_consumeItems.Clear();
            }
        }
    }
}
