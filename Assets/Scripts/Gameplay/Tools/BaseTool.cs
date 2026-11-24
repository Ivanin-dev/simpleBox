using SimpleBox.Core;
using System;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Base for every tool (Remover, Physgun adapter, later Thruster / Weld / Rope / Explosives).
    /// A tool never reads input itself and has no Update: ToolManager drives it through
    /// OnEquip / OnUnequip / Tick / OnPrimary / FixedTick, only while it is the active tool.
    /// Each tool owns its Inspector references and validates them in ValidateReferences().
    /// </summary>
    public abstract class BaseTool : MonoBehaviour
    {
        [Header("Tool")]
        [SerializeField] private string displayName = "Tool";
        [Tooltip("Optional HUD icon.")]
        [SerializeField] private Sprite icon;

        public string DisplayName => displayName;
        public Sprite Icon => icon;

        /// <summary>False if required references are missing; ToolManager skips such tools.</summary>
        public bool IsReady { get; private set; }

        /// <summary>True between OnEquip and OnUnequip.</summary>
        public bool IsEquipped { get; private set; }

        /// <summary>Short status / instruction line for the HUD. Use constant strings (no per-frame formatting).</summary>
        public string Hint { get; private set; } = string.Empty;

        /// <summary>Raised when Hint changes.</summary>
        public event Action HintChanged;

        private string _hintKey;

        protected void SetHint(string hint)
        {
            if (hint == null) hint = string.Empty;
            if (ReferenceEquals(hint, _hintKey)) return;
            _hintKey = hint;
            Hint = Loc.T(hint); // Russian source -> current language (cached string, no allocation)
            HintChanged?.Invoke();
        }

        protected virtual void Awake()
        {
            if (!ValidateReferences(out string error))
            {
                Debug.LogError($"[{GetType().Name}] '{name}': {error} Tool disabled.", this);
                IsReady = false;
                enabled = false;
                return;
            }
            IsReady = true;
        }

        /// <summary>Return false with a readable message when a required reference is missing.</summary>
        protected abstract bool ValidateReferences(out string error);

        internal void Equip()
        {
            if (IsEquipped) return;
            IsEquipped = true;
            OnEquip();
        }

        internal void Unequip()
        {
            if (!IsEquipped) return;
            IsEquipped = false;
            OnUnequip();
        }

        /// <summary>Tool became active.</summary>
        protected virtual void OnEquip() { }

        /// <summary>Tool stops being active. Must leave no lingering state (highlights, held objects owned by the tool).</summary>
        protected virtual void OnUnequip() { }

        /// <summary>Every frame while equipped. primaryHeld = Use button/key currently held.</summary>
        public virtual void Tick(float deltaTime, bool primaryHeld) { }

        /// <summary>Every physics step while equipped.</summary>
        public virtual void FixedTick(float fixedDeltaTime) { }

        /// <summary>Use button / tap pressed this frame.</summary>
        public abstract void OnPrimary();

        /// <summary>ALT button / X key pressed this frame. Meaning is tool-specific (freeze, reset, toggle...).</summary>
        public virtual void OnSecondary() { }

        // ------------------------------------------------------------------ Settings (tool options panel)

        /// <summary>Number of settings shown in the tool options panel (0 = the ⚙ button is hidden).</summary>
        public virtual int SettingCount => 0;

        public virtual ToolSettingInfo GetSettingInfo(int index) => default;

        public virtual float GetSettingValue(int index) => 0f;

        /// <summary>Value is already clamped / snapped to the setting's range by the panel.</summary>
        public virtual void SetSettingValue(int index, float value) { }
    }
}
