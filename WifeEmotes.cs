using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// All social emotes — Fires CompanionAnimationController + Player.UpdateEmote stop path.
    /// Oneshot (wave/blowkiss/…): ZSyncAnimation.SetTrigger only (Fires).
    /// Persistent (rest/vibe/dance/…): SetBool hold (Fires).
    /// Durations: exact Fires EmoteDurations table — never cut early.
    /// After any stop, wait 2 frames before starting so emote_stop cannot chop the new clip
    /// (same root cause as ultra-fast E-kiss / music).
    /// </summary>
    internal static class WifeEmotes
    {
        internal static readonly string[] QuickIdle =
        {
            "emote_wave", "emote_laugh", "emote_shrug", "emote_toast", "emote_blowkiss"
        };

        internal static readonly string[] PersistentIdle =
        {
            "emote_rest", "emote_vibe"
        };

        /// <summary>
        /// Homestead "music" = longer Fires persistent poses (dance/vibe).
        /// headbang (1.4s Fires) omitted here — reads as a flash / "sped up".
        /// </summary>
        internal static readonly string[] MusicEmotes =
        {
            "emote_dance", "emote_vibe", "emote_dance", "emote_vibe", "emote_toast"
        };

        /// <summary>Copy of Fires CompanionIdleBehavior.EmoteDurations.</summary>
        private static readonly Dictionary<string, float> FiresDurations =
            new Dictionary<string, float>
            {
                { "emote_blowkiss", 2.0f },
                { "emote_bow", 3.5f },
                { "emote_challenge", 3.2f },
                { "emote_cheer", 2.5f },
                { "emote_cower", 2.7f },
                { "emote_cry", 4.0f },
                { "emote_flex", 2.9f },
                { "emote_laugh", 3.1f },
                { "emote_nonono", 2.1f },
                { "emote_point", 2.5f },
                { "emote_roar", 2.2f },
                { "emote_shrug", 2.7f },
                { "emote_thumbsup", 1.3f },
                { "emote_wave", 2.5f },
                { "emote_toast", 2.7f },
                { "emote_loveyou", 2.7f },
                { "emote_comehere", 2.4f },
                { "emote_sit", 10.87f },
                { "emote_despair", 6.7f },
                { "emote_kneel", 2.0f },
                { "emote_headbang", 1.4f },
                { "emote_dance", 5.2f },
                { "emote_rest", 10f },
                { "emote_vibe", 5f }
            };

        /// <summary>Fires PersistentEmotes.</summary>
        private static readonly HashSet<string> PersistentNames = new HashSet<string>
        {
            "emote_sit", "emote_despair", "emote_rest", "emote_vibe",
            "emote_kneel", "emote_headbang", "emote_dance"
        };

        private const float HardTimeout = 30f;
        private const float MinGap = 0.4f;

        /// <param name="duration">
        /// Ignored if &lt; 0 — uses Fires table. If set, never shorter than Fires.
        /// </param>
        internal static void Play(GameObject go, string emote, float duration = -1f, bool persistent = false)
        {
            if (go == null || string.IsNullOrEmpty(emote))
            {
                return;
            }

            if (!emote.StartsWith("emote_"))
            {
                emote = "emote_" + emote;
            }

            var isPersistent = persistent || PersistentNames.Contains(emote);
            var wait = ResolveDuration(emote, duration, isPersistent);

            var driver = go.GetComponent<WifeEmoteDriver>() ?? go.AddComponent<WifeEmoteDriver>();
            driver.Play(emote, wait, isPersistent);
        }

        internal static float DurationFor(string emote)
        {
            if (string.IsNullOrEmpty(emote))
            {
                return 2.5f;
            }

            if (!emote.StartsWith("emote_"))
            {
                emote = "emote_" + emote;
            }

            return FiresDurations.TryGetValue(emote, out var d) ? d : 2.5f;
        }

        private static float ResolveDuration(string emote, float requested, bool persistent)
        {
            var table = DurationFor(emote);
            if (requested < 0f)
            {
                return table;
            }

            return Mathf.Max(requested, table);
        }

        internal static void PlayRandomIdle(GameObject go)
        {
            if (Random.value < 0.78f)
            {
                var e = QuickIdle[Random.Range(0, QuickIdle.Length)];
                Play(go, e);
            }
            else
            {
                var e = PersistentIdle[Random.Range(0, PersistentIdle.Length)];
                Play(go, e, persistent: true);
            }
        }

        internal static string PickGreetEmote()
        {
            return Random.value < 0.5f ? "emote_wave" : "emote_blowkiss";
        }

        internal static string PickAffectionEmote()
        {
            var roll = Random.value;
            if (roll < 0.4f)
            {
                return "emote_loveyou";
            }

            if (roll < 0.75f)
            {
                return "emote_blowkiss";
            }

            return "emote_comehere";
        }

        internal static void PlayGreet(GameObject go)
        {
            Play(go, PickGreetEmote());
        }

        internal static void PlayAffection(GameObject go)
        {
            Play(go, PickAffectionEmote());
        }

        internal static void PlayMusic(GameObject go)
        {
            var e = MusicEmotes[Random.Range(0, MusicEmotes.Length)];
            Play(go, e, persistent: PersistentNames.Contains(e));
        }

        internal static void Stop(GameObject go)
        {
            go?.GetComponent<WifeEmoteDriver>()?.ForceStop();
        }

        internal static bool IsPersistentActive(GameObject go) => IsPlaying(go);

        internal static bool IsPlaying(GameObject go)
        {
            var driver = go != null ? go.GetComponent<WifeEmoteDriver>() : null;
            return driver != null && driver.IsPlaying;
        }

        /// <summary>Current emote id, or null.</summary>
        internal static string CurrentEmote(GameObject go)
        {
            var driver = go != null ? go.GetComponent<WifeEmoteDriver>() : null;
            return driver != null ? driver.CurrentEmote : null;
        }

        /// <summary>True only for music-style emotes (not sit/rest by the fire).</summary>
        internal static bool IsMusicStylePlaying(GameObject go)
        {
            var e = CurrentEmote(go);
            if (string.IsNullOrEmpty(e))
            {
                return false;
            }

            return e == "emote_dance" || e == "emote_vibe" || e == "emote_toast" ||
                   e == "emote_headbang";
        }

        /// <summary>Standing rest/vibe (idle calm) — not music, not chair.</summary>
        internal static bool IsCalmRestPlaying(GameObject go)
        {
            var e = CurrentEmote(go);
            return e == "emote_rest" || e == "emote_kneel";
        }

        /// <summary>Hover token for idle emote, or null to keep "at home".</summary>
        internal static string IdleEmoteHoverToken(GameObject go)
        {
            if (IsMusicStylePlaying(go))
            {
                return "$hearthwife_busy_music";
            }

            if (IsCalmRestPlaying(go))
            {
                return "$hearthwife_busy_sit";
            }

            return null;
        }

        /// <summary>Call from Boot — keep playback at player rate.</summary>
        internal static void NormalizeAnimatorSpeed(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
            {
                if (anim != null && !Mathf.Approximately(anim.speed, 1f))
                {
                    anim.speed = 1f;
                }
            }
        }

        private sealed class WifeEmoteDriver : MonoBehaviour
        {
            private ZSyncAnimation _zanim;
            private Animator _anim;
            private string _current;
            private float _startedAt;
            private bool _persistent;
            private Coroutine _routine;

            internal bool IsPlaying => !string.IsNullOrEmpty(_current);
            internal string CurrentEmote => _current;

            private void Awake()
            {
                _zanim = GetComponent<ZSyncAnimation>();
                _anim = GetComponentInChildren<Animator>();
            }

            internal void Play(string emote, float duration, bool persistent)
            {
                if (!string.IsNullOrEmpty(_current) && Time.time - _startedAt < MinGap)
                {
                    return;
                }

                ForceStop();
                _routine = StartCoroutine(PlayRoutine(emote, duration, persistent));
            }

            private IEnumerator PlayRoutine(string emote, float duration, bool persistent)
            {
                _current = emote;
                _persistent = persistent;
                _startedAt = Time.time;

                // Critical: ForceStop / external Stop fires emote_stop. Starting the new
                // trigger/bool the same frame makes clips look ultra-fast (Fires End then
                // Play is fine only after the stop has been consumed).
                yield return null;
                yield return null;

                if (_current != emote)
                {
                    yield break;
                }

                NormalizeAnimatorSpeed(gameObject);
                ClearEmoteStopTrigger();

                if (persistent)
                {
                    // Fires: SetAnimationBool(emote, true) on zanim (+ animator if present).
                    _zanim?.SetBool(emote, true);
                    if (_anim != null)
                    {
                        _anim.SetBool(emote, true);
                    }
                }
                else
                {
                    // Fires oneshot: ONLY zanim.SetTrigger — do not also hammer Animator.
                    if (_zanim != null)
                    {
                        _zanim.SetTrigger(emote);
                    }
                    else if (_anim != null)
                    {
                        _anim.SetTrigger(emote);
                    }
                }

                GetComponent<WifeAgent>()?.RefreshRandomAnimationGate();

                var wait = Mathf.Clamp(duration, 0.8f, HardTimeout);
                var endAt = Time.time + wait;
                while (Time.time < endAt)
                {
                    // Keep player-rate for the whole hold (music/dance especially).
                    if (_anim != null && _anim.speed > 1.01f)
                    {
                        _anim.speed = 1f;
                    }

                    yield return null;
                }

                if (_current == emote)
                {
                    EndLikeFires();
                }

                _routine = null;
            }

            private void ClearEmoteStopTrigger()
            {
                if (_anim == null)
                {
                    return;
                }

                try
                {
                    _anim.ResetTrigger("emote_stop");
                }
                catch
                {
                }
            }

            /// <summary>Fires CallVanillaStopEmote fallback + emote_stop.</summary>
            private void EndLikeFires()
            {
                if (string.IsNullOrEmpty(_current))
                {
                    GetComponent<WifeAgent>()?.RefreshRandomAnimationGate();
                    return;
                }

                var ending = _current;

                _zanim?.SetBool(ending, false);
                if (_anim != null)
                {
                    _anim.SetBool(ending, false);
                }

                try
                {
                    _zanim?.SetTrigger("emote_stop");
                    if (_anim != null)
                    {
                        _anim.SetTrigger("emote_stop");
                    }
                }
                catch
                {
                }

                _current = null;
                _persistent = false;
                GetComponent<WifeAgent>()?.RefreshRandomAnimationGate();
            }

            internal void ForceStop()
            {
                if (_routine != null)
                {
                    StopCoroutine(_routine);
                    _routine = null;
                }

                EndLikeFires();
            }
        }
    }
}
