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
            var fromDisk = 0;
            var fromEmbed = 0;

            var beside = TryGetBesideDllTranslationsRoot();
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            Jotunn.Logger.LogInfo(
                $"Hearthwife localization boot v{ver}: Translations beside DLL = {(beside != null ? beside : "(not found)")}");

            foreach (var lang in Languages)
            {
                var source = TryLoadLanguage(loc, lang);
                if (source == LoadSource.None)
                {
                    continue;
                }

                loaded++;
                if (source == LoadSource.Disk)
                {
                    fromDisk++;
                }
                else if (source == LoadSource.Embedded)
                {
                    fromEmbed++;
                }
            }

            // Last-resort fallback if both disk and embedded resources failed.
            if (loaded == 0)
            {
                RegisterInlineEnglish(loc);
                Jotunn.Logger.LogWarning(
                    "Hearthwife: NO translations from disk or DLL embed — inline English only. " +
                    "Reinstall 1.0.3+ so plugins/.../Hearthwife/Translations/ exists, or the DLL is the updated build.");
            }
            else
            {
                Jotunn.Logger.LogInfo(
                    $"Hearthwife: loaded localization for {loaded} language(s) (disk={fromDisk}, embedded={fromEmbed})");
            }
        }

        private static string TryGetBesideDllTranslationsRoot()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(asm))
                {
                    return null;
                }

                var root = Path.Combine(Path.GetDirectoryName(asm) ?? "", "Translations");
                return Directory.Exists(root) ? root : null;
            }
            catch
            {
                return null;
            }
        }

        private enum LoadSource
        {
            None,
            Disk,
            Embedded
        }

        private static LoadSource TryLoadLanguage(Jotunn.Entities.CustomLocalization loc, string language)
        {
            try
            {
                var path = FindTranslationPath(language);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json))
                    {
                        loc.AddJsonFile(language, json);
                        return LoadSource.Disk;
                    }
                }

                var embedded = ReadEmbeddedJson(language);
                if (!string.IsNullOrEmpty(embedded))
                {
                    loc.AddJsonFile(language, embedded);
                    return LoadSource.Embedded;
                }

                return LoadSource.None;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"Hearthwife: failed loading {language} loc — {ex.Message}");
                return LoadSource.None;
            }
        }

        private static string ReadEmbeddedJson(string language)
        {
            var asm = Assembly.GetExecutingAssembly();
            // SDK default: Hearthwife.Translations.English.hearthwife.json
            var wantSuffix = $".Translations.{language}.hearthwife.json";
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(wantSuffix, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, $"Hearthwife.Translations.{language}.hearthwife.json", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream == null)
                        {
                            continue;
                        }

                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }

            return null;
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
                    var dir = Path.GetDirectoryName(asm) ?? "";
                    var beside = Path.Combine(dir, file);
                    if (File.Exists(beside))
                    {
                        return beside;
                    }

                    // Mis-packed flat file next to DLL (legacy / broken zip extract).
                    var flat = Path.Combine(dir, "hearthwife.json");
                    if (language == "English" && File.Exists(flat))
                    {
                        return flat;
                    }
                }
            }
            catch
            {
            }

            // BepInEx plugins root scan (one level + nested owner-mod folder).
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
