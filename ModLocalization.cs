using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using Jotunn.Managers;
using UnityEngine;

namespace Hearthwife
{
    internal static class ModLocalization
    {
        private static readonly string[] Languages =
        {
            "English",
            "Portuguese_Brazilian",
            "Portuguese_European",
            "German",
            "French",
            "Spanish",
            "Russian",
            "Polish",
            "Dutch",
            "Italian",
            "Swedish",
            "Turkish",
            "Ukrainian",
            "Chinese",
            "Chinese_Trad",
            "Japanese",
            "Korean"
        };

        internal static void Register()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            var loaded = 0;

            foreach (var lang in Languages)
            {
                if (TryLoadLanguageJson(loc, lang))
                {
                    loaded++;
                }
            }

            // Hard fallback if files missing next to the DLL (dev / odd deploy).
            if (loaded == 0)
            {
                RegisterInlineEnglish(loc);
                Jotunn.Logger.LogWarning("Hearthwife: Translations folder missing — English inline fallback only");
            }
            else
            {
                Jotunn.Logger.LogInfo($"Hearthwife: loaded localization for {loaded} language(s)");
            }
        }

        private static bool TryLoadLanguageJson(Jotunn.Entities.CustomLocalization loc, string language)
        {
            try
            {
                var path = FindTranslationPath(language);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                {
                    return false;
                }

                loc.AddJsonFile(language, json);
                return true;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"Hearthwife: failed loading {language} loc — {ex.Message}");
                return false;
            }
        }

        private static string FindTranslationPath(string language)
        {
            var file = Path.Combine("Translations", language, "hearthwife.json");

            // Next to DLL (Thunderstore / Mod tests deploy).
            try
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    var beside = Path.Combine(Path.GetDirectoryName(asm) ?? "", file);
                    if (File.Exists(beside))
                    {
                        return beside;
                    }
                }
            }
            catch
            {
            }

            // BepInEx plugins root scan (one level).
            try
            {
                var plugins = Path.Combine(Paths.BepInExRootPath, "plugins");
                if (Directory.Exists(plugins))
                {
                    foreach (var dir in Directory.GetDirectories(plugins))
                    {
                        var candidate = Path.Combine(dir, file);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }

                        // Nested owner-mod folder
                        foreach (var sub in Directory.GetDirectories(dir))
                        {
                            candidate = Path.Combine(sub, file);
                            if (File.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Minimal English so the mod never shows raw $tokens if JSON deploy failed.</summary>
        private static void RegisterInlineEnglish(Jotunn.Entities.CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "hearthwife_wife_name", "Wife" },
                { "hearthwife_idol_name", "Wife Idol" },
                { "hearthwife_menu_hint", "E = chest   Shift+E = menu   Crouch+E = recall" },
                { "hearthwife_menu_tab_home", "Home" },
                { "hearthwife_menu_tab_look", "Look" },
                { "hearthwife_menu_tab_work", "Work" },
                { "hearthwife_menu_tab_life", "Life" },
                { "hearthwife_status_idle", "At home" },
                { "hearthwife_life_leisure", "Home life" },
                { "hearthwife_life_balanced", "Balanced" },
                { "hearthwife_life_diligent", "Hardworking" },
                { "hearthwife_btn_bring", "Bring" },
                { "hearthwife_btn_chest", "Chest" },
                { "hearthwife_btn_close", "Close" },
                { "hearthwife_bind_recall", "[<color=yellow><b>Crouch+E</b></color>] " },
                { "hearthwife_menu_recall", "Call her back / unstuck" }
            });
        }

        internal static string T(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "";
            }

            if (token[0] != '$')
            {
                token = "$" + token;
            }

            try
            {
                return Localization.instance != null
                    ? Localization.instance.Localize(token)
                    : token;
            }
            catch
            {
                return token;
            }
        }

        internal static string T(string token, params string[] args)
        {
            var s = T(token);
            try
            {
                if (args != null && args.Length > 0)
                {
                    return string.Format(s, args);
                }
            }
            catch
            {
            }

            return s;
        }
    }
}
