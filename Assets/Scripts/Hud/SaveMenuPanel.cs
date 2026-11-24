using System;
using SimpleBox.Controls;
using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Save menu: SAVE opens it; one row per slot (0 = autosave: load only) with its description,
    /// SAVE and LOAD buttons. Labels are refreshed when the menu opens or a slot changes (file read, user action).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SaveMenuPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class SlotRow
        {
            public Text label;
            [Tooltip("Empty for the autosave row.")]
            public TouchButton saveButton;
            public TouchButton loadButton;
        }

        [Header("References (required)")]
        [SerializeField] private SaveService saves;
        [SerializeField] private TouchButton toggleButton;
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Index = slot (0 = autosave).")]
        [SerializeField] private SlotRow[] rows = new SlotRow[0];

        [Header("References (optional)")]
        [SerializeField] private TouchButton closeButton;

        private static readonly string[] SlotNames = { "Авто", "Слот 1", "Слот 2", "Слот 3" };

        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            _valid = saves != null && toggleButton != null && panelRoot != null && rows != null && rows.Length > 0;
            for (int i = 0; _valid && i < rows.Length; i++)
            {
                if (rows[i] == null || rows[i].label == null || rows[i].loadButton == null) _valid = false;
            }
            if (!_valid)
            {
                Debug.LogError($"[{nameof(SaveMenuPanel)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            saves.SlotsChanged += Refresh;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (saves != null) saves.SlotsChanged -= Refresh;
            _subscribed = false;
        }

        private void Update()
        {
            if (toggleButton.ConsumePress())
            {
                bool open = !panelRoot.activeSelf;
                panelRoot.SetActive(open);
                if (open) Refresh();
            }
            if (closeButton != null && closeButton.isActiveAndEnabled && closeButton.ConsumePress()) panelRoot.SetActive(false);
            if (!panelRoot.activeSelf) return;

            for (int slot = 0; slot < rows.Length; slot++)
            {
                SlotRow row = rows[slot];
                if (row.saveButton != null && row.saveButton.ConsumePress()) saves.Save(slot);
                if (row.loadButton.ConsumePress())
                {
                    saves.Load(slot);
                    panelRoot.SetActive(false);
                }
            }
        }

        private void Refresh()
        {
            if (!panelRoot.activeSelf) return;
            for (int slot = 0; slot < rows.Length; slot++)
            {
                string name = slot < SlotNames.Length ? SlotNames[slot] : "Слот " + slot;
                rows[slot].label.text = name + ": " + saves.DescribeSlot(slot);
            }
        }
    }
}
