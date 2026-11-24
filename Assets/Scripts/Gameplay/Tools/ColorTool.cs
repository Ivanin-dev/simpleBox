using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Paint. USE: paint the aimed prop with the selected palette color. ALT: restore its original materials.
    /// Setting: color (palette names). The palette lives in SandboxActions (shared materials: SRP batching stays intact),
    /// the color index is replicated and saved. Optional swatch graphic shows the selected color.
    /// </summary>
    public sealed class ColorTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("Tinted with the selected color while the tool is active (e.g. the crosshair).")]
        [SerializeField] private Graphic swatch;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 25f;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private LayerMask targetMask;

        [Header("Palette names (index = palette material)")]
        [SerializeField] private string[] colorNames =
        {
            "Белый", "Чёрный", "Серый", "Красный", "Оранжевый", "Жёлтый",
            "Зелёный", "Голубой", "Синий", "Фиолетовый", "Розовый", "Коричневый"
        };

        private const string HintIdle = "USE: покрасить   ALT: вернуть исходный цвет   SET: выбрать цвет";
        private const string HintPainted = "Покрашено";
        private const string HintReset = "Исходный цвет";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintNotAllowed = "Это чужой проп";

        private int _color = 3; // red
        private Color _swatchDefault;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (targetMask.value == 0) error = "Target Mask is empty.";
            else if (actions.ColorPalette == null || actions.ColorPalette.Length == 0) error = "SandboxActions has an empty Color Palette.";
            return error == null;
        }

        protected override void OnEquip()
        {
            _color = Mathf.Clamp(_color, 0, PaletteCount - 1);
            if (swatch != null) _swatchDefault = swatch.color;
            UpdateSwatch();
            SetHint(HintIdle);
        }

        protected override void OnUnequip()
        {
            if (swatch != null) swatch.color = _swatchDefault;
        }

        public override void OnPrimary() => Paint(_color, HintPainted);
        public override void OnSecondary() => Paint(-1, HintReset);

        private void Paint(int colorIndex, string okHint)
        {
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore) ||
                hit.rigidbody == null || (targetMask.value & (1 << hit.rigidbody.gameObject.layer)) == 0)
            {
                SetHint(HintNoTarget);
                return;
            }
            SandboxEntity entity = SandboxEntity.FromBody(hit.rigidbody); // user action only
            if (entity == null || !entity.IsRegistered)
            {
                SetHint(HintNoTarget);
                return;
            }

            ActionResult result = actions.SetColor(entity.Id, colorIndex);
            SetHint(result == ActionResult.Ok || result == ActionResult.Pending ? okHint
                : result == ActionResult.NotAllowed ? HintNotAllowed : HintNoTarget);
        }

        private int PaletteCount => Mathf.Min(actions.ColorPalette.Length, colorNames.Length);

        private void UpdateSwatch()
        {
            if (swatch == null) return;
            Material m = actions.ColorPalette[_color];
            if (m != null && m.HasProperty("_BaseColor")) swatch.color = m.GetColor("_BaseColor");
        }

        // ------------------------------------------------------------------ Settings

        public override int SettingCount => 1;

        public override ToolSettingInfo GetSettingInfo(int index) => ToolSettingInfo.Choice("Цвет", colorNames);

        public override float GetSettingValue(int index) => _color;

        public override void SetSettingValue(int index, float value)
        {
            _color = Mathf.Clamp(Mathf.RoundToInt(value), 0, PaletteCount - 1);
            UpdateSwatch();
        }
    }
}
