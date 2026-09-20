using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Soft "come see" while the Visual menu tab is open — walk to idol porch, no teleport.
    /// </summary>
    public partial class WifeAgent
    {
        private bool _lookPresentActive;
        private Vector3 _lookPresentStand;
        private float _lookPresentRetargetAt;
        private float _lookPresentStartedAt;

        private const float LookPresentNearMeters = 7.5f;
        private const float LookPresentStandoff = 2.2f;
        private const float LookPresentArriveMeters = 1.6f;
        private const float LookPresentMaxSeconds = 180f;

        /// <summary>Menu Visual tab opened / look cycled — walk over if far; stay posed if near.</summary>
        internal void CallForLookPresent(WifeHome home)
        {
            if (home == null || !_booted)
            {
                return;
            }

            _home = home;
            _lookPresentActive = true;
            _lookPresentStand = ComputeLookPresentStand(home);
            _lookPresentRetargetAt = Time.time + 2.5f;
            _lookPresentStartedAt = Time.time;

            // Soft interrupt — same spirit as recall, but walk instead of warp.
            StopWorkRoutine();
            if (_sleeping || _sleepAttached)
            {
                ExitSleepVisual();
            }

            if (_sitting || _playerAttached)
            {
                ExitSitting();
            }

            UnequipTool();
            WifeEmotes.Stop(gameObject);
            if (_occupiedProp != null)
            {
                WifeOccupancy.Release(_occupiedProp, this);
                _occupiedProp = null;
            }

            _chore = Chore.Idle;
            _actionUntil = 0f;
            _idlePauseUntil = 0f;
            _choreCooldownUntil = 0f;
            CancelBeat();

            var dist = HorizontalDistance(transform.position, _lookPresentStand);
            if (dist <= LookPresentNearMeters)
            {
                _hasTarget = false;
                StopLocomotion();
                ParkStill();
                FaceLookPresentAudience();
                BeginBeat(Beat.Attend, 120f);
                return;
            }

            SetTarget(_lookPresentStand);
            BeginBeat(Beat.Stroll);
        }

        /// <summary>Left Visual tab or closed menu — resume homestead life.</summary>
        internal void ReleaseLookPresent()
        {
            if (!_lookPresentActive)
            {
                return;
            }

            _lookPresentActive = false;
            _lookPresentStartedAt = 0f;
            if (_hasTarget && _chore == Chore.Idle)
            {
                // Still walking to the porch — cancel path so she doesn't keep going for nothing.
                _hasTarget = false;
                StopLocomotion();
            }

            ParkStill();
            EndBeat(1.2f);
            _idlePauseUntil = Time.time + Random.Range(0.8f, 1.6f);
        }

        private bool TickLookPresentSession()
        {
            if (!_lookPresentActive || _home == null)
            {
                return false;
            }

            // Keep stand fresh if idol moved (rare).
            if (Time.time >= _lookPresentRetargetAt)
            {
                _lookPresentStand = ComputeLookPresentStand(_home);
                _lookPresentRetargetAt = Time.time + 2.5f;
                if (_hasTarget)
                {
                    SetTarget(_lookPresentStand);
                }
            }

            if (_sitting || _sleeping || _sleepAttached)
            {
                if (_sleeping || _sleepAttached)
                {
                    ExitSleepVisual();
                }

                if (_sitting)
                {
                    ExitSitting();
                }

                _chore = Chore.Idle;
            }

            if (_hasTarget)
            {
                // Idle Update branch drives StepToward; just ensure chore stays Idle.
                _chore = Chore.Idle;
                return true;
            }

            // Arrived / nearby — hold pose facing the player (or idol).
            var dist = HorizontalDistance(transform.position, _lookPresentStand);
            if (dist > LookPresentArriveMeters + 0.4f)
            {
                SetTarget(_lookPresentStand);
                BeginBeat(Beat.Stroll);
                return true;
            }

            ParkStill();
            FaceLookPresentAudience();
            BeginBeat(Beat.Attend, 120f);
            return true;
        }

        private Vector3 ComputeLookPresentStand(WifeHome home)
        {
            var stand = home.HomePosition + home.transform.forward * LookPresentStandoff;
            stand.y = home.HomePosition.y + 0.35f;
            // Prefer a spot toward the local player if they're in the ward.
            var player = Player.m_localPlayer;
            if (player != null && home.IsInside(player.transform.position, 2f))
            {
                var toward = player.transform.position - home.HomePosition;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.25f)
                {
                    toward.Normalize();
                    stand = home.HomePosition + toward * LookPresentStandoff;
                    stand.y = home.HomePosition.y + 0.35f;
                }
            }

            SnapToPieceOrTerrain(ref stand, home.HomePosition.y);
            return stand;
        }

        private void FaceLookPresentAudience()
        {
            var player = Player.m_localPlayer;
            if (player != null)
            {
                var to = player.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.05f)
                {
                    transform.rotation = Quaternion.LookRotation(to.normalized);
                    _facePlayerUntil = Time.time + 2f;
                    return;
                }
            }

            FaceHome();
        }

        private void OnLookPresentArrived()
        {
            _hasTarget = false;
            StopLocomotion();
            ParkStill();
            FaceLookPresentAudience();
            BeginBeat(Beat.Attend, 120f);
            _idlePauseUntil = Time.time + 120f;
        }
    }
}
