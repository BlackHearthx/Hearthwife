using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Romantic lines when the player presses E on the wife (IdleActors Talker-style bubbles).
    /// </summary>
    internal static class WifeTalk
    {
        private static readonly string[] LineKeys =
        {
            "hearthwife_talk_0",
            "hearthwife_talk_1",
            "hearthwife_talk_2",
            "hearthwife_talk_3",
            "hearthwife_talk_4",
            "hearthwife_talk_5",
            "hearthwife_talk_6",
            "hearthwife_talk_7",
            "hearthwife_talk_8",
            "hearthwife_talk_9"
        };

        private static int _last = -1;

        internal static string AffectionLine()
        {
            var keys = new[]
            {
                "hearthwife_love_0", "hearthwife_love_1", "hearthwife_love_2", "hearthwife_love_3"
            };
            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string GreetLine()
        {
            var keys = new[]
            {
                "hearthwife_greet_0", "hearthwife_greet_1", "hearthwife_greet_2", "hearthwife_greet_3"
            };
            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string GoodbyeLine()
        {
            var keys = new[]
            {
                "hearthwife_bye_0", "hearthwife_bye_1", "hearthwife_bye_2", "hearthwife_bye_3"
            };
            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string MorningLine()
        {
            var keys = new[]
            {
                "hearthwife_morning_0", "hearthwife_morning_1", "hearthwife_morning_2", "hearthwife_morning_3"
            };
            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string FireLine()
        {
            var keys = new[]
            {
                "hearthwife_fire_0", "hearthwife_fire_1", "hearthwife_fire_2", "hearthwife_fire_3"
            };
            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string NextLine()
        {
            if (LineKeys.Length == 0)
            {
                return "$hearthwife_wife_name";
            }

            int pick;
            do
            {
                pick = Random.Range(0, LineKeys.Length);
            }
            while (pick == _last && LineKeys.Length > 1);

            _last = pick;
            return "$" + LineKeys[pick];
        }

        /// <summary>How long the overhead bubble stays (Chat NpcText ttl).</summary>
        private const float BubbleTtl = 12f;

        /// <summary>IdleActors/Fires use ~20–25 — 4m was clearing bubbles as soon as the camera pulled back.</summary>
        private const float BubbleCull = 25f;

        internal static void Say(GameObject speaker, string localizedText)
        {
            if (speaker == null || string.IsNullOrEmpty(localizedText))
            {
                return;
            }

            var name = Localization.instance.Localize("$hearthwife_wife_name");
            var agent = speaker.GetComponent<WifeAgent>();
            if (agent != null)
            {
                name = agent.DisplayName;
            }

            if (Chat.instance != null)
            {
                // SetNpcText(talker, offset, cullDistance, ttl, topic, text, large)
                Chat.instance.SetNpcText(
                    speaker,
                    Vector3.up * 2f,
                    BubbleCull,
                    BubbleTtl,
                    name,
                    localizedText,
                    false);
            }
        }
    }
}
