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
        private const string DefaultLanguage = "English";

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

            var beside = TryGetBesideDllTranslationsRoot();
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            var gameLanguage = GetGameLanguage();
            var forced = ResolveForcedLanguage();

            Jotunn.Logger.LogInfo(
                $"Hearthwife localization boot v{ver}: game language = '{gameLanguage}', " +
                $"config Language = '{(forced ?? "Auto")}', " +
                $"Translations beside DLL = {(beside != null ? beside : "(not found)")}");

            var loaded = forced != null
                ? RegisterForced(loc, forced)
                : RegisterAuto(loc, gameLanguage);

            // Last-resort fallback if both disk and embedded resources failed.
            if (loaded == 0)
            {
                RegisterInlineEnglish(loc);
                Jotunn.Logger.LogWarning(
                    "Hearthwife: NO translations from disk or DLL embed — inline English only. " +
                    "Reinstall so plugins/.../Hearthwife/Translations/ exists, or the DLL is the updated build.");
            }
        }

        /// <summary>
        /// Jötunn applies our English map first, then overlays <see cref="GetGameLanguage"/> on top
        /// (LocalizationManager.AddTranslations). Registering every language therefore lets a stale
        /// PlayerPrefs "language" value replace English. Only English + the active language are added.
        /// </summary>
        private static int RegisterAuto(Jotunn.Entities.CustomLocalization loc, string gameLanguage)
        {
            var loaded = 0;
            if (AddLanguage(loc, DefaultLanguage, DefaultLanguage))
            {
                loaded++;
            }

            if (!string.Equals(gameLanguage, DefaultLanguage, StringComparison.Ordinal)
                && IsKnownLanguage(gameLanguage)
                && AddLanguage(loc, gameLanguage, gameLanguage))
            {
                loaded++;
            }

            return loaded;
        }

        /// <summary>
        /// Config override. The chosen pack is registered under English as well, because Jötunn only
        /// overlays the game language — otherwise forcing a language the game is not set to does nothing.
        /// </summary>
        private static int RegisterForced(Jotunn.Entities.CustomLocalization loc, string forced)
        {
            var loaded = 0;
            if (AddLanguage(loc, forced, DefaultLanguage))
            {
                loaded++;
            }

            if (!string.Equals(forced, DefaultLanguage, StringComparison.Ordinal)
                && AddLanguage(loc, forced, forced))
            {
                loaded++;
            }

            return loaded;
        }

        /// <summary>Load <paramref name="language"/> JSON and register it under <paramref name="registerAs"/>.</summary>
        private static bool AddLanguage(
            Jotunn.Entities.CustomLocalization loc,
            string language,
            string registerAs)
        {
            try
            {
                var json = ReadLanguageJson(language, out var source);
                if (string.IsNullOrEmpty(json))
                {
                    Jotunn.Logger.LogWarning($"Hearthwife: no {language} translation on disk or in the DLL");
                    return false;
                }

                loc.AddJsonFile(registerAs, json);
                var note = string.Equals(language, registerAs, StringComparison.Ordinal)
                    ? language
                    : $"{language} as {registerAs}";
                Jotunn.Logger.LogInfo($"Hearthwife: localization {note} ({source})");
                return true;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"Hearthwife: failed loading {language} loc — {ex.Message}");
                return false;
            }
        }

        /// <summary>Same value Jötunn uses to pick the overlay language.</summary>
        private static string GetGameLanguage()
        {
            try
            {
                var lang = PlayerPrefs.GetString("language", DefaultLanguage);
                return string.IsNullOrEmpty(lang) ? DefaultLanguage : lang;
            }
            catch
            {
                return DefaultLanguage;
            }
        }

        /// <returns>Configured language, or null for Auto / unknown values.</returns>
        private static string ResolveForcedLanguage()
        {
            var value = PluginConfig.Language != null ? PluginConfig.Language.Value : null;
            value = (value ?? "").Trim();

            if (value.Length == 0 || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var lang in Languages)
            {
                if (string.Equals(lang, value, StringComparison.OrdinalIgnoreCase))
                {
                    return lang;
                }
            }

            Jotunn.Logger.LogWarning(
                $"Hearthwife: config Language '{value}' is not shipped — using Auto. " +
                $"Valid: Auto, {string.Join(", ", Languages)}");
            return null;
        }

        private static bool IsKnownLanguage(string language)
        {
            foreach (var lang in Languages)
            {
                if (string.Equals(lang, language, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static string[] ShippedLanguages => Languages;

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

        /// <summary>Disk (user-editable) wins over the copy embedded in the DLL.</summary>
        private static string ReadLanguageJson(string language, out LoadSource source)
        {
            var path = FindTranslationPath(language);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var json = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(json))
                {
                    source = LoadSource.Disk;
                    return json;
                }
            }

            var embedded = ReadEmbeddedJson(language);
            if (!string.IsNullOrEmpty(embedded))
            {
                source = LoadSource.Embedded;
                return embedded;
            }

            source = LoadSource.None;
            return null;
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
