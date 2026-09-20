using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Affection, home-damage warnings, and musical idle.
    /// </summary>
    public partial class WifeAgent
    {
        private float _affectionCooldown;
        private float _playerStillNearFor;
        private float _protectScanAt;
        private int _lastPieceCount = -1;
        private float _lastWorstHealth = 1f;
        private float _protectWarnCooldown;

        private void TickAffectionAndProtect(WifeHome home)
        {
            if (home == null || !_booted)
            {
                return;
            }

            TickHomeProtect(home);
            TickAffection(home);
        }

        private void TickAffection(WifeHome home)
        {
            if (!home.DoAffection)
            {
                _playerStillNearFor = 0f;
                return;
            }

            // Never wake / stand for affection — sleep & sit stay attached.
            if (_sleeping || _sleepAttached || _sitting || _playerAttached)
            {
                _playerStillNearFor = 0f;
                return;
            }

            if (Time.time < _affectionCooldown || Time.time < _socialEmoteQuietUntil)
            {
                return;
            }

            if (!CanAmbientSocialTalk())
            {
                _playerStillNearFor = 0f;
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                _playerStillNearFor = 0f;
                return;
            }

            var dist = Vector3.Distance(player.transform.position, transform.position);
            if (dist > 2.5f)
            {
                _playerStillNearFor = 0f;
                return;
            }

            // Player standing still nearby.
            var moving = true;
            try
            {
                moving = player.GetVelocity().magnitude > 0.35f;
            }
            catch
            {
                try
                {
                    moving = ((Character)player).GetVelocity().magnitude > 0.35f;
                }
                catch
                {
                    moving = false;
                }
            }

            if (moving)
            {
                _playerStillNearFor = 0f;
                return;
            }

            _playerStillNearFor += Time.deltaTime;
            // Longer linger so greet → affection doesn't double-fire on approach.
            if (_playerStillNearFor < 4f)
            {
                return;
            }

            _playerStillNearFor = 0f;
            _affectionCooldown = Time.time + Random.Range(90f, 140f);

            var look = player.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(look.normalized);
                try
                {
                    _character?.SetLookDir(look.normalized, 0f);
                }
                catch
                {
                }
            }

            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.AffectionLine()));
            if (!TryOfferSocialEmote(WifeEmotes.PickAffectionEmote(), 16f))
            {
                MarkSocialQuiet(10f);
            }
        }

        private System.Collections.IEnumerator AffectionEmoteNextFrame()
        {
            // Kept for debug ForceAffection — still respects sleep/sit.
            WifeEmotes.Stop(gameObject);
            yield return null;
            yield return null;
            if (_sleeping || _sitting || _sleepAttached)
            {
                yield break;
            }

            TryOfferSocialEmote(WifeEmotes.PickAffectionEmote(), 16f);
        }

        private void TickHomeProtect(WifeHome home)
        {
            if (!home.DoProtect)
            {
                return;
            }

            if (Time.time < _protectScanAt)
            {
                return;
            }

            _protectScanAt = Time.time + 4f;

            var pieces = 0;
            var worst = 1f;
            var critical = false;
            foreach (var wear in WearNTear.GetAllInstances())
            {
                if (wear == null || wear.m_nview == null || !wear.m_nview.IsValid())
                {
                    continue;
                }

                if (!home.IsInside(wear.transform.position, 1f))
                {
                    continue;
                }

                if (wear.GetComponent<WifeHome>() != null)
                {
                    continue;
                }

                pieces++;
                var hp = wear.GetHealthPercentage();
                if (hp < worst)
                {
                    worst = hp;
                }

                if (hp < 0.45f)
                {
                    critical = true;
                }
            }

            if (_lastPieceCount < 0)
            {
                _lastPieceCount = pieces;
                _lastWorstHealth = worst;
                return;
            }

            var lostPieces = pieces < _lastPieceCount;
            var gotWorse = worst < _lastWorstHealth - 0.12f;
            _lastPieceCount = pieces;
            _lastWorstHealth = worst;

            if (Time.time < _protectWarnCooldown)
            {
                return;
            }

            // Zone unload shrinks WearNTear instance counts — only warn when the player is home.
            var player = Player.m_localPlayer;
            if (player == null || !home.IsInside(player.transform.position, 10f))
            {
                return;
            }

            if (lostPieces)
            {
                _protectWarnCooldown = Time.time + 35f;
                WifeTalk.Say(gameObject, Localization.instance.Localize("$hearthwife_warn_destroyed"));
                // Never stand up from bed/chair to wave "nonono".
                if (!_sleeping && !_sleepAttached && !_sitting)
                {
                    TryOfferSocialEmote("emote_nonono", 10f);
                }

                Notify("$hearthwife_busy_protect");
                return;
            }

            if (gotWorse || critical)
            {
                _protectWarnCooldown = Time.time + 40f;
                WifeTalk.Say(gameObject, Localization.instance.Localize("$hearthwife_warn_damage"));
                if (!_sleeping && !_sleepAttached && !_sitting)
                {
                    TryOfferSocialEmote("emote_point", 10f);
                }

                Notify("$hearthwife_busy_protect");
            }
        }

        private bool TryMusicIdle(WifeHome home)
        {
            if (!home.DoMusic)
            {
                return false;
            }

            if (Random.value > 0.32f)
            {
                return false;
            }

            if (!TryOfferSocialEmote(
                    WifeEmotes.MusicEmotes[Random.Range(0, WifeEmotes.MusicEmotes.Length)],
                    14f))
            {
                return false;
            }

            Notify("$hearthwife_busy_music");
            _idlePauseUntil = Time.time + Random.Range(10f, 18f);
            FaceHome();
            return true;
        }
    }
}
