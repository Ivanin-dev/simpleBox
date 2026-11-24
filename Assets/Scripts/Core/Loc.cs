using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// Localization: the Russian source text is the key, LocTable holds the English translation.
    /// Language: user choice (settings, PlayerPrefs "sb.lang": 0 = auto, 1 = Russian, 2 = English) or, on auto, the
    /// device language (Russian / Ukrainian / Belarusian → Russian, everything else → English).
    /// T() is a dictionary lookup returning cached strings (no allocations); untranslated keys are shown as is.
    /// Static scene labels use LocalizedText; code-driven texts call T() where they are displayed.
    /// </summary>
    public static class Loc
    {
        public enum Language { Russian = 0, English = 1 }

        public const string PrefsKey = "sb.lang";

        private static bool _initialized;
        private static Language _current;
        private static Dictionary<string, string> _english;

        /// <summary>Raised after the language changes (static labels re-apply their text).</summary>
        public static event Action Changed;

        public static Language Current
        {
            get
            {
                EnsureInitialized();
                return _current;
            }
        }

        public static bool IsEnglish => Current == Language.English;

        /// <summary>0 = auto (device language), 1 = Russian, 2 = English. Saved by GameSettings.</summary>
        public static void SetChoice(int choice)
        {
            EnsureInitialized();
            Language next = Resolve(choice);
            if (next == _current) return;
            _current = next;
            Changed?.Invoke();
        }

        /// <summary>Translation of a Russian source string for the current language.</summary>
        public static string T(string russian)
        {
            if (string.IsNullOrEmpty(russian)) return russian;
            EnsureInitialized();
            if (_current == Language.Russian) return russian;
            if (_english == null) _english = LocTable.BuildEnglish();
            return _english.TryGetValue(russian, out string english) ? english : russian;
        }

        /// <summary>string.Format over the translated template (allocates: events / menus only).</summary>
        public static string F(string russianTemplate, string arg0) => string.Format(T(russianTemplate), arg0);

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            _current = Resolve(PlayerPrefs.GetInt(PrefsKey, 0));
        }

        private static Language Resolve(int choice)
        {
            if (choice == 1) return Language.Russian;
            if (choice == 2) return Language.English;
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian:
                case SystemLanguage.Ukrainian:
                case SystemLanguage.Belarusian:
                    return Language.Russian;
                default:
                    return Language.English;
            }
        }
    }
}
