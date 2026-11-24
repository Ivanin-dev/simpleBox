using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SimpleBox.Core
{
    /// <summary>
    /// Players blocked by the local user, keyed by their Unity Authentication player id (stable across sessions),
    /// stored in PlayerPrefs. Reports are appended to persistentDataPath/reports.log and, if a support address is set,
    /// handed to the mail app (mailto) so the developer receives them.
    /// </summary>
    public static class BlockList
    {
        private const string Key = "sb.blocked";
        private const char Separator = '\n';

        private static HashSet<string> _ids;

        /// <summary>Raised after a block / unblock.</summary>
        public static event Action Changed;

        public static bool IsBlocked(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            Load();
            return _ids.Contains(playerId);
        }

        public static void SetBlocked(string playerId, bool blocked)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            Load();
            bool changed = blocked ? _ids.Add(playerId) : _ids.Remove(playerId);
            if (!changed) return;
            PlayerPrefs.SetString(Key, string.Join(Separator.ToString(), _ids));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static int Count
        {
            get
            {
                Load();
                return _ids.Count;
            }
        }

        public static void Clear()
        {
            Load();
            if (_ids.Count == 0) return;
            _ids.Clear();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>
        /// Logs a report locally and opens the mail app addressed to supportEmail (if not empty) with the details.
        /// Returns true if the mail app was asked to open.
        /// </summary>
        public static bool Report(string nickname, string playerId, string sessionCode, string supportEmail)
        {
            string time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
            string line = $"{time}\t{nickname}\t{playerId}\t{sessionCode}";
            try
            {
                File.AppendAllText(Path.Combine(Application.persistentDataPath, "reports.log"), line + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(BlockList)}] Could not write the report log: {e.Message}");
            }

            if (string.IsNullOrWhiteSpace(supportEmail)) return false;
            string subject = Uri.EscapeDataString(Loc.T("SimpleBox: жалоба на игрока"));
            string body = Uri.EscapeDataString(
                $"{Loc.T("Игрок")}: {nickname}\nID: {playerId}\n{Loc.T("Сессия")}: {sessionCode}\n{Loc.T("Время")}: {time}\n" +
                $"{Loc.T("Версия")}: {Application.version}\n\n{Loc.T("Опишите, что произошло:")}\n");
            Application.OpenURL($"mailto:{supportEmail.Trim()}?subject={subject}&body={body}");
            return true;
        }

        private static void Load()
        {
            if (_ids != null) return;
            _ids = new HashSet<string>(StringComparer.Ordinal);
            string raw = PlayerPrefs.GetString(Key, string.Empty);
            if (raw.Length == 0) return;
            foreach (string id in raw.Split(Separator))
            {
                if (id.Length > 0) _ids.Add(id);
            }
        }
    }
}
