using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Homestead wife: chores inside the idol circle.
    /// Idle = parked presence (LookAt/greet/emotes); rare optional stroll.
    /// Movement = MonsterAI.MoveTo (VillageNPCs path) + Character walk — never raw CC.Move.
    /// </summary>
    public partial class WifeAgent : MonoBehaviour, Hoverable, Interactable
    {
        private enum Chore
        {
            Idle,
            Repair,
            Collect,
            Fish,
            Sleep,
            Cook,
            Cauldron,
            Fire,
            Mead,
            Sit,
            Nap,
            Smelt,
            Farm,
            Forage,
            SitFire
        }

        private WifeHome _home;
        internal WifeHome Home => _home;
        private Humanoid _humanoid;
        private VisEquipment _vis;
        private ZNetView _nview;
        private MonsterAI _ai;
        private Character _character;
        private Chore _chore = Chore.Idle;
        private Vector3 _target;
        private WearNTear _repairTarget;
        private ItemDrop _lootTarget;
        private ItemDrop.ItemData _carriedItem;
        private bool _carryingLoot;
        private float _actionUntil;
        private float _fishCooldown;
        /// <summary>Legacy soft block; primary gate is WifeHome.IsStorageFull.</summary>
        private float _collectBlockedUntil;
        private float _talkCooldown;
        private float _idlePauseUntil;
        private float _idleActionUntil;
        private float _wanderUntil;
        private string _hoverHint;
        private float _hoverHintUntil;
        private int _appliedDress = -1;
        private int _appliedHair = -1;
        private int _appliedHairColor = -1;
        private int _appliedSkin = -1;
        private bool _booted;
        /// <summary>True after Boot finished — home think loop can decide idle/work.</summary>
        internal bool IsBooted => _booted;
        /// <summary>First idle pick after spawn/load prefers a short stroll over a long linger.</summary>
        private bool _wakeWithStroll;
        private bool _hasTarget;
        private bool _sleeping;
        private bool _sleepAttached;
        private bool _sitting;
        private CharacterController _cc;
        private bool _ccWasEnabled = true;
        private Fireplace _fireTarget;
        private CookingStation _cookTarget;
        /// <summary>Grill she filled this cook block — only she may reclaim done food here.</summary>
        private CookingStation _wifeCookStation;
        private CraftingStation _cauldronTarget;
        private Recipe _cauldronRecipe;
        private Fermenter _meadTarget;
        private Chair _sitTarget;
        private Object _occupiedProp;
        private const float SitHardTimeout = 90f;
        private bool _borrowedHammer;

        // Player.AttachStart parity — Character.AttachStart is empty; logic lives on Player.
        private bool _playerAttached;
        private Transform _attachPoint;
        private string _attachAnimation = "";
        private Vector3 _attachDetachOffset;
        private Collider[] _attachColliders;
        private RandomAnimation _randomAnim;

        private Vector3 _stuckCheckPos;
        private float _lastProgressAt;
        private const float StuckSeconds = 3.5f;
        private const float StuckMoveEpsilon = 0.35f;
        /// <summary>Repair from this far (roofs / walls) — no need to glue to the mesh.</summary>
        private const float RepairReach = 7.5f;

        // Fires-style: one chore owns the agent until done; cooldown before next pick.
        private float _choreCooldownUntil;
        private float _choreStartedAt;
        private bool _wasBusyLastThink;
        private const float MaxBusySeconds = 75f;
        /// <summary>Fires CompanionIdleBehavior.chairSitCooldown — avoid sit/stand loop.</summary>
        private float _sitCooldownUntil;
        private Object _lastSitChair;
        private float _waterRescueAt;
        private Collider _ignoredPlayerCollider;
        private bool _playerCollisionRestored;
        private const float PlayerClearance = 1.6f;
        private float _statusHygieneAt;

        // Organic gather session — don't pick 1 then jump to another chore.
        private bool _gatherSessionActive;
        private const int GatherMinItems = 3;
        private const int GatherMaxItems = 6;
        private const float GatherMinSeconds = 18f;
        private const float GatherMaxSeconds = 35f;
        private const float GatherNearLeash = 14f;

        /// <summary>
        /// Soft walk-home when outside the ward. Must NOT spam _chore=Idle every frame —
        /// that cancelled Collect/Fire mid-task and frozen her on the radius edge.
        /// </summary>
        private bool _leashingHome;
        private float _leashStartedAt;

        internal static WifeAgent Spawn(WifeHome home)
        {
            if (home == null)
            {
                return null;
            }

            WifeLimits.CullExtraWives(home);
            if (!WifeLimits.CanSpawnWifeFor(home))
            {
                WifeLimits.NotifyWifeLimit();
                return null;
            }

            // Spawn beside the idol at the SAME floor height (wood/stone piece floors, not only dirt).
            var pos = home.HomePosition + home.transform.forward * 2.5f;
            pos.y = home.HomePosition.y + 0.5f;
            SnapToPieceOrTerrain(ref pos, home.HomePosition.y);
            if (IsUnsafeWaterWalk(pos) &&
                !TryFindDryNear(home, home.HomePosition, 2.5f, 10f, out pos))
            {
                pos = home.HomePosition + home.transform.forward * 1.2f;
                SnapToPieceOrTerrain(ref pos, home.HomePosition.y);
            }

            var rot = Quaternion.LookRotation(home.transform.forward);
            var go = WifeNpcPrefab.SpawnAt(pos, rot);
            if (go == null)
            {
                return null;
            }

            var agent = go.GetComponent<WifeAgent>() ?? go.AddComponent<WifeAgent>();
            agent._home = home;
            agent.CacheComponents();
            agent.StartCoroutine(agent.Boot(home));
            return agent;
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            _humanoid = GetComponent<Humanoid>();
            _vis = GetComponent<VisEquipment>();
            _nview = GetComponent<ZNetView>();
            _ai = GetComponent<MonsterAI>();
            _character = GetComponent<Character>();
        }

        private void Update()
        {
            if (!_booted || _home == null || _ai == null)
            {
                return;
            }

            TickAmbientGreet();
            TryMorningGreet();
            TickAffectionAndProtect(_home);
            TickRestedNearFire();
            TickStatusHygiene();
            TickStuckDoors();
            TickWaterRescue();
            TickMapPin();

            if (_lookPresentActive)
            {
                TickLookPresentSession();
            }

            // Keep her leashed to the ward circle (fishing may leave the circle for the river).
            // Soft slack: work near the edge must not thrash ownership every frame.
            if (!_home.IsInside(transform.position, 2.5f) &&
                _chore != Chore.Sleep &&
                _chore != Chore.Fish &&
                _chore != Chore.Nap)
            {
                TickHomeLeash();
                return;
            }

            if (_leashingHome && _home.IsInside(transform.position, 1.2f))
            {
                _leashingHome = false;
                _leashStartedAt = 0f;
            }

            if (_sleeping || _sitting)
            {
                SilenceAiMove();
                // Player.UpdateAttach — keep glued to seat/bed with sit/sleep pose.
                TickPlayerAttach();
                var rbSit = GetComponent<Rigidbody>();
                if (rbSit != null && !rbSit.isKinematic)
                {
                    rbSit.linearVelocity = Vector3.zero;
                    rbSit.angularVelocity = Vector3.zero;
                }

                // Night sleep: wake with daylight (don't wait for ThinkInterval).
                if (_sleeping && _chore == Chore.Sleep)
                {
                    TickDaybreakWake();
                }

                // Hard timeout so she never stays attached forever (Fires pattern).
                if (_sitting && (Time.time >= _idleActionUntil ||
                                 (_choreStartedAt > 0f && Time.time - _choreStartedAt >= SitHardTimeout)))
                {
                    ExitSitting();
                    EndOwnedChore(2f);
                }
                else if (_sleeping && _chore == Chore.Nap && Time.time >= _idleActionUntil)
                {
                    ExitSleepVisual();
                    EndOwnedChore(2f);
                }

                return;
            }

            // Idle walks via MonsterAI.MoveTo (path + stairs); never CC.Move.
            if (_chore == Chore.Idle)
            {
                if (_hasTarget)
                {
                    if (ShouldYieldToPlayer(_target))
                    {
                        if (_lookPresentActive)
                        {
                            // Soft yield — don't cancel the fitting walk.
                            StopLocomotion();
                            return;
                        }

                        // Abort stroll once — never pulse StopLocomotion with target still set
                        // (that looked like robotic stutter when the player stood nearby).
                        _hasTarget = false;
                        StopLocomotion();
                        ParkStill();
                        RefreshRandomAnimationGate();
                        var yieldPause = Random.Range(6f, 12f);
                        _idlePauseUntil = Time.time + yieldPause;
                        BeginBeat(Beat.Linger, yieldPause);
                        return;
                    }

                    StepToward(_target, 1.8f, out var arrived, out var stuck);
                    if (arrived || stuck)
                    {
                        if (stuck)
                        {
                            WifeStuckMemory.Mark(transform.position, 28f);
                            TryOpenNearbyDoors(force: true);
                        }

                        if (_lookPresentActive)
                        {
                            OnLookPresentArrived();
                            return;
                        }

                        // Idle arrived: linger here (organic presence — don't instantly re-pick sit/nap).
                        _hasTarget = false;
                        StopLocomotion();
                        ParkStill();
                        RefreshRandomAnimationGate();
                        var arrivePause = Random.Range(12f, 24f);
                        _idlePauseUntil = Time.time + arrivePause;
                        BeginBeat(Beat.Linger, arrivePause);
                        _wanderUntil = Mathf.Max(_wanderUntil, Time.time + Random.Range(18f, 36f));
                    }
                }
                else
                {
                    ParkStill();
                }

                return;
            }

            // Stationary work hold — ParkStill so Character lookDir / greet don't spin her on Y.
            // Gather session walks to the next bush/drop — don't freeze on leftover pick hold.
            if (Time.time < _actionUntil && IsStationaryWorkHold() &&
                !(_gatherSessionActive && _hasTarget))
            {
                ParkStill();
                return;
            }

            if (!_hasTarget)
            {
                _ai?.StopMoving();
                return;
            }

            if (Time.time < _actionUntil &&
                _chore != Chore.Repair &&
                _chore != Chore.Collect &&
                _chore != Chore.Forage)
            {
                _ai?.StopMoving();
                return;
            }

            var arrive = (_chore == Chore.Sleep || _chore == Chore.Nap) ? 0.9f
                : (_chore == Chore.Sit ? 0.85f
                : (_chore == Chore.Collect ? 1.5f
                : (_chore == Chore.Repair ? 4.2f
                : (_chore == Chore.Forage ? 2.4f : 2.0f))));

            // Repair: if already in hammer reach of the piece (roof etc.), stop walking and swing.
            if (_chore == Chore.Repair &&
                _repairTarget != null &&
                InRepairReach(transform.position, _repairTarget))
            {
                OnArrived();
                return;
            }

            // Forage: pick from a short stand-off (bush/mushroom), no mesh glue.
            if (_chore == Chore.Forage &&
                _forageTarget != null &&
                InForageReach(transform.position, _forageTarget))
            {
                OnArrived();
                return;
            }

            // Bed: pathfinding rarely hits the mattress center — snap-lie when close enough.
            if ((_chore == Chore.Sleep || _chore == Chore.Nap) &&
                HorizontalDistance(transform.position, _target) <= 1.65f)
            {
                OnArrived();
                return;
            }

            StepToward(_target, arrive, out var choreArrived, out var choreStuck);
            if (choreArrived)
            {
                OnArrived();
                return;
            }

            if (choreStuck)
            {
                WifeStuckMemory.Mark(transform.position, 30f);
                WifeStuckMemory.Mark(_target, 30f);
                TryOpenNearbyDoors(force: true);

                // Stuck en route to a piece but still in reach — repair from here (roof / fence).
                if (_chore == Chore.Repair &&
                    _repairTarget != null &&
                    InRepairReach(transform.position, _repairTarget))
                {
                    OnArrived();
                    return;
                }

                if (_chore == Chore.Forage &&
                    _forageTarget != null &&
                    InForageReach(transform.position, _forageTarget))
                {
                    OnArrived();
                    return;
                }

                // Sleep / nap: closer retry, then force lie on bed (never cancel in Basics).
                if (_chore == Chore.Sleep || _chore == Chore.Nap)
                {
                    if (_home != null && _home.HasAssignedBed)
                    {
                        var bedPos = _home.GetSleepPosition(out var bedRot);
                        if (HorizontalDistance(transform.position, bedPos) > 1.7f)
                        {
                            var closer = GetBedWalkTarget(_home, out _);
                            SetTarget(closer);
                            ResetStuck();
                            return;
                        }

                        _hasTarget = false;
                        if (!_sleeping)
                        {
                            EnterSleepVisual(bedPos, bedRot);
                        }

                        if (_chore == Chore.Nap)
                        {
                            _idleActionUntil = Time.time + Random.Range(18f, 35f);
                        }

                        return;
                    }
                }

                // Sit: one closer retry before cancel (arrive was often short of attach).
                if (_chore == Chore.Sit && _sitTarget != null)
                {
                    var attach = _sitTarget.m_attachPoint != null
                        ? _sitTarget.m_attachPoint
                        : _sitTarget.transform;
                    var closer = ApproachPoint(attach.position, 0.2f);
                    SnapToPieceOrTerrain(ref closer, attach.position.y);
                    if (HorizontalDistance(transform.position, closer) > 0.5f)
                    {
                        SetTarget(closer);
                        ResetStuck();
                        return;
                    }

                    _sitTarget = null;
                    EndOwnedChore(2f);
                    _hasTarget = false;
                    SilenceAiMove();
                    SetWalkAnim(0f);
                    ParkStill();
                    _idlePauseUntil = Time.time + Random.Range(8f, 16f);
                    return;
                }

                // Idle only: never warp onto roofs — cancel and chill.
                if (_chore == Chore.Idle)
                {
                    _hasTarget = false;
                    SilenceAiMove();
                    SetWalkAnim(0f);
                    ParkStill();
                    _idlePauseUntil = Time.time + Random.Range(8f, 16f);
                    return;
                }

                // Work chores: never hard-warp into pieces (stuck / nude / freeze).
                // Mark the approach blocked, abort, walk normally next think.
                if (_chore == Chore.Repair ||
                    _chore == Chore.Cook ||
                    _chore == Chore.Fire ||
                    _chore == Chore.Collect ||
                    _chore == Chore.Smelt ||
                    _chore == Chore.Farm ||
                    _chore == Chore.Forage ||
                    _chore == Chore.Mead ||
                    _chore == Chore.Cauldron ||
                    _chore == Chore.Fish)
                {
                    AbortStuckWorkChore();
                    return;
                }

                // Prefer a nearby unblocked approach instead of hard warp spam.
                if (_home != null &&
                    WifeStuckMemory.TryPickUnblocked(_home, 0.1f, 0.35f, out var alt) &&
                    HorizontalDistance(alt, _target) < 8f)
                {
                    SetTarget(alt);
                    ResetStuck();
                    return;
                }

                // Last resort only for non-work (should be rare) — park, don't warp.
                _hasTarget = false;
                SilenceAiMove();
                ParkStill();
                EndOwnedChore(2f);
            }
        }

        private void AbortStuckWorkChore()
        {
            WifeStuckMemory.Mark(transform.position, 30f);
            if (_hasTarget)
            {
                WifeStuckMemory.Mark(_target, 45f);
            }

            StopWorkRoutine();
            ForceClearRightHand();
            UnequipFishingRod();
            _repairTarget = null;
            _lootTarget = null;
            _fireTarget = null;
            _cookTarget = null;
            _cauldronTarget = null;
            _meadTarget = null;
            _smeltTarget = null;
            _farmTarget = null;
            _forageTarget = null;
            if (_carryingLoot)
            {
                DropCarriedAtFeet();
            }

            EndOwnedChore(4f);
            SilenceAiMove();
            SetWalkAnim(0f);
            ParkStill();
            _idlePauseUntil = Time.time + Random.Range(4f, 8f);
            ResetStuck();
        }

        /// <summary>
        /// VillageNPCs MoveTo for path/stairs; Fires SetMoveDir fallback.
        /// Never CharacterController.Move — clips floors / climbs roofs.
        /// Critical: BaseAI.MoveTo returns true on FindPath failure (not only arrival) —
        /// treating that as arrived cancelled every stroll (she stood still).
        /// </summary>
        private void StepToward(Vector3 worldTarget, float arrive, out bool arrived, out bool stuck)
        {
            arrived = false;
            stuck = false;

            if (_character == null)
            {
                _character = GetComponent<Character>();
            }

            if (_ai == null)
            {
                _ai = GetComponent<MonsterAI>();
            }

            // MoveTo needs the Behaviour enabled (Character.UpdateWalking gates on m_baseAI).
            if (_ai != null && !_ai.enabled)
            {
                _ai.enabled = true;
            }

            // Hot-reload / old Boot may have left kinematic on — Character can't translate then.
            var rbWalk = GetComponent<Rigidbody>();
            if (rbWalk != null && rbWalk.isKinematic)
            {
                rbWalk.isKinematic = false;
            }

            var flat = worldTarget - transform.position;
            flat.y = 0f;
            var dist = flat.magnitude;
            if (dist <= arrive)
            {
                arrived = true;
                StopLocomotion();
                return;
            }

            // Don't shove / walk through the player (CharacterControllers push each other).
            if (ShouldYieldToPlayer(worldTarget))
            {
                StopLocomotion();
                return;
            }

            if (IsStuck())
            {
                stuck = true;
                StopLocomotion();
                return;
            }

            // Refuse walking onto roofs / tabletops (stairs rise gradually; names checked too).
            var dir = flat / dist;
            var probe = transform.position + dir * 0.75f + Vector3.up * 0.4f;
            if (Physics.Raycast(probe, Vector3.down, out var ahead, 3f,
                    LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"),
                    QueryTriggerInteraction.Ignore) &&
                ahead.normal.y >= 0.55f &&
                ahead.point.y > transform.position.y + 1.05f &&
                !IsStairLikeSurface(ahead.collider))
            {
                stuck = true;
                StopLocomotion();
                return;
            }

            try
            {
                _character?.SetWalk(true);
                _character?.SetRun(false);
            }
            catch
            {
            }

            // Pathfinding owns locomotion (Humanoid agent) — do NOT StopMoving here.
            // Do NOT hammer forward_speed while moving: prefab has m_smoothCharacterSpeeds;
            // forcing 1.0 fights Character velocity → robotic stutter (OfflineCompanions:
            // drive anim from real motion, not a constant).
            // Prefer Fires SetMoveDir (worked before LookAt fight); MoveTo for stairs when path OK.
            try
            {
                if (_ai != null)
                {
                    var moveDone = _ai.MoveTo(Time.deltaTime, worldTarget, arrive, false);
                    if (!moveDone)
                    {
                        return; // following path waypoints
                    }

                    // Real arrival only when close. MoveTo also returns true on FindPath fail.
                    var left = worldTarget - transform.position;
                    left.y = 0f;
                    if (left.magnitude <= Mathf.Max(arrive, 0.55f))
                    {
                        arrived = true;
                        StopLocomotion();
                        return;
                    }
                }
            }
            catch
            {
            }

            // Fires FireTending: direct Character drive (stairs via Character, no CC.Move).
            try
            {
                _character?.SetMoveDir(dir);
            }
            catch
            {
            }
        }

        private void StopLocomotion()
        {
            SilenceAiMove();
            try
            {
                _character?.SetMoveDir(Vector3.zero);
                _character?.SetWalk(false);
                _character?.SetRun(false);
            }
            catch
            {
            }

            SetWalkAnim(0f);
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                var v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(0f, v.y, 0f);
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Yield only when the player blocks the path ahead — not merely nearby in front.
        /// Blanket d&lt;1.2 caused stop/start stutter (robot walk) while watching her stroll.
        /// </summary>
        private bool ShouldYieldToPlayer(Vector3 worldTarget)
        {
            // Sleep/nap/sit/fire-sit must reach the furniture — don't freeze beside the bed/fire.
            // Gather lote must keep walking when you're watching her (status without pick felt broken).
            if (_chore == Chore.Sleep || _chore == Chore.Nap ||
                _chore == Chore.Sit || _chore == Chore.SitFire ||
                _gatherSessionActive ||
                _chore == Chore.Forage || _chore == Chore.Collect)
            {
                return false;
            }

            var player = Player.m_localPlayer;
            if (player == null || player == _character)
            {
                return false;
            }

            var toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            var d = toPlayer.magnitude;
            if (d < 0.05f || d >= PlayerClearance)
            {
                return false;
            }

            var toPlayerDir = toPlayer / d;

            // Behind: keep walking — solid capsules handle contact; don't abort the stroll.
            var forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.01f &&
                Vector3.Dot(forward.normalized, toPlayerDir) < -0.05f)
            {
                return false;
            }

            var toTarget = worldTarget - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f)
            {
                return false;
            }

            // Player must sit roughly between her and the destination.
            return Vector3.Dot(toTarget.normalized, toPlayerDir) > 0.45f;
        }

        /// <summary>Unused — keep collision solid; yield handles shove without IgnoreCollision.</summary>
        private void EnsurePlayerCollisionIgnored(Player player)
        {
            // Intentionally no-op. Permanent IgnoreCollision let the player walk through her.
        }

        internal static bool IsPointNearPlayer(Vector3 world, float minDist = 2.0f)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            var a = world;
            var b = player.transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) < minDist;
        }

        private static bool IsTooCloseToPlayer(Vector3 world, float minDist = 2.0f) =>
            IsPointNearPlayer(world, minDist);

        /// <summary>Fires floor keywords inverted — reject roofs/walls/furniture as walk targets.</summary>
        private static bool IsUnwalkablePieceName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var n = name.ToLowerInvariant();
            return n.Contains("roof") ||
                   n.Contains("thatch") ||
                   n.Contains("ridge") ||
                   n.Contains("wall") ||
                   n.Contains("beam") ||
                   n.Contains("pillar") ||
                   n.Contains("fence") ||
                   n.Contains("gate") ||
                   n.Contains("chest") ||
                   n.Contains("table") ||
                   n.Contains("bench") ||
                   n.Contains("chair") ||
                   n.Contains("bed") ||
                   n.Contains("shelf") ||
                   n.Contains("torch") ||
                   n.Contains("banner");
        }

        private static bool IsStairLikeSurface(Collider col)
        {
            if (col == null)
            {
                return false;
            }

            var goName = col.gameObject != null ? col.gameObject.name : "";
            var piece = col.GetComponentInParent<Piece>();
            var pieceName = piece != null ? piece.m_name ?? piece.name : "";
            var n = (goName + " " + pieceName).ToLowerInvariant();
            return n.Contains("stair") ||
                   n.Contains("ladder") ||
                   n.Contains("ramp") ||
                   n.Contains("step");
        }

        private static bool IsWalkableHit(RaycastHit hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                return false;
            }

            if (hit.collider.GetComponentInParent<Character>() != null ||
                hit.collider.GetComponentInParent<ItemDrop>() != null ||
                hit.collider.GetComponentInParent<WifeAgent>() != null ||
                hit.collider.GetComponentInParent<WifeHome>() != null)
            {
                return false;
            }

            if (IsWaterCollider(hit.collider))
            {
                return false;
            }

            if (hit.normal.y < 0.55f)
            {
                return false;
            }

            var goName = hit.collider.gameObject != null ? hit.collider.gameObject.name : "";
            var piece = hit.collider.GetComponentInParent<Piece>();
            var pieceName = piece != null ? (piece.m_name ?? piece.name) : "";
            if (IsUnwalkablePieceName(goName) || IsUnwalkablePieceName(pieceName))
            {
                // Stairs named oddly still allowed.
                if (!IsStairLikeSurface(hit.collider))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Fires IsDestinationReachable: path + Y delta + spherecast (don't pick through walls/roofs).
        /// </summary>
        private bool IsDestinationReachable(Vector3 destination)
        {
            var from = transform.position;
            // Stairs between storeys — allow more than flat wander; still clamp crazy warps.
            if (Mathf.Abs(destination.y - from.y) > 6f)
            {
                return false;
            }

            try
            {
                if (Pathfinding.instance != null &&
                    !Pathfinding.instance.HavePath(from, destination, Pathfinding.AgentType.Humanoid))
                {
                    return false;
                }
            }
            catch
            {
            }

            var origin = from + Vector3.up * 0.8f;
            var delta = destination + Vector3.up * 0.8f - origin;
            var mag = delta.magnitude;
            RaycastHit hit = default;
            if (mag > 0.5f &&
                Physics.SphereCast(origin, 0.28f, delta.normalized, out hit, mag,
                    LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid"),
                    QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null &&
                    hit.collider.GetComponentInParent<Piece>() != null &&
                    hit.collider.GetComponentInParent<WifeHome>() == null &&
                    hit.collider.GetComponentInParent<WifeAgent>() == null)
                {
                    // Fires: any Piece in cast blocks. Soften for stairs underfoot (high normal).
                    if (hit.normal.y < 0.45f && !IsStairLikeSurface(hit.collider))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Place a wander point on the walkable surface near the character's floor
        /// (supports upstairs), not forced to totem ground height.
        /// </summary>
        private bool TryPlaceOnWalkable(ref Vector3 point)
        {
            var nearY = transform.position.y;
            var origin = new Vector3(point.x, nearY + 2.8f, point.z);
            var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            var hits = Physics.RaycastAll(origin, Vector3.down, 7f, mask, QueryTriggerInteraction.Ignore);
            var bestY = float.NaN;
            var bestScore = float.MaxValue;

            foreach (var hit in hits)
            {
                if (!IsWalkableHit(hit))
                {
                    continue;
                }

                var y = hit.point.y;
                // Prefer surface close to current floor (stairs/same storey), allow ±3.5m.
                if (y > nearY + 3.5f || y < nearY - 4f)
                {
                    continue;
                }

                var score = Mathf.Abs(y - nearY);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestY = y;
                }
            }

            if (float.IsNaN(bestY))
            {
                return false;
            }

            point.y = bestY + 0.05f;
            // Lake/river bed is walkable terrain — reject if under the water surface.
            if (IsUnsafeWaterWalk(point))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Feet would be under the water surface (lake bed / deep wade).
        /// Surface = Water-layer hit; compares to proposed feet Y.
        /// </summary>
        internal static bool IsUnsafeWaterWalk(Vector3 feetPos, float maxWade = 0.4f)
        {
            if (!TryGetWaterSurfaceY(feetPos, out var surfaceY))
            {
                return false;
            }

            return feetPos.y < surfaceY - maxWade;
        }

        private static bool TryGetWaterSurfaceY(Vector3 near, out float surfaceY)
        {
            surfaceY = 0f;
            try
            {
                var origin = near + Vector3.up * 12f;
                var waterMask = LayerMask.GetMask("Water");
                if (waterMask != 0 &&
                    Physics.Raycast(origin, Vector3.down, out var hit, 30f, waterMask,
                        QueryTriggerInteraction.Collide))
                {
                    surfaceY = hit.point.y;
                    return true;
                }

                // Fallback: named water colliders on any layer.
                var hits = Physics.RaycastAll(origin, Vector3.down, 30f, ~0,
                    QueryTriggerInteraction.Collide);
                foreach (var h in hits)
                {
                    if (!IsWaterCollider(h.collider))
                    {
                        continue;
                    }

                    surfaceY = h.point.y;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsWaterCollider(Collider col)
        {
            if (col == null)
            {
                return false;
            }

            try
            {
                if (col.gameObject.layer == LayerMask.NameToLayer("Water"))
                {
                    return true;
                }
            }
            catch
            {
            }

            var n = col.gameObject != null ? col.gameObject.name : "";
            return n.IndexOf("water", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Pull out of lakes/rivers — never stay underwater on the lake bed.</summary>
        private void TickWaterRescue()
        {
            if (_home == null || Time.time < _waterRescueAt)
            {
                return;
            }

            _waterRescueAt = Time.time + 0.8f;

            if (_sitting || _sleepAttached || _playerAttached)
            {
                return;
            }

            if (!IsSelfInWater())
            {
                return;
            }

            Vector3 dry;
            if (!TryFindDryNear(_home, transform.position, 3f, _home.Radius, out dry) &&
                !TryFindDryNear(_home, _home.HomePosition, 2f, 12f, out dry))
            {
                dry = _home.HomePosition + _home.transform.forward * 2f;
                SnapToPieceOrTerrain(ref dry, _home.HomePosition.y);
            }

            ExitSitting();
            if (_sleeping)
            {
                ExitSleepVisual();
            }

            _chore = Chore.Idle;
            _hasTarget = false;
            SilenceAiMove();
            transform.position = dry;
            if (_nview != null && _nview.IsValid())
            {
                _nview.GetZDO().SetPosition(dry);
            }

            _idlePauseUntil = Time.time + 4f;
            Jotunn.Logger.LogInfo("Hearthwife: rescued from water → dry land");
        }

        private bool IsSelfInWater()
        {
            if (_character != null)
            {
                try
                {
                    // Liquid surface above mid-torso = underwater / deep wade (UnderTheSea pattern).
                    if (_character.GetLiquidLevel() > transform.position.y + 0.5f)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return IsUnsafeWaterWalk(transform.position, 0.3f);
        }

        internal static bool TryFindDryNear(WifeHome home, Vector3 around, float minDist, float maxDist,
            out Vector3 dry)
        {
            dry = around;
            if (home == null)
            {
                return false;
            }

            var preferY = home.HomePosition.y;
            for (var i = 0; i < 14; i++)
            {
                var ang = Random.Range(0f, Mathf.PI * 2f);
                var dist = Random.Range(minDist, Mathf.Max(minDist + 0.5f, maxDist));
                var p = around + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (!home.IsInside(p, 1f))
                {
                    continue;
                }

                SnapToPieceOrTerrain(ref p, preferY);
                if (IsUnsafeWaterWalk(p) || IsTooCloseToPlayer(p, 2.0f))
                {
                    continue;
                }

                dry = p;
                return true;
            }

            return false;
        }

        private void SilenceAiMove()
        {
            try
            {
                _ai?.StopMoving();
            }
            catch
            {
            }
        }

        private bool IsBusyOwned()
        {
            if (_sleeping || _sitting)
            {
                return true;
            }

            if (_chore != Chore.Idle)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// At a station/prop with no path target (cook wait, repair swings, fish, smelt…).
        /// Must not fight LookAt body yaw or RandomAnimation.
        /// </summary>
        private bool IsStationaryWorkHold()
        {
            if (_hasTarget || _sitting || _sleeping)
            {
                return false;
            }

            return _chore == Chore.Repair ||
                   _chore == Chore.Fish ||
                   _chore == Chore.Cook ||
                   _chore == Chore.Cauldron ||
                   _chore == Chore.Fire ||
                   _chore == Chore.Mead ||
                   _chore == Chore.Smelt ||
                   _chore == Chore.Farm ||
                   _chore == Chore.Forage ||
                   _chore == Chore.Collect;
        }

        /// <summary>
        /// Arrive at a prop and freeze for the action. Face once — never re-snap every tick
        /// while LookAt / Character.lookDir / RandomAnimation also write yaw (Y-axis jitter).
        /// </summary>
        private void EnterStationaryWorkHold(Vector3 faceWorld, float actionSeconds)
        {
            _hasTarget = false;
            _facePlayerUntil = 0f;
            _actionUntil = Time.time + Mathf.Max(0.5f, actionSeconds);
            FacePoint(faceWorld);
            ParkStill();
            RefreshRandomAnimationGate();
        }

        /// <summary>Same hold, keep current yaw (e.g. already facing water).</summary>
        private void EnterStationaryWorkHold(float actionSeconds)
        {
            _hasTarget = false;
            _facePlayerUntil = 0f;
            _actionUntil = Time.time + Mathf.Max(0.5f, actionSeconds);
            try
            {
                _character?.SetLookDir(transform.forward, 0f);
            }
            catch
            {
            }

            ParkStill();
            RefreshRandomAnimationGate();
        }

        /// <summary>Refresh hold while waiting (cook timer, fire fuel loop) — no FacePoint.</summary>
        private void HoldStationaryWorkTick(float extendSeconds = 2f)
        {
            _actionUntil = Time.time + Mathf.Max(0.5f, extendSeconds);
            ParkStill();
        }

        private Coroutine _workRoutine;

        private void StartWork(IEnumerator routine)
        {
            StopWorkRoutine();
            _workRoutine = StartCoroutine(routine);
        }

        private void StopWorkRoutine()
        {
            if (_workRoutine == null)
            {
                return;
            }

            try
            {
                StopCoroutine(_workRoutine);
            }
            catch
            {
            }

            _workRoutine = null;
        }

        /// <summary>
        /// E while walking/working: drop the chore, stop, face the player (organic attend).
        /// Sitting/sleeping stay attached — bubble only.
        /// </summary>
        private void InterruptToAttendPlayer()
        {
            StopWorkRoutine();
            ForceClearRightHand();
            UnequipFishingRod();

            _repairTarget = null;
            _lootTarget = null;
            _carryingLoot = false;
            _carriedItem = null;
            _fireTarget = null;
            _cookTarget = null;
            _cauldronTarget = null;
            _cauldronRecipe = null;
            _meadTarget = null;
            _smeltTarget = null;
            _farmTarget = null;
            _forageTarget = null;
            _sitTarget = null;
            _hoverHint = null;
            _hoverHintUntil = 0f;

            if (_chore != Chore.Idle || _hasTarget)
            {
                EndOwnedChore(0.6f);
            }
            else
            {
                ParkStill();
            }

            // Linger with the player instead of instantly picking another chore.
            var linger = Random.Range(5f, 9f);
            _idlePauseUntil = Time.time + linger;
            _facePlayerUntil = Time.time + linger;
            BeginBeat(Beat.Attend, linger);
            var cd = PluginConfig.ChoreCooldown != null ? PluginConfig.ChoreCooldown.Value : 12f;
            _choreCooldownUntil = Time.time + Mathf.Max(6f, cd * 0.5f);
        }

        private void BeginOwnedChore(Chore chore)
        {
            _chore = chore;
            _choreStartedAt = Time.time;
            _idlePauseUntil = 0f;
            _actionUntil = 0f;
            _leashingHome = false;
            ResetStuck();
            BeginBeat(BeatFromChore(chore));
        }

        private void EndOwnedChore(float idlePause = 1.2f)
        {
            if (_occupiedProp != null)
            {
                WifeOccupancy.Release(_occupiedProp, this);
                _occupiedProp = null;
            }

            _chore = Chore.Idle;
            _hasTarget = false;
            _workRoutine = null;
            _gatherSessionActive = false;
            _actionUntil = Time.time + idlePause;
            var cd = PluginConfig.ChoreCooldown != null ? PluginConfig.ChoreCooldown.Value : 12f;
            if (_home != null && _home.Lifestyle == LifestyleMode.Diligent)
            {
                cd = Mathf.Max(3f, cd * 0.35f);
            }

            _choreCooldownUntil = Time.time + Mathf.Max(3f, cd);
            SilenceAiMove();
            SetWalkAnim(0f);
            EndBeat(idlePause);
        }

        /// <summary>Menu switched Leisure/Balanced/Diligent — soft reset pick timer.</summary>
        internal void OnLifestyleChanged(LifestyleMode mode)
        {
            if (mode == LifestyleMode.Leisure &&
                _chore != Chore.Idle &&
                _chore != Chore.Sit &&
                _chore != Chore.SitFire &&
                _chore != Chore.Nap &&
                _chore != Chore.Sleep)
            {
                StopWorkRoutine();
                EndOwnedChore(2f);
            }

            if (PluginConfig.AllowsWork(mode))
            {
                // Let her pick fire/chores soon after leaving Leisure — cancel idle stroll lock.
                _choreCooldownUntil = 0f;
                _idlePauseUntil = 0f;
                _actionUntil = 0f;
                _wasBusyLastThink = false;
                _leashingHome = false;
                if (_lookPresentActive)
                {
                    ReleaseLookPresent();
                }

                if (_chore == Chore.Idle && !_sitting && !_sleeping && !_sleepAttached)
                {
                    _hasTarget = false;
                    CancelBeat();
                    StopLocomotion();
                    ParkStill();
                }
                else if (_chore != Chore.Idle && _chore != Chore.Sit && _chore != Chore.SitFire &&
                         _chore != Chore.Sleep && _chore != Chore.Nap)
                {
                    // Switching into Trabalhadora while zombie-busy — hard clear.
                    StopWorkRoutine();
                    _gatherSessionActive = false;
                    _hasTarget = false;
                    ForceClearRightHand();
                    EndOwnedChore(0.2f);
                    _choreCooldownUntil = 0f;
                }

                _home?.RequestThinkSoon();
            }
            else
            {
                _idlePauseUntil = Time.time + Random.Range(2f, 5f);
            }
        }

        private void AbortBusyIfTimedOut()
        {
            // Chair attach has its own end time — don't yank her off mid-sit.
            if (_sitting || _chore == Chore.Sit && _sleepAttached)
            {
                return;
            }

            if (!IsBusyOwned() || _chore == Chore.Sleep)
            {
                return;
            }

            if (_choreStartedAt <= 0f)
            {
                _choreStartedAt = Time.time;
                return;
            }

            if (Time.time - _choreStartedAt < MaxBusySeconds)
            {
                return;
            }

            // Hard timeout — Fires-style phase abort.
            StopWorkRoutine();
            _gatherSessionActive = false;
            ForceClearRightHand();
            ExitSitting();
            if (_sleeping && _chore == Chore.Nap)
            {
                ExitSleepVisual();
            }

            _repairTarget = null;
            _lootTarget = null;
            _fireTarget = null;
            _cookTarget = null;
            _cauldronTarget = null;
            _meadTarget = null;
            _sitTarget = null;
            _smeltTarget = null;
            _farmTarget = null;
            _forageTarget = null;
            ClearCarry();
            EndOwnedChore(0.5f);
            _choreCooldownUntil = 0f;
            FaceHome();
        }

        /// <summary>
        /// Walk back into the ward once — abort owned work cleanly, never wipe chore every frame.
        /// </summary>
        private void TickHomeLeash()
        {
            if (_home == null)
            {
                return;
            }

            ExitSitting();

            if (!_leashingHome)
            {
                _leashingHome = true;
                _leashStartedAt = Time.time;

                // One clean abort — not _chore=Idle while a gather coroutine still runs.
                if (_chore != Chore.Idle &&
                    _chore != Chore.Sit &&
                    _chore != Chore.SitFire &&
                    _chore != Chore.Sleep &&
                    _chore != Chore.Nap)
                {
                    StopWorkRoutine();
                    _gatherSessionActive = false;
                    ForceClearRightHand();
                    UnequipTool();
                    UnequipFishingRod();
                    ClearCarry();
                    _repairTarget = null;
                    _lootTarget = null;
                    _fireTarget = null;
                    _cookTarget = null;
                    _cauldronTarget = null;
                    _meadTarget = null;
                    _smeltTarget = null;
                    _farmTarget = null;
                    _forageTarget = null;
                    EndOwnedChore(0.25f);
                    _choreCooldownUntil = 0f;
                }
                else
                {
                    _chore = Chore.Idle;
                    CancelBeat();
                }

                Vector3 stand;
                if (!_home.TryPickPointInRadius(0.2f, 0.55f, out stand) ||
                    !TryPlaceOnWalkable(ref stand) ||
                    !IsDestinationReachable(stand))
                {
                    stand = _home.HomePosition + _home.transform.forward * 2.5f;
                    TryPlaceOnWalkable(ref stand);
                }

                // Far outside — warp once, then resume think.
                if (HorizontalDistance(transform.position, stand) > 14f)
                {
                    _target = stand;
                    WarpToTarget();
                    _hasTarget = false;
                    _leashingHome = false;
                    _idlePauseUntil = Time.time + 1.5f;
                    _home.RequestThinkSoon();
                    return;
                }

                SetTarget(stand);
                _facePlayerUntil = 0f;
                ResetStuck();
            }

            if (!_hasTarget)
            {
                var stand = _home.HomePosition + _home.transform.forward * 2.5f;
                TryPlaceOnWalkable(ref stand);
                SetTarget(stand);
                ResetStuck();
            }

            _facePlayerUntil = 0f;
            StepToward(_target, 1.8f, out var leashArrived, out var leashStuck);

            var leashTooLong = _leashStartedAt > 0f && Time.time - _leashStartedAt > 18f;
            if (leashArrived || leashStuck || leashTooLong)
            {
                if (leashStuck || leashTooLong)
                {
                    WifeStuckMemory.Mark(transform.position, 25f);
                    TryOpenNearbyDoors(force: true);
                    // Still outside after stuck — soft warp onto walkable porch.
                    if (!_home.IsInside(transform.position, 1.0f))
                    {
                        var porch = _home.HomePosition + _home.transform.forward * 2.5f;
                        TryPlaceOnWalkable(ref porch);
                        _target = porch;
                        WarpToTarget();
                    }
                }

                _hasTarget = false;
                _leashingHome = false;
                _leashStartedAt = 0f;
                StopLocomotion();
                ParkStill();
                _idlePauseUntil = Time.time + Random.Range(1.2f, 2.5f);
                _choreCooldownUntil = 0f;
                _home.RequestThinkSoon();
            }
        }

        /// <summary>
        /// Dead owned chore (status/hover busy, no walk, no coroutine) — unblock within seconds,
        /// not after MaxBusySeconds, and not only when night atmosphere allows AbortBusy.
        /// </summary>
        private void RecoverStalledWork()
        {
            // Visual tab left open / never released — don't starve Trabalhadora forever.
            if (_lookPresentActive &&
                _lookPresentStartedAt > 0f &&
                Time.time - _lookPresentStartedAt > LookPresentMaxSeconds)
            {
                ReleaseLookPresent();
            }

            if (_sitting || _sleeping || _sleepAttached)
            {
                return;
            }

            if (_chore == Chore.Idle ||
                _chore == Chore.Sit ||
                _chore == Chore.SitFire ||
                _chore == Chore.Sleep ||
                _chore == Chore.Nap)
            {
                // Idle stroll that never arrives — clear so work can pick.
                if (_chore == Chore.Idle && _hasTarget &&
                    _lastProgressAt > 0f && Time.time - _lastProgressAt > 40f)
                {
                    _hasTarget = false;
                    StopLocomotion();
                    CancelBeat();
                    _idlePauseUntil = 0f;
                }

                return;
            }

            // Gather flag without a live routine = zombie session.
            if (_gatherSessionActive && _workRoutine == null)
            {
                _gatherSessionActive = false;
            }

            var noDriver = _workRoutine == null && !_gatherSessionActive;
            var noMotion = !_hasTarget &&
                           (_lastProgressAt <= 0f || Time.time - _lastProgressAt > 5f);
            var ownedTooLong = _choreStartedAt > 0f && Time.time - _choreStartedAt > 10f;

            // Pathing forever without progress.
            var pathStuck = _hasTarget &&
                            _lastProgressAt > 0f &&
                            Time.time - _lastProgressAt > 22f;

            // Hung coroutine at a prop (no path, no progress) — don't wait MaxBusySeconds.
            var hungRoutine = _workRoutine != null &&
                              !_hasTarget &&
                              !_gatherSessionActive &&
                              _choreStartedAt > 0f &&
                              Time.time - _choreStartedAt > 22f &&
                              (_lastProgressAt <= 0f || Time.time - _lastProgressAt > 10f);

            // Dead claim: owned chore, nothing driving her (common after leash wipe / failed OnArrived).
            var deadClaim = noDriver && !_hasTarget &&
                            _choreStartedAt > 0f &&
                            Time.time - _choreStartedAt > 4f;

            if (deadClaim ||
                (noDriver && noMotion && ownedTooLong) ||
                pathStuck ||
                hungRoutine)
            {
                StopWorkRoutine();
                _gatherSessionActive = false;
                _hasTarget = false;
                _leashingHome = false;
                _forageTarget = null;
                _lootTarget = null;
                _fireTarget = null;
                _cookTarget = null;
                _repairTarget = null;
                _cauldronTarget = null;
                _meadTarget = null;
                _smeltTarget = null;
                _farmTarget = null;
                ForceClearRightHand();
                UnequipTool();
                StopLocomotion();
                EndOwnedChore(0.2f);
                _choreCooldownUntil = 0f;
                _actionUntil = 0f;
                _idlePauseUntil = 0f;
                CancelBeat();
                _home?.RequestThinkSoon();
            }
        }

        private void SetWalkAnim(float forwardSpeed)
        {
            var zanim = GetComponent<ZSyncAnimation>();
            var anim = GetComponentInChildren<Animator>();
            try
            {
                zanim?.SetFloat("forward_speed", forwardSpeed);
                zanim?.SetFloat("sideway_speed", 0f);
            }
            catch
            {
            }

            if (anim != null)
            {
                try
                {
                    anim.SetFloat("forward_speed", forwardSpeed);
                    anim.SetFloat("sideway_speed", 0f);
                    if (forwardSpeed > 0.15f)
                    {
                        anim.SetBool("wakeup", true);
                    }
                }
                catch
                {
                }
            }
        }

        private void OnArrived()
        {
            _ai?.StopMoving();

            if (_chore == Chore.Repair && _repairTarget != null)
            {
                var wear = _repairTarget;
                _repairTarget = null;
                _hasTarget = false;
                StartWork(RepairRoutine(wear));
                return;
            }

            if (_chore == Chore.Collect)
            {
                // Gather session owns walk+pickup — don't spawn a nested one-shot routine.
                if (_gatherSessionActive)
                {
                    _hasTarget = false;
                    StopLocomotion();
                    return;
                }

                if (!_carryingLoot && _lootTarget != null)
                {
                    try
                    {
                        if (_lootTarget.gameObject == null)
                        {
                            _lootTarget = null;
                        }
                    }
                    catch
                    {
                        _lootTarget = null;
                    }
                }

                if (!_carryingLoot && _lootTarget != null)
                {
                    var drop = _lootTarget;
                    _lootTarget = null;
                    _hasTarget = false;
                    StartWork(PickupLootRoutine(drop));
                    return;
                }

                if (_carryingLoot)
                {
                    DepositLootAtChest();
                    return;
                }

                EndOwnedChore(1f);
                return;
            }

            if (_chore == Chore.Sleep)
            {
                _hasTarget = false;
                if (_home != null && _home.HasAssignedBed)
                {
                    var pos = _home.GetSleepPosition(out var rot);
                    EnterSleepVisual(pos, rot);
                }
                else
                {
                    // No bed — at least stop and look asleep near totem.
                    SetSleepAnim(true);
                    _sleeping = true;
                }

                return;
            }

            if (_chore == Chore.Sit && _sitTarget != null)
            {
                EnterSitVisual(_sitTarget);
                return;
            }

            if (_chore == Chore.Nap)
            {
                _hasTarget = false;
                if (_home != null && _home.HasAssignedBed)
                {
                    var pos = _home.GetSleepPosition(out var rot);
                    EnterSleepVisual(pos, rot);
                    _idleActionUntil = Time.time + Random.Range(18f, 35f);
                    _chore = Chore.Nap;
                }

                return;
            }

            if (_chore == Chore.Fire && _fireTarget != null)
            {
                StartWork(TendFireRoutine(_fireTarget));
                _fireTarget = null;
                return;
            }

            if (_chore == Chore.Cook && _cookTarget != null)
            {
                StartWork(CookRoutine(_cookTarget));
                _cookTarget = null;
                return;
            }

            if (_chore == Chore.Cauldron && _cauldronTarget != null)
            {
                StartWork(CauldronRoutine(_cauldronTarget, _cauldronRecipe));
                _cauldronTarget = null;
                _cauldronRecipe = null;
                return;
            }

            if (_chore == Chore.Mead && _meadTarget != null)
            {
                StartWork(MeadRoutine(_meadTarget));
                _meadTarget = null;
                return;
            }

            if (_chore == Chore.Smelt && _smeltTarget != null)
            {
                StartWork(SmeltRoutine(_smeltTarget));
                _smeltTarget = null;
                return;
            }

            if (_chore == Chore.Farm && _farmTarget != null)
            {
                StartWork(FarmRoutine(_farmTarget));
                _farmTarget = null;
                return;
            }

            if (_chore == Chore.Forage && _forageTarget != null)
            {
                if (_gatherSessionActive)
                {
                    _hasTarget = false;
                    StopLocomotion();
                    return;
                }

                StartWork(ForageRoutine(_forageTarget));
                _forageTarget = null;
                return;
            }

            if (_chore == Chore.SitFire)
            {
                EnterSitByFire();
                return;
            }

            // Lost target mid-chore (e.g. piece despawned) — never leave hammer stuck.
            ForceClearRightHand();
            _chore = Chore.Idle;
            _hasTarget = false;
            _actionUntil = Time.time + 1f;
        }

        private IEnumerator RepairRoutine(WearNTear wear)
        {
            BeginOwnedChore(Chore.Repair);
            EquipTool("Hammer");
            WifeEmotes.Stop(gameObject);

            if (wear == null || wear.m_nview == null || !wear.m_nview.IsValid())
            {
                ForceClearRightHand();
                EndOwnedChore(1f);
                yield break;
            }

            EnterStationaryWorkHold(wear.transform.position, 8f);
            EnsureToolVisual("Hammer");

            // Three visible swings with SFX/hit, then actually repair (Fires-style).
            for (var i = 0; i < 3 && _chore == Chore.Repair; i++)
            {
                HoldStationaryWorkTick(2f);
                FacePoint(wear.transform.position);
                EnsureToolVisual("Hammer");
                PlayHammerSwing(wear);
                yield return new WaitForSeconds(0.45f);
                PlayRepairHit(wear);
                yield return new WaitForSeconds(0.55f);
            }

            if (_chore == Chore.Repair && wear != null)
            {
                TryRepairPiece(wear);
                PlayRepairHit(wear);
            }

            ForceClearRightHand();
            EndOwnedChore(1.5f);
            _repairTarget = null;
        }

        /// <summary>Swing + hammer SFX. Tool anim only — not combat StartAttack (nude / lose hammer).</summary>
        private void PlayHammerSwing(WearNTear wear)
        {
            var aim = wear != null ? wear.transform.position : transform.position + transform.forward;
            FacePoint(aim);
            EnsureToolVisual("Hammer");

            // Player/Fires: ZSyncAnimation trigger from the tool's attackAnimation (Hammer → swing_pickaxe).
            var swing = "swing_pickaxe";
            try
            {
                var data = GetItemData("Hammer");
                var animName = data?.m_shared?.m_attack?.m_attackAnimation;
                if (!string.IsNullOrEmpty(animName))
                {
                    swing = animName;
                }
            }
            catch
            {
            }

            var zanim = GetComponent<ZSyncAnimation>();
            try
            {
                zanim?.SetTrigger(swing);
            }
            catch
            {
            }

            var origin = transform.position + Vector3.up * 1.1f + transform.forward * 0.35f;
            var fx = GetItemData("Hammer");
            try
            {
                fx?.m_shared?.m_attack?.m_startEffect?.Create(origin, transform.rotation);
            }
            catch
            {
            }

            SpawnFx("sfx_build_hammer", origin);
        }

        /// <summary>Repair via WearNTear.Repair, with ZDO health fallback (no workbench).</summary>
        private static bool TryRepairPiece(WearNTear wear)
        {
            if (wear == null || wear.m_nview == null || !wear.m_nview.IsValid())
            {
                return false;
            }

            try
            {
                if (!wear.m_nview.IsOwner())
                {
                    wear.m_nview.ClaimOwnership();
                }
            }
            catch
            {
            }

            try
            {
                if (wear.Repair())
                {
                    return true;
                }
            }
            catch
            {
            }

            // Fallback: write full health to ZDO (Fires Companions pattern).
            try
            {
                var max = wear.m_health;
                if (max <= 0f)
                {
                    return false;
                }

                var zdo = wear.m_nview.GetZDO();
                var cur = zdo.GetFloat(ZDOVars.s_health, max);
                if (cur >= max * 0.99f)
                {
                    return false;
                }

                zdo.Set(ZDOVars.s_health, max);
                wear.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", max);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void PlayRepairHit(WearNTear wear)
        {
            if (wear == null)
            {
                return;
            }

            var hit = wear.transform.position + Vector3.up * 0.5f;
            try
            {
                wear.m_hitEffect?.Create(hit, Quaternion.identity, wear.transform, 1f);
            }
            catch
            {
                try
                {
                    wear.m_hitEffect?.Create(hit, Quaternion.identity);
                }
                catch
                {
                }
            }

            var data = GetItemData("Hammer");
            try
            {
                data?.m_shared?.m_attack?.m_hitTerrainEffect?.Create(hit, Quaternion.identity);
            }
            catch
            {
            }

            try
            {
                data?.m_shared?.m_attack?.m_hitEffect?.Create(hit, Quaternion.identity);
            }
            catch
            {
            }

            SpawnFx("sfx_build_hammer", hit);
            SpawnFx("vfx_Place_wood_pole", hit);
        }

        private static void SpawnFx(string prefabName, Vector3 pos)
        {
            try
            {
                if (ZNetScene.instance == null)
                {
                    return;
                }

                var prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab == null)
                {
                    return;
                }

                Object.Instantiate(prefab, pos, Quaternion.identity);
            }
            catch
            {
            }
        }

        private IEnumerator PickupLootRoutine(ItemDrop drop)
        {
            BeginOwnedChore(Chore.Collect);
            WifeEmotes.Stop(gameObject);

            if (_home == null || drop == null)
            {
                FinishPickupLoot(1f);
                yield break;
            }

            try
            {
                if (drop.gameObject == null || drop.m_itemData == null)
                {
                    FinishPickupLoot(1f);
                    yield break;
                }
            }
            catch
            {
                FinishPickupLoot(1f);
                yield break;
            }

            var pos = drop.transform.position;
            // Session lote: short bend only — long hold made her "think" between every item.
            EnterStationaryWorkHold(pos, _gatherSessionActive ? 1.1f : 3.2f);
            FacePoint(pos);
            PlayPickupBend();
            Notify("$hearthwife_busy_collect");

            // Bend first, then magnet the drop into her (player auto-pickup feel).
            yield return new WaitForSeconds(_gatherSessionActive ? 0.22f : 0.4f);

            try
            {
                drop.Load();
            }
            catch
            {
            }

            if (drop == null || drop.m_itemData == null)
            {
                FinishPickupLoot(1f);
                yield break;
            }

            // Chest must accept BEFORE we destroy — but magnet first so it doesn't pop away.
            if (!(_home.Storage?.GetInventory()?.CanAddItem(drop.m_itemData) ?? false))
            {
                Notify("$hearthwife_chest_full");
                FinishPickupLoot(2f);
                yield break;
            }

            yield return MagnetDropToSelf(drop, _gatherSessionActive ? 0.28f : 0.45f);

            if (drop == null || drop.m_itemData == null)
            {
                FinishPickupLoot(1f);
                yield break;
            }

            if (!TryAddItemToTotemChest(drop.m_itemData, drop.gameObject != null ? drop.gameObject.name : null))
            {
                Notify("$hearthwife_chest_full");
                FinishPickupLoot(2f);
                yield break;
            }

            PlayPickupEffects();
            DestroyWorldDrop(drop);

            Notify("$hearthwife_busy_deposit");
            ForceClearRightHand();
            FinishPickupLoot(1.2f);
        }

        /// <summary>Inside a gather session the session owns EndOwnedChore — don't abort mid-lote.</summary>
        private void FinishPickupLoot(float idlePause)
        {
            if (_gatherSessionActive)
            {
                return;
            }

            EndOwnedChore(idlePause);
        }

        /// <summary>Bend / reach — same interact trigger the player uses at stations.</summary>
        private void PlayPickupBend()
        {
            var zanim = GetComponent<ZSyncAnimation>();
            zanim?.SetTrigger("interact");

            var anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetTrigger("interact");
            }
        }

        /// <summary>
        /// Player auto-pickup look: item slides into her hands, then pops with pickup SFX/VFX.
        /// </summary>
        private IEnumerator MagnetDropToSelf(ItemDrop drop, float seconds)
        {
            if (drop == null)
            {
                yield break;
            }

            // Soften physics so MoveTowards isn't fighting the Rigidbody.
            Rigidbody rb = null;
            try
            {
                rb = drop.GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
            }
            catch
            {
            }

            Transform floatDummy = null;
            try
            {
                var floating = drop.GetComponentInChildren<Floating>();
                if (floating != null)
                {
                    floatDummy = floating.transform;
                }
            }
            catch
            {
            }

            var end = Time.time + Mathf.Max(0.15f, seconds);
            while (Time.time < end)
            {
                try
                {
                    if (drop == null || drop.gameObject == null)
                    {
                        yield break;
                    }
                }
                catch
                {
                    yield break;
                }

                var hand = transform.position + Vector3.up * 1.05f + transform.forward * 0.35f;
                var next = Vector3.MoveTowards(drop.transform.position, hand, 14f * Time.deltaTime);
                drop.transform.position = next;
                if (floatDummy != null)
                {
                    floatDummy.position = next;
                }

                if (Vector3.Distance(next, hand) < 0.25f)
                {
                    break;
                }

                yield return null;
            }
        }

        /// <summary>Use the local player's pickup EffectList — wife Humanoid often has none.</summary>
        private void PlayPickupEffects()
        {
            try
            {
                EffectList fx = null;
                if (Player.m_localPlayer != null)
                {
                    fx = Player.m_localPlayer.m_pickupEffects;
                }

                if (fx == null || fx.m_effectPrefabs == null || fx.m_effectPrefabs.Length == 0)
                {
                    if (_humanoid == null)
                    {
                        _humanoid = GetComponent<Humanoid>();
                    }

                    fx = _humanoid?.m_pickupEffects;
                }

                var hasFx = false;
                try
                {
                    hasFx = fx != null && fx.m_effectPrefabs != null && fx.m_effectPrefabs.Length > 0;
                }
                catch
                {
                    hasFx = fx != null;
                }

                var zdoid = ZDOID.None;
                try
                {
                    if (_character == null)
                    {
                        _character = GetComponent<Character>();
                    }

                    if (_character != null)
                    {
                        zdoid = _character.GetZDOID();
                    }
                    else if (_nview != null && _nview.IsValid())
                    {
                        zdoid = _nview.GetZDO().m_uid;
                    }
                }
                catch
                {
                }

                if (hasFx)
                {
                    fx.Create(transform.position, Quaternion.identity, null, 1f, -1, zdoid);
                }
                else
                {
                    SpawnFx("sfx_inventory", transform.position + Vector3.up * 1f);
                }
            }
            catch
            {
                SpawnFx("sfx_inventory", transform.position + Vector3.up * 1f);
            }
        }

        private static void DestroyWorldDrop(ItemDrop drop)
        {
            if (drop == null)
            {
                return;
            }

            try
            {
                if (ZNetScene.instance != null && drop.gameObject != null)
                {
                    ZNetScene.instance.Destroy(drop.gameObject);
                }
                else if (drop.m_nview != null && drop.m_nview.IsValid())
                {
                    drop.m_nview.Destroy();
                }
                else if (drop.gameObject != null)
                {
                    Object.Destroy(drop.gameObject);
                }
            }
            catch
            {
                try
                {
                    if (drop.gameObject != null)
                    {
                        Object.Destroy(drop.gameObject);
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>Player-like ground pickup: face + interact bend + pickup SFX/VFX.</summary>
        private void PlayPickupFeedback(Vector3 facePoint)
        {
            FacePoint(facePoint);
            PlayPickupBend();
            PlayPickupEffects();
        }

        /// <summary>
        /// Add into idol Container via prefab name (ZDO-safe). Never destroy the world drop
        /// until this returns true.
        /// </summary>
        private bool TryAddItemToTotemChest(ItemDrop.ItemData data, string fallbackPrefabName = null)
        {
            if (_home == null || data?.m_shared == null || data.m_stack < 1)
            {
                return false;
            }

            var container = _home.Storage;
            if (container == null)
            {
                return false;
            }

            try
            {
                var nv = container.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid() && !nv.IsOwner())
                {
                    nv.ClaimOwnership();
                }
            }
            catch
            {
            }

            var inv = container.GetInventory();
            if (inv == null)
            {
                return false;
            }

            if (!inv.CanAddItem(data))
            {
                return false;
            }

            var prefabName = data.m_dropPrefab != null ? data.m_dropPrefab.name : null;
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = fallbackPrefabName;
            }

            if (!string.IsNullOrEmpty(prefabName))
            {
                // Strip Unity "(Clone)" if present.
                var cloneTag = "(Clone)";
                if (prefabName.EndsWith(cloneTag, System.StringComparison.Ordinal))
                {
                    prefabName = prefabName.Substring(0, prefabName.Length - cloneTag.Length);
                }

                // Current Valheim: AddItem(name, stack, quality, variant, crafterID, crafterName, bool, bool)
                var added = inv.AddItem(
                    prefabName,
                    data.m_stack,
                    data.m_quality,
                    data.m_variant,
                    data.m_crafterID,
                    data.m_crafterName ?? "",
                    false,
                    false);
                if (added != null)
                {
                    return true;
                }
            }

            // Ensure drop prefab so Container ZDO Save keeps the item.
            var clone = data.Clone();
            if (clone.m_dropPrefab == null && !string.IsNullOrEmpty(prefabName) && ObjectDB.instance != null)
            {
                clone.m_dropPrefab = ObjectDB.instance.GetItemPrefab(prefabName);
            }

            return inv.AddItem(clone);
        }

        private void PickupLoot()
        {
            // Legacy sync entry — route through routine.
            if (_lootTarget == null)
            {
                EndOwnedChore(1f);
                return;
            }

            var drop = _lootTarget;
            _lootTarget = null;
            StartWork(PickupLootRoutine(drop));
        }

        private void DepositLootAtChest()
        {
            if (_home == null || _carriedItem == null)
            {
                ClearCarry();
                EndOwnedChore(1f);
                return;
            }

            FacePoint(_home.HomePosition);
            PlayInteractAnimation(_home.HomePosition);

            if (TryAddItemToTotemChest(_carriedItem))
            {
                Notify("$hearthwife_busy_deposit");
                ClearCarry();
                EndOwnedChore(1.5f);
                FaceHome();
                return;
            }

            // Chest full — drop at feet; collect stays off until totem has a free slot.
            DropCarriedAtFeet();
            ClearCarry();
            Notify("$hearthwife_chest_full");
            EndOwnedChore(3f);
            FaceHome();
        }

        private static Vector3 GetChestDepositStand(WifeHome home)
        {
            if (home == null)
            {
                return Vector3.zero;
            }

            var p = home.HomePosition + home.transform.forward * 1.6f;
            p.y = home.HomePosition.y;
            SnapToPieceOrTerrain(ref p, home.HomePosition.y);
            return p;
        }

        private void DropCarriedAtFeet()
        {
            if (_carriedItem == null)
            {
                return;
            }

            try
            {
                var pos = transform.position + transform.forward * 0.4f + Vector3.up * 0.2f;
                ItemDrop.DropItem(_carriedItem, _carriedItem.m_stack, pos, transform.rotation);
            }
            catch
            {
                try
                {
                    var prefab = _carriedItem.m_dropPrefab;
                    if (prefab != null)
                    {
                        var go = Object.Instantiate(
                            prefab,
                            transform.position + Vector3.up * 0.3f,
                            Quaternion.identity);
                        var drop = go.GetComponent<ItemDrop>();
                        if (drop != null)
                        {
                            drop.m_itemData = _carriedItem.Clone();
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private void ClearCarry()
        {
            _carryingLoot = false;
            _carriedItem = null;
            _lootTarget = null;
            UnequipTool();
            InvalidateLooksCache();
            ApplyAppearanceFromHome();
        }

        private void EquipTool(string itemName)
        {
            if (_vis == null)
            {
                _vis = GetComponent<VisEquipment>();
            }

            if (_humanoid == null)
            {
                _humanoid = GetComponent<Humanoid>();
            }

            // Do NOT Humanoid.EquipItem on the wife clone: her inventory has no dress/armor,
            // so EquipItem rebuilds VisEquipment as nude + tool. Visual-only right hand + reassert clothes.
            _borrowedHammer = false;
            try
            {
                var inv = _humanoid?.GetInventory();
                if (inv != null)
                {
                    ClearTempTools(inv);

                    if (itemName == "Hammer")
                    {
                        var fromChest = TryTakeToolFromHomeChest("Hammer");
                        if (fromChest != null)
                        {
                            _borrowedHammer = true;
                            if (!inv.AddItem(fromChest))
                            {
                                // Chest already lost the stack — put it back if bag full.
                                var chest = _home?.Storage?.GetInventory();
                                chest?.AddItem(fromChest);
                                _borrowedHammer = false;
                            }
                        }
                        else
                        {
                            var template = GetItemData("Hammer");
                            if (template != null)
                            {
                                var ghost = template.Clone();
                                ghost.m_stack = 1;
                                inv.AddItem(ghost);
                            }
                        }
                    }
                    else
                    {
                        var template = GetItemData(itemName);
                        if (template != null)
                        {
                            var ghost = template.Clone();
                            ghost.m_stack = 1;
                            inv.AddItem(ghost);
                        }
                    }
                }
            }
            catch
            {
            }

            EnsureToolVisual(itemName);
        }

        /// <summary>Dress + tool in right hand without Humanoid.EquipItem nude wipe.</summary>
        private void EnsureToolVisual(string itemName)
        {
            if (_vis == null)
            {
                _vis = GetComponent<VisEquipment>();
            }

            if (_vis == null)
            {
                return;
            }

            if (_home != null)
            {
                WifeLooks.ReassertClothes(
                    _vis,
                    _home.DressIndex,
                    _home.HairIndex,
                    _home.HairColorIndex,
                    _home.SkinIndex);
                _appliedDress = _home.DressIndex;
                _appliedHair = _home.HairIndex;
                _appliedHairColor = _home.HairColorIndex;
                _appliedSkin = _home.SkinIndex;
            }

            var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
            var hash = prefab != null ? prefab.name.GetStableHashCode() : itemName.GetStableHashCode();
            if (hash != 0)
            {
                _vis.SetRightItem(hash, 1);
                _vis.UpdateEquipmentVisuals();
            }
        }

        private static void ClearTempTools(Inventory inv)
        {
            if (inv == null)
            {
                return;
            }

            var remove = new List<ItemDrop.ItemData>();
            foreach (var existing in inv.GetAllItems())
            {
                var n = existing?.m_dropPrefab != null
                    ? existing.m_dropPrefab.name
                    : null;
                if (n == "Hammer" || n == "FishingRod" || n == "Torch" || n == "KnifeCopper")
                {
                    remove.Add(existing);
                }
            }

            foreach (var item in remove)
            {
                inv.RemoveItem(item);
            }
        }

        private ItemDrop.ItemData TryTakeToolFromHomeChest(string itemName)
        {
            var chest = _home?.Storage?.GetInventory();
            if (chest == null)
            {
                return null;
            }

            foreach (var existing in chest.GetAllItems())
            {
                if (existing?.m_dropPrefab == null || existing.m_stack < 1)
                {
                    continue;
                }

                if (!existing.m_dropPrefab.name.Equals(itemName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var clone = existing.Clone();
                clone.m_stack = 1;
                chest.RemoveItem(existing, 1);
                return clone;
            }

            return null;
        }

        private void UnequipTool()
        {
            // Return borrowed hammer to idol chest when possible.
            try
            {
                if (_borrowedHammer && _humanoid != null)
                {
                    var inv = _humanoid.GetInventory();
                    ItemDrop.ItemData hammer = null;
                    if (inv != null)
                    {
                        foreach (var it in inv.GetAllItems())
                        {
                            if (it?.m_dropPrefab != null &&
                                it.m_dropPrefab.name.Equals("Hammer", System.StringComparison.OrdinalIgnoreCase))
                            {
                                hammer = it;
                                break;
                            }
                        }
                    }

                    if (hammer != null)
                    {
                        var chest = _home?.Storage?.GetInventory();
                        if (chest != null)
                        {
                            var back = hammer.Clone();
                            back.m_stack = 1;
                            chest.AddItem(back);
                        }
                    }
                }
            }
            catch
            {
            }

            _borrowedHammer = false;
            ForceClearRightHand();
            InvalidateLooksCache();
            ApplyAppearanceFromHome();
        }

        private void ForceClearRightHand()
        {
            try
            {
                if (_humanoid != null)
                {
                    var right = _humanoid.GetRightItem();
                    if (right != null)
                    {
                        _humanoid.UnequipItem(right, false);
                    }

                    ClearTempTools(_humanoid.GetInventory());
                }
            }
            catch
            {
            }

            if (_vis == null)
            {
                _vis = GetComponent<VisEquipment>();
            }

            if (_vis == null)
            {
                return;
            }

            _vis.SetRightItem(0, 1);
            // Do NOT UpdateEquipmentVisuals here — it briefly rebuilds body/dress and flickers nude.
            // Looks stay on VisEquipment; next ApplyAppearanceFromHome refreshes if needed.
        }

        /// <summary>
        /// Vanilla/Fires station use: face target + ZSyncAnimation "interact"
        /// (Fireplace/CookingStation/Pickable path — not tool attack).
        /// </summary>
        private void PlayInteractAnimation(Vector3 facePoint)
        {
            FacePoint(facePoint);

            var zanim = GetComponent<ZSyncAnimation>();
            zanim?.SetTrigger("interact");

            var anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetTrigger("interact");
            }
        }

        /// <summary>Player-like swing: attack trigger + StartAttack + item start/hit effects.</summary>
        private void PlayToolSwing(string itemName, Vector3 aimPoint)
        {
            var zanim = GetComponent<ZSyncAnimation>();
            zanim?.SetTrigger("attack");

            var anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetTrigger("attack");
            }

            try
            {
                _humanoid?.StartAttack(null, false);
            }
            catch
            {
                // Some Humanoid builds reject null target — anim/effects still run.
            }

            var data = GetItemData(itemName);
            var origin = transform.position + Vector3.up * 1.1f + transform.forward * 0.4f;
            try
            {
                data?.m_shared?.m_attack?.m_startEffect?.Create(origin, transform.rotation);
            }
            catch
            {
            }

            // Face the piece while swinging.
            var look = aimPoint - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(look.normalized);
            }
        }

        private void PlayToolHit(string itemName, Vector3 hitPoint)
        {
            var data = GetItemData(itemName);
            try
            {
                data?.m_shared?.m_attack?.m_hitTerrainEffect?.Create(hitPoint, Quaternion.identity);
            }
            catch
            {
            }

            try
            {
                data?.m_shared?.m_attack?.m_hitEffect?.Create(hitPoint, Quaternion.identity);
            }
            catch
            {
            }

            try
            {
                data?.m_shared?.m_hitEffect?.Create(hitPoint, Quaternion.identity);
            }
            catch
            {
            }
        }

        private static ItemDrop.ItemData GetItemData(string itemName)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(itemName);
            return prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData : null;
        }

        private bool IsStuck()
        {
            if (!_hasTarget)
            {
                return false;
            }

            var moved = HorizontalDistance(transform.position, _stuckCheckPos);
            if (moved > StuckMoveEpsilon)
            {
                _stuckCheckPos = transform.position;
                _lastProgressAt = Time.time;
                return false;
            }

            if (_lastProgressAt <= 0f)
            {
                _stuckCheckPos = transform.position;
                _lastProgressAt = Time.time;
                return false;
            }

            return Time.time - _lastProgressAt >= StuckSeconds;
        }

        private void ResetStuck()
        {
            _lastProgressAt = Time.time;
            _stuckCheckPos = transform.position;
        }

        private void WarpToTarget()
        {
            var pos = _target;
            var fromY = transform.position.y;
            var preferY = _home != null ? _home.HomePosition.y : fromY;
            if (_chore != Chore.Sleep)
            {
                SnapToPieceOrTerrain(ref pos, preferY);
                // Never warp onto a roof / loft above current or idol floor.
                if (pos.y > preferY + 1.4f || pos.y > fromY + 1.6f)
                {
                    pos.y = Mathf.Min(fromY, preferY) + 0.1f;
                    SnapToPieceOrTerrain(ref pos, preferY);
                }

                pos += Vector3.up * 0.05f;
            }

            if (_nview != null && _nview.IsValid())
            {
                if (!_nview.IsOwner())
                {
                    _nview.ClaimOwnership();
                }

                _nview.GetZDO().SetPosition(pos);
            }

            transform.position = pos;
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
            }

            _ai?.StopMoving();
            ResetStuck();
            if (_chore != Chore.Idle)
            {
                OnArrived();
            }
        }

        private Vector3 ClampToHome(Vector3 world)
        {
            var center = _home.HomePosition;
            var flat = world - center;
            flat.y = 0f;
            var max = _home.Radius * 0.85f;
            if (flat.magnitude > max)
            {
                flat = flat.normalized * max;
            }

            var p = center + flat;
            SnapToFloor(ref p);
            return p;
        }

        private static void SnapToFloor(ref Vector3 pos)
        {
            SnapToPieceOrTerrain(ref pos, pos.y);
        }

        /// <summary>
        /// Snap to walkable floor — never roofs / ceiling boards.
        /// No ZoneSystem.GetGroundHeight — that snaps to dirt under raised wood floors.
        /// </summary>
        internal static void SnapToPieceOrTerrain(ref Vector3 pos, float preferNearY)
        {
            // Start above expected floor, not high enough to "prefer" a roof hit by accident.
            var origin = new Vector3(pos.x, preferNearY + 3.5f, pos.z);
            var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            var hits = Physics.RaycastAll(origin, Vector3.down, 14f, mask, QueryTriggerInteraction.Ignore);

            var bestY = float.NaN;
            var bestScore = float.MaxValue;

            // Allow a little up (stairs/ramp) but hard-cap so roofs lose.
            var maxY = preferNearY + 1.15f;
            var minY = preferNearY - 5f;

            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger)
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<Character>() != null ||
                    hit.collider.GetComponentInParent<ItemDrop>() != null ||
                    hit.collider.GetComponentInParent<WifeAgent>() != null ||
                    hit.collider.GetComponentInParent<WifeHome>() != null)
                {
                    continue;
                }

                var y = hit.point.y;
                if (y > maxY || y < minY)
                {
                    continue;
                }

                if (hit.normal.y < 0.55f)
                {
                    continue;
                }

                var goName = hit.collider.gameObject != null ? hit.collider.gameObject.name : "";
                var piece = hit.collider.GetComponentInParent<Piece>();
                var pieceName = piece != null ? (piece.m_name ?? piece.name) : "";
                if (IsUnwalkablePieceName(goName) || IsUnwalkablePieceName(pieceName))
                {
                    var n = (goName + " " + pieceName).ToLowerInvariant();
                    if (!(n.Contains("stair") || n.Contains("ladder") || n.Contains("ramp") ||
                          n.Contains("step")))
                    {
                        continue;
                    }
                }

                // Prefer surfaces closest to the expected floor height; slight bias to lower = floor not loft.
                var score = Mathf.Abs(y - preferNearY) + (y - preferNearY > 0.35f ? 0.8f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestY = y;
                }
            }

            if (!float.IsNaN(bestY))
            {
                pos.y = bestY + 0.08f;
                return;
            }

            // Piece raycast miss: keep prefer height (raised floors).
            pos.y = preferNearY + 0.1f;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private IEnumerator Boot(WifeHome home)
        {
            yield return null;
            CacheComponents();

            if (_nview != null && _nview.IsValid() && !_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            try
            {
                _character?.SetTamed(true);
            }
            catch
            {
            }

            _home = home;
            InvalidateLooksCache();
            ApplyAppearanceFromHome();
            yield return null;
            InvalidateLooksCache();
            ApplyAppearanceFromHome();
            yield return new WaitForSeconds(0.25f);
            ApplyAppearanceFromHome();
            _cc = GetComponent<CharacterController>();
            if (_cc != null)
            {
                _ccWasEnabled = _cc.enabled;
                _cc.enabled = true;
                // Player-like: stairs OK, furniture/roof climb blocked (VillageNPCs leave defaults;
                // we only lower step so she cannot mount tables).
                _cc.stepOffset = 0.35f;
                _cc.slopeLimit = 45f;
                _cc.skinWidth = 0.08f;
            }

            // Character.UpdateWalking drives via Rigidbody.AddForce / linearVelocity.
            // Kinematic=true → LookTowards still rotates (só vira) but she never translates.
            // WifeNpcPrefab: leave Player RB alone; Fires also expects non-kinematic to walk.
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.None;
            }

            // VillageNPCs: Humanoid path + walk while turning (default m_moveMinAngle=10
            // with smoothMovement ≈ spin in place forever).
            if (_ai != null)
            {
                try
                {
                    ((BaseAI)_ai).m_pathAgentType = Pathfinding.AgentType.Humanoid;
                    ((BaseAI)_ai).m_smoothMovement = true;
                    ((BaseAI)_ai).m_moveMinAngle = 90f;
                    ((BaseAI)_ai).m_avoidWater = true;
                }
                catch
                {
                }
            }

            MutePassiveAi();
            ApplyHomesteadResistances();
            ClearHostileStatusEffects();

            _booted = true;
            _chore = Chore.Idle;
            _hasTarget = false;
            // Brief settle, then wake into a stroll (not a long parked linger).
            _idlePauseUntil = 0f;
            _actionUntil = 0f;
            _choreCooldownUntil = 0f;
            _wakeWithStroll = true;
            FaceHome();
            WifeEmotes.NormalizeAnimatorSpeed(gameObject);
            RefreshRandomAnimationGate();
            RestorePlayerCollision();
            home?.RequestThinkSoon();
        }

        /// <summary>Homestead NPC — ignore poison/fire DoT smoke that monsters apply.</summary>
        private void ApplyHomesteadResistances()
        {
            if (_character == null)
            {
                return;
            }

            try
            {
                var mods = _character.m_damageModifiers;
                mods.m_poison = HitData.DamageModifier.Immune;
                mods.m_fire = HitData.DamageModifier.Immune;
                mods.m_frost = HitData.DamageModifier.Immune;
                mods.m_lightning = HitData.DamageModifier.Resistant;
                mods.m_spirit = HitData.DamageModifier.Immune;
                _character.m_damageModifiers = mods;
            }
            catch
            {
            }
        }

        private void TickStatusHygiene()
        {
            if (!_booted || Time.time < _statusHygieneAt)
            {
                return;
            }

            _statusHygieneAt = Time.time + 2.5f;
            ClearHostileStatusEffects();
        }

        private void ClearHostileStatusEffects()
        {
            if (_character == null)
            {
                return;
            }

            try
            {
                var se = _character.GetSEMan();
                if (se == null)
                {
                    return;
                }

                // Quiet remove — kills green poison / burn / frost VFX without HUD spam.
                se.RemoveStatusEffect(SEMan.s_statusEffectPoison, true);
                se.RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
                se.RemoveStatusEffect(SEMan.s_statusEffectFrost, true);
                se.RemoveStatusEffect(SEMan.s_statusEffectSpirit, true);
                se.RemoveStatusEffect(SEMan.s_statusEffectLightning, true);
            }
            catch
            {
                try
                {
                    var se = _character.GetSEMan();
                    se?.RemoveStatusEffect("Poison".GetStableHashCode(), true);
                    se?.RemoveStatusEffect("Burning".GetStableHashCode(), true);
                }
                catch
                {
                }
            }
        }

        private void MutePassiveAi()
        {
            if (_ai == null)
            {
                return;
            }

            try
            {
                // Keep MonsterAI.enabled = true (Character.UpdateWalking needs m_baseAI).
                // Brain (IdleMovement/StopMoving) is suppressed by WifePatches MonsterAI.UpdateAI.
                _ai.enabled = true;
                _ai.m_randomMoveInterval = 99999f;
                _ai.m_randomMoveRange = 0f;
                _ai.m_enableHuntPlayer = false;
                _ai.m_attackPlayerObjects = false;
                _ai.SetAlerted(false);
                _ai.SetFollowTarget(null);
                try
                {
                    _ai.ResetRandomMovement();
                }
                catch
                {
                }

                if (_ai is MonsterAI mai)
                {
                    mai.SetTarget(null);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Always re-enable solid contact with the local player.
        /// Permanent IgnoreCollision (old builds) let the player clip through her.
        /// </summary>
        private void RestorePlayerCollision()
        {
            if (_character == null)
            {
                _character = GetComponent<Character>();
            }

            var mine = _character != null ? _character.m_collider : null;
            if (mine == null)
            {
                _ignoredPlayerCollider = null;
                return;
            }

            // Tracked pair from a previous IgnoreCollision call.
            if (_ignoredPlayerCollider != null)
            {
                try
                {
                    Physics.IgnoreCollision(mine, _ignoredPlayerCollider, false);
                }
                catch
                {
                }

                _ignoredPlayerCollider = null;
            }

            // Also clear against current local player — covers hot-reload / old DLL left Ignore on.
            var player = Player.m_localPlayer;
            if (player == null || player == _character)
            {
                return;
            }

            Collider theirs = null;
            try
            {
                theirs = player.m_collider;
            }
            catch
            {
            }

            if (theirs == null)
            {
                return;
            }

            try
            {
                Physics.IgnoreCollision(mine, theirs, false);
            }
            catch
            {
            }

            _playerCollisionRestored = true;
        }

        private void FaceHome()
        {
            if (_home == null)
            {
                return;
            }

            // Don't fight LookAt / player face — causes idle tremble.
            var player = Player.m_localPlayer;
            if (player != null &&
                Vector3.Distance(player.transform.position, transform.position) < 11f)
            {
                return;
            }

            var look = _home.HomePosition - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.05f)
            {
                transform.rotation = Quaternion.LookRotation(look.normalized);
            }
        }

        /// <summary>Hard idle freeze — no walk anim, no AI, no residual velocity (stops chest-front tremble).</summary>
        private void ParkStill()
        {
            StopLocomotion();
            // Do not CC.Move settle — that clips thin wood floors (Fires: SetMoveDir zero only).
        }

        internal void InvalidateLooksCache()
        {
            _appliedDress = -1;
            _appliedHair = -1;
            _appliedHairColor = -1;
            _appliedSkin = -1;
        }

        internal void ApplyAppearance(int dress, int hair, int hairColor, int skin)
        {
            if (_vis == null)
            {
                _vis = GetComponent<VisEquipment>();
            }

            if (_vis == null)
            {
                return;
            }

            dress = Mathf.Clamp(dress, 0, WifeLooks.DressCount - 1);
            hair = Mathf.Clamp(hair, 0, WifeLooks.HairCount - 1);
            hairColor = Mathf.Clamp(hairColor, 0, WifeLooks.HairColorCount - 1);
            skin = Mathf.Clamp(skin, 0, WifeLooks.SkinCount - 1);

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            if (_nview != null && _nview.IsValid())
            {
                if (!_nview.IsOwner())
                {
                    _nview.ClaimOwnership();
                }

                _vis.m_nview = _nview;
            }

            // Always write looks from idol ZDO — tools/clear can wipe VisEquipment while cache still matches.
            WifeLooks.Apply(_vis, dress, hair, hairColor, skin);
            _appliedDress = dress;
            _appliedHair = hair;
            _appliedHairColor = hairColor;
            _appliedSkin = skin;
        }

        internal void ApplyAppearance(int index) =>
            ApplyAppearance(index, index % WifeLooks.HairCount, index % WifeLooks.HairColorCount, 0);

        internal void ApplyAppearanceFromHome()
        {
            if (_home == null)
            {
                return;
            }

            ApplyAppearance(_home.DressIndex, _home.HairIndex, _home.HairColorIndex, _home.SkinIndex);
            SyncDisplayName();
        }

        /// <summary>Keep Humanoid.m_name in sync with idol ZDO display name.</summary>
        internal void SyncDisplayName()
        {
            if (_humanoid == null)
            {
                _humanoid = GetComponent<Humanoid>();
            }

            if (_humanoid != null)
            {
                _humanoid.m_name = DisplayName;
            }
        }

        internal string DisplayName =>
            _home != null ? _home.WifeDisplayName : Localization.instance.Localize("$hearthwife_wife_name");

        /// <summary>Pull her to the idol and hard-reset stuck sleep/sit/chore/anim state.</summary>
        internal void RecallToHome(WifeHome home)
        {
            _home = home;
            // Stop chore coroutines, but re-boot if Boot was killed mid-start.
            var needsBoot = !_booted;
            StopAllCoroutines();
            _workRoutine = null;
            CacheComponents();

            // Full behavior wipe — keeps identity (name + looks come back from the idol).
            HardResetBehaviorState();

            // Stand in front of the idol on the same floor plate (wood/stone).
            var stand = home.HomePosition + home.transform.forward * 2.5f;
            stand.y = home.HomePosition.y + 0.5f;
            SnapToPieceOrTerrain(ref stand, home.HomePosition.y);

            // Hard teleport — don't rely on pathfind (breaks on raised floors).
            _cc = GetComponent<CharacterController>();
            if (_cc != null)
            {
                _cc.enabled = false;
            }

            if (_nview != null && _nview.IsValid())
            {
                if (!_nview.IsOwner())
                {
                    _nview.ClaimOwnership();
                }

                _nview.GetZDO().SetPosition(stand);
                _nview.GetZDO().SetRotation(Quaternion.LookRotation(home.transform.forward));
            }

            transform.SetPositionAndRotation(stand, Quaternion.LookRotation(home.transform.forward));

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (_cc != null)
            {
                _cc.enabled = true;
            }

            _ccWasEnabled = true;
            _actionUntil = Time.time + 0.6f;
            _idlePauseUntil = 0f;
            _wakeWithStroll = true;

            // Identity only — dress/hair/skin/name from WifeHome ZDO (not runtime chore state).
            InvalidateLooksCache();
            ApplyAppearanceFromHome();
            FaceHome();

            if (needsBoot)
            {
                StartCoroutine(Boot(home));
                return;
            }

            _booted = true;
            MutePassiveAi();
            ApplyHomesteadResistances();
            ClearHostileStatusEffects();
            RestorePlayerCollision();
            FaceHome();
            home?.RequestThinkSoon();
        }

        /// <summary>
        /// Unstick wipe: chores, attach, sleep/sit, emotes, tools, beats, timers.
        /// Does not touch WifeHome identity (name / dress / hair / skin) — caller re-applies those.
        /// </summary>
        private void HardResetBehaviorState()
        {
            StopWorkRoutine();
            _workRoutine = null;
            _gatherSessionActive = false;

            if (_occupiedProp != null)
            {
                WifeOccupancy.Release(_occupiedProp, this);
                _occupiedProp = null;
            }

            var bed = _home != null ? _home.GetAssignedBed() : null;
            if (bed != null)
            {
                WifeOccupancy.Release(bed, this);
            }

            // Detach without the long sit-cooldown ExitSitting applies (recall must feel instant).
            try
            {
                StopPlayerAttach(stepAwayFromSeat: false);
            }
            catch
            {
            }

            _sleeping = false;
            _sleepAttached = false;
            _sitting = false;
            _playerAttached = false;
            _attachPoint = null;
            _attachAnimation = "";
            _attachColliders = null;
            _sitTarget = null;
            _sitFire = null;
            _lastSitChair = null;

            if (_cc == null)
            {
                _cc = GetComponent<CharacterController>();
            }

            if (_cc != null)
            {
                _cc.enabled = true;
            }

            _ccWasEnabled = true;

            ForceClearRightHand();
            UnequipTool();
            UnequipFishingRod();
            WifeEmotes.Stop(gameObject);
            ClearCarry();
            ForceResetAnimatorPose();

            _chore = Chore.Idle;
            _hasTarget = false;
            _target = transform.position;
            _repairTarget = null;
            _lootTarget = null;
            _fireTarget = null;
            _cookTarget = null;
            _wifeCookStation = null;
            _cauldronTarget = null;
            _cauldronRecipe = null;
            _meadTarget = null;
            _smeltTarget = null;
            _farmTarget = null;
            _forageTarget = null;
            _carryingLoot = false;
            _carriedItem = null;
            _borrowedHammer = false;

            CancelBeat();
            _lookPresentActive = false;
            _lookPresentStartedAt = 0f;
            _leashingHome = false;
            _leashStartedAt = 0f;
            _preferShelter = false;
            _wantsMorningGreet = false;
            _morningGreetReadyAt = 0f;
            _facePlayerUntil = 0f;
            _headLookWeight = 0f;
            _headLookTargetWeight = 0f;

            _actionUntil = 0f;
            _idlePauseUntil = 0f;
            _idleActionUntil = 0f;
            _choreCooldownUntil = 0f;
            _choreStartedAt = 0f;
            _sitCooldownUntil = 0f;
            _wanderUntil = 0f;
            _wasBusyLastThink = false;
            _hoverHint = null;
            _hoverHintUntil = 0f;
            ResetStuck();

            _ai?.StopMoving();
            SilenceAiMove();
            SetWalkAnim(0f);
            SetSleepAnim(false);
            RefreshRandomAnimationGate();

            if (_nview != null && _nview.IsValid())
            {
                try
                {
                    _nview.GetZDO().Set(ZDOVars.s_inBed, false);
                }
                catch
                {
                    try
                    {
                        _nview.GetZDO().Set("inBed", false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>Clear sleep/sit/emote animator flags so recall never leaves attach_bed stuck.</summary>
        private void ForceResetAnimatorPose()
        {
            try
            {
                var zanim = GetComponent<ZSyncAnimation>();
                zanim?.SetTrigger("emote_stop");
                zanim?.SetBool("sleeping", false);
                zanim?.SetBool("attach_chair", false);
                zanim?.SetBool("attach_bed", false);
                zanim?.SetBool("emote_sit", false);
                zanim?.SetBool("moving", false);
                zanim?.SetBool("wakeup", true);
                zanim?.SetFloat("forward_speed", 0f);
                zanim?.SetFloat("sideway_speed", 0f);

                var anim = GetComponentInChildren<Animator>();
                if (anim == null)
                {
                    return;
                }

                TrySetAnimBool(anim, "sleeping", false);
                TrySetAnimBool(anim, "attach_chair", false);
                TrySetAnimBool(anim, "attach_bed", false);
                TrySetAnimBool(anim, "emote_sit", false);
                TrySetAnimBool(anim, "moving", false);
                TrySetAnimBool(anim, "wakeup", true);
                try
                {
                    anim.SetFloat("forward_speed", 0f);
                    anim.SetFloat("sideway_speed", 0f);
                    anim.SetTrigger("emote_stop");
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private void ExitSleepVisual()
        {
            var bed = _home?.GetAssignedBed();
            if (bed != null)
            {
                WifeOccupancy.Release(bed, this);
            }

            StopPlayerAttach();

            if (_cc != null)
            {
                _cc.enabled = _ccWasEnabled;
            }

            SetSleepAnim(false);
            _sleeping = false;
            if (_chore == Chore.Sleep)
            {
                _chore = Chore.Idle;
            }

            CancelBeat();
            RefreshRandomAnimationGate();
        }

        private void EnterSleepVisual(Vector3 pos, Quaternion rot)
        {
            // Already lying — never re-AttachStart every frame (animator spam + FPS death).
            if (_sleeping && (_sleepAttached || _playerAttached))
            {
                TickPlayerAttach();
                return;
            }

            _ai?.StopMoving();
            StopLocomotion();
            UnequipTool();
            WifeEmotes.Stop(gameObject);

            var bed = _home?.GetAssignedBed();
            if (IsPlayerOccupyingBed(bed))
            {
                _chore = Chore.Idle;
                CancelBeat();
                _idlePauseUntil = Time.time + Random.Range(6f, 12f);
                ParkStill();
                return;
            }

            var attach = GetBedAttachPoint(bed);
            if (attach != null)
            {
                pos = attach.position;
                rot = attach.rotation;
            }

            if (bed != null)
            {
                WifeOccupancy.TryOccupy(bed, this, 600f);
            }

            _cc = GetComponent<CharacterController>();
            if (_cc != null)
            {
                _ccWasEnabled = _cc.enabled;
                _cc.enabled = false;
            }

            if (_character != null && attach != null)
            {
                _sleepAttached = TryAttachStart(attach);
            }
            else
            {
                _sleepAttached = false;
            }

            // Always pin to bed pose — attach may fail on some bed prefabs.
            if (!_sleepAttached)
            {
                transform.SetPositionAndRotation(pos, rot);
                if (_nview != null && _nview.IsValid())
                {
                    _nview.GetZDO().SetPosition(pos);
                    _nview.GetZDO().SetRotation(rot);
                }
            }

            SetSleepAnim(true);
            _sleeping = true;
            _hasTarget = false;
            RefreshRandomAnimationGate();
        }

        /// <summary>
        /// Walk target beside the bed (navmesh-friendly). Lie-down uses GetSleepPosition/attach.
        /// </summary>
        private Vector3 GetBedWalkTarget(WifeHome home, out Quaternion lieRot)
        {
            var lie = home.GetSleepPosition(out lieRot);
            var bed = home.GetAssignedBed();
            if (bed == null)
            {
                return lie;
            }

            // Stand at the foot / side of the bed — not on the mattress center.
            var approach = lie - bed.transform.forward * 0.9f;
            approach.y = lie.y;
            SnapToPieceOrTerrain(ref approach, lie.y);
            if (IsUnsafeWaterWalk(approach))
            {
                approach = lie + bed.transform.right * 0.85f;
                SnapToPieceOrTerrain(ref approach, lie.y);
            }

            if (IsUnsafeWaterWalk(approach))
            {
                return ApproachPoint(lie, 0.5f);
            }

            return approach;
        }

        private void SetSleepAnim(bool sleeping)
        {
            var zanim = GetComponent<ZSyncAnimation>();
            if (zanim != null)
            {
                try
                {
                    zanim.SetBool("sleeping", sleeping);
                }
                catch
                {
                }
            }

            var anim = GetComponentInChildren<Animator>();
            if (anim == null)
            {
                return;
            }

            // Humanoid NPC often lacks Player bed states — SetBool/CrossFade spam = Invalid Layer -1.
            TrySetAnimBool(anim, "sleeping", sleeping);
            TrySetAnimBool(anim, "attach_bed", sleeping);
            if (sleeping && AnimatorHasState(anim, "Sleep"))
            {
                try
                {
                    anim.CrossFade("Sleep", 0.15f);
                }
                catch
                {
                }
            }

            if (zanim != null)
            {
                try
                {
                    zanim.SetBool("attach_bed", sleeping);
                }
                catch
                {
                }
            }
        }

        private static void TrySetAnimBool(Animator anim, string name, bool value)
        {
            if (anim == null || string.IsNullOrEmpty(name) || !AnimatorHasParam(anim, name))
            {
                return;
            }

            try
            {
                anim.SetBool(name, value);
            }
            catch
            {
            }
        }

        private static bool AnimatorHasParam(Animator anim, string name)
        {
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                return false;
            }

            try
            {
                foreach (var p in anim.parameters)
                {
                    if (p != null && p.name == name)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool AnimatorHasState(Animator anim, string stateName)
        {
            if (anim == null)
            {
                return false;
            }

            try
            {
                return anim.HasState(0, Animator.StringToHash(stateName));
            }
            catch
            {
                return false;
            }
        }

        private static Transform GetBedAttachPoint(Bed bed)
        {
            if (bed == null)
            {
                return null;
            }

            foreach (var fieldName in new[] { "m_spawnPoint", "m_attachPoint", "spawnPoint" })
            {
                var field = typeof(Bed).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(bed) is Transform t && t != null)
                {
                    return t;
                }
            }

            foreach (var name in new[] { "spawnpoint", "SpawnPoint", "attach_bed", "AttachPoint" })
            {
                var found = FindChildNamed(bed.transform, name);
                if (found != null)
                {
                    return found;
                }
            }

            return bed.transform;
        }

        /// <summary>Player already on this bed — wife yields instead of clipping.</summary>
        internal static bool IsPlayerOccupyingBed(Bed bed)
        {
            if (bed == null)
            {
                return false;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            try
            {
                if (!player.InBed() && !player.IsSleeping())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            var attach = GetBedAttachPoint(bed);
            var bedPos = attach != null ? attach.position : bed.transform.position;
            return Vector3.Distance(player.transform.position, bedPos) < 2.4f;
        }

        /// <summary>True if any homestead wife is sleeping on this bed.</summary>
        internal static bool IsWifeSleepingOnBed(Bed bed)
        {
            if (bed == null)
            {
                return false;
            }

            foreach (var wife in Object.FindObjectsByType<WifeAgent>(FindObjectsSortMode.None))
            {
                if (wife == null || (!wife._sleeping && !wife._sleepAttached))
                {
                    continue;
                }

                var assigned = wife._home != null ? wife._home.GetAssignedBed() : null;
                if (assigned == bed)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildNamed(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Vanilla sit/sleep attach lives on Player.AttachStart (Character.AttachStart is empty).
        /// Wife is Humanoid-only (VillageNPCs strip Player) — replicate Player attach here.
        /// </summary>
        private bool StartPlayerAttach(
            Transform attachPoint,
            GameObject colliderRoot,
            bool hideWeapons,
            bool isBed,
            string attachAnimation,
            Vector3 detachOffset)
        {
            if (attachPoint == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(attachAnimation))
            {
                attachAnimation = isBed ? "attach_bed" : "attach_chair";
            }

            // Same seat already — keep TickPlayerAttach, never Stop+Start (detach thrash).
            if (_playerAttached && _attachPoint == attachPoint && _attachAnimation == attachAnimation)
            {
                TickPlayerAttach();
                return true;
            }

            if (_playerAttached)
            {
                StopPlayerAttach();
            }

            _playerAttached = true;
            _attachPoint = attachPoint;
            _attachAnimation = attachAnimation;
            _attachDetachOffset = detachOffset;
            _sleepAttached = true;

            // Clear locomotion so attach pose can win (Fires ForceAnimationStateReset subset).
            try
            {
                _character?.SetMoveDir(Vector3.zero);
                _character?.SetWalk(false);
                _character?.SetRun(false);
            }
            catch
            {
            }

            SetWalkAnim(0f);

            var zanim = GetComponent<ZSyncAnimation>();
            var anim = GetComponentInChildren<Animator>();
            try
            {
                zanim?.SetBool("wakeup", false);
                zanim?.SetBool("moving", false);
                if (!string.IsNullOrEmpty(_attachAnimation))
                {
                    zanim?.SetBool(_attachAnimation, true);
                }

                if (anim != null)
                {
                    TrySetAnimBool(anim, "wakeup", false);
                    anim.SetFloat("forward_speed", 0f);
                    anim.SetFloat("sideway_speed", 0f);
                    if (!string.IsNullOrEmpty(_attachAnimation))
                    {
                        TrySetAnimBool(anim, _attachAnimation, true);
                    }
                }
            }
            catch
            {
            }

            if (_nview != null && _nview.IsValid())
            {
                try
                {
                    _nview.GetZDO().Set(ZDOVars.s_inBed, isBed);
                }
                catch
                {
                    _nview.GetZDO().Set("inBed", isBed);
                }
            }

            _attachColliders = null;
            if (colliderRoot != null)
            {
                _attachColliders = colliderRoot.GetComponentsInChildren<Collider>();
                var capsule = _character != null ? _character.m_collider : null;
                if (capsule != null && _attachColliders != null)
                {
                    foreach (var col in _attachColliders)
                    {
                        if (col != null)
                        {
                            Physics.IgnoreCollision(capsule, col, true);
                        }
                    }
                }
            }

            if (hideWeapons && _humanoid != null)
            {
                try
                {
                    _humanoid.HideHandItems(false, true);
                }
                catch
                {
                }
            }

            RefreshRandomAnimationGate();
            TickPlayerAttach();

            try
            {
                _character?.ResetCloth();
            }
            catch
            {
            }

            return true;
        }

        /// <summary>Player.UpdateAttach — keep transform on the seat each frame.</summary>
        private void TickPlayerAttach()
        {
            if (!_playerAttached || _attachPoint == null)
            {
                return;
            }

            transform.SetPositionAndRotation(_attachPoint.position, _attachPoint.rotation);

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                var parentRb = _attachPoint.GetComponentInParent<Rigidbody>();
                rb.linearVelocity = parentRb != null
                    ? parentRb.GetPointVelocity(transform.position)
                    : Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Re-assert sit/sleep bool (RandomAnimation / emote_stop can clear it).
            try
            {
                if (!string.IsNullOrEmpty(_attachAnimation))
                {
                    GetComponent<ZSyncAnimation>()?.SetBool(_attachAnimation, true);
                }
            }
            catch
            {
            }
        }

        private void StopPlayerAttach(bool stepAwayFromSeat = false)
        {
            if (!_playerAttached && !_sleepAttached)
            {
                return;
            }

            // Move while collisions still ignored, then restore (avoids jam in bench/table).
            if (_attachPoint != null)
            {
                // Vanilla default detach is (0, 0.5, 0) — lift only. Add local -Z step out
                // (sit facing is +Z; leave behind the seat like walking off a chair).
                var local = _attachDetachOffset;
                if (stepAwayFromSeat)
                {
                    if (Mathf.Abs(local.z) < 0.2f)
                    {
                        local.z = -0.95f;
                    }
                    else
                    {
                        local.z -= Mathf.Sign(local.z) * 0.35f;
                    }

                    if (local.y < 0.15f)
                    {
                        local.y = 0.35f;
                    }
                }

                var pos = _attachPoint.TransformPoint(local);
                SnapToPieceOrTerrain(ref pos, _attachPoint.position.y);
                // Never land above the seat again.
                if (pos.y > _attachPoint.position.y + 1.2f)
                {
                    pos.y = _attachPoint.position.y + 0.1f;
                }

                transform.position = pos;
                if (_nview != null && _nview.IsValid() && _nview.IsOwner())
                {
                    _nview.GetZDO().SetPosition(pos);
                }
            }

            var capsule = _character != null ? _character.m_collider : null;
            if (capsule != null && _attachColliders != null)
            {
                foreach (var col in _attachColliders)
                {
                    if (col != null)
                    {
                        Physics.IgnoreCollision(capsule, col, false);
                    }
                }
            }

            _attachColliders = null;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            try
            {
                var zanim = GetComponent<ZSyncAnimation>();
                if (zanim != null && !string.IsNullOrEmpty(_attachAnimation))
                {
                    zanim.SetBool(_attachAnimation, false);
                }

                var anim = GetComponentInChildren<Animator>();
                if (anim != null && !string.IsNullOrEmpty(_attachAnimation))
                {
                    anim.SetBool(_attachAnimation, false);
                }
            }
            catch
            {
            }

            if (_nview != null && _nview.IsValid())
            {
                try
                {
                    _nview.GetZDO().Set(ZDOVars.s_inBed, false);
                }
                catch
                {
                    _nview.GetZDO().Set("inBed", false);
                }
            }

            _playerAttached = false;
            _sleepAttached = false;
            _attachPoint = null;
            _attachAnimation = "";
            _attachDetachOffset = Vector3.zero;

            RefreshRandomAnimationGate();

            try
            {
                _character?.ResetCloth();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Vanilla RandomAnimation fights attach_chair / persistent emotes — only on free idle.
        /// </summary>
        internal void RefreshRandomAnimationGate()
        {
            if (_randomAnim == null)
            {
                _randomAnim = GetComponent<RandomAnimation>();
            }

            if (_randomAnim == null)
            {
                return;
            }

            var allow = !_sitting &&
                        !_sleeping &&
                        !_playerAttached &&
                        !_hasTarget &&
                        _chore == Chore.Idle &&
                        !WifeEmotes.IsPersistentActive(gameObject);
            _randomAnim.enabled = allow;
        }

        private bool TryAttachStart(Transform attach)
        {
            // Bed path — Player uses attach_bed + isBed.
            return StartPlayerAttach(attach, null, true, true, "attach_bed", Vector3.zero);
        }

        internal void Despawn()
        {
            if (gameObject == null)
            {
                return;
            }

            ClearMapPin();

            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _nview.Destroy();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        internal void TickWork(WifeHome home)
        {
            _home = home;
            if (!_booted || home == null)
            {
                return;
            }

            // Player may spawn after Boot — keep trying until solid contact is restored once.
            if (!_playerCollisionRestored && Player.m_localPlayer != null)
            {
                RestorePlayerCollision();
                _playerCollisionRestored = true;
            }

            // Recover BEFORE atmosphere — night used to return early and skip AbortBusy forever
            // while she was stuck on a dead Collect/Forage/Fire chore (frozen, no new tasks).
            RecoverStalledWork();
            AbortBusyIfTimedOut();

            // Day/night + weather (EnvMan) — before idle/chores.
            if (TickAtmosphere(home))
            {
                return;
            }

            // Leftover pick/repair hold must not delay the next chore pick while Idle.
            if (Time.time < _actionUntil && (IsBusyOwned() || _sitting || _sleeping))
            {
                return;
            }

            var mode = home.Lifestyle;
            var workMode = PluginConfig.AllowsWork(mode);

            // Hard locks — never snatch work mid-sit/sleep/look/attend.
            if (_lookPresentActive || _sitting || _sleeping || _sleepAttached ||
                (_beat == Beat.Attend && Time.time < _facePlayerUntil))
            {
                return;
            }

            // Active owned chore / path to a work target — let Update drive it.
            if (IsBusyOwned() || (_hasTarget && _chore != Chore.Idle))
            {
                return;
            }

            // Leisure: presence owns the think (stroll/linger/sit).
            // Work modes: idle stroll/linger must NOT starve fire/cook/collect picks.
            if (!workMode)
            {
                if (IsBeatLocked || Time.time < _idlePauseUntil)
                {
                    return;
                }

                TickIdlePresence(home);
                return;
            }

            // Balanced / Diligent — try work even if a presence beat is lingering.
            if (Time.time < _actionUntil && _chore != Chore.Idle)
            {
                return;
            }

            var busy = IsBusyOwned() || (_hasTarget && _chore != Chore.Idle);
            if (_wasBusyLastThink && !busy)
            {
                var cd = PluginConfig.ChoreCooldown != null ? PluginConfig.ChoreCooldown.Value : 12f;
                if (mode == LifestyleMode.Diligent)
                {
                    cd = Mathf.Max(2.5f, cd * 0.28f);
                }
                else
                {
                    cd = Mathf.Max(4f, cd * 0.65f);
                }

                _choreCooldownUntil = Mathf.Max(_choreCooldownUntil, Time.time + cd);
            }

            _wasBusyLastThink = busy;

            if (busy)
            {
                return;
            }

            // Cooldown after finishing work — reclaim only; don't start a long stroll here
            // (that used to set _hasTarget and lock IsBeatLocked for 20–40s).
            if (Time.time < _choreCooldownUntil)
            {
                if (home.DoCook && PluginConfig.EnableCook.Value && TryBeginWifeCookReclaim(home))
                {
                    return;
                }

                if (mode == LifestyleMode.Diligent)
                {
                    ParkStill();
                    return;
                }

                // Balanced: short linger only (no stroll during chore cooldown).
                if (Time.time >= _idlePauseUntil && !_hasTarget)
                {
                    ParkStill();
                    var pause = Random.Range(2.5f, 5f);
                    _idlePauseUntil = Time.time + pause;
                    BeginBeat(Beat.Linger, pause);
                }

                return;
            }

            // Clear idle pause so a finished linger doesn't block the first work try.
            if (_chore == Chore.Idle && (_beat == Beat.Stroll || _beat == Beat.Linger))
            {
                _idlePauseUntil = 0f;
            }

            // Light maintenance first.
            if (home.DoEat && PluginConfig.EnableEat.Value && TryAutoEat(home))
            {
                return;
            }

            // One chore at a time. Priority for the lar:
            // 1) fire  2) her cook reclaim/place  3) recolher session  4) repair  5) smelt…
            // → idle presence. No free-for-all "grab any done food".
            if (home.DoFire && PluginConfig.EnableFire.Value && TryBeginFire(home))
            {
                return;
            }

            if (home.DoCook && PluginConfig.EnableCook.Value && TryBeginWifeCookReclaim(home))
            {
                return;
            }

            if (home.DoCook && PluginConfig.EnableCook.Value && TryBeginCook(home))
            {
                return;
            }

            if (home.DoCollect &&
                (PluginConfig.EnableCollect.Value || PluginConfig.EnableForage.Value) &&
                TryBeginCollect(home))
            {
                return;
            }

            if (home.DoRepair && PluginConfig.EnableRepair.Value && TryBeginRepair(home))
            {
                return;
            }

            if (home.DoSmelt && PluginConfig.EnableSmelt.Value && TryBeginSmelt(home))
            {
                return;
            }

            if (home.DoCook && PluginConfig.EnableCook.Value && PluginConfig.EnableCauldron.Value &&
                TryBeginCauldron(home))
            {
                return;
            }

            if (home.DoMead && PluginConfig.EnableMead.Value && TryBeginMead(home))
            {
                return;
            }

            if (home.DoFarm && PluginConfig.EnableFarm.Value && TryBeginFarm(home))
            {
                return;
            }

            // Pescar / jardim: code lives for later polish — not in 0.9 menu surface.
            if (home.DoFish && PluginConfig.EnableFish.Value && TryBeginFish(home))
            {
                return;
            }

            if (home.DoGarden && PluginConfig.EnableGarden.Value && TryGardenTips(home))
            {
                return;
            }

            TickIdlePresence(home);
        }

        private void BeginSleep(WifeHome home)
        {
            if (_sleeping)
            {
                return;
            }

            // Clean abort if somehow called mid-work (atmosphere normally waits for IsBusyOwned).
            if (_chore != Chore.Idle &&
                _chore != Chore.Sleep &&
                _chore != Chore.Nap &&
                _chore != Chore.Sit &&
                _chore != Chore.SitFire)
            {
                StopWorkRoutine();
                ForceClearRightHand();
                UnequipFishingRod();
            }

            ExitSitting();

            if (home.HasAssignedBed)
            {
                var bed = home.GetAssignedBed();
                if (IsPlayerOccupyingBed(bed))
                {
                    // Yield the mattress — wait nearby instead of clipping into the player.
                    _chore = Chore.Idle;
                    CancelBeat();
                    var wait = GetBedWalkTarget(home, out _);
                    if (HorizontalDistance(transform.position, wait) > 2.2f)
                    {
                        SetTarget(wait);
                    }
                    else
                    {
                        ParkStill();
                        FacePoint(bed != null ? bed.transform.position : home.HomePosition);
                    }

                    _idlePauseUntil = Time.time + Random.Range(6f, 12f);
                    return;
                }
            }

            _chore = Chore.Sleep;
            BeginBeat(Beat.Sleep);

            if (!home.HasAssignedBed)
            {
                var stand = home.HomePosition + home.transform.forward * 1.5f;
                SnapToPieceOrTerrain(ref stand, home.HomePosition.y);
                SetTarget(stand);
                if (HorizontalDistance(transform.position, stand) < 1.2f)
                {
                    // Already at stand — enter sleep or we soft-lock on "Going to bed…" forever.
                    _hasTarget = false;
                    _ai?.StopMoving();
                    SetSleepAnim(true);
                    _sleeping = true;
                    SilenceAiMove();
                    ParkStill();
                }

                return;
            }

            var pos = home.GetSleepPosition(out var rot);
            var walk = GetBedWalkTarget(home, out _);
            SetTarget(walk);

            // Walk to the bed like the player; only attach when close.
            if (HorizontalDistance(transform.position, walk) < 1.65f ||
                HorizontalDistance(transform.position, pos) < 1.65f)
            {
                _hasTarget = false;
                if (!_sleeping)
                {
                    EnterSleepVisual(pos, rot);
                }
            }
        }

        /// <summary>
        /// Organic homestead presence (IdleActors / Fires): stroll and linger most of the time.
        /// Sit / fire / nap are rare beats — never a rapid sit→stand→nap hop.
        /// </summary>
        private void TickIdlePresence(WifeHome home)
        {
            // Never clobber an active sit/attach.
            if (_sitting || _sleepAttached || _sleeping)
            {
                return;
            }

            _chore = Chore.Idle;

            if (_hasTarget)
            {
                return;
            }

            if (Time.time < _idlePauseUntil)
            {
                ParkStill();
                return;
            }

            var campTotem = PluginConfig.ParkNearIdol != null && PluginConfig.ParkNearIdol.Value;
            var wanderOn = PluginConfig.IdleWander == null || PluginConfig.IdleWander.Value;
            // Shelter weights only in Vida no lar — work modes keep normal idle between chores.
            var leisure = !PluginConfig.AllowsWork(home.Lifestyle);
            var diligent = home.Lifestyle == LifestyleMode.Diligent;
            var shelter = leisure && _preferShelter;
            if (shelter)
            {
                wanderOn = wanderOn && Random.value < 0.40f;
            }

            // Work modes: short presence only — long strolls were starving chore picks.
            if (!leisure)
            {
                wanderOn = wanderOn && Random.value < (diligent ? 0.18f : 0.40f);
            }

            // Optional: camp at totem porch (off by default — totem is spawn/chest/menu only).
            if (campTotem)
            {
                var stand = home.HomePosition + home.transform.forward * 2.5f;
                SnapToPieceOrTerrain(ref stand, home.HomePosition.y);
                if (HorizontalDistance(transform.position, stand) > 3.5f)
                {
                    SetTarget(stand);
                    return;
                }
            }

            // One weighted beat per think — not independent sit/nap rolls that stack.
            // Fair weather: stroll ~70%, linger ~22%, sit ~6%, fire/nap ~2%.
            // Shelter: stroll ~35%, linger ~50%, sit ~12%, fire ~3%.
            // Work / Diligent: mostly short linger so the next think can pick fire/cook again.
            // Right after spawn/load: force a short porch walk so she doesn't look frozen.
            var wakeStroll = _wakeWithStroll;
            if (_wakeWithStroll)
            {
                _wakeWithStroll = false;
                var porch = home.HomePosition + home.transform.forward * 2.8f;
                SnapToPieceOrTerrain(ref porch, home.HomePosition.y);
                if (HorizontalDistance(transform.position, porch) > 0.8f)
                {
                    SetTarget(porch);
                    BeginBeat(Beat.Stroll);
                    _facePlayerUntil = 0f;
                    _wanderUntil = Time.time + Random.Range(8f, 14f);
                    return;
                }
            }

            var roll = wakeStroll ? 0f : Random.value;
            var strollChance = leisure
                ? (shelter ? 0.35f : 0.72f)
                : (diligent ? 0.12f : 0.32f);
            var lingerUntil = strollChance + (leisure
                ? (shelter ? 0.50f : 0.20f)
                : (diligent ? 0.80f : 0.55f));
            var sitUntil = lingerUntil + (leisure ? (shelter ? 0.12f : 0.06f) : 0.04f);

            if (wanderOn && roll < strollChance)
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    if (!home.TryPickPointInRadius(0.18f, 0.88f, out var point))
                    {
                        continue;
                    }

                    if (!TryPlaceOnWalkable(ref point))
                    {
                        continue;
                    }

                    if (WifeStuckMemory.IsBlocked(point))
                    {
                        continue;
                    }

                    // After boot, allow shorter first steps (she's right at the idol).
                    var minStep = wakeStroll ? 1.2f : 2.5f;
                    if (HorizontalDistance(transform.position, point) < minStep)
                    {
                        continue;
                    }

                    if (!IsDestinationReachable(point))
                    {
                        continue;
                    }

                    SetTarget(point);
                    BeginBeat(Beat.Stroll);
                    _facePlayerUntil = 0f; // stroll owns facing — don't fight MoveTo yaw
                    _wanderUntil = Time.time + (diligent
                        ? Random.Range(8f, 14f)
                        : leisure
                            ? Random.Range(20f, 40f)
                            : Random.Range(12f, 22f));
                    return;
                }

                // No walkable point — short linger only (not 10–18s after load).
                ParkStill();
                var failPause = diligent ? Random.Range(1.5f, 3f) : Random.Range(2.5f, 5f);
                _idlePauseUntil = Time.time + failPause;
                BeginBeat(Beat.Linger, failPause);
                return;
            }

            if (roll < lingerUntil)
            {
                if (leisure && Random.value < 0.08f && CanAmbientSocialTalk())
                {
                    var idle = WifeEmotes.QuickIdle[Random.Range(0, WifeEmotes.QuickIdle.Length)];
                    TryOfferSocialEmote(idle, 20f);
                }
                else if (leisure && TryMusicIdle(home))
                {
                    var musicPause = Random.Range(10f, 16f);
                    _idlePauseUntil = Time.time + musicPause;
                    BeginBeat(Beat.Linger, musicPause);
                    return;
                }

                ParkStill();
                var lingerPause = diligent
                    ? Random.Range(2f, 4.5f)
                    : leisure
                        ? Random.Range(12f, 22f)
                        : Random.Range(4f, 8f);
                _idlePauseUntil = Time.time + lingerPause;
                BeginBeat(Beat.Linger, lingerPause);
                return;
            }

            // Sit / nap rare beats — Leisure (and light Balanced). Diligent almost never sits mid-shift.
            if (!diligent &&
                roll < sitUntil &&
                Time.time >= _sitCooldownUntil &&
                home.DoSit &&
                TryBeginSit(home))
            {
                return;
            }

            if (!diligent &&
                roll < sitUntil + 0.03f &&
                home.DoSitFire &&
                Time.time >= _sitCooldownUntil &&
                TrySitByFire(home))
            {
                return;
            }

            // Day nap only in Vida no lar — work modes must not keep trying to lie down in the rain.
            if (leisure &&
                home.DoNap && home.HasAssignedBed &&
                Time.time >= _sitCooldownUntil &&
                Random.value < 0.35f &&
                TryBeginNap(home))
            {
                return;
            }

            ParkStill();
            var endPause = diligent
                ? Random.Range(2f, 4f)
                : leisure
                    ? Random.Range(10f, 18f)
                    : Random.Range(4f, 7f);
            _idlePauseUntil = Time.time + endPause;
            BeginBeat(Beat.Linger, endPause);
        }

        private void SetTarget(Vector3 world)
        {
            if (_home != null &&
                !_home.IsInside(world, 0.5f) &&
                _chore != Chore.Sleep &&
                _chore != Chore.Fish)
            {
                world = ClampToHome(world);
            }

            if (WifeStuckMemory.IsBlocked(world) && _home != null &&
                WifeStuckMemory.TryPickUnblocked(_home, 0.15f, 0.5f, out var alt))
            {
                world = alt;
            }

            // Never path onto lake/river bed (except we still pull fish spots to dry shore).
            if (IsUnsafeWaterWalk(world))
            {
                if (_home != null &&
                    TryFindDryNear(_home, world, 1.5f, 8f, out var dry))
                {
                    world = dry;
                }
                else if (_chore == Chore.Fish)
                {
                    // Fishing: stand on bank — nudge toward home.
                    var pull = _home != null ? _home.HomePosition : transform.position;
                    var flat = world - pull;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.01f)
                    {
                        world = world - flat.normalized * 2.5f;
                    }

                    SnapToPieceOrTerrain(ref world, world.y);
                    if (IsUnsafeWaterWalk(world))
                    {
                        return; // keep previous target
                    }
                }
                else
                {
                    return;
                }
            }

            // Don't stroll onto the player's feet.
            if (_chore == Chore.Idle && IsTooCloseToPlayer(world, 2.0f))
            {
                if (_home != null &&
                    TryFindDryNear(_home, world, 2.2f, 8f, out var away) &&
                    !IsTooCloseToPlayer(away, 2.0f))
                {
                    world = away;
                }
                else
                {
                    return;
                }
            }

            _target = world;
            _hasTarget = true;
            ResetStuck();
            RefreshRandomAnimationGate();
        }

        private bool TryBeginRepair(WifeHome home)
        {
            WearNTear best = null;
            var bestDist = home.Radius;

            foreach (var wear in WearNTear.GetAllInstances())
            {
                if (wear == null || wear.m_nview == null || !wear.m_nview.IsValid())
                {
                    continue;
                }

                if (wear.GetHealthPercentage() >= 0.95f)
                {
                    continue;
                }

                if (wear.GetComponent<WifeHome>() != null)
                {
                    continue;
                }

                if (!home.IsInside(wear.transform.position))
                {
                    continue;
                }

                var d = Vector3.Distance(wear.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = wear;
            }

            if (best == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Repair);
            _repairTarget = best;
            EquipTool("Hammer");
            Notify("$hearthwife_busy_repair");

            // Already close enough (or under a roof piece) — repair in place.
            if (InRepairReach(transform.position, best))
            {
                OnArrived();
                return true;
            }

            // Walk toward a stand near the piece; arrival also triggers if she enters RepairReach.
            var approach = FindRepairStand(best);
            if (WifeStuckMemory.IsBlocked(approach) &&
                WifeStuckMemory.TryPickUnblocked(home, 0.15f, 0.45f, out var alt))
            {
                approach = alt;
            }

            SetTarget(approach);

            if (HorizontalDistance(transform.position, _target) < 4.2f ||
                InRepairReach(transform.position, best))
            {
                OnArrived();
            }

            return true;
        }

        private static bool InRepairReach(Vector3 from, WearNTear wear)
        {
            if (wear == null)
            {
                return false;
            }

            return Vector3.Distance(from, wear.transform.position) <= RepairReach;
        }

        /// <summary>Walkable stand near a damaged piece — stand-off, not glued to the mesh.</summary>
        private Vector3 FindRepairStand(WearNTear wear)
        {
            var piecePos = wear.transform.position;
            var preferY = _home != null ? _home.HomePosition.y : transform.position.y;
            // Prefer same floor as her (ground under a roof), not climbing onto the piece.
            var best = ApproachPoint(piecePos, 3.0f);
            best.y = preferY + 0.2f;
            SnapToPieceOrTerrain(ref best, preferY);
            var bestScore = -1f;

            for (var i = 0; i < 8; i++)
            {
                var ang = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var p = piecePos + dir * 3.0f;
                p.y = preferY + 0.2f;
                SnapToPieceOrTerrain(ref p, preferY);
                if (WifeStuckMemory.IsBlocked(p))
                {
                    continue;
                }

                // Must still be in repair reach of the piece from this stand.
                if (Vector3.Distance(p, piecePos) > RepairReach)
                {
                    continue;
                }

                var score = 12f - Mathf.Abs(p.y - transform.position.y) * 2f;
                score -= HorizontalDistance(transform.position, p) * 0.04f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        private Vector3 ApproachPoint(Vector3 piecePos, float standoff)
        {
            var dir = transform.position - piecePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.05f)
            {
                dir = _home != null ? -_home.transform.forward : -transform.forward;
            }

            dir.Normalize();
            var p = piecePos + dir * standoff;
            p.y = piecePos.y + 0.2f;
            SnapToFloor(ref p);
            return p;
        }

        /// <summary>
        /// Start an organic gather session (several nearby bushes/drops) — not one-and-done.
        /// </summary>
        private bool TryBeginCollect(WifeHome home)
        {
            if (_carryingLoot)
            {
                BeginOwnedChore(Chore.Collect);
                SetTarget(GetChestDepositStand(home));
                Notify("$hearthwife_busy_deposit");
                if (HorizontalDistance(transform.position, _target) < 1.6f)
                {
                    OnArrived();
                }

                return true;
            }

            if (home.IsStorageFull)
            {
                return false;
            }

            if (Time.time < _collectBlockedUntil)
            {
                return false;
            }

            if (!TryFindGatherTarget(home, transform.position, home.Radius, out _, out _))
            {
                return false;
            }

            BeginOwnedChore(Chore.Collect);
            StartWork(GatherSessionRoutine(home));
            return true;
        }

        private bool TryFindGatherTarget(
            WifeHome home,
            Vector3 near,
            float maxFromHome,
            out Pickable pick,
            out ItemDrop drop)
        {
            pick = null;
            drop = null;
            if (home == null)
            {
                return false;
            }

            var homeLeash = Mathf.Max(8f, maxFromHome > 1f ? maxFromHome : home.Radius);
            // Prefer something close to her (organic lote), then expand to the full ward.
            if (TryFindGatherTargetInRange(home, near, GatherNearLeash, out pick, out drop))
            {
                return true;
            }

            return TryFindGatherTargetInRange(home, near, homeLeash, out pick, out drop);
        }

        private bool TryFindGatherTargetInRange(
            WifeHome home,
            Vector3 near,
            float leash,
            out Pickable pick,
            out ItemDrop drop)
        {
            pick = null;
            drop = null;
            var mask = home.PickupMask;
            var bestPickDist = leash;
            var bestDropDist = leash;
            var forageOn = PluginConfig.EnableForage == null || PluginConfig.EnableForage.Value ||
                           PluginConfig.EnableCollect == null || PluginConfig.EnableCollect.Value;

            if (forageOn)
            {
                foreach (var p in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
                {
                    if (p == null || !home.IsInside(p.transform.position))
                    {
                        continue;
                    }

                    // Farm crops belong to Colher plantações — not Recolher.
                    if (IsCropPickable(p))
                    {
                        continue;
                    }

                    try
                    {
                        if (p.GetPicked() || !p.CanBePicked())
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (!p.CanBePicked())
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (!WifePickupFilter.IsAllowedPickable(p, mask))
                    {
                        continue;
                    }

                    if (WifeStuckMemory.IsBlocked(p.transform.position))
                    {
                        continue;
                    }

                    var d = Vector3.Distance(p.transform.position, near);
                    if (d > leash || d >= bestPickDist)
                    {
                        continue;
                    }

                    bestPickDist = d;
                    pick = p;
                }
            }

            if (pick != null)
            {
                drop = null;
                return true;
            }

            foreach (var d0 in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (d0 == null || d0.m_itemData == null)
                {
                    continue;
                }

                if (!IsCollectibleLoot(d0, home))
                {
                    continue;
                }

                if (!home.IsInside(d0.transform.position))
                {
                    continue;
                }

                try
                {
                    if (d0.IsPiece())
                    {
                        continue;
                    }
                }
                catch
                {
                }

                if (WifeStuckMemory.IsBlocked(d0.transform.position))
                {
                    continue;
                }

                var d = Vector3.Distance(d0.transform.position, near);
                if (d > leash || d >= bestDropDist)
                {
                    continue;
                }

                bestDropDist = d;
                drop = d0;
            }

            return drop != null;
        }

        private IEnumerator GatherSessionRoutine(WifeHome home)
        {
            _gatherSessionActive = true;
            BeginOwnedChore(Chore.Collect);
            WifeEmotes.Stop(gameObject);
            _actionUntil = 0f;
            _idlePauseUntil = 0f;
            CancelBeat();

            var gathered = 0;
            var started = Time.time;
            var focus = transform.position;

            try
            {
                while (_gatherSessionActive && this != null && home != null)
                {
                    if (home.IsStorageFull)
                    {
                        Notify("$hearthwife_chest_full");
                        break;
                    }

                    var elapsed = Time.time - started;
                    if (gathered >= GatherMaxItems || elapsed >= GatherMaxSeconds)
                    {
                        break;
                    }

                    if (gathered >= GatherMinItems || elapsed >= GatherMinSeconds)
                    {
                        if (ShouldYieldGatherSession(home))
                        {
                            break;
                        }
                    }

                    if (!TryFindGatherTarget(home, focus, home.Radius, out var pick, out var drop))
                    {
                        break;
                    }

                    if (pick != null)
                    {
                        BeginOwnedChore(Chore.Forage);
                        _forageTarget = pick;
                        Notify("$hearthwife_busy_forage");
                        _actionUntil = 0f;

                        // Stand beside the bush and pick from range — never warp into the collider.
                        yield return ApproachGatherFocus(
                            pick.transform.position,
                            GatherBushStandoff,
                            () => pick != null && InForageReach(transform.position, pick),
                            home);

                        if (pick == null || !InForageReach(transform.position, pick))
                        {
                            if (pick != null)
                            {
                                WifeStuckMemory.Mark(pick.transform.position, 14f);
                            }

                            _forageTarget = null;
                            continue;
                        }

                        var forageSteps = ForageOnePickable(pick);
                        while (forageSteps.MoveNext())
                        {
                            yield return forageSteps.Current;
                        }

                        _forageTarget = null;
                        _actionUntil = 0f;
                        focus = transform.position;
                        gathered++;
                    }
                    else if (drop != null)
                    {
                        BeginOwnedChore(Chore.Collect);
                        Notify("$hearthwife_busy_collect");
                        _actionUntil = 0f;

                        // Same idea: stand near the drop; magnet pulls the item — she stays outside.
                        yield return ApproachGatherFocus(
                            drop.transform.position,
                            GatherDropStandoff,
                            () =>
                            {
                                try
                                {
                                    return drop != null && drop.gameObject != null &&
                                           HorizontalDistance(
                                               transform.position, drop.transform.position) <=
                                           GatherDropPickupRange;
                                }
                                catch
                                {
                                    return false;
                                }
                            },
                            home);

                        try
                        {
                            if (drop == null || drop.gameObject == null)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }

                        if (HorizontalDistance(transform.position, drop.transform.position) >
                            GatherDropPickupRange)
                        {
                            WifeStuckMemory.Mark(drop.transform.position, 12f);
                            continue;
                        }

                        var pickSteps = PickupLootRoutine(drop);
                        while (pickSteps.MoveNext())
                        {
                            yield return pickSteps.Current;
                        }

                        _actionUntil = 0f;
                        focus = transform.position;
                        gathered++;
                    }

                    yield return new WaitForSeconds(0.08f);
                }
            }
            finally
            {
                _gatherSessionActive = false;
                _forageTarget = null;
                _lootTarget = null;
            }

            ForceClearRightHand();
            _actionUntil = 0f;
            EndOwnedChore(gathered > 0 ? 0.55f : 0.35f);
        }

        /// <summary>
        /// Walk to a stand-off around focus (bush / ground loot). Retries orbit angles if path stalls.
        /// Never teleports her into the target (that jammed her inside bush colliders).
        /// </summary>
        private IEnumerator ApproachGatherFocus(
            Vector3 focus,
            float standoff,
            System.Func<bool> canAct,
            WifeHome home)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (!_gatherSessionActive || canAct == null)
                {
                    yield break;
                }

                if (canAct())
                {
                    yield break;
                }

                Vector3 stand;
                if (attempt == 0)
                {
                    stand = GetGatherStandOff(focus, standoff, home);
                }
                else
                {
                    var ang = (attempt * 90f + 40f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    stand = focus + dir * standoff;
                    stand.y = focus.y;
                    SnapToPieceOrTerrain(ref stand, home != null ? home.HomePosition.y : focus.y);
                }

                if (IsUnsafeWaterWalk(stand) || WifeStuckMemory.IsBlocked(stand))
                {
                    continue;
                }

                // Keep a clear ring — if snap collapsed into the bush, push out.
                EnforceGatherStandoff(ref stand, focus, standoff * 0.9f);

                yield return WalkGatherTo(stand, canAct, 7f);
                if (canAct())
                {
                    yield break;
                }
            }
        }

        private Vector3 GetGatherStandOff(Vector3 focus, float standoff, WifeHome home)
        {
            // Approach from her current side so she doesn't path through the plant.
            var stand = ApproachPoint(focus, standoff);
            var floorY = home != null ? home.HomePosition.y : focus.y;
            SnapToPieceOrTerrain(ref stand, floorY);
            EnforceGatherStandoff(ref stand, focus, standoff * 0.9f);
            SnapToPieceOrTerrain(ref stand, floorY);
            EnforceGatherStandoff(ref stand, focus, standoff * 0.85f);
            return stand;
        }

        private static void EnforceGatherStandoff(ref Vector3 stand, Vector3 focus, float minDist)
        {
            var flat = stand - focus;
            flat.y = 0f;
            if (flat.sqrMagnitude >= minDist * minDist)
            {
                return;
            }

            var dir = flat.sqrMagnitude > 0.02f ? flat.normalized : Vector3.forward;
            stand = focus + dir * minDist;
            stand.y = focus.y;
        }

        /// <summary>Path to a stand — retarget only, never warp the wife into the focus.</summary>
        private IEnumerator WalkGatherTo(Vector3 stand, System.Func<bool> arrived, float maxSeconds)
        {
            _actionUntil = 0f;
            SetTarget(stand);
            ResetStuck();

            var start = Time.time;
            var lastMoveAt = Time.time;
            var lastPos = transform.position;

            while (_gatherSessionActive && Time.time < start + maxSeconds)
            {
                if (arrived != null && arrived())
                {
                    break;
                }

                if (HorizontalDistance(transform.position, stand) <= 1.35f)
                {
                    break;
                }

                if (HorizontalDistance(transform.position, lastPos) > 0.25f)
                {
                    lastPos = transform.position;
                    lastMoveAt = Time.time;
                }
                else if (Time.time - lastMoveAt > 2.2f)
                {
                    // Path stalled — refresh path only (no teleport).
                    ResetStuck();
                    SetTarget(stand);
                    lastMoveAt = Time.time;
                }

                yield return null;
            }

            _hasTarget = false;
            StopLocomotion();
        }

        /// <summary>Soft yield only — fire out, or her grill has done food waiting.</summary>
        private bool ShouldYieldGatherSession(WifeHome home)
        {
            if (home == null)
            {
                return false;
            }

            if (home.DoFire && PluginConfig.EnableFire.Value && HasUrgentFireNeed(home))
            {
                return true;
            }

            if (home.DoCook && PluginConfig.EnableCook.Value &&
                _wifeCookStation != null &&
                StationHasDoneOrBurntFood(_wifeCookStation))
            {
                return true;
            }

            return false;
        }

        private bool HasUrgentFireNeed(WifeHome home)
        {
            // Lightweight: any fireplace in circle needing fuel/lit — reuse TryBeginFire probe.
            // Avoid starting the chore here; only signal yield.
            try
            {
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
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>One bush interact + scoop (no EndOwnedChore — session owns lifetime).</summary>
        private IEnumerator ForageOnePickable(Pickable pick)
        {
            WifeEmotes.Stop(gameObject);
            var inv = _home?.Storage?.GetInventory();
            if (inv == null || pick == null)
            {
                yield break;
            }

            if (_home != null && _home.IsStorageFull)
            {
                yield break;
            }

            var pos = pick.transform.position;
            EnterStationaryWorkHold(pos, _gatherSessionActive ? 1.0f : 3.2f);
            // One bend only — PlayPickupFeedback already includes PlayPickupBend.
            PlayPickupFeedback(pos);

            // Fires ResourceGathering: Pickable.Interact(Humanoid) — not Player RPC "Pick".
            var picked = false;
            try
            {
                if (_humanoid != null && pick.CanBePicked())
                {
                    picked = pick.Interact(_humanoid, false, false);
                }
            }
            catch
            {
                picked = false;
            }

            if (!picked)
            {
                try
                {
                    if (pick.m_nview != null && pick.m_nview.IsValid() && pick.CanBePicked())
                    {
                        pick.m_nview.InvokeRPC("RPC_Pick");
                        picked = true;
                    }
                }
                catch
                {
                    try
                    {
                        pick.m_nview?.InvokeRPC("Pick", 0);
                    }
                    catch
                    {
                    }
                }
            }

            yield return new WaitForSeconds(_gatherSessionActive ? 0.28f : 0.55f);
            ScoopForageNear(inv, pos, 3.5f, pick);
            ForceClearRightHand();
            if (_gatherSessionActive)
            {
                _actionUntil = 0f;
            }
        }

        private bool TryBeginFish(WifeHome home)
        {
            if (Time.time < _fishCooldown)
            {
                return false;
            }

            var inv = home.Storage?.GetInventory();
            if (inv == null || !HasBait(inv))
            {
                return false;
            }

            Vector3 spot;
            if (!home.TryGetFishSpot(out spot) && !TryFindWaterSpot(home, out spot))
            {
                return false;
            }

            // Bait is consumed only after a successful catch (see FishRoutine).
            BeginOwnedChore(Chore.Fish);
            _fishCooldown = Time.time + 90f;
            SilenceAiMove();
            StartWork(FishRoutine(home, inv, spot));
            return true;
        }

        private IEnumerator FishRoutine(WifeHome home, Inventory inv, Vector3 spot)
        {
            Notify("$hearthwife_busy_fish");
            BeginOwnedChore(Chore.Fish);

            // Walk / warp to the fishing spot (river bank etc.).
            SnapToPieceOrTerrain(ref spot, spot.y);
            SetTarget(spot);
            if (HorizontalDistance(transform.position, spot) > 2.5f)
            {
                WarpToTarget();
            }

            _hasTarget = false;
            FaceWaterAt(spot, home);
            EnterStationaryWorkHold(10f);
            EquipFishingRod();

            var zanim = GetComponent<ZSyncAnimation>();
            var anim = GetComponentInChildren<Animator>();
            var rodData = GetItemData("FishingRod");

            // Cast — same attack / start effects as the player rod.
            zanim?.SetTrigger("attack");
            if (anim != null)
            {
                try
                {
                    anim.SetTrigger("attack");
                    anim.Play("attack", 0, 0f);
                }
                catch
                {
                }
            }

            try
            {
                _humanoid?.StartAttack(null, false);
            }
            catch
            {
            }

            try
            {
                rodData?.m_shared?.m_attack?.m_startEffect?.Create(
                    transform.position + Vector3.up * 1.2f + transform.forward * 0.5f,
                    transform.rotation);
            }
            catch
            {
            }

            var end = Time.time + 7.5f;
            var nextPulse = Time.time + 2.2f;
            while (Time.time < end && _chore == Chore.Fish && this != null)
            {
                HoldStationaryWorkTick(2f);
                if (Time.time >= nextPulse)
                {
                    zanim?.SetTrigger("attack");
                    try
                    {
                        _humanoid?.StartAttack(null, false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        rodData?.m_shared?.m_attack?.m_startEffect?.Create(
                            transform.position + Vector3.up * 1.2f + transform.forward,
                            transform.rotation);
                    }
                    catch
                    {
                    }

                    nextPulse = Time.time + 2.4f;
                }

                yield return null;
            }

            if (_chore != Chore.Fish)
            {
                UnequipFishingRod();
                yield break;
            }

            // Catch splash / hit effect
            try
            {
                rodData?.m_shared?.m_attack?.m_hitTerrainEffect?.Create(
                    transform.position + transform.forward * 2.5f,
                    Quaternion.identity);
            }
            catch
            {
            }

            if (!TryConsumeBait(inv))
            {
                ForceClearRightHand();
                EndOwnedChore(2f);
                yield break;
            }

            var fishId = PickSimpleFish(spot);
            var prefab = ObjectDB.instance?.GetItemPrefab(fishId);
            var fishItem = prefab?.GetComponent<ItemDrop>()?.m_itemData?.Clone();
            if (fishItem != null)
            {
                fishItem.m_stack = 1;
                if (inv == null || !inv.AddItem(fishItem))
                {
                    try
                    {
                        ItemDrop.DropItem(
                            fishItem,
                            1,
                            transform.position + transform.forward * 0.5f,
                            transform.rotation);
                    }
                    catch
                    {
                    }
                }
            }

            ForceClearRightHand();
            EndOwnedChore(2f);
            SilenceAiMove();
            FaceHome();
        }

        private static bool TryFindWaterSpot(WifeHome home, out Vector3 spot)
        {
            spot = home.HomePosition;
            var best = home.HomePosition;
            var found = false;
            var bestScore = -1f;
            var range = Mathf.Min(home.FishRange, home.Radius + 24f);

            for (var i = 0; i < 16; i++)
            {
                var ang = i * (Mathf.PI * 2f / 16f);
                var p = home.HomePosition + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * range * 0.7f;
                var score = ScoreWater(p, home.HomePosition.y);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    found = score > 0f;
                }
            }

            if (!found)
            {
                return false;
            }

            spot = best;
            SnapToPieceOrTerrain(ref spot, home.HomePosition.y);
            return true;
        }

        private static float ScoreWater(Vector3 p, float homeY)
        {
            var score = 0f;
            var biome = Heightmap.FindBiome(p);
            if (biome == Heightmap.Biome.Ocean)
            {
                score += 3f;
            }
            else if (biome == Heightmap.Biome.Swamp || biome == Heightmap.Biome.Mistlands)
            {
                score += 0.5f;
            }

            // Prefer shoreline: floor well below home elevation near ocean only.
            if (score >= 3f &&
                ZoneSystem.instance != null &&
                ZoneSystem.instance.FindFloor(p + Vector3.up * 5f, out var y) &&
                y < homeY - 0.4f)
            {
                score += 1.5f;
            }

            // Liquid depth (rivers / lakes) — no meadow false-positive from y < 32.
            if (TryGetLiquidDepth(p, out var depth) && depth > 0.35f)
            {
                score += 2.5f;
            }

            return score;
        }

        private static bool TryGetLiquidDepth(Vector3 pos, out float depth)
        {
            depth = 0f;
            if (!TryGetWaterSurfaceY(pos, out var surfaceY))
            {
                return false;
            }

            // Depth of water column above the proposed feet / ground.
            depth = Mathf.Max(0f, surfaceY - pos.y);
            return depth > 0.2f;
        }

        private void FaceWaterAt(Vector3 from, WifeHome home)
        {
            Vector3 bestDir = home != null ? home.transform.forward : transform.forward;
            var bestScore = -1f;
            for (var i = 0; i < 8; i++)
            {
                var ang = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var p = from + dir * 8f;
                var score = ScoreWater(p, from.y);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = dir;
                }
            }

            if (bestDir.sqrMagnitude > 0.01f)
            {
                var n = bestDir.normalized;
                transform.rotation = Quaternion.LookRotation(n);
                try
                {
                    _character?.SetLookDir(n, 0f);
                }
                catch
                {
                }
            }
        }

        private void FaceWater(WifeHome home)
        {
            FaceWaterAt(home.HomePosition, home);
        }

        private void EquipFishingRod()
        {
            EquipTool("FishingRod");
        }

        private void UnequipFishingRod()
        {
            ForceClearRightHand();
        }

        private void Notify(string token)
        {
            var msg = Localization.instance.Localize(token);
            // Head bubble only — no yellow center HUD (blocks gameplay).
            if (gameObject != null)
            {
                WifeTalk.Say(gameObject, msg);
            }

            // Mirror busy_* onto hover so eat/garden/tap/protect match the bubble.
            if (!string.IsNullOrEmpty(token) &&
                token.IndexOf("hearthwife_busy_", System.StringComparison.Ordinal) >= 0)
            {
                _hoverHint = token;
                var hold = Mathf.Max(3.5f, _actionUntil - Time.time, _idlePauseUntil - Time.time);
                _hoverHintUntil = Time.time + Mathf.Clamp(hold, 3.5f, 14f);
            }
        }

        private static bool TryConsumeBait(Inventory inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                if (!item.m_dropPrefab.name.StartsWith("FishingBait"))
                {
                    continue;
                }

                inventory.RemoveItem(item, 1);
                return true;
            }

            return false;
        }

        private static bool HasBait(Inventory inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            foreach (var item in inventory.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                if (item.m_dropPrefab.name.StartsWith("FishingBait"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCollectibleLoot(ItemDrop drop, WifeHome home)
        {
            if (drop == null || drop.m_itemData?.m_shared == null)
            {
                return false;
            }

            // Skip quest / unique gear and anything still attached to a corpse marker.
            try
            {
                if (drop.m_itemData.m_shared.m_questItem)
                {
                    return false;
                }
            }
            catch
            {
            }

            if (drop.GetComponentInParent<TombStone>() != null)
            {
                return false;
            }

            if (drop.GetComponentInParent<Character>() != null)
            {
                return false;
            }

            // Ignore tiny debris / effects with no real stack.
            if (drop.m_itemData.m_stack < 1)
            {
                return false;
            }

            var mask = home != null ? home.PickupMask : PickupCategory.DefaultMask;
            return WifePickupFilter.IsAllowedDrop(drop, mask);
        }

        private static bool HasWaterNear(Vector3 pos, float radius)
        {
            if (Heightmap.FindBiome(pos) == Heightmap.Biome.Ocean)
            {
                return true;
            }

            if (TryGetLiquidDepth(pos, out var d) && d > 0.35f)
            {
                return true;
            }

            for (var i = 0; i < 8; i++)
            {
                var ang = i * 45f * Mathf.Deg2Rad;
                var p = pos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                if (Heightmap.FindBiome(p) == Heightmap.Biome.Ocean)
                {
                    return true;
                }

                if (TryGetLiquidDepth(p, out d) && d > 0.35f)
                {
                    return true;
                }
            }

            return false;
        }

        private static string PickSimpleFish(Vector3 pos)
        {
            switch (Heightmap.FindBiome(pos))
            {
                case Heightmap.Biome.Ocean:
                    return "Fish3";
                case Heightmap.Biome.Swamp:
                    return "Fish6";
                case Heightmap.Biome.Plains:
                    return "Fish7";
                case Heightmap.Biome.Mistlands:
                    return "Fish9";
                default:
                    return "Fish1";
            }
        }

        public string GetHoverText()
        {
            if (WifeMenu.IsOpen)
            {
                return "";
            }

            var name = _home != null ? _home.WifeDisplayName : Localization.instance.Localize("$hearthwife_wife_name");
            var status = GetActivityHint();
            var line = name;
            if (!string.IsNullOrEmpty(status))
            {
                line += "\n<color=orange>" + status + "</color>";
            }

            // Sleeping: no "press E to talk" — she's asleep.
            if (!IsAsleepQuiet())
            {
                line += "\n[<color=yellow><b>E</b></color>] "
                        + Localization.instance.Localize("$hearthwife_talk_hint");
            }

            return line;
        }

        /// <summary>
        /// Hover status — full state matrix (priority top → bottom).
        /// Never show music while sit/sleep/fire (emote_sit/rest are not songs).
        /// </summary>
        private string GetActivityHint()
        {
            // 1) Attached sleep / nap
            if (_sleeping)
            {
                return Localization.instance.Localize(
                    _chore == Chore.Nap ? "$hearthwife_busy_nap" : "$hearthwife_status_sleep");
            }

            // 2) Seated (chair attach or fire sit pose)
            if (_sitting)
            {
                return Localization.instance.Localize(
                    _chore == Chore.SitFire ? "$hearthwife_busy_sitfire" : "$hearthwife_busy_sit");
            }

            // 3) Walking toward bed / chair / fire (before Notify text)
            switch (_chore)
            {
                case Chore.Sleep:
                    return Localization.instance.Localize("$hearthwife_status_to_sleep");
                case Chore.Nap:
                    return Localization.instance.Localize("$hearthwife_status_to_nap");
                case Chore.Sit:
                    return Localization.instance.Localize("$hearthwife_status_to_sit");
                case Chore.SitFire:
                    return Localization.instance.Localize("$hearthwife_status_to_fire");
            }

            // 4) Ephemeral / more-specific busy (eat, garden, mead tap, smelt out, protect…)
            if (!string.IsNullOrEmpty(_hoverHint) && Time.time < _hoverHintUntil)
            {
                return Localization.instance.Localize(_hoverHint);
            }

            // 5) Work chores
            switch (_chore)
            {
                case Chore.Repair:
                    return Localization.instance.Localize("$hearthwife_busy_repair");
                case Chore.Collect:
                    return Localization.instance.Localize(
                        _carryingLoot ? "$hearthwife_busy_deposit" : "$hearthwife_busy_collect");
                case Chore.Fish:
                    return Localization.instance.Localize("$hearthwife_busy_fish");
                case Chore.Cook:
                    return Localization.instance.Localize("$hearthwife_busy_cook");
                case Chore.Cauldron:
                    return Localization.instance.Localize("$hearthwife_busy_cauldron");
                case Chore.Fire:
                    return Localization.instance.Localize("$hearthwife_busy_fire");
                case Chore.Mead:
                    return Localization.instance.Localize("$hearthwife_busy_mead");
                case Chore.Smelt:
                    return Localization.instance.Localize("$hearthwife_busy_smelt");
                case Chore.Farm:
                    return Localization.instance.Localize("$hearthwife_busy_farm");
                case Chore.Forage:
                    return Localization.instance.Localize("$hearthwife_busy_forage");
                case Chore.Idle:
                    break;
                default:
                    break;
            }

            // 6) Idle emotes only (music vs calm rest — never confuse with fire sit)
            var emoteHover = WifeEmotes.IdleEmoteHoverToken(gameObject);
            if (!string.IsNullOrEmpty(emoteHover))
            {
                return Localization.instance.Localize(emoteHover);
            }

            return Localization.instance.Localize("$hearthwife_status_idle");
        }

        public string GetHoverName()
        {
            return _home != null ? _home.WifeDisplayName : Localization.instance.Localize("$hearthwife_wife_name");
        }

        public float GetHoverOffset() => 0.6f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user == null)
            {
                return false;
            }

            // Night sleep / bed attach — E does nothing social (not organic to chat while asleep).
            if (IsAsleepQuiet())
            {
                return true;
            }

            if (Time.time < _talkCooldown)
            {
                return true;
            }

            _talkCooldown = Time.time + 2.5f;

            // Sitting: soft bubble only — never stand up / wave (keeps attach_chair).
            if (_sitting || _playerAttached)
            {
                WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.NextLine()));
                MarkSocialQuiet(8f);
                return true;
            }

            // Awake & free: stop chore, face player, greet.
            InterruptToAttendPlayer();

            // Hold face through greet; same window as idle linger (avoids stroll fight).
            _facePlayerUntil = Mathf.Max(_facePlayerUntil, Time.time + 6f);
            FacePlayerBody(user as Player ?? Player.m_localPlayer, snap: true);

            EnsureHeadBone();
            // Refresh bind from current idle pose right before looking.
            if (_headBone != null)
            {
                _headBaseLocal = _headBone.localRotation;
            }

            try
            {
                var hp = ((Character)user).GetHeadPoint();
                var to = hp - (transform.position + Vector3.up * 1.5f);
                if (to.sqrMagnitude > 0.001f)
                {
                    _headLookTargetDir = to.normalized;
                    _headLookCurrentDir = _headLookTargetDir;
                }

                _headLookTargetWeight = 1f;
                _headLookWeight = 1f;
            }
            catch
            {
                _headLookTargetWeight = 1f;
                _headLookWeight = 1f;
            }

            // Stop then greet next frame so emote_stop does not eat the new oneshot (Fires).
            StartCoroutine(GreetEmoteNextFrame());
            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.NextLine()));
            return true;
        }

        private IEnumerator GreetEmoteNextFrame()
        {
            WifeEmotes.Stop(gameObject);
            yield return null;
            yield return null;
            if (IsAsleepQuiet() || _sitting || _playerAttached)
            {
                yield break;
            }

            if (!TryOfferSocialEmote(WifeEmotes.PickGreetEmote(), 10f))
            {
                MarkSocialQuiet(6f);
            }
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}

