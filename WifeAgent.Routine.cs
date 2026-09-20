using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Presence Beat lock — one owner at a time (idle + future work chores).
    /// Does not replace StepToward / attach / LookAt; only gates picking and social.
    /// </summary>
    public partial class WifeAgent
    {
        private enum Beat
        {
            None,
            Linger,
            Stroll,
            Sit,
            SitFire,
            Nap,
            Sleep,
            Attend,
            /// <summary>v2: work chores map here via BeginOwnedChore.</summary>
            Work
        }

        private Beat _beat = Beat.None;
        private float _beatUntil;

        /// <summary>True while a beat owns her — do not PickIdle / TryBegin another.</summary>
        private bool IsBeatLocked =>
            _lookPresentActive ||
            _sitting ||
            _sleeping ||
            _sleepAttached ||
            (_chore != Chore.Idle) ||
            _hasTarget ||
            (_beat == Beat.Attend && Time.time < _facePlayerUntil) ||
            (_beat == Beat.Linger && Time.time < _idlePauseUntil && Time.time < _beatUntil);

        /// <summary>
        /// True while night-sleep / nap / bed attach — no talk, greet, weather chat, or E converse.
        /// </summary>
        private bool IsAsleepQuiet() =>
            _sleeping || _sleepAttached || _chore == Chore.Sleep || _chore == Chore.Nap;

        /// <summary>
        /// Greet / ambient emote / body face — not while pathing, mid-work, or asleep.
        /// Sit/Attend/Linger OK; Sleep and Walk/Stroll blocked.
        /// </summary>
        private bool CanTalkOrEmote()
        {
            if (IsAsleepQuiet())
            {
                return false;
            }

            if (_hasTarget)
            {
                return false;
            }

            if (_chore != Chore.Idle && _chore != Chore.Sit && _chore != Chore.SitFire)
            {
                return false;
            }

            if (_beat == Beat.Stroll || _beat == Beat.Work || _beat == Beat.Sleep || _beat == Beat.Nap)
            {
                return false;
            }

            return true;
        }

        private void BeginBeat(Beat beat, float holdSeconds = 0f)
        {
            _beat = beat;
            _beatUntil = holdSeconds > 0.01f
                ? Time.time + holdSeconds
                : 0f;
        }

        private void EndBeat(float lingerSeconds = -1f)
        {
            _beat = Beat.None;
            _beatUntil = 0f;
            if (lingerSeconds < 0f)
            {
                return;
            }

            var pause = lingerSeconds;
            if (_home != null && _home.Lifestyle == LifestyleMode.Diligent)
            {
                pause = Mathf.Min(pause, Random.Range(2f, 5f));
            }

            _idlePauseUntil = Mathf.Max(_idlePauseUntil, Time.time + pause);
            BeginBeat(Beat.Linger, pause);
        }

        private void CancelBeat()
        {
            _beat = Beat.None;
            _beatUntil = 0f;
        }

        private static Beat BeatFromChore(Chore chore)
        {
            switch (chore)
            {
                case Chore.Sit:
                    return Beat.Sit;
                case Chore.SitFire:
                    return Beat.SitFire;
                case Chore.Nap:
                    return Beat.Nap;
                case Chore.Sleep:
                    return Beat.Sleep;
                case Chore.Idle:
                    return Beat.None;
                default:
                    return Beat.Work;
            }
        }
    }
}
