using System.Collections;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Homestead chores: fire, cook, mead, sit, nap.
    /// </summary>
    public partial class WifeAgent
    {
        private void ExitSitting()
        {
            if (!_sitting && !_sleepAttached && !_playerAttached)
            {
                return;
            }

            WifeEmotes.Stop(gameObject);

            if (_occupiedProp != null)
            {
                _lastSitChair = _occupiedProp;
                WifeOccupancy.Release(_occupiedProp, this);
                _occupiedProp = null;
            }

            // Player-parity detach (Character.AttachStop is empty on Humanoid).
            // Vanilla Chair detachOffset is (0, 0.5, 0) — only lifts; step back so she
            // (and the player) are not jammed inside the table/bench collider.
            StopPlayerAttach(stepAwayFromSeat: true);

            // Clear residual sit/attach anim (Fires ForceAnimationStateReset).
            try
            {
                var zanim = GetComponent<ZSyncAnimation>();
                zanim?.SetTrigger("emote_stop");
                zanim?.SetBool("sleeping", false);
                zanim?.SetBool("attach_chair", false);
                zanim?.SetBool("attach_bed", false);
                var anim = GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    anim.SetTrigger("emote_stop");
                    anim.SetBool("sleeping", false);
                    anim.SetBool("emote_sit", false);
                    anim.SetBool("attach_chair", false);
                    anim.SetBool("attach_bed", false);
                }
            }
            catch
            {
            }

            if (_cc == null)
            {
                _cc = GetComponent<CharacterController>();
            }

            if (_cc != null)
            {
                _cc.enabled = _ccWasEnabled;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            _sitting = false;
            _sitTarget = null;
            _sitFire = null;
            // Fires: chairSitCooldown = 60s before sitting again; linger before next beat.
            _sitCooldownUntil = Time.time + 60f;
            _idlePauseUntil = Mathf.Max(_idlePauseUntil, Time.time + Random.Range(14f, 24f));
            SilenceAiMove();
            SetWalkAnim(0f);
            RefreshRandomAnimationGate();
        }

        /// <summary>Fires-style chair sit: walk close, then AttachStart like the player.</summary>
        private void EnterSitVisual(Chair chair)
        {
            if (chair == null)
            {
                EndOwnedChore(1f);
                return;
            }

            StartCoroutine(AttachToChairRoutine(chair));
        }

        private IEnumerator AttachToChairRoutine(Chair chair)
        {
            _hasTarget = false;
            SilenceAiMove();
            UnequipTool();
            SetWalkAnim(0f);
            WifeEmotes.Stop(gameObject);

            if (chair == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            var attach = chair.m_attachPoint != null ? chair.m_attachPoint : chair.transform;
            if (attach == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            // Must be near the seat — never AttachStart from across the base (teleport blink).
            // Vanilla Chair.m_useDistance is often ~2; Fires uses useDistance * 1.5.
            var useDist = 2.8f;
            try
            {
                if (chair.m_useDistance > 0.1f)
                {
                    useDist = Mathf.Max(2.4f, chair.m_useDistance * 1.75f);
                }
            }
            catch
            {
            }

            var dist = Vector3.Distance(transform.position, attach.position);
            if (dist > useDist)
            {
                // Too far — walk closer, keep Sit chore (do NOT abort to idle).
                Jotunn.Logger.LogInfo(
                    $"Hearthwife: sit retry closer (dist={dist:F2} > {useDist:F2})");
                var closer = ApproachPoint(attach.position, 0.2f);
                SnapToPieceOrTerrain(ref closer, attach.position.y);
                _sitTarget = chair;
                _chore = Chore.Sit;
                SetTarget(closer);
                yield break;
            }

            try
            {
                if (chair.IsInUse())
                {
                    EndOwnedChore(1f);
                    yield break;
                }
            }
            catch
            {
            }

            var sitFor = Random.Range(28f, 55f);
            if (!WifeOccupancy.TryOccupy(chair, this, sitFor + 15f))
            {
                EndOwnedChore(1f);
                yield break;
            }

            _occupiedProp = chair;

            // Freeze physics for two fixed frames (Fires AttachToChairCoroutine).
            _cc = GetComponent<CharacterController>();
            if (_cc != null)
            {
                _ccWasEnabled = _cc.enabled;
                _cc.enabled = false;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (chair == null)
            {
                WifeOccupancy.Release(_occupiedProp, this);
                _occupiedProp = null;
                if (_cc != null)
                {
                    _cc.enabled = _ccWasEnabled;
                }

                EndOwnedChore(1f);
                yield break;
            }

            try
            {
                if (chair.IsInUse())
                {
                    WifeOccupancy.Release(chair, this);
                    _occupiedProp = null;
                    if (_cc != null)
                    {
                        _cc.enabled = _ccWasEnabled;
                    }

                    EndOwnedChore(1f);
                    yield break;
                }
            }
            catch
            {
            }

            transform.SetPositionAndRotation(attach.position, attach.rotation);
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            yield return null;

            // Chair default anim is attach_chair (vanilla Chair..ctor). Use piece override if set.
            var animName = string.IsNullOrEmpty(chair.m_attachAnimation)
                ? "attach_chair"
                : chair.m_attachAnimation;

            // Vanilla Chair.Interact → Player.AttachStart. Character.AttachStart is empty —
            // wife is Humanoid-only, so use Player-parity attach (SetBool + UpdateAttach).
            var ok = StartPlayerAttach(
                attach,
                chair.gameObject,
                false,
                false,
                animName,
                chair.m_detachOffset);

            if (!ok)
            {
                WifeOccupancy.Release(chair, this);
                _occupiedProp = null;
                if (_cc != null)
                {
                    _cc.enabled = _ccWasEnabled;
                }

                EndOwnedChore(1f);
                yield break;
            }

            _sitting = true;
            _sitTarget = null;
            _chore = Chore.Sit;
            _choreStartedAt = Time.time;
            _idleActionUntil = Time.time + sitFor;
            RefreshRandomAnimationGate();
            // Hover shows Descansando only while _sitting; no chat spam.
        }

        private bool TryBeginSit(WifeHome home)
        {
            if (Time.time < _sitCooldownUntil)
            {
                return false;
            }

            Chair best = null;
            var bestDist = home.Radius;
            foreach (var chair in Object.FindObjectsByType<Chair>(FindObjectsSortMode.None))
            {
                if (chair == null || !home.IsInside(chair.transform.position))
                {
                    continue;
                }

                // Just stood up from this seat — skip until cooldown (sit/stand loop).
                if (_lastSitChair != null && ReferenceEquals(chair, _lastSitChair))
                {
                    continue;
                }

                if (chair.transform.position.y > home.HomePosition.y + 2.2f)
                {
                    continue;
                }

                if (chair.m_attachPoint == null)
                {
                    continue;
                }

                if (!WifeOccupancy.IsFree(chair, this))
                {
                    continue;
                }

                try
                {
                    if (chair.IsInUse())
                    {
                        continue;
                    }
                }
                catch
                {
                }

                var d = Vector3.Distance(chair.transform.position, transform.position);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = chair;
            }

            if (best == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Sit);
            _sitTarget = best;
            // Walk almost onto the attach point (arrive 0.85m) — old 0.55 standoff + 2m arrive
            // left her too far and AttachToChair aborted.
            var attach = best.m_attachPoint != null ? best.m_attachPoint : best.transform;
            var approach = ApproachPoint(attach.position, 0.2f);
            SnapToPieceOrTerrain(ref approach, attach.position.y);
            if (approach.y > home.HomePosition.y + 2.2f)
            {
                approach = attach.position;
            }

            SetTarget(approach);
            if (HorizontalDistance(transform.position, _target) < 0.9f)
            {
                OnArrived();
            }

            return true;
        }

        /// <summary>Menu TESTE: walk to nearest chair and sit (Player-parity attach).</summary>
        internal string ForceSitNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            if (_sitting && _playerAttached)
            {
                return "Já sentada (" + _attachAnimation + ").";
            }

            ExitSitting();
            _sitCooldownUntil = 0f;
            if (!TryBeginSit(_home))
            {
                return "Nenhuma cadeira livre no círculo (Chair com attach).";
            }

            return "Indo sentar… olhe o hover nela.";
        }

        /// <summary>Menu TESTE: stand up from chair/bed attach.</summary>
        internal string ForceStandNow()
        {
            if (!_sitting && !_sleeping && !_playerAttached)
            {
                return "Ela não está sentada/dormindo.";
            }

            ExitSitting();
            if (_sleeping)
            {
                ExitSleepVisual();
            }

            EndOwnedChore(3f);
            _idlePauseUntil = Time.time + 4f;
            // Short cooldown so you can re-test sit quickly (ambient still uses 60s).
            _sitCooldownUntil = Time.time + 2f;
            return "Levantou.";
        }

        internal string ForceNapNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            if (!_home.HasAssignedBed)
            {
                return "Defina a cama no menu Lar primeiro.";
            }

            ExitSitting();
            if (_sleeping)
            {
                ExitSleepVisual();
            }

            if (!TryBeginNap(_home))
            {
                return "Não conseguiu iniciar o cochilo.";
            }

            return "Indo cochilar na cama…";
        }

        internal string ForceSitFireNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            ExitSitting();
            _sitCooldownUntil = 0f;
            if (!TrySitByFire(_home))
            {
                return "Nenhuma fogueira/lareira acesa livre no círculo.";
            }

            return "Indo para o fogo…";
        }

        internal string ForceMusicNow()
        {
            if (_sleeping)
            {
                return "Acorda ela primeiro (TESTE: acordar).";
            }

            if (_sitting)
            {
                return "Levante antes (TESTE: levantar).";
            }

            WifeEmotes.PlayMusic(gameObject);
            Notify("$hearthwife_busy_music");
            _idlePauseUntil = Time.time + Mathf.Max(8f, WifeEmotes.DurationFor("emote_dance"));
            return "Música / dança agora.";
        }

        internal string ForceAffectionNow()
        {
            if (_sleeping || _sitting)
            {
                return "Levante / acorde primeiro pra ver o carinho.";
            }

            var player = Player.m_localPlayer;
            if (player != null)
            {
                var look = player.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(look.normalized);
                }
            }

            StartCoroutine(AffectionEmoteNextFrame());
            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.AffectionLine()));
            return "Carinho (emote) agora.";
        }

        internal string ForceMorningNow()
        {
            if (_sleeping)
            {
                ExitSleepVisual();
                EndOwnedChore(1f);
            }

            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.MorningLine()));
            WifeEmotes.Play(gameObject, "emote_wave");
            _lastMorningGreetAt = Time.time;
            _wantsMorningGreet = false;
            return "Bom dia (frase + wave).";
        }

        internal string ForceSleepNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            BeginSleep(_home);
            return _home.HasAssignedBed
                ? "Indo dormir na cama…"
                : "Sem cama — indo ao ídolo dormir em pé.";
        }

        internal string ForceWakeNow()
        {
            if (!_sleeping && !_sitting && !_playerAttached)
            {
                return "Ela já está acordada.";
            }

            ExitSitting();
            if (_sleeping)
            {
                ExitSleepVisual();
            }

            EndOwnedChore(2f);
            _idlePauseUntil = Time.time + 3f;
            _wantsMorningGreet = false;
            return "Acordou / levantou.";
        }

        internal string ForceRestedNow()
        {
            if (_character == null)
            {
                return "Sem Character.";
            }

            if (!NearLitFire(5.5f))
            {
                return "Chegue ela perto de um fogo aceso (ou TESTE fogueira).";
            }

            TryApplyRested();
            return "Buff Descansado aplicado.";
        }

        /// <summary>Clear wait timers so the next chore/think can run immediately.</summary>
        internal string ForceSkipWaitNow()
        {
            _choreCooldownUntil = 0f;
            _idlePauseUntil = 0f;
            _actionUntil = 0f;
            _sitCooldownUntil = 0f;
            _home?.RequestThinkSoon();
            return "Esperas zeradas — ela decide já.";
        }

        /// <summary>Abort current beat and clear waits before a forced chore.</summary>
        private void PrepareForcedChore()
        {
            ExitSitting();
            if (_sleeping)
            {
                ExitSleepVisual();
            }

            StopWorkRoutine();
            if (_chore != Chore.Idle)
            {
                EndOwnedChore(0f);
            }

            _choreCooldownUntil = 0f;
            _idlePauseUntil = 0f;
            _actionUntil = 0f;
            _sitCooldownUntil = 0f;
            _home?.RequestThinkSoon();
        }

        internal string ForceFireNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            PrepareForcedChore();
            if (!TryBeginFire(_home))
            {
                return "Sem fogueira/tocha baixa + combustível (ídolo ou baús com reserva).";
            }

            return "Forçando: cuidar do fogo…";
        }

        internal string ForceCollectNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            PrepareForcedChore();
            if (_home.IsStorageFull)
            {
                return "Baú do ídolo cheio — libera espaço.";
            }

            if (!TryBeginCollect(_home))
            {
                return "Nada pra recolher no círculo (arbusto ou chão, com filtros).";
            }

            return "Forçando: recolher…";
        }

        internal string ForceRepairNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            PrepareForcedChore();
            if (!TryBeginRepair(_home))
            {
                return "Nenhuma peça danificada (ou sem martelo) no círculo.";
            }

            return "Forçando: reparar…";
        }

        internal string ForceCookCollectNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            PrepareForcedChore();
            if (!TryBeginCookCollect(_home))
            {
                return "Nenhuma comida pronta no fogão (no círculo).";
            }

            return "Forçando: pegar comida do fogão…";
        }

        internal string ForceCookNow()
        {
            if (_home == null)
            {
                return "Sem lar.";
            }

            PrepareForcedChore();
            if (!TryBeginCook(_home))
            {
                return "Sem cru no baú do ídolo, fogão cheio, sem fogo embaixo da grelha, ou sem espaço.";
            }

            return "Forçando: pôr comida no fogão…";
        }

        internal string GetTestStatusLine()
        {
            var bits = new System.Text.StringBuilder();
            bits.Append("Estado: ").Append(_chore);
            if (_sitting)
            {
                bits.Append(" | SENTADA");
            }

            if (_playerAttached)
            {
                bits.Append(" | attach=").Append(_attachAnimation);
            }
            else if (_sitting)
            {
                bits.Append(" | attach=FALHOU");
            }

            if (_hasTarget)
            {
                bits.Append(" | andando");
            }

            if (_sleeping)
            {
                bits.Append(" | dormindo");
            }

            return bits.ToString();
        }

        private bool TryBeginNap(WifeHome home)
        {
            if (!home.HasAssignedBed)
            {
                return false;
            }

            ExitSitting();
            BeginOwnedChore(Chore.Nap);
            var pos = home.GetSleepPosition(out var rot);
            var walk = GetBedWalkTarget(home, out _);
            SetTarget(walk);
            Notify("$hearthwife_busy_nap");

            if (HorizontalDistance(transform.position, walk) < 1.65f ||
                HorizontalDistance(transform.position, pos) < 1.65f)
            {
                _hasTarget = false;
                EnterSleepVisual(pos, rot);
                _idleActionUntil = Time.time + Random.Range(18f, 35f);
            }

            return true;
        }

        private bool TryBeginFire(WifeHome home)
        {
            Fireplace best = null;
            var bestDist = home.Radius;

            foreach (var fire in Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None))
            {
                if (fire == null || fire.m_nview == null || !fire.m_nview.IsValid())
                {
                    continue;
                }

                if (!home.IsInside(fire.transform.position))
                {
                    continue;
                }

                if (fire.m_infiniteFuel || !fire.m_canRefill)
                {
                    continue;
                }

                // FiresUnified: top up under ~70% fuel. Torches/sconces (low maxFuel) use 50%.
                var fuel = fire.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel, fire.m_startFuel);
                var threshold = fire.m_maxFuel <= 4f ? 0.5f : 0.7f;
                if (fuel >= fire.m_maxFuel * threshold)
                {
                    continue;
                }

                if (!WifeOccupancy.IsFree(fire, this))
                {
                    continue;
                }

                var fuelName = fire.m_fuelItem != null
                    ? fire.m_fuelItem.gameObject.name
                    : "Wood";
                if (!HomeHasSpendableFireFuel(home, fuelName))
                {
                    continue;
                }

                var d = Vector3.Distance(fire.transform.position, home.HomePosition);
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

            BeginOwnedChore(Chore.Fire);
            _fireTarget = best;
            WifeOccupancy.TryOccupy(best, this, 45f);
            SetTarget(best.transform.position);
            Notify("$hearthwife_busy_fire");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        private IEnumerator TendFireRoutine(Fireplace fire)
        {
            BeginOwnedChore(Chore.Fire);
            WifeEmotes.Stop(gameObject);

            if (fire == null || !WifeOccupancy.TryOccupy(fire, this, 40f))
            {
                EndOwnedChore(1f);
                yield break;
            }

            _occupiedProp = fire;
            EnterStationaryWorkHold(fire.transform.position, 8f);

            var fuelName = fire.m_fuelItem != null ? fire.m_fuelItem.gameObject.name : "Wood";
            // Torches often use Resin — also accept Wood as soft fallback already via TryConsumeFuel.
            var added = 0;
            var phaseDeadline = Time.time + 20f;
            while (added < 4 && _home != null && Time.time < phaseDeadline && _chore == Chore.Fire)
            {
                HoldStationaryWorkTick(2f);
                var fuelNow = fire.m_nview != null && fire.m_nview.IsValid()
                    ? fire.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel, 0f)
                    : 0f;
                if (fuelNow >= fire.m_maxFuel * 0.95f)
                {
                    break;
                }

                if (!TryConsumeFireFuel(_home, fuelName))
                {
                    break;
                }

                try
                {
                    fire.m_nview.InvokeRPC("RPC_AddFuel");
                }
                catch
                {
                    try
                    {
                        fire.AddFuel(1f);
                    }
                    catch
                    {
                    }
                }

                PlayInteractAnimation(fire.transform.position);
                added++;
                yield return new WaitForSeconds(0.85f);
            }

            WifeOccupancy.Release(fire, this);
            if (_occupiedProp == fire)
            {
                _occupiedProp = null;
            }

            ForceClearRightHand();
            EndOwnedChore(2f);
        }

        /// <summary>
        /// Reclaim only the grill she filled — never snatch the player's cooking.
        /// </summary>
        private bool TryBeginWifeCookReclaim(WifeHome home)
        {
            if (_wifeCookStation == null)
            {
                return false;
            }

            var station = _wifeCookStation;
            try
            {
                if (station.gameObject == null ||
                    station.m_nview == null ||
                    !station.m_nview.IsValid() ||
                    !home.IsInside(station.transform.position))
                {
                    _wifeCookStation = null;
                    return false;
                }
            }
            catch
            {
                _wifeCookStation = null;
                return false;
            }

            if (!StationHasDoneOrBurntFood(station) && !StationHasAnyFood(station))
            {
                _wifeCookStation = null;
                return false;
            }

            BeginOwnedChore(Chore.Cook);
            _cookTarget = station;
            WifeOccupancy.TryOccupy(station, this, 35f);
            SetTarget(station.transform.position);
            Notify("$hearthwife_busy_cook");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        /// <summary>Legacy name — only reclaim her own grill.</summary>
        private bool TryBeginCookCollect(WifeHome home) => TryBeginWifeCookReclaim(home);

        /// <summary>Done or burnt food sitting on the grill (ready to take).</summary>
        private static bool StationHasDoneOrBurntFood(CookingStation station)
        {
            if (station == null)
            {
                return false;
            }

            try
            {
                if (station.HaveDoneItem())
                {
                    return true;
                }
            }
            catch
            {
            }

            if (station.m_nview == null || !station.m_nview.IsValid())
            {
                return false;
            }

            var zdo = station.m_nview.GetZDO();
            var n = station.m_slots != null ? station.m_slots.Length : 0;
            for (var i = 0; i < n; i++)
            {
                var name = zdo.GetString("slot" + i, "");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // Villages: slotstatus >= 1 → done or burnt.
                if (zdo.GetInt("slotstatus" + i, 0) >= 1)
                {
                    return true;
                }

                try
                {
                    if (station.IsItemDone(name))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryBeginCook(WifeHome home)
        {
            var inv = home.Storage?.GetInventory();
            if (inv == null)
            {
                return false;
            }

            CookingStation best = null;
            var bestDist = home.Radius;
            ItemDrop.ItemData cookable = null;

            foreach (var station in Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None))
            {
                if (station == null || station.m_nview == null || !station.m_nview.IsValid())
                {
                    continue;
                }

                if (!home.IsInside(station.transform.position))
                {
                    continue;
                }

                // Player already using this grill — leave it alone (unless it's her claim).
                if (StationHasAnyFood(station) && station != _wifeCookStation)
                {
                    continue;
                }

                if (!IsCookingStationUsable(station))
                {
                    continue;
                }

                if (!WifeOccupancy.IsFree(station, this))
                {
                    continue;
                }

                try
                {
                    if (station.IsStationFull())
                    {
                        continue;
                    }
                }
                catch
                {
                }

                var item = FindCookableForRecipe(station, inv, home.CookRecipeId);
                if (item == null)
                {
                    continue;
                }

                var d = Vector3.Distance(station.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = station;
                cookable = item;
            }

            if (best == null || cookable == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Cook);
            _cookTarget = best;
            WifeOccupancy.TryOccupy(best, this, 90f);
            SetTarget(best.transform.position);
            Notify("$hearthwife_busy_cook");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        private IEnumerator CookRoutine(CookingStation station)
        {
            BeginOwnedChore(Chore.Cook);
            WifeEmotes.Stop(gameObject);

            if (station != null && WifeOccupancy.TryOccupy(station, this, 90f))
            {
                _occupiedProp = station;
            }

            EnterStationaryWorkHold(station != null ? station.transform.position : transform.position, 20f);

            var inv = _home?.Storage?.GetInventory();
            if (inv == null || station == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            // Never touch the player's meat — only scoop if this is her claimed grill.
            var wifeOwns = station == _wifeCookStation;
            if (StationHasAnyFood(station) && !wifeOwns)
            {
                ForceClearRightHand();
                EndOwnedChore(1f);
                yield break;
            }

            var hadDone = wifeOwns && StationHasDoneOrBurntFood(station);
            if (hadDone)
            {
                PlayInteractAnimation(station.transform.position);
                RemoveDoneFoodFromSlots(station);
                yield return new WaitForSeconds(0.4f);
                ScoopCookProducts(station, inv);
                yield return new WaitForSeconds(0.25f);
            }

            // Fuel the station if needed.
            if (station.m_useFuel)
            {
                var needFuel = false;
                try
                {
                    needFuel = station.GetFuel() < 1f;
                }
                catch
                {
                }

                if (needFuel && TryConsumeFuel(inv))
                {
                    try
                    {
                        station.m_nview.InvokeRPC("RPC_AddFuel");
                    }
                    catch
                    {
                        try
                        {
                            station.m_nview.InvokeRPC("AddFuel");
                        }
                        catch
                        {
                        }
                    }

                    PlayInteractAnimation(station.transform.position);
                    yield return new WaitForSeconds(0.75f);
                }
            }

            if (!IsCookingStationUsable(station))
            {
                if (!hadDone)
                {
                    Notify("$hearthwife_cook_need_fire");
                }

                ForceClearRightHand();
                EndOwnedChore(2f);
                yield break;
            }

            // Fill every free slot while chest still has cookable food.
            var placed = 0;
            var waitSeconds = 28f;
            var maxSlots = station.m_slots != null ? station.m_slots.Length : 5;
            for (var i = 0; i < maxSlots; i++)
            {
                try
                {
                    if (station.IsStationFull())
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }

                var item = FindCookableForRecipe(station, inv, _home.CookRecipeId);
                if (item == null)
                {
                    break;
                }

                var prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (!TryPlaceFoodOnStation(station, inv, item))
                {
                    if (placed == 0 && !hadDone)
                    {
                        Notify("$hearthwife_cook_place_fail");
                    }

                    break;
                }

                placed++;
                _wifeCookStation = station;
                waitSeconds = Mathf.Max(waitSeconds, EstimateCookWaitSeconds(station, prefabName));
                PlayInteractAnimation(station.transform.position);
                HoldStationaryWorkTick(2f);
                yield return new WaitForSeconds(0.4f);
            }

            if (placed == 0 && !hadDone)
            {
                // Nothing to do on this grill.
                ForceClearRightHand();
                EndOwnedChore(1f);
                yield break;
            }

            if (placed > 0 || StationHasAnyFood(station))
            {
                // Face grill once — HoldStationaryWorkTick only (no FacePoint spam).
                EnterStationaryWorkHold(station.transform.position, 2f);
                var deadline = Time.time + Mathf.Clamp(waitSeconds, 18f, 70f);
                while (Time.time < deadline)
                {
                    HoldStationaryWorkTick(2f);
                    _choreStartedAt = Time.time;
                    WifeOccupancy.TryOccupy(station, this, 25f);

                    if (StationHasDoneOrBurntFood(station))
                    {
                        PlayInteractAnimation(station.transform.position);
                        RemoveDoneFoodFromSlots(station);
                        yield return new WaitForSeconds(0.4f);
                        ScoopCookProducts(station, inv);

                        // Free slots opened — keep filling while meat remains.
                        for (var i = 0; i < maxSlots; i++)
                        {
                            try
                            {
                                if (station.IsStationFull())
                                {
                                    break;
                                }
                            }
                            catch
                            {
                                break;
                            }

                            var more = FindCookableForRecipe(station, inv, _home.CookRecipeId);
                            if (more == null)
                            {
                                break;
                            }

                            var prefabName = more.m_dropPrefab != null ? more.m_dropPrefab.name : null;
                            if (!TryPlaceFoodOnStation(station, inv, more))
                            {
                                break;
                            }

                            placed++;
                            _wifeCookStation = station;
                            waitSeconds = Mathf.Max(waitSeconds, EstimateCookWaitSeconds(station, prefabName));
                            PlayInteractAnimation(station.transform.position);
                            HoldStationaryWorkTick(2f);
                            yield return new WaitForSeconds(0.35f);
                        }

                        deadline = Time.time + Mathf.Clamp(waitSeconds, 18f, 70f);
                        if (!StationHasAnyFood(station))
                        {
                            break;
                        }

                        continue;
                    }

                    // Player took raw/done food off the rack — leave quietly.
                    if (!StationHasAnyFood(station))
                    {
                        ForceClearRightHand();
                        EndOwnedChore(1f);
                        yield break;
                    }

                    if (!IsCookingStationUsable(station))
                    {
                        Notify("$hearthwife_cook_need_fire");
                        ForceClearRightHand();
                        EndOwnedChore(2f);
                        yield break;
                    }

                    yield return new WaitForSeconds(0.45f);
                }

                if (StationHasDoneOrBurntFood(station))
                {
                    PlayInteractAnimation(station.transform.position);
                    RemoveDoneFoodFromSlots(station);
                    yield return new WaitForSeconds(0.4f);
                    ScoopCookProducts(station, inv);
                }
            }

            // Clear claim when her grill is empty again.
            if (_wifeCookStation == station && !StationHasAnyFood(station))
            {
                _wifeCookStation = null;
            }

            ForceClearRightHand();
            EndOwnedChore(2f);
        }

        /// <summary>Any item still on a cook slot (raw, done, or burnt).</summary>
        private static bool StationHasAnyFood(CookingStation station)
        {
            if (station?.m_nview == null || !station.m_nview.IsValid())
            {
                return false;
            }

            var zdo = station.m_nview.GetZDO();
            var n = station.m_slots != null ? station.m_slots.Length : 0;
            for (var i = 0; i < n; i++)
            {
                if (!string.IsNullOrEmpty(zdo.GetString("slot" + i, "")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Cook time for placed item + short buffer before burn window.</summary>
        private static float EstimateCookWaitSeconds(CookingStation station, string fromPrefab)
        {
            var cook = 25f;
            if (station?.m_conversion == null || string.IsNullOrEmpty(fromPrefab))
            {
                return cook + 12f;
            }

            try
            {
                foreach (var c in station.m_conversion)
                {
                    if (c?.m_from == null)
                    {
                        continue;
                    }

                    if (c.m_from.name == fromPrefab)
                    {
                        cook = Mathf.Max(8f, c.m_cookTime);
                        break;
                    }
                }
            }
            catch
            {
            }

            // Done at cookTime; burnt after another cookTime — pick ASAP after done.
            return cook + 12f;
        }

        /// <summary>
        /// Wood/iron cooking rack (grelha / bancada). Needs lit fire under it when m_requireFire.
        /// </summary>
        private static bool IsCookingStationUsable(CookingStation station)
        {
            if (station == null)
            {
                return false;
            }

            try
            {
                if (station.m_requireFire &&
                    !EffectArea.IsPointPlus025InsideBurningArea(station.transform.position))
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        /// <summary>
        /// Player path: UseItem → CookItem → RPC_AddItem. Never lose raw food on failure.
        /// </summary>
        private bool TryPlaceFoodOnStation(
            CookingStation station,
            Inventory chest,
            ItemDrop.ItemData fromChest)
        {
            if (station == null || chest == null || fromChest == null || _humanoid == null)
            {
                return false;
            }

            if (station.m_nview == null || !station.m_nview.IsValid())
            {
                return false;
            }

            var prefab = fromChest.m_dropPrefab;
            if (prefab == null)
            {
                return false;
            }

            var prefabName = prefab.name;
            try
            {
                if (!station.IsItemAllowed(fromChest) && !station.IsItemAllowed(prefabName))
                {
                    return false;
                }

                if (station.IsStationFull())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            var wifeInv = _humanoid.GetInventory();
            if (wifeInv == null)
            {
                return false;
            }

            chest.RemoveItem(fromChest, 1);
            var piece = fromChest.Clone();
            piece.m_stack = 1;
            if (!wifeInv.AddItem(piece))
            {
                chest.AddItem(piece);
                return false;
            }

            ItemDrop.ItemData held = null;
            foreach (var it in wifeInv.GetAllItems())
            {
                if (it?.m_dropPrefab == null || it.m_stack < 1)
                {
                    continue;
                }

                if (it.m_dropPrefab.name == prefabName)
                {
                    held = it;
                    break;
                }
            }

            if (held == null)
            {
                return false;
            }

            var ok = false;
            try
            {
                ok = station.UseItem(_humanoid, held);
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                return true;
            }

            // Fallback: correct RPC name is RPC_AddItem (old code called "AddItem" and lost the meat).
            try
            {
                if (station.IsItemAllowed(prefabName))
                {
                    wifeInv.RemoveItem(held, 1);
                    station.m_nview.InvokeRPC("RPC_AddItem", prefabName, false);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (held.m_stack > 0)
                {
                    wifeInv.RemoveItem(held, 1);
                }
            }
            catch
            {
            }

            var back = fromChest.Clone();
            back.m_stack = 1;
            if (!chest.AddItem(back))
            {
                try
                {
                    ItemDrop.DropItem(
                        back,
                        1,
                        station.transform.position + Vector3.up * 0.3f,
                        Quaternion.identity);
                }
                catch
                {
                }
            }

            return false;
        }

        private static void CollectDoneFood(CookingStation station, Inventory inv)
        {
            RemoveDoneFoodFromSlots(station);
            ScoopCookProducts(station, inv);
        }

        private static void RemoveDoneFoodFromSlots(CookingStation station)
        {
            if (station == null || station.m_nview == null || !station.m_nview.IsValid())
            {
                return;
            }

            if (!StationHasDoneOrBurntFood(station))
            {
                return;
            }

            var spawn = station.m_spawnPoint != null
                ? station.m_spawnPoint.position
                : station.transform.position + station.transform.forward * 0.6f + Vector3.up * 0.2f;

            // Vanilla: RPC_RemoveDoneItem(userPoint, amount) — finds first done slot itself.
            // Passing slot index as amount was wrong (slot 0 → amount 0 cleared with no drop).
            var slotCount = station.m_slots != null ? station.m_slots.Length : 5;
            for (var n = 0; n < slotCount; n++)
            {
                if (!StationHasDoneOrBurntFood(station))
                {
                    break;
                }

                try
                {
                    station.m_nview.InvokeRPC("RPC_RemoveDoneItem", spawn, 1);
                }
                catch
                {
                    break;
                }
            }
        }

        private static void ScoopCookProducts(CookingStation station, Inventory inv)
        {
            if (station == null || inv == null)
            {
                return;
            }

            foreach (var drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || drop.m_itemData == null)
                {
                    continue;
                }

                if (Vector3.Distance(drop.transform.position, station.transform.position) > 4.5f)
                {
                    continue;
                }

                if (!IsStationCookProduct(station, drop))
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

        private static bool IsStationCookProduct(CookingStation station, ItemDrop drop)
        {
            if (station?.m_conversion == null || drop?.m_itemData == null)
            {
                return false;
            }

            var name = drop.m_itemData.m_dropPrefab != null
                ? drop.m_itemData.m_dropPrefab.name
                : drop.gameObject.name;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var conv in station.m_conversion)
            {
                if (conv?.m_to == null)
                {
                    continue;
                }

                if (conv.m_to.name == name ||
                    (conv.m_to.m_itemData?.m_shared != null &&
                     conv.m_to.m_itemData.m_shared.m_name == drop.m_itemData.m_shared.m_name))
                {
                    return true;
                }
            }

            // Burnt leftovers from cooking stations.
            return name.IndexOf("Burnt", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Cooked", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryBeginMead(WifeHome home)
        {
            var inv = home.Storage?.GetInventory();
            if (inv == null)
            {
                return false;
            }

            Fermenter best = null;
            var bestDist = home.Radius;
            var tap = false;

            foreach (var fer in Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None))
            {
                if (fer == null || fer.m_nview == null || !fer.m_nview.IsValid())
                {
                    continue;
                }

                if (!home.IsInside(fer.transform.position))
                {
                    continue;
                }

                Fermenter.Status status;
                try
                {
                    status = fer.GetStatus();
                }
                catch
                {
                    continue;
                }

                var useful = false;
                if (status == Fermenter.Status.Ready)
                {
                    useful = true;
                    tap = true;
                }
                else if (status == Fermenter.Status.Empty)
                {
                    var baseItem = fer.FindCookableItem(inv);
                    useful = baseItem != null;
                    tap = false;
                }

                if (!useful)
                {
                    continue;
                }

                var d = Vector3.Distance(fer.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = fer;
            }

            if (best == null)
            {
                return false;
            }

            BeginOwnedChore(Chore.Mead);
            _meadTarget = best;
            SetTarget(best.transform.position);
            Notify(tap ? "$hearthwife_busy_mead_tap" : "$hearthwife_busy_mead");
            if (HorizontalDistance(transform.position, _target) < 2.2f)
            {
                OnArrived();
            }

            return true;
        }

        private IEnumerator MeadRoutine(Fermenter fer)
        {
            BeginOwnedChore(Chore.Mead);
            if (fer == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            EnterStationaryWorkHold(fer.transform.position, 5f);

            var inv = _home?.Storage?.GetInventory();
            if (inv == null)
            {
                EndOwnedChore(1f);
                yield break;
            }

            Fermenter.Status status;
            try
            {
                status = fer.GetStatus();
            }
            catch
            {
                EndOwnedChore(1f);
                yield break;
            }

            if (status == Fermenter.Status.Ready)
            {
                try
                {
                    fer.m_nview.InvokeRPC("Tap");
                }
                catch
                {
                    fer.m_nview.InvokeRPC("RPC_Tap");
                }

                PlayInteractAnimation(fer.transform.position);
                yield return new WaitForSeconds(1.2f);

                // Scoop only mead/potion drops from the fermenter — not random nearby loot.
                foreach (var drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                {
                    if (drop == null || drop.m_itemData == null)
                    {
                        continue;
                    }

                    if (Vector3.Distance(drop.transform.position, fer.transform.position) > 3.5f)
                    {
                        continue;
                    }

                    var n = drop.m_itemData.m_dropPrefab != null
                        ? drop.m_itemData.m_dropPrefab.name
                        : drop.gameObject.name;
                    if (string.IsNullOrEmpty(n) ||
                        (n.IndexOf("Mead", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                         n.IndexOf("BarleyWine", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                         n.IndexOf("Potion", System.StringComparison.OrdinalIgnoreCase) < 0))
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
            else if (status == Fermenter.Status.Empty)
            {
                var baseItem = fer.FindCookableItem(inv);
                if (baseItem != null)
                {
                    var hash = baseItem.m_shared.m_name.GetStableHashCode();
                    if (baseItem.m_dropPrefab != null)
                    {
                        hash = baseItem.m_dropPrefab.name.GetStableHashCode();
                    }

                    inv.RemoveItem(baseItem, 1);
                    try
                    {
                        fer.m_nview.InvokeRPC("AddItem", hash, false);
                    }
                    catch
                    {
                        fer.m_nview.InvokeRPC("RPC_AddItem", hash, false);
                    }

                    PlayInteractAnimation(fer.transform.position);
                }
            }

            yield return new WaitForSeconds(1.5f);
            ForceClearRightHand();
            EndOwnedChore(2f);
        }

        private void FacePoint(Vector3 world)
        {
            var look = world - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                var n = look.normalized;
                transform.rotation = Quaternion.LookRotation(n);
                // Keep Character.m_lookDir aligned — otherwise UpdateLookDir fights snap rotation.
                try
                {
                    _character?.SetLookDir(n, 0f);
                }
                catch
                {
                }
            }
        }

        private static ItemDrop.ItemData FindCookableForRecipe(
            CookingStation station, Inventory inv, string recipeProduct)
        {
            if (station == null || inv == null)
            {
                return null;
            }

            // Prefer exact product if set.
            if (!string.IsNullOrEmpty(recipeProduct) && recipeProduct != "Any")
            {
                foreach (var conv in station.m_conversion)
                {
                    if (conv?.m_from == null || conv.m_to == null)
                    {
                        continue;
                    }

                    var toName = conv.m_to.name;
                    if (!toName.Equals(recipeProduct, System.StringComparison.OrdinalIgnoreCase) &&
                        !(conv.m_to.m_itemData?.m_shared?.m_name?.Contains(recipeProduct) ?? false))
                    {
                        continue;
                    }

                    var fromName = conv.m_from.name;
                    foreach (var item in inv.GetAllItems())
                    {
                        if (item?.m_dropPrefab == null || item.m_stack < 1)
                        {
                            continue;
                        }

                        if (item.m_dropPrefab.name == fromName)
                        {
                            return item;
                        }
                    }
                }
            }

            try
            {
                return station.FindCookableItem(inv);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasFuelNamed(Inventory inv, string fuelName)
        {
            return CountNamedFuel(inv, fuelName) > 0;
        }

        private static int CountNamedFuel(Inventory inv, string fuelName)
        {
            if (inv == null || string.IsNullOrEmpty(fuelName))
            {
                return 0;
            }

            var n = 0;
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                if (item.m_dropPrefab.name.Equals(fuelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    n += item.m_stack;
                }
            }

            return n;
        }

        private static bool TryConsumeNamedFuel(Inventory inv, string fuelName)
        {
            if (inv == null || string.IsNullOrEmpty(fuelName))
            {
                return false;
            }

            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                if (!item.m_dropPrefab.name.Equals(fuelName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                inv.RemoveItem(item, 1);
                return true;
            }

            return false;
        }

        private static int CountFuel(Inventory inv)
        {
            var n = 0;
            foreach (var item in inv.GetAllItems())
            {
                if (item?.m_dropPrefab == null || item.m_stack < 1)
                {
                    continue;
                }

                var name = item.m_dropPrefab.name;
                if (name == "Wood" || name == "RoundLog" || name == "FineWood" ||
                    name == "Resin" || name == "Coal")
                {
                    n += item.m_stack;
                }
            }

            return n;
        }

        private static bool TryConsumeFuel(Inventory inv)
        {
            foreach (var fuelName in new[] { "Wood", "RoundLog", "Resin", "FineWood", "Coal" })
            {
                if (TryConsumeNamedFuel(inv, fuelName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Totem: no reserve. Other chests in the home circle: leave home.FireWoodReserve.</summary>
        private static int GetFireFuelReserve(WifeHome home) =>
            home != null ? Mathf.Max(0, home.FireWoodReserve) : 20;

        private static bool InventoryHasSpendableFireFuel(Inventory inv, string preferredFuel, int reserve)
        {
            if (inv == null)
            {
                return false;
            }

            if (CountNamedFuel(inv, preferredFuel) > reserve)
            {
                return true;
            }

            foreach (var fuelName in new[] { "Wood", "RoundLog", "Resin", "FineWood", "Coal" })
            {
                if (CountNamedFuel(inv, fuelName) > reserve)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HomeHasSpendableFireFuel(WifeHome home, string preferredFuel)
        {
            if (home == null)
            {
                return false;
            }

            var totem = home.Storage?.GetInventory();
            if (InventoryHasSpendableFireFuel(totem, preferredFuel, 0))
            {
                return true;
            }

            var reserve = GetFireFuelReserve(home);
            foreach (var chest in EnumerateHomeFuelChests(home))
            {
                if (InventoryHasSpendableFireFuel(chest.GetInventory(), preferredFuel, reserve))
                {
                    return true;
                }
            }

            return false;
        }

        private static System.Collections.Generic.IEnumerable<Container> EnumerateHomeFuelChests(WifeHome home)
        {
            if (home == null)
            {
                yield break;
            }

            var totem = home.Storage;
            foreach (var chest in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (chest == null || chest == totem)
                {
                    continue;
                }

                if (!home.IsInside(chest.transform.position))
                {
                    continue;
                }

                if (chest.GetInventory() == null)
                {
                    continue;
                }

                yield return chest;
            }
        }

        /// <summary>Totem first (no reserve), then nearest home chest leaving FireWoodReserve.</summary>
        private static bool TryConsumeFireFuel(WifeHome home, string preferredFuel)
        {
            if (home == null)
            {
                return false;
            }

            var totem = home.Storage?.GetInventory();
            if (TryConsumeNamedFuel(totem, preferredFuel) || TryConsumeFuel(totem))
            {
                return true;
            }

            var reserve = GetFireFuelReserve(home);
            Container best = null;
            var bestDist = float.MaxValue;
            foreach (var chest in EnumerateHomeFuelChests(home))
            {
                var inv = chest.GetInventory();
                if (!InventoryHasSpendableFireFuel(inv, preferredFuel, reserve))
                {
                    continue;
                }

                var d = Vector3.Distance(chest.transform.position, home.HomePosition);
                if (d >= bestDist)
                {
                    continue;
                }

                bestDist = d;
                best = chest;
            }

            if (best == null)
            {
                return false;
            }

            var pick = best.GetInventory();
            if (CountNamedFuel(pick, preferredFuel) > reserve &&
                TryConsumeNamedFuel(pick, preferredFuel))
            {
                return true;
            }

            foreach (var fuelName in new[] { "Wood", "RoundLog", "Resin", "FineWood", "Coal" })
            {
                if (CountNamedFuel(pick, fuelName) > reserve &&
                    TryConsumeNamedFuel(pick, fuelName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
