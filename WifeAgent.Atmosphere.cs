using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Day/night sleep + weather comments/behavior (EnvMan).
    /// </summary>
    public partial class WifeAgent
    {
        private WifeAtmosphere.Mood _lastWxMood = WifeAtmosphere.Mood.None;
        private float _wxCommentAt;
        private bool _preferShelter;
        private bool _wantsMorningGreet;
        private float _morningGreetReadyAt;
        private float _lastMorningGreetAt;

        /// <summary>
        /// Returns true if atmosphere fully owns this think (e.g. going to bed).
        /// </summary>
        private bool TickAtmosphere(WifeHome home)
        {
            if (_lookPresentActive)
            {
                // Visual menu session owns her — don't yank into night sleep mid-fitting.
                return false;
            }

            if (PluginConfig.EnableAtmosphere != null && !PluginConfig.EnableAtmosphere.Value)
            {
                _preferShelter = false;
                return false;
            }

            if (_sitting || _sleepAttached)
            {
                // Still allow night→sleep to interrupt long sits.
            }

            var mood = WifeAtmosphere.Classify();
            // Weather “stay home” (sit by fire / couch) is Leisure presence only.
            // Balanced/Diligent keep working — rain must not spam sit/nap.
            var leisure = home != null && !PluginConfig.AllowsWork(home.Lifestyle);
            _preferShelter = leisure &&
                             (WifeAtmosphere.WantsShelter(mood) || mood == WifeAtmosphere.Mood.Cold);

            // --- Day / night cycle (all lifestyle modes) ---
            // Sleep only on EnvMan.IsNight(); wake only on IsDaylight().
            // Never use !IsDaylight for sleep + !IsNight for wake — twilight made both
            // true and WakeAtDaybreak→RequestThinkSoon→BeginSleep looped every frame (FPS death).
            var dayNight = PluginConfig.EnableDayNight == null || PluginConfig.EnableDayNight.Value;
            if (dayNight)
            {
                if (IsHomesteadNight())
                {
                    // Finish owned work first — but only if it's still alive.
                    // Dead Collect/Forage (no routine, no target) must not block night forever.
                    if (IsBusyOwned() &&
                        _chore != Chore.Sleep &&
                        _chore != Chore.Nap &&
                        _chore != Chore.Sit &&
                        _chore != Chore.SitFire)
                    {
                        // Alive = actually driven. A stale _hasTarget alone used to block night
                        // (and TickWork picks) forever while she stood frozen.
                        var workAlive = _workRoutine != null ||
                                        _gatherSessionActive ||
                                        (_hasTarget &&
                                         _lastProgressAt > 0f &&
                                         Time.time - _lastProgressAt < 12f);
                        if (workAlive)
                        {
                            MaybeWeatherComment(mood, forceChangeOnly: false);
                            return true;
                        }
                    }

                    if (_sleeping && _chore == Chore.Nap)
                    {
                        _chore = Chore.Sleep;
                        return true;
                    }

                    if (!_sleeping || _chore != Chore.Sleep)
                    {
                        BeginSleep(home);
                    }

                    MaybeWeatherComment(mood, forceChangeOnly: false);
                    return true;
                }

                if (IsHomesteadDaylight() && _sleeping && _chore != Chore.Nap)
                {
                    WakeAtDaybreak();
                    MaybeWeatherComment(WifeAtmosphere.Mood.Clear, forceChangeOnly: true);
                }
            }

            MaybeWeatherComment(mood, forceChangeOnly: false);

            // Bad weather shelter sit — Leisure / vida no lar only (smooth home day).
            if (leisure &&
                _preferShelter &&
                !_sitting && !_sleeping && !_hasTarget &&
                _chore == Chore.Idle &&
                Time.time >= _idlePauseUntil &&
                Time.time >= _sitCooldownUntil &&
                Random.value < 0.10f)
            {
                if (WifeAtmosphere.WantsFire(mood) &&
                    home.DoSitFire &&
                    TrySitByFire(home))
                {
                    return true;
                }

                if (home.DoSit && TryBeginSit(home))
                {
                    return true;
                }
            }

            return false;
        }

        private void MaybeWeatherComment(WifeAtmosphere.Mood mood, bool forceChangeOnly)
        {
            // Never chat while asleep — weather wait until she's up.
            if (IsAsleepQuiet())
            {
                return;
            }

            if (mood == WifeAtmosphere.Mood.None)
            {
                return;
            }

            var changed = mood != _lastWxMood;
            if (changed)
            {
                _lastWxMood = mood;
            }

            // Speak on change, or rarely while mood lasts (player nearby).
            var due = Time.time >= _wxCommentAt;
            if (!due && !changed)
            {
                return;
            }

            if (forceChangeOnly && !changed)
            {
                return;
            }

            if (!changed && Random.value > 0.12f)
            {
                _wxCommentAt = Time.time + Random.Range(45f, 90f);
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            // Only comment if player is around the home (not across the map).
            if (Vector3.Distance(player.transform.position, transform.position) > 22f)
            {
                _wxCommentAt = Time.time + 30f;
                return;
            }

            if (_sitting)
            {
                // Bubble only while seated — don't break attach pose with emotes.
                var keySit = WifeAtmosphere.LineKey(mood);
                if (!string.IsNullOrEmpty(keySit))
                {
                    WifeTalk.Say(gameObject, Localization.instance.Localize(keySit));
                }

                _wxCommentAt = Time.time + Random.Range(90f, 160f);
                return;
            }

            // Don't stack weather emotes on top of greet/wave (looks like hand spam).
            if (WifeEmotes.IsPlaying(gameObject) && !changed)
            {
                _wxCommentAt = Time.time + 40f;
                return;
            }

            var key = WifeAtmosphere.LineKey(mood);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            WifeTalk.Say(gameObject, Localization.instance.Localize(key));
            var emote = WifeAtmosphere.EmoteFor(mood);
            if (!string.IsNullOrEmpty(emote) && mood != WifeAtmosphere.Mood.Night)
            {
                TryOfferSocialEmote(emote, 20f);
            }

            _wxCommentAt = Time.time + Random.Range(100f, 180f);
        }

        /// <summary>
        /// Polled every frame while night-sleeping — rise with daylight / player, not on ThinkInterval.
        /// </summary>
        private void TickDaybreakWake()
        {
            if (PluginConfig.EnableDayNight != null && !PluginConfig.EnableDayNight.Value)
            {
                return;
            }

            if (!IsHomesteadDaylight())
            {
                return;
            }

            WakeAtDaybreak();
        }

        /// <summary>True night only — twilight is neither sleep nor forced wake.</summary>
        private static bool IsHomesteadNight()
        {
            try
            {
                return EnvMan.IsNight();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Clear daylight — inverse of night for wake (no !IsNight twilight thrash).</summary>
        private static bool IsHomesteadDaylight()
        {
            try
            {
                return EnvMan.IsDaylight();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Get up promptly — short pause, no long chore cooldown lag.</summary>
        private void WakeAtDaybreak()
        {
            if (!_sleeping || _chore == Chore.Nap)
            {
                return;
            }

            ExitSleepVisual();
            // Short settle — old EndOwnedChore(2f) + full cooldown left her in bed after the player.
            _chore = Chore.Idle;
            _hasTarget = false;
            _workRoutine = null;
            _gatherSessionActive = false;
            _actionUntil = Time.time + 0.35f;
            _idlePauseUntil = Time.time + 0.5f;
            _choreCooldownUntil = 0f;
            SilenceAiMove();
            SetWalkAnim(0f);
            CancelBeat();
            EndBeat(0.35f);

            _wantsMorningGreet = true;
            _morningGreetReadyAt = Time.time + 0.7f;
            // Do NOT RequestThinkSoon here — same-frame TickWork + BeginSleep was the FPS loop.
        }

        /// <summary>After night sleep → day: soft wave + morning line (Basics life).</summary>
        private void TryMorningGreet()
        {
            if (!_wantsMorningGreet || Time.time < _morningGreetReadyAt)
            {
                return;
            }

            _wantsMorningGreet = false;

            if (IsAsleepQuiet() || _sitting || _playerAttached)
            {
                return;
            }

            // At most ~once per Valheim day cycle.
            if (Time.time < _lastMorningGreetAt + 350f)
            {
                return;
            }

            _lastMorningGreetAt = Time.time;
            WifeTalk.Say(gameObject, Localization.instance.Localize(WifeTalk.MorningLine()));
            if (!TryOfferSocialEmote("emote_wave", 18f))
            {
                MarkSocialQuiet(10f);
            }
        }
    }
}
