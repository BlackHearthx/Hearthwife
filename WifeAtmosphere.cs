using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Classify Valheim EnvMan weather for homestead reactions (flags + env name).
    /// </summary>
    internal static class WifeAtmosphere
    {
        internal enum Mood
        {
            None = 0,
            Clear,
            Fog,
            Rain,
            Storm,
            Cold,
            Snow,
            Night
        }

        internal static Mood Classify()
        {
            try
            {
                if (EnvMan.instance == null)
                {
                    return Mood.None;
                }

                // Night wins for sleep cycle; weather comments still use wet/cold under it.
                if (EnvMan.IsNight() || !EnvMan.IsDaylight())
                {
                    return Mood.Night;
                }

                var env = EnvMan.instance.GetCurrentEnvironment();
                if (env == null)
                {
                    return Mood.Clear;
                }

                var n = (env.m_name ?? "").ToLowerInvariant();

                if (n.Contains("thunder") || (n.Contains("storm") && !n.Contains("snow")))
                {
                    return Mood.Storm;
                }

                if (env.m_isWet || n.Contains("rain"))
                {
                    return Mood.Rain;
                }

                if (n.Contains("snow") || env.m_snowBuildup > 0.01f)
                {
                    return Mood.Snow;
                }

                if (env.m_isFreezing || (env.m_isCold && n.Contains("cold")))
                {
                    return Mood.Cold;
                }

                if (env.m_isCold)
                {
                    return Mood.Cold;
                }

                if (n.Contains("mist") || n.Contains("fog") || env.m_fogDensityDay >= 0.08f)
                {
                    return Mood.Fog;
                }

                return Mood.Clear;
            }
            catch
            {
                return Mood.None;
            }
        }

        /// <summary>Wet enough to prefer shelter / fire.</summary>
        internal static bool WantsShelter(Mood mood)
        {
            return mood == Mood.Rain || mood == Mood.Storm || mood == Mood.Snow;
        }

        internal static bool WantsFire(Mood mood)
        {
            return mood == Mood.Rain || mood == Mood.Storm || mood == Mood.Cold ||
                   mood == Mood.Snow || mood == Mood.Night;
        }

        internal static string LineKey(Mood mood)
        {
            string[] keys;
            switch (mood)
            {
                case Mood.Rain:
                    keys = new[]
                    {
                        "hearthwife_wx_rain_0", "hearthwife_wx_rain_1", "hearthwife_wx_rain_2"
                    };
                    break;
                case Mood.Storm:
                    keys = new[]
                    {
                        "hearthwife_wx_storm_0", "hearthwife_wx_storm_1", "hearthwife_wx_storm_2"
                    };
                    break;
                case Mood.Fog:
                    keys = new[]
                    {
                        "hearthwife_wx_fog_0", "hearthwife_wx_fog_1", "hearthwife_wx_fog_2"
                    };
                    break;
                case Mood.Clear:
                    keys = new[]
                    {
                        "hearthwife_wx_clear_0", "hearthwife_wx_clear_1", "hearthwife_wx_clear_2"
                    };
                    break;
                case Mood.Cold:
                    keys = new[]
                    {
                        "hearthwife_wx_cold_0", "hearthwife_wx_cold_1", "hearthwife_wx_cold_2"
                    };
                    break;
                case Mood.Snow:
                    keys = new[]
                    {
                        "hearthwife_wx_snow_0", "hearthwife_wx_snow_1", "hearthwife_wx_snow_2"
                    };
                    break;
                case Mood.Night:
                    keys = new[]
                    {
                        "hearthwife_wx_night_0", "hearthwife_wx_night_1", "hearthwife_wx_night_2"
                    };
                    break;
                default:
                    return null;
            }

            return "$" + keys[Random.Range(0, keys.Length)];
        }

        internal static string EmoteFor(Mood mood)
        {
            switch (mood)
            {
                case Mood.Rain:
                case Mood.Storm:
                    return "emote_shrug";
                case Mood.Cold:
                case Mood.Snow:
                    return "emote_shrug";
                case Mood.Fog:
                    return "emote_wave";
                case Mood.Clear:
                    return Random.value < 0.5f ? "emote_cheer" : "emote_thumbsup";
                case Mood.Night:
                    return "emote_rest";
                default:
                    return null;
            }
        }
    }
}
