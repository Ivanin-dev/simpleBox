using SimpleBox.Controls;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Physgun as a regular tool. PhysicsGun reads no input; this tool is its only driver:
    /// USE = grab / release, ALT = freeze held prop / unfreeze aimed prop.
    /// While a prop is held: input.HoldMode = true -> ROTATE (or RMB) + swipe turns the prop, the distance strip
    /// (or mouse wheel) moves it closer / farther. Setting: rotation snap (free / 15° / 45°).
    /// Unequip drops the held prop (no momentum), so a prop never stays suspended while another tool is active.
    /// </summary>
    public sealed class PhysicsGunTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private PhysicsGun physicsGun;
        [SerializeField] private MobileInputController input;

        private const string HintIdle = "USE: взять / отпустить   ALT: заморозить / разморозить";
        private const string HintHolding = "ROTATE + свайп: вращать   полоса справа: дальше / ближе   ALT: заморозить";

        private static readonly string[] SnapChoices = { "Свободно", "15°", "45°" };
        private static readonly float[] SnapValues = { 0f, 15f, 45f };

        private int _snapIndex;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (physicsGun == null) error = "Physics Gun not assigned.";
            else if (input == null) error = "Input not assigned.";
            else if (!input.HasHoldWidgets) error = "MobileInputController has no Rotate Button / Distance Zone assigned.";
            return error == null;
        }

        protected override void OnEquip()
        {
            physicsGun.RotationSnap = SnapValues[_snapIndex];
            SetHint(HintIdle);
        }

        protected override void OnUnequip()
        {
            input.HoldMode = false;
            if (physicsGun != null) physicsGun.Drop();
        }

        public override void Tick(float deltaTime, bool primaryHeld)
        {
            bool holding = physicsGun.IsHolding;
            if (input.HoldMode != holding)
            {
                input.HoldMode = holding;
                SetHint(holding ? HintHolding : HintIdle);
            }

            // Consume every frame (also when not holding) so deltas never apply late.
            Vector2 rotation = input.ConsumeHeldRotationDegrees();
            float distance = input.ConsumeHoldDistanceDelta();
            if (!holding) return;

            physicsGun.RotateHeld(rotation);
            if (distance != 0f) physicsGun.AdjustHoldDistance(distance);
        }

        public override void OnPrimary()
        {
            if (physicsGun.isActiveAndEnabled) physicsGun.ToggleGrab();
        }

        public override void OnSecondary()
        {
            if (physicsGun.isActiveAndEnabled) physicsGun.ToggleFreeze();
        }

        // ------------------------------------------------------------------ Settings

        public override int SettingCount => 1;

        public override ToolSettingInfo GetSettingInfo(int index) => ToolSettingInfo.Choice("Шаг поворота", SnapChoices);

        public override float GetSettingValue(int index) => _snapIndex;

        public override void SetSettingValue(int index, float value)
        {
            _snapIndex = Mathf.Clamp(Mathf.RoundToInt(value), 0, SnapValues.Length - 1);
            physicsGun.RotationSnap = SnapValues[_snapIndex];
        }
    }
}
