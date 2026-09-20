using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Organic social presence — one body emote at a time; sleep/sit never stand up to wave.
    /// Ambient approach/leave uses wide hysteresis so walking past the door doesn't spam.
    /// </summary>
    public partial class WifeAgent
    {
        /// <summary>No new social body emote until this time (greet/affection/weather/protect/idle).</summary>
        private float _socialEmoteQuietUntil;

        /// <summary>Player is inside the "home visit" band (entered near, not left far yet).</summary>
        private bool _playerVisitNear;

        /// <summary>
        /// Offer a oneshot social emote. False = skip body anim (caller may still bubble-talk).
        /// Never interrupts sleep, sit, attach, pathing, or work.
        /// </summary>
        private bool TryOfferSocialEmote(string emote, float quietAfter = 12f)
        {
            if (string.IsNullOrEmpty(emote))
            {
                return false;
            }

            if (IsAsleepQuiet() || _sitting || _playerAttached)
            {
                return false;
            }

            if (_hasTarget)
            {
                return false;
            }

            if (_chore != Chore.Idle)
            {
                return false;
            }

            if (Time.time < _socialEmoteQuietUntil)
            {
                return false;
            }

            if (WifeEmotes.IsPlaying(gameObject))
            {
                return false;
            }

            WifeEmotes.Play(gameObject, emote);
            var hold = WifeEmotes.DurationFor(emote) + Mathf.Max(6f, quietAfter);
            _socialEmoteQuietUntil = Time.time + hold;
            return true;
        }

        /// <summary>After bubble-talk without emote — still block stacked waves for a bit.</summary>
        private void MarkSocialQuiet(float seconds = 8f)
        {
            _socialEmoteQuietUntil = Mathf.Max(_socialEmoteQuietUntil, Time.time + Mathf.Max(2f, seconds));
        }

        private bool CanAmbientSocialTalk()
        {
            if (IsAsleepQuiet() || _sitting)
            {
                return false;
            }

            return CanTalkOrEmote() && _chore == Chore.Idle && !_hasTarget;
        }
    }
}
