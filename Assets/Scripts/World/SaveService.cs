using SimpleBox.Core;
using System;
using System.IO;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Save slots for the local player's construction (everything they own): slot 0 = autosave, 1..3 = manual.
    /// Files: persistentDataPath/saves/slotN.json (Blueprint JSON, world space).
    /// Save = SandboxActions.RequestCapture(all mine) -> file. Load = remove own entities, spawn the blueprint
    /// (works on a multiplayer client too: capture / spawn run on the host).
    /// Autosave when the app is paused / closed (single player and host only: a client cannot capture synchronously).
    /// User actions only (file IO, allocations).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveService : MonoBehaviour
    {
        public const int AutosaveSlot = 0;
        public const int SlotCount = 4;

        [Header("References (required)")]
        [SerializeField] private SandboxActions actions;

        [Header("Autosave")]
        [SerializeField] private bool autosaveOnPause = true;

        private const int CaptureTag = 1;
        private const string MsgSaved = "Сохранено";
        private const string MsgNothing = "Нечего сохранять";
        private const string MsgLoaded = "Загружено";
        private const string MsgEmpty = "Слот пуст";
        private const string MsgError = "Ошибка сохранения";
        private const string MsgLoadError = "Не удалось загрузить";
        private const string MsgLoadSent = "Загрузка…";

        private int _pendingSlot = -1;
        private bool _valid;
        private bool _subscribed;

        /// <summary>Short status for the toast HUD.</summary>
        public event Action<string> Message;
        /// <summary>A slot file changed (save / delete): menus refresh their labels.</summary>
        public event Action SlotsChanged;

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError($"[{nameof(SaveService)}] '{name}': Actions not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            actions.CaptureReady += OnCaptureReady;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (actions != null) actions.CaptureReady -= OnCaptureReady;
            _subscribed = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Autosave();
        }

        private void OnApplicationQuit() => Autosave();

        // ------------------------------------------------------------------ Public API

        public void Save(int slot)
        {
            if (!_valid || slot <= AutosaveSlot || slot >= SlotCount) return;
            _pendingSlot = slot;
            actions.RequestCapture(0, 0f, CaptureTag);
        }

        public void Load(int slot)
        {
            if (!_valid || slot < 0 || slot >= SlotCount) return;
            string json = ReadSlot(slot);
            if (string.IsNullOrEmpty(json))
            {
                Message?.Invoke(MsgEmpty);
                return;
            }

            ActionResult result = actions.SpawnBlueprint(json, Vector3.zero, 0f, liftToFit: false, replaceOwned: true);
            Message?.Invoke(result == ActionResult.Ok ? MsgLoaded : result == ActionResult.Pending ? MsgLoadSent : MsgLoadError);
        }

        /// <summary>"12 объектов · 2026-11-23 21:40", or "пусто". Reads the file (menu open only).</summary>
        public string DescribeSlot(int slot)
        {
            string json = ReadSlot(slot);
            if (string.IsNullOrEmpty(json)) return Loc.T("пусто");
            try
            {
                Blueprint bp = JsonUtility.FromJson<Blueprint>(json);
                int count = bp != null && bp.entities != null ? bp.entities.Count : 0;
                return count + " " + Loc.T("об.") + " · " + bp?.savedAt;
            }
            catch (Exception)
            {
                return Loc.T("повреждён");
            }
        }

        // ------------------------------------------------------------------ Internals

        private void OnCaptureReady(int tag, string json)
        {
            if (tag != CaptureTag || _pendingSlot < 0) return;
            int slot = _pendingSlot;
            _pendingSlot = -1;

            if (string.IsNullOrEmpty(json))
            {
                Message?.Invoke(MsgNothing);
                return;
            }
            Message?.Invoke(WriteSlot(slot, json) ? MsgSaved : MsgError);
        }

        /// <summary>Writes the autosave slot now (host / offline only). Also used when returning to the main menu.</summary>
        public void Autosave()
        {
            if (!_valid || !autosaveOnPause || actions.IsRemoteClient) return;
            string json = actions.ExecuteCapture(actions.LocalOwnerId, 0, 0f);
            if (!string.IsNullOrEmpty(json)) WriteSlot(AutosaveSlot, json);
        }

        private static string SlotPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, "saves", $"slot{slot}.json");
        }

        private bool WriteSlot(int slot, string json)
        {
            try
            {
                string path = SlotPath(slot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);       // write then swap: a crash never leaves a half-written save
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                SlotsChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(SaveService)}] Save failed: {e.Message}", this);
                return false;
            }
        }

        private static string ReadSlot(int slot)
        {
            try
            {
                string path = SlotPath(slot);
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
