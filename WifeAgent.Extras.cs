using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Extra homestead chores from OfflineCompanions / DynamicNPCs / Renegade ideas:
    /// smelt, farm harvest, garden tips, auto-eat, sit-by-fire, ambient greet.
    /// </summary>
    public partial class WifeAgent
    {
        private float _greetCooldown;
        private bool _playerWasNear;
        private Animator _lookAnimator;
        private Transform _headBone;
        private Quaternion _headBaseLocal;
        private bool _headBoneReady;
        private bool _lookAtIkDisabled;
        /// <summary>Fires: smoothed aim (not raw head→player — that feedback-loops).</summary>
        private Vector3 _headLookCurrentDir = Vector3.forward;
        private Vector3 _headLookTargetDir = Vector3.forward;
        private float _headLookWeight;
        private float _headLookTargetWeight;
        /// <summary>While set, keep body yaw on the player (E attend) without fighting walk AI.</summary>
        private float _facePlayerUntil;
        private float _gardenTipCooldown;
        private float _eatCooldown;
        private float _doorCooldown;
        private float _restedApplyAt;
        private Smelter _smeltTarget;
        private Pickable _farmTarget;
        private Pickable _forageTarget;
        private Vector3 _sitFirePos;
        private Fireplace _sitFire;

        private void TickAmbientGreet()
        {
            if (_home == null)
            {
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null || _sleeping || _sleepAttached)
            {
                _headLookTargetWeight = 0f;
                return;
            }

            EnsureHeadBone();
            DisableConflictingLookAt();

            var dist = Vector3.Distance(player.transform.position, transform.position);
            // Wide hysteresis: enter ~7.5m to greet, must leave ~16m before another visit greet.
            const float enterRange = 7.5f;
            const float leaveRange = 16f;
            const float lookRange = 11f;

            // Fires UpdateHeadLookAt — root-space aim + smooth weight/dir (no bone feedback).
            TickLookAtPlayer(player, dist, lookRange);

            if (PluginConfig.EnableAmbientGreet == null || !PluginConfig.EnableAmbientGreet.Value)
            {
                return;
            }

            if (dist < enterRange)
            {
                if (!_playerVisitNear && Time.time >= _greetCooldown)
                {
                    _playerVisitNear = true;
                    _greetCooldown = Time.time + Random.Range(90f, 150f);
                    if (CanAmbientSocialTalk())
                    {
                        WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.GreetLine()));
                        if (!TryOfferSocialEmote(WifeEmotes.PickGreetEmote(), 14f))
                        {
                            MarkSocialQuiet(8f);
                        }
                    }
                }
                else if (!_playerVisitNear)
                {
                    // Inside band but greet on cooldown — still mark visit so we don't spam-check.
                    _playerVisitNear = true;
                }
            }
            else if (dist > leaveRange)
            {
                if (_playerVisitNear)
                {
                    _playerVisitNear = false;
                    if (CanAmbientSocialTalk())
                    {
                        // Soft goodbye: talk often, body wave rarely.
                        if (Random.value < 0.7f)
                        {
                            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.GoodbyeLine()));
                            MarkSocialQuiet(5f);
                        }

                        if (Random.value < 0.28f)
                        {
                            TryOfferSocialEmote("emote_wave", 12f);
                        }
                    }
                }

                _headLookTargetWeight = 0f;
            }

            _playerWasNear = dist < enterRange;
            if (dist > 22f)
            {
                _playerWasNear = false;
                _playerVisitNear = false;
                _headLookTargetWeight = 0f;
            }
        }

        /// <summary>
        /// assembly_utils LookAt IK + bone LateUpdate = head fight/tremble.
        /// Keep bone-only (Fires); disable any LookAt on the clone.
        /// </summary>
        private void DisableConflictingLookAt()
        {
            if (_lookAtIkDisabled)
            {
                return;
            }

            if (_lookAnimator == null)
            {
                _lookAnimator = GetComponentInChildren<Animator>(true);
            }

            if (_lookAnimator == null)
            {
                return;
            }

            var looks = GetComponentsInChildren<LookAt>(true);
            for (var i = 0; i < looks.Length; i++)
            {
                if (looks[i] == null)
                {
                    continue;
                }

                try
                {
                    looks[i].ResetTarget();
                }
                catch
                {
                }

                looks[i].enabled = false;
            }

            _lookAtIkDisabled = true;
        }

        /// <summary>Fires companion path — bone look after Animator, no IK Pass required.</summary>
        private void EnsureHeadBone()
        {
            if (_headBoneReady && _headBone != null)
            {
                return;
            }

            var anim = _lookAnimator != null ? _lookAnimator : GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                return;
            }

            _lookAnimator = anim;
            _headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (_headBone == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t == null)
                    {
                        continue;
                    }

                    var n = t.name.ToLowerInvariant();
                    if (n == "head" || n.EndsWith("/head") || n.EndsWith(":head"))
                    {
                        _headBone = t;
                        break;
                    }
                }
            }

            if (_headBone == null)
            {
                return;
            }

            // Fires: freeze bind/idle local once — Apply overwrites Animator head while looking.
            _headBaseLocal = _headBone.localRotation;
            _headBoneReady = true;
            _headLookCurrentDir = transform.forward;
            _headLookTargetDir = transform.forward;
        }

        /// <summary>
        /// Port of Fires UpdateHeadLookAt + soft body yaw when parked / attending (E).
        /// Head alone only works inside ~75° — must turn body or she never glances.
        /// </summary>
        private void TickLookAtPlayer(Player player, float dist, float lookRange)
        {
            const float maxLookAtAngle = 75f;
            const float headRotationSpeed = 3f;
            const float headReturnSpeed = 1.5f;

            var attending = Time.time < _facePlayerUntil;
            var emoteBusy = WifeEmotes.IsPlaying(gameObject) && !_sitting;
            // During E-greet emote, still face/look — otherwise she snaps then stares past you.
            var blockHead = !attending && emoteBusy;
            // Pathing: skip body yaw (fights MoveTo). Head glance still OK — Fires aims from root.
            var pathing = _hasTarget && !_sitting && !_sleeping;
            // Cook wait / repair / smelt: body faces the station — head+yaw toward player = axis jitter.
            var workHold = IsStationaryWorkHold();

            if (!_headBoneReady ||
                dist >= lookRange ||
                _playerAttached ||
                _sleepAttached ||
                _sitting ||
                workHold ||
                blockHead)
            {
                _headLookTargetDir = transform.forward;
                _headLookTargetWeight = 0f;
                var ret = headReturnSpeed;
                _headLookCurrentDir = Vector3.Slerp(
                    _headLookCurrentDir, _headLookTargetDir, Time.deltaTime * ret);
                _headLookWeight = Mathf.Lerp(_headLookWeight, 0f, Time.deltaTime * ret);
                if (attending && !workHold && !_playerAttached && !_sleepAttached && !_sleeping && !_hasTarget)
                {
                    FacePlayerBody(player, snap: false);
                }

                return;
            }

            Vector3 headPoint;
            try
            {
                headPoint = ((Character)player).GetHeadPoint();
            }
            catch
            {
                headPoint = player.transform.position + Vector3.up * 1.6f;
            }

            var flatToPlayer = player.transform.position - transform.position;
            flatToPlayer.y = 0f;
            var bodyAngle = flatToPlayer.sqrMagnitude > 0.01f
                ? Vector3.Angle(transform.forward, flatToPlayer.normalized)
                : 0f;

            // Body yaw only when parked — never while pathing (MoveTo owns yaw).
            // softBodyHold: garden tip / post-chore _actionUntil — don't fight FacePoint.
            var softBodyHold = !_hasTarget && Time.time < _actionUntil;
            if (!_sitting && !_sleeping && !pathing && !workHold && !softBodyHold)
            {
                if (attending && !_hasTarget)
                {
                    FacePlayerBody(player, snap: false);
                }
                else if (!_hasTarget && _chore == Chore.Idle &&
                         bodyAngle > 28f && bodyAngle <= 90f)
                {
                    FacePlayerBody(player, snap: false, speed: 1.4f);
                }
            }

            // Fires: aim from root + eye height, not from the head bone.
            var toTarget = headPoint - (transform.position + Vector3.up * 1.5f);
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                _headLookTargetDir = transform.forward;
                _headLookTargetWeight = 0f;
            }
            else
            {
                var normalized = toTarget.normalized;
                var angle = Vector3.Angle(transform.forward, normalized);
                if (attending || angle <= maxLookAtAngle)
                {
                    _headLookTargetDir = normalized;
                    _headLookTargetWeight = attending
                        ? 1f
                        : 1f - angle / maxLookAtAngle * 0.3f;
                }
                else
                {
                    _headLookTargetDir = transform.forward;
                    _headLookTargetWeight = 0f;
                }
            }

            var speed = _headLookTargetWeight > 0.1f ? headRotationSpeed : headReturnSpeed;
            _headLookCurrentDir = Vector3.Slerp(
                _headLookCurrentDir, _headLookTargetDir, Time.deltaTime * speed);
            _headLookWeight = Mathf.Lerp(_headLookWeight, _headLookTargetWeight, Time.deltaTime * speed);
        }

        /// <summary>Yaw only — never pitch. snap=true for E; else Slerp.</summary>
        private void FacePlayerBody(Player player, bool snap, float speed = 3.5f)
        {
            if (player == null)
            {
                return;
            }

            var look = player.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f)
            {
                return;
            }

            var want = Quaternion.LookRotation(look.normalized);
            if (snap)
            {
                transform.rotation = want;
            }
            else
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, want, Time.deltaTime * speed);
            }
        }

        /// <summary>Fires CompanionIdleBehavior.ApplyHeadLookAt (LateUpdate).</summary>
        private void LateUpdate()
        {
            // After Minimap.UpdatePins may reset marker size — keep wife pin small.
            LateTickMapPinUi();
            ApplyHeadBoneLook();
        }

        private void ApplyHeadBoneLook()
        {
            if (!_headBoneReady || _headBone == null)
            {
                return;
            }

            if (_sleeping || _playerAttached || _sitting)
            {
                return;
            }

            // Don't touch the bone when not looking — writing bind every frame fights idle anim.
            if (_headLookWeight < 0.01f)
            {
                return;
            }

            // Emote owns the head unless we're in E-attend (wave while facing you is OK).
            if (WifeEmotes.IsPlaying(gameObject) && !_sitting && Time.time >= _facePlayerUntil)
            {
                return;
            }

            try
            {
                var dir = _headLookCurrentDir;
                dir.y *= 0.5f;
                var mag = dir.magnitude;
                if (mag < 0.001f)
                {
                    return;
                }

                dir /= mag;
                var worldLook = Quaternion.LookRotation(dir);
                if (float.IsNaN(worldLook.x) || float.IsNaN(worldLook.y) ||
                    float.IsNaN(worldLook.z) || float.IsNaN(worldLook.w))
                {
                    return;
                }

                var local = Quaternion.Inverse(transform.rotation) * worldLook;
                var euler = local.eulerAngles;
                euler.x = ClampLookAngle(euler.x, -25f, 35f);
                euler.y = ClampLookAngle(euler.y, -75f, 75f);
                euler.z = 0f;
                var offset = Quaternion.Euler(euler);
                var result = Quaternion.Slerp(
                    _headBaseLocal, _headBaseLocal * offset, _headLookWeight);
                if (!float.IsNaN(result.x) && !float.IsNaN(result.y) &&
                    !float.IsNaN(result.z) && !float.IsNaN(result.w))
                {
                    _headBone.localRotation = result;
                }
            }
            catch
            {
            }
        }

        private static float ClampLookAngle(float angle, float min, float max)
        {
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return Mathf.Clamp(angle, min, max);
        }

        private bool TryBeginSmelt(WifeHome home)
        {
            var inv = home.Storage?.GetInventory();
            if (inv == null)
            {
                return false;
            }

            Smelter best = null;
            var bestDist = home.Radius;
            var mode = 0; // 1 fuel, 2 ore, 3 empty

            foreach (var sm in Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
            {
                if (sm == null || sm.m_nview == null || !sm.m_nview.IsValid())
                {
                    continue;
                }

                if (!home.IsInside(sm.transform.position))
                {
                    continue;
                }

                var useful = 0;
                try
                {
                    if (sm.GetFuel() < sm.m_maxFuel && HasSmeltFuel(inv, sm))
                    {
                        useful = 1;
                    }
                    else if (sm.GetQueueSize() < sm.m_maxOre)
                    {
                        var ore = sm.FindCookableItem(inv);
                        if (ore != null)
                        {
                            useful = 2;
                        }
                    }

                    if (sm.GetProcessedQueueSize() > 0)
                    {
                        useful = 3;
                    }
                }
                catch
                {
                    continue;
                }

                if (useful == 0)
                {
                    continue;
                }

                var d = Vector3.Distance(sm.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = sm;
                mode = useful;
            }

            if (best == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Smelt);
            _smeltTarget = best;
            SetTarget(best.transform.position);
            Notify(mode == 3 ? "$hearthwife_busy_smelt_out" : "$hearthwife_busy_smelt");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        private IEnumerator SmeltRoutine(Smelter sm)
        {
            BeginOwnedChore(Chore.Smelt);
            WifeEmotes.Stop(gameObject);

            if (sm == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            EnterStationaryWorkHold(sm.transform.position, 6f);

            var inv = _home?.Storage?.GetInventory();
            if (inv == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            var didEmpty = false;
            try
            {
                didEmpty = sm.GetProcessedQueueSize() > 0;
            }
            catch
            {
            }

            if (didEmpty)
            {
                try
                {
                    sm.m_nview.InvokeRPC("EmptyProcessed");
                }
                catch
                {
                }

                PlayInteractAnimation(sm.transform.position);
                yield return new WaitForSeconds(0.8f);
                ScoopNear(inv, sm.transform.position, 4f);
            }

            var needFuel = false;
            try
            {
                needFuel = sm.GetFuel() < sm.m_maxFuel && TryConsumeSmeltFuel(inv, sm);
            }
            catch
            {
            }

            if (needFuel)
            {
                try
                {
                    sm.m_nview.InvokeRPC("AddFuel");
                }
                catch
                {
                }

                PlayInteractAnimation(sm.transform.position);
                yield return new WaitForSeconds(0.5f);
            }

            ItemDrop.ItemData ore = null;
            try
            {
                if (sm.GetQueueSize() < sm.m_maxOre)
                {
                    ore = sm.FindCookableItem(inv);
                }
            }
            catch
            {
            }

            if (ore != null)
            {
                var name = ore.m_dropPrefab != null ? ore.m_dropPrefab.name : ore.m_shared.m_name;
                inv.RemoveItem(ore, 1);
                try
                {
                    sm.m_nview.InvokeRPC("AddOre", name, false);
                }
                catch
                {
                }

                PlayInteractAnimation(sm.transform.position);
            }

            yield return new WaitForSeconds(1.2f);
            ForceClearRightHand();
            EndOwnedChore(2f);
        }

        private bool TryBeginFarm(WifeHome home)
        {
            Pickable best = null;
            var bestDist = home.Radius;

            foreach (var pick in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if (pick == null || !home.IsInside(pick.transform.position))
                {
                    continue;
                }

                try
                {
                    if (!pick.CanBePicked() || pick.GetPicked())
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (!IsCropPickable(pick))
                {
                    continue;
                }

                var d = Vector3.Distance(pick.transform.position, transform.position);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = pick;
            }

            if (best == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Farm);
            _farmTarget = best;
            SetTarget(best.transform.position);
            Notify("$hearthwife_busy_farm");
            if (HorizontalDistance(transform.position, _target) < 1.8f)
            {
                OnArrived();
            }

            return true;
        }

        /// <summary>Legacy entry — forage folded into TryBeginCollect (Recolher).</summary>
        private bool TryBeginForage(WifeHome home) => TryBeginCollect(home);

        private const float ForageReach = 2.75f;
        private const float GatherBushStandoff = 1.95f;
        private const float GatherDropStandoff = 1.4f;
        private const float GatherDropPickupRange = 2.35f;

        private static bool InForageReach(Vector3 from, Pickable pick)
        {
            if (pick == null)
            {
                return false;
            }

            // Horizontal — pick from a stand-off; never need to stand inside the bush.
            var a = from;
            var b = pick.transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= ForageReach;
        }

        private IEnumerator ForageRoutine(Pickable pick)
        {
            BeginOwnedChore(Chore.Forage);
            WifeEmotes.Stop(gameObject);

            var inv = _home?.Storage?.GetInventory();
            if (inv == null || pick == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            if (_home != null && _home.IsStorageFull)
            {
                Notify("$hearthwife_chest_full");
                EndOwnedChore(2f);
                yield break;
            }

            var pos = pick.transform.position;
            EnterStationaryWorkHold(pos, 2.2f);
            PlayPickupFeedback(pos);

            try
            {
                if (_humanoid != null && pick.CanBePicked())
                {
                    pick.Interact(_humanoid, false, false);
                }
            }
            catch
            {
                try
                {
                    if (pick.m_nview != null && pick.m_nview.IsValid() && pick.CanBePicked())
                    {
                        pick.m_nview.InvokeRPC("RPC_Pick");
                    }
                }
                catch
                {
                }
            }

            yield return new WaitForSeconds(0.45f);
            ScoopForageNear(inv, pos, 3.5f, pick);

            if (_home != null && _home.IsStorageFull)
            {
                Notify("$hearthwife_chest_full");
            }

            ForceClearRightHand();
            EndOwnedChore(1.2f);
            _forageTarget = null;
        }

        private void TickStuckDoors()
        {
            if (!_hasTarget || _sleeping || _sitting || _chore == Chore.Idle)
            {
                return;
            }

            if (Time.time - _lastProgressAt > 1.4f)
            {
                TryOpenNearbyDoors(force: false);
            }
        }

        private void TryOpenNearbyDoors(bool force)
        {
            if (!PluginConfig.EnableOpenDoors.Value)
            {
                return;
            }

            if (!force && Time.time < _doorCooldown)
            {
                return;
            }

            Door best = null;
            var bestDist = 3.4f;
            foreach (var door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                if (door == null)
                {
                    continue;
                }

                var d = Vector3.Distance(door.transform.position, transform.position);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = door;
            }

            if (best == null)
            {
                return;
            }

            _doorCooldown = Time.time + 1.6f;
            try
            {
                if (_humanoid != null)
                {
                    best.Interact(_humanoid, false, false);
                }
                else if (best.m_nview != null && best.m_nview.IsValid())
                {
                    best.m_nview.InvokeRPC("UseDoor", true);
                }
            }
            catch
            {
                try
                {
                    if (best.m_nview != null && best.m_nview.IsValid())
                    {
                        best.m_nview.InvokeRPC("UseDoor", true);
                    }
                }
                catch
                {
                }
            }
        }

        private void TickRestedNearFire()
        {
            if (_home == null || !_home.DoRested || _character == null)
            {
                return;
            }

            if (!_sitting && !_sleeping)
            {
                return;
            }

            if (!NearLitFire(_sitting ? 4f : 5.5f))
            {
                return;
            }

            if (Time.time < _restedApplyAt)
            {
                return;
            }

            _restedApplyAt = Time.time + 25f;
            TryApplyRested();
        }

        private bool NearLitFire(float radius)
        {
            foreach (var fire in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
            {
                if (fire == null)
                {
                    continue;
                }

                if (Vector3.Distance(fire.transform.position, transform.position) > radius)
                {
                    continue;
                }

                try
                {
                    if (fire.IsBurning())
                    {
                        return true;
                    }
                }
                catch
                {
                    try
                    {
                        if (fire.m_nview != null && fire.m_nview.IsValid() &&
                            fire.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel, 0f) > 0.1f)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private void TryApplyRested()
        {
            try
            {
                var seman = _character.GetSEMan();
                if (seman == null || ObjectDB.instance == null)
                {
                    return;
                }

                StatusEffect se = null;
                try
                {
                    se = ObjectDB.instance.GetStatusEffect("Rested".GetStableHashCode());
                }
                catch
                {
                }

                if (se != null)
                {
                    seman.AddStatusEffect(se, true);
                }
            }
            catch
            {
            }
        }

        private IEnumerator FarmRoutine(Pickable pick)
        {
            BeginOwnedChore(Chore.Farm);
            WifeEmotes.Stop(gameObject);
            var plantPos = pick != null ? pick.transform.position : transform.position;
            var cropKey = pick != null ? CropKeyFromPickable(pick) : null;
            EnterStationaryWorkHold(plantPos, 5f);

            var inv = _home?.Storage?.GetInventory();
            PlayInteractAnimation(plantPos);
            try
            {
                if (pick != null && pick.m_nview != null && pick.m_nview.IsValid() && pick.CanBePicked())
                {
                    pick.m_nview.InvokeRPC("Pick", 0);
                }
            }
            catch
            {
            }

            yield return new WaitForSeconds(0.6f);
            if (inv != null)
            {
                ScoopNear(inv, plantPos, 3.5f);
            }

            if (PluginConfig.EnableReplant.Value && !string.IsNullOrEmpty(cropKey) && inv != null)
            {
                TryReplant(inv, cropKey, plantPos);
                yield return new WaitForSeconds(0.45f);
            }

            ForceClearRightHand();
            EndOwnedChore(1.5f);
        }

        private bool TryBeginCauldron(WifeHome home)
        {
            var inv = home.Storage?.GetInventory();
            if (inv == null)
            {
                return false;
            }

            var cauldron = FindCauldron(home);
            if (cauldron == null || !WifeOccupancy.IsFree(cauldron, this))
            {
                return false;
            }

            var recipe = PickCauldronRecipe(cauldron, inv);
            if (recipe == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Cauldron);
            _cauldronTarget = cauldron;
            _cauldronRecipe = recipe;
            SetTarget(cauldron.transform.position);
            Notify("$hearthwife_busy_cauldron");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        private IEnumerator CauldronRoutine(CraftingStation station, Recipe recipe)
        {
            BeginOwnedChore(Chore.Cauldron);
            WifeEmotes.Stop(gameObject);

            if (station != null && WifeOccupancy.TryOccupy(station, this, 40f))
            {
                _occupiedProp = station;
            }

            EnterStationaryWorkHold(
                station != null ? station.transform.position : transform.position, 14f);

            var inv = _home?.Storage?.GetInventory();
            if (inv == null || station == null || recipe == null || !CanAffordRecipe(inv, recipe))
            {
                EndOwnedChore(1f);
                yield break;
            }

            // Timed craft beat — not instant dump (still chest-based, no crafting UI).
            PlayInteractAnimation(station.transform.position);
            yield return new WaitForSeconds(1.4f);
            PlayInteractAnimation(station.transform.position);
            yield return new WaitForSeconds(1.4f);

            if (!CanAffordRecipe(inv, recipe))
            {
                ForceClearRightHand();
                EndOwnedChore(1.5f);
                yield break;
            }

            ConsumeRecipe(inv, recipe);
            yield return new WaitForSeconds(0.8f);

            var product = recipe.m_item?.m_itemData?.Clone();
            if (product != null)
            {
                product.m_stack = Mathf.Max(1, recipe.m_amount);
                inv.AddItem(product);
            }

            PlayInteractAnimation(station.transform.position);
            yield return new WaitForSeconds(0.6f);
            ForceClearRightHand();
            EndOwnedChore(2f);
        }

        private static string CropKeyFromPickable(Pickable pick)
        {
            var prefab = pick.m_itemPrefab;
            var name = prefab != null ? prefab.name : pick.name;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (name.IndexOf("Carrot", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Carrot";
            if (name.IndexOf("Turnip", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Turnip";
            if (name.IndexOf("Onion", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Onion";
            if (name.IndexOf("Barley", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Barley";
            if (name.IndexOf("Flax", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Flax";
            return null;
        }

        private static void TryReplant(Inventory inv, string cropKey, Vector3 pos)
        {
            string seedName;
            string saplingName;
            switch (cropKey)
            {
                case "Carrot": seedName = "CarrotSeeds"; saplingName = "sapling_carrot"; break;
                case "Turnip": seedName = "TurnipSeeds"; saplingName = "sapling_turnip"; break;
                case "Onion": seedName = "OnionSeeds"; saplingName = "sapling_onion"; break;
                case "Barley": seedName = "BarleySeeds"; saplingName = "sapling_barley"; break;
                case "Flax": seedName = "FlaxSeeds"; saplingName = "sapling_flax"; break;
                default: return;
            }

            if (!TryConsumeNamedItem(inv, seedName))
            {
                return;
            }

            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(saplingName) : null;
            if (prefab == null)
            {
                TryAddNamedItem(inv, seedName, 1);
                return;
            }

            Object.Instantiate(prefab, pos + Vector3.up * 0.05f, Quaternion.identity);
        }

        private static bool TryConsumeNamedItem(Inventory inv, string itemName)
        {
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                if (!item.m_dropPrefab.name.Equals(itemName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                inv.RemoveItem(item, 1);
                return true;
            }

            return false;
        }

        private static void TryAddNamedItem(Inventory inv, string itemName, int amount)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
            var data = prefab?.GetComponent<ItemDrop>()?.m_itemData?.Clone();
            if (data == null)
            {
                return;
            }

            data.m_stack = amount;
            inv.AddItem(data);
        }

        private static CraftingStation FindCauldron(WifeHome home)
        {
            CraftingStation best = null;
            var bestDist = home.Radius;
            foreach (var cs in Object.FindObjectsByType<CraftingStation>(FindObjectsSortMode.None))
            {
                if (cs == null || !home.IsInside(cs.transform.position))
                {
                    continue;
                }

                var label = (cs.m_name ?? "") + " " + cs.name;
                if (label.IndexOf("cauldron", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var d = Vector3.Distance(cs.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = cs;
            }

            return best;
        }

        private static Recipe PickCauldronRecipe(CraftingStation cauldron, Inventory inv)
        {
            if (ObjectDB.instance == null || cauldron == null)
            {
                return null;
            }

            var matches = new List<Recipe>();
            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null || recipe.m_craftingStation == null)
                {
                    continue;
                }

                if (recipe.m_craftingStation.m_name != cauldron.m_name)
                {
                    continue;
                }

                var shared = recipe.m_item.m_itemData?.m_shared;
                if (shared == null)
                {
                    continue;
                }

                var name = recipe.m_item.gameObject.name;
                var isFood = shared.m_food > 0f || shared.m_foodStamina > 0f;
                var isMeadBase = name.IndexOf("MeadBase", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isFood && !isMeadBase)
                {
                    continue;
                }

                if (!CanAffordRecipe(inv, recipe))
                {
                    continue;
                }

                matches.Add(recipe);
            }

            return matches.Count == 0 ? null : matches[Random.Range(0, matches.Count)];
        }

        private static bool CanAffordRecipe(Inventory inv, Recipe recipe)
        {
            if (recipe?.m_resources == null)
            {
                return false;
            }

            foreach (var req in recipe.m_resources)
            {
                if (req?.m_resItem == null || req.m_amount <= 0)
                {
                    continue;
                }

                var need = req.m_amount;
                var have = 0;
                var resName = req.m_resItem.name;
                foreach (var item in inv.GetAllItems())
                {
                    if (item?.m_dropPrefab == null)
                    {
                        continue;
                    }

                    if (item.m_dropPrefab.name == resName)
                    {
                        have += item.m_stack;
                    }
                }

                if (have < need)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ConsumeRecipe(Inventory inv, Recipe recipe)
        {
            foreach (var req in recipe.m_resources)
            {
                if (req?.m_resItem == null || req.m_amount <= 0)
                {
                    continue;
                }

                var left = req.m_amount;
                var resName = req.m_resItem.name;
                var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
                foreach (var item in items)
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    if (item?.m_dropPrefab == null || item.m_stack < 1)
                    {
                        continue;
                    }

                    if (item.m_dropPrefab.name != resName)
                    {
                        continue;
                    }

                    var take = Mathf.Min(left, item.m_stack);
                    inv.RemoveItem(item, take);
                    left -= take;
                }
            }
        }

        private bool TryGardenTips(WifeHome home)
        {
            if (Time.time < _gardenTipCooldown)
            {
                return false;
            }

            // Bad plants
            foreach (var plant in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
            {
                if (plant == null || !home.IsInside(plant.transform.position))
                {
                    continue;
                }

                Plant.Status st;
                try
                {
                    st = plant.GetStatus();
                }
                catch
                {
                    continue;
                }

                if (st == Plant.Status.Healthy)
                {
                    continue;
                }

                _gardenTipCooldown = Time.time + 90f;
                EnterStationaryWorkHold(plant.transform.position, 3f);
                var tip = PlantTip(st);
                WifeTalk.Say(gameObject, tip);
                Notify("$hearthwife_busy_garden");
                return true;
            }

            // Beehives needing space or ready honey
            foreach (var hive in Object.FindObjectsByType<Beehive>(FindObjectsSortMode.None))
            {
                if (hive == null || !home.IsInside(hive.transform.position))
                {
                    continue;
                }

                try
                {
                    if (!hive.HaveFreeSpace())
                    {
                        _gardenTipCooldown = Time.time + 90f;
                        EnterStationaryWorkHold(hive.transform.position, 3f);
                        WifeTalk.Say(gameObject, Localization.instance.Localize("$hearthwife_tip_bee_space"));
                        Notify("$hearthwife_busy_garden");
                        return true;
                    }

                    if (hive.GetHoneyLevel() >= Mathf.Max(1, hive.m_maxHoney / 2))
                    {
                        // Extract honey into idol chest
                        var inv = home.Storage?.GetInventory();
                        EnterStationaryWorkHold(hive.transform.position, 2f);
                        hive.Extract();
                        if (inv != null)
                        {
                            ScoopNear(inv, hive.transform.position, 3f);
                        }

                        _gardenTipCooldown = Time.time + 60f;
                        Notify("$hearthwife_busy_honey");
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryAutoEat(WifeHome home)
        {
            if (Time.time < _eatCooldown || _character == null)
            {
                return false;
            }

            float hp;
            try
            {
                hp = _character.GetHealthPercentage();
            }
            catch
            {
                return false;
            }

            if (hp > 0.72f)
            {
                return false;
            }

            var inv = home.Storage?.GetInventory();
            if (inv == null)
            {
                return false;
            }

            ItemDrop.ItemData food = null;
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_shared == null || item.m_stack < 1)
                {
                    continue;
                }

                if (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f)
                {
                    food = item;
                    break;
                }
            }

            if (food == null)
            {
                return false;
            }

            inv.RemoveItem(food, 1);
            try
            {
                _character.Heal(Mathf.Max(15f, food.m_shared.m_food * 0.35f));
            }
            catch
            {
            }

            _eatCooldown = Time.time + 40f;
            Notify("$hearthwife_busy_eat");
            WifeEmotes.Play(gameObject, "emote_toast");
            _actionUntil = Time.time + WifeEmotes.DurationFor("emote_toast");
            return true;
        }

        private bool TrySitByFire(WifeHome home)
        {
            Fireplace best = null;
            var bestDist = home.Radius;
            foreach (var fire in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
            {
                if (fire == null || !home.IsInside(fire.transform.position))
                {
                    continue;
                }

                try
                {
                    if (!fire.IsBurning())
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (!WifeOccupancy.IsFree(fire, this))
                {
                    continue;
                }

                var d = Vector3.Distance(fire.transform.position, transform.position);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = fire;
            }

            if (best == null)
            {
                return false;
            }

            var dir = (transform.position - best.transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = best.transform.forward;
            }

            dir.Normalize();
            _sitFirePos = best.transform.position + dir * 1.6f;
            _sitFirePos.y = best.transform.position.y + 0.2f;
            SnapToPieceOrTerrain(ref _sitFirePos, home.HomePosition.y);
            _sitFire = best;
            BeginOwnedChore(Chore.SitFire);
            SetTarget(_sitFirePos);
            Notify("$hearthwife_busy_sitfire");
            if (HorizontalDistance(transform.position, _sitFirePos) < 1.2f)
            {
                OnArrived();
            }

            return true;
        }

        private void EnterSitByFire()
        {
            _hasTarget = false;
            SilenceAiMove();
            UnequipTool();

            if (_sitFire != null)
            {
                if (!WifeOccupancy.TryOccupy(_sitFire, this, SitHardTimeout))
                {
                    EndOwnedChore(1f);
                    return;
                }

                _occupiedProp = _sitFire;
                EnterStationaryWorkHold(_sitFire.transform.position, 2f);
            }

            var sitFor = Random.Range(18f, 40f);
            // Fire sit: emote_sit or calm emote_rest (Fires persistent pool).
            var firePose = Random.value < 0.45f ? "emote_rest" : "emote_sit";
            WifeEmotes.Play(gameObject, firePose, sitFor, persistent: true);
            _sitting = true;
            _idleActionUntil = Time.time + Mathf.Min(sitFor, SitHardTimeout);
            Notify("$hearthwife_busy_sitfire");
            if (Random.value < 0.55f)
            {
                WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.FireLine()));
            }
        }

        private static string PlantTip(Plant.Status st)
        {
            switch (st)
            {
                case Plant.Status.NoSun:
                    return Localization.instance.Localize("$hearthwife_tip_plant_nosun");
                case Plant.Status.NoSpace:
                    return Localization.instance.Localize("$hearthwife_tip_plant_nospace");
                case Plant.Status.WrongBiome:
                    return Localization.instance.Localize("$hearthwife_tip_plant_biome");
                case Plant.Status.NotCultivated:
                    return Localization.instance.Localize("$hearthwife_tip_plant_soil");
                case Plant.Status.TooHot:
                    return Localization.instance.Localize("$hearthwife_tip_plant_hot");
                case Plant.Status.TooCold:
                    return Localization.instance.Localize("$hearthwife_tip_plant_cold");
                default:
                    return Localization.instance.Localize("$hearthwife_tip_plant_bad");
            }
        }

        private static bool IsCropPickable(Pickable pick)
        {
            var prefab = pick.m_itemPrefab;
            var name = prefab != null ? prefab.name : pick.name;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Cultivated farm crops only (wild berries/mushrooms → forage).
            return name.IndexOf("Carrot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Turnip", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Onion", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Barley", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Flax", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsForagePickable(Pickable pick)
        {
            return WifePickupFilter.IsAllowedPickable(pick, PickupCategory.DefaultMask);
        }

        private static bool IsForageItemName(string name) => WifePickupFilter.IsPlantGatherName(name);

        private void ScoopForageNear(Inventory inv, Vector3 center, float radius, Pickable pick)
        {
            if (inv == null)
            {
                return;
            }

            var mask = _home != null ? _home.PickupMask : PickupCategory.DefaultMask;
            var want = pick?.m_itemPrefab != null ? pick.m_itemPrefab.name : null;

            foreach (var drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || drop.m_itemData == null)
                {
                    continue;
                }

                if (Vector3.Distance(drop.transform.position, center) > radius)
                {
                    continue;
                }

                var n = drop.m_itemData.m_dropPrefab != null
                    ? drop.m_itemData.m_dropPrefab.name
                    : drop.gameObject.name;
                var allowed = WifePickupFilter.IsAllowedName(n, mask) ||
                              (want != null && n.Equals(want, System.StringComparison.OrdinalIgnoreCase));
                if (!allowed)
                {
                    continue;
                }

                if (!TryAddItemToTotemChest(drop.m_itemData, drop.gameObject != null ? drop.gameObject.name : n))
                {
                    continue;
                }

                PlayPickupEffects();
                DestroyWorldDrop(drop);
            }
        }

        private static bool HasSmeltFuel(Inventory inv, Smelter sm)
        {
            var fuelName = sm.m_fuelItem != null ? sm.m_fuelItem.gameObject.name : "Coal";
            return HasFuelNamed(inv, fuelName) || HasFuelNamed(inv, "Coal") || HasFuelNamed(inv, "Wood");
        }

        private static bool TryConsumeSmeltFuel(Inventory inv, Smelter sm)
        {
            var fuelName = sm.m_fuelItem != null ? sm.m_fuelItem.gameObject.name : "Coal";
            return TryConsumeNamedFuel(inv, fuelName) ||
                   TryConsumeNamedFuel(inv, "Coal") ||
                   TryConsumeNamedFuel(inv, "Wood");
        }

        private static void ScoopNear(Inventory inv, Vector3 center, float radius)
        {
            foreach (var drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || drop.m_itemData == null)
                {
                    continue;
                }

                if (Vector3.Distance(drop.transform.position, center) > radius)
                {
                    continue;
                }

                var clone = drop.m_itemData.Clone();
                if (!inv.AddItem(clone))
                {
                    continue;
                }

                if (drop.m_nview != null && drop.m_nview.IsValid())
                {
                    drop.m_nview.Destroy();
                }
                else
                {
                    Object.Destroy(drop.gameObject);
                }
            }
        }
    }
}
