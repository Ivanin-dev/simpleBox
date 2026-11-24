using System;
using SimpleBox.Controls;
using SimpleBox.Core;
using SimpleBox.Net;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Players in the current session (store requirement for user-generated content): one row per player with
    /// ЖАЛОБА (logs + mail to support, also blocks), БЛОК / РАЗБЛОК (local: hides their body, name and props; the host
    /// also kicks and refuses them later) and КИК (host only). The local player's row is marked "(ты)" without buttons.
    /// Rows are refreshed on player / block changes only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayersPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Row
        {
            public GameObject root;
            public Text nameLabel;
            public TouchButton reportButton;
            public TouchButton blockButton;
            public Text blockLabel;
            public TouchButton kickButton;
        }

        [Header("References (required)")]
        [SerializeField] private NetworkGameplay gameplay;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Row[] rows = new Row[0];

        [Header("References (optional)")]
        [SerializeField] private TouchButton closeButton;
        [SerializeField] private Text statusLabel;
        [SerializeField] private Text emptyLabel;

        [Header("Reports")]
        [Tooltip("Support address that receives reports (mail app opens prefilled). Empty = reports are only logged locally.")]
        [SerializeField] private string supportEmail = string.Empty;

        private const string BlockText = "БЛОК";
        private const string UnblockText = "РАЗБЛОК";
        private const string SelfSuffix = "  (ты)";
        private const string HostSuffix = "  [хост]";

        private bool _valid;
        private bool _subscribed;

        public bool IsOpen => _valid && panelRoot.activeSelf;

        private void Awake()
        {
            _valid = gameplay != null && panelRoot != null && rows != null && rows.Length > 0;
            for (int i = 0; _valid && i < rows.Length; i++)
            {
                Row r = rows[i];
                if (r == null || r.root == null || r.nameLabel == null || r.reportButton == null || r.blockButton == null ||
                    r.blockLabel == null || r.kickButton == null) _valid = false;
            }
            if (!_valid)
            {
                Debug.LogError($"[{nameof(PlayersPanel)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            gameplay.PlayersChanged += Refresh;
            BlockList.Changed += Refresh;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (gameplay != null) gameplay.PlayersChanged -= Refresh;
            BlockList.Changed -= Refresh;
            _subscribed = false;
        }

        public void Open()
        {
            if (!_valid) return;
            panelRoot.SetActive(true);
            if (statusLabel != null) statusLabel.text = string.Empty;
            Refresh();
        }

        private void Update()
        {
            if (!panelRoot.activeSelf) return;
            if (closeButton != null && closeButton.ConsumePress())
            {
                panelRoot.SetActive(false);
                return;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                Row r = rows[i];
                NetworkPlayerAvatar p = i < gameplay.PlayerCount ? gameplay.GetPlayer(i) : null;
                bool report = r.reportButton.ConsumePress();
                bool block = r.blockButton.ConsumePress();
                bool kick = r.kickButton.ConsumePress();
                if (p == null || p.IsLocalPlayer) continue;

                if (report) Report(p);
                else if (block) ToggleBlock(p);
                else if (kick && gameplay.IsHost)
                {
                    gameplay.Kick(p);
                    SetStatus(Loc.F("{0} исключён", p.Nickname));
                }
            }
        }

        private void Report(NetworkPlayerAvatar p)
        {
            bool mailed = BlockList.Report(p.Nickname, p.AuthId, gameplay.SessionCode, supportEmail);
            Block(p, true);
            SetStatus(mailed
                ? Loc.F("Жалоба на {0}: допиши письмо и отправь. Игрок заблокирован", p.Nickname)
                : Loc.F("Жалоба на {0} записана. Игрок заблокирован", p.Nickname));
        }

        private void ToggleBlock(NetworkPlayerAvatar p)
        {
            bool blocked = !BlockList.IsBlocked(p.AuthId);
            Block(p, blocked);
            SetStatus(Loc.F(blocked ? "{0} заблокирован" : "{0} разблокирован", p.Nickname));
        }

        private void Block(NetworkPlayerAvatar p, bool blocked)
        {
            if (string.IsNullOrEmpty(p.AuthId))
            {
                SetStatus(Loc.T("Профиль игрока ещё не загружен, попробуй через секунду"));
                return;
            }
            BlockList.SetBlocked(p.AuthId, blocked);
            if (blocked && gameplay.IsHost) gameplay.Kick(p); // the host cannot "not see" a player in its own world
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.text = text;
        }

        private void Refresh()
        {
            if (!_valid || !panelRoot.activeSelf) return;
            int count = gameplay.PlayerCount;
            bool host = gameplay.IsHost;
            for (int i = 0; i < rows.Length; i++)
            {
                Row r = rows[i];
                NetworkPlayerAvatar p = i < count ? gameplay.GetPlayer(i) : null;
                bool visible = p != null;
                if (r.root.activeSelf != visible) r.root.SetActive(visible);
                if (!visible) continue;

                bool self = p.IsLocalPlayer;
                bool isHostPlayer = p.PlayerOwnerId == 0;
                r.nameLabel.text = p.Nickname + Loc.T(self ? SelfSuffix : isHostPlayer ? HostSuffix : string.Empty);
                r.reportButton.gameObject.SetActive(!self);
                r.blockButton.gameObject.SetActive(!self);
                r.kickButton.gameObject.SetActive(!self && host);
                r.blockLabel.text = BlockList.IsBlocked(p.AuthId) ? Loc.T(UnblockText) : Loc.T(BlockText);
            }
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(count == 0);
        }
    }
}
