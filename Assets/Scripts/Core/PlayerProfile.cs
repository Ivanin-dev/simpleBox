using System.Text;
using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// Local player profile stored in PlayerPrefs: nickname (shown to other players in multiplayer).
    /// First read creates "Игрок" + 3 random digits. Names are trimmed, limited to MaxLength and stripped of
    /// control / markup characters.
    /// </summary>
    public static class PlayerProfile
    {
        public const int MaxLength = 16;
        private const string NicknameKey = "sb.nickname";

        private static string _nickname;

        public static string Nickname
        {
            get
            {
                if (_nickname != null) return _nickname;
                string saved = PlayerPrefs.GetString(NicknameKey, string.Empty);
                _nickname = Sanitize(saved);
                if (_nickname.Length == 0)
                {
                    _nickname = Loc.T("Игрок") + Random.Range(100, 1000);
                    PlayerPrefs.SetString(NicknameKey, _nickname);
                    PlayerPrefs.Save();
                }
                return _nickname;
            }
            set
            {
                string clean = Sanitize(value);
                if (clean.Length == 0 || clean == _nickname) return;
                _nickname = clean;
                PlayerPrefs.SetString(NicknameKey, clean);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Letters, digits, space, '_', '-', '.'; collapsed spaces; at most MaxLength characters.</summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(MaxLength);
            bool lastSpace = true;
            for (int i = 0; i < raw.Length && sb.Length < MaxLength; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                {
                    sb.Append(c);
                    lastSpace = false;
                }
                else if (c == ' ' && !lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            return sb.ToString().Trim();
        }
    }
}
