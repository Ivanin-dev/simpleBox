using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Axis (wheel / hinge), two steps:
    /// USE #1 on the wheel prop: point + surface normal = hinge axis (local to the wheel).
    /// USE #2 on the base prop (chassis) or static geometry: SandboxActions.CreateAxis.
    /// Motor: spins forward while binding "Вперёд" is held, backward while "Назад" is held (DriveMode bindings 1..4,
    /// arrows on desktop). "Выравнивать ось" flips the axis to point to the player's right, so wheels on both sides of
    /// a car (placed while facing the same way) spin the same direction with the same bindings.
    /// ALT: with a selection -> reset; without -> remove all constraints of the aimed prop.
    /// </summary>
    public sealed class AxisTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [SerializeField] private Transform selectionMarker;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 20f;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private LayerMask worldMask;
        [SerializeField, Min(0f)] private float cooldown = 0.3f;

        private const string HintIdle = "USE: колесо (точка = ось)   затем основа   ALT: снять связи";
        private const string HintFirst = "Колесо выбрано — выберите основу (проп или мир)   ALT: сброс";
        private const string HintDone = "Ось создана";
        private const string HintSelf = "Нельзя прикрепить объект к самому себе";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintInvalid = "Сюда прикрепить нельзя";
        private const string HintLost = "Колесо удалено — сброс";
        private const string HintReset = "Сброс";
        private const string HintRemoved = "Связи сняты";
        private const string HintNothing = "У пропа нет связей";
        private const string HintAlready = "Эти объекты уже связаны";
        private const string HintLimit = "Лимит связей";
        private const string HintNotAllowed = "Это чужой проп";

        private static readonly string[] BindingChoices = { "Нет", "1", "2", "3", "4" };
        private const float SpeedMin = 100f;
        private const float SpeedMax = 2000f;
        private const float ForceMin = 50f;
        private const float ForceMax = 2000f;

        private int _forward = 1;
        private int _backward = 2;
        private float _speed = 600f;
        private float _force = 300f;
        private bool _alignAxis = true;

        private int _firstId;
        private Vector3 _firstLocalPoint;
        private Vector3 _firstLocalAxis;
        private float _nextUseTime;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (targetMask.value == 0) error = "Target Mask is empty.";
            else if ((targetMask.value & (1 << aimOrigin.root.gameObject.layer)) != 0) error = "Target Mask contains the player layer.";
            return error == null;
        }

        protected override void OnEquip()
        {
            ClearSelection();
            SetHint(HintIdle);
        }

        protected override void OnUnequip() => ClearSelection();

        public override void Tick(float deltaTime, bool primaryHeld)
        {
            if (_firstId == 0) return;
            if (!actions.TryGetEntity(_firstId, out SandboxEntity first) || !first.gameObject.activeInHierarchy)
            {
                ClearSelection();
                SetHint(HintLost);
                return;
            }
            if (selectionMarker != null) selectionMarker.position = first.transform.TransformPoint(_firstLocalPoint);
        }

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore))
            {
                SetHint(HintNoTarget);
                return;
            }

            SandboxEntity aimed = GetTarget(hit.rigidbody);

            if (_firstId == 0)
            {
                if (aimed == null)
                {
                    SetHint(HintNoTarget);
                    return;
                }
                Vector3 axisWorld = hit.normal;
                if (_alignAxis && Vector3.Dot(axisWorld, aimOrigin.right) < 0f) axisWorld = -axisWorld;

                Transform t = aimed.transform;
                _firstId = aimed.Id;
                _firstLocalPoint = t.InverseTransformPoint(hit.point);
                _firstLocalAxis = t.InverseTransformDirection(axisWorld);
                if (selectionMarker != null)
                {
                    selectionMarker.position = hit.point;
                    selectionMarker.gameObject.SetActive(true);
                }
                SetHint(HintFirst);
                return;
            }

            int secondId;
            if (aimed != null)
            {
                if (aimed.Id == _firstId)
                {
                    SetHint(HintSelf);
                    return;
                }
                secondId = aimed.Id;
            }
            else if (hit.rigidbody == null && (worldMask.value & (1 << hit.collider.gameObject.layer)) != 0)
            {
                secondId = 0;
            }
            else
            {
                SetHint(HintInvalid);
                return;
            }

            var motor = new AxisMotorSettings(_forward, _backward, _speed, _force);
            ActionResult result = actions.CreateAxis(_firstId, _firstLocalPoint, _firstLocalAxis, secondId, motor);
            ClearSelection();
            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending:
                    _nextUseTime = now + cooldown;
                    SetHint(HintDone);
                    break;
                case ActionResult.AlreadyExists: SetHint(HintAlready); break;
                case ActionResult.EntityLimit:
                case ActionResult.ServerLimit: SetHint(HintLimit); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                case ActionResult.Invalid: SetHint(HintSelf); break;
                default: SetHint(HintLost); break;
            }
        }

        public override void OnSecondary()
        {
            if (_firstId != 0)
            {
                ClearSelection();
                SetHint(HintReset);
                return;
            }
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore) ||
                GetTarget(hit.rigidbody) is not SandboxEntity aimed)
            {
                SetHint(HintNoTarget);
                return;
            }
            ActionResult result = actions.RemoveConstraintsOf(aimed.Id);
            SetHint(result == ActionResult.Ok || result == ActionResult.Pending ? HintRemoved
                : result == ActionResult.NotAllowed ? HintNotAllowed : HintNothing);
        }

        private SandboxEntity GetTarget(Rigidbody rb)
        {
            if (rb == null || (targetMask.value & (1 << rb.gameObject.layer)) == 0) return null;
            SandboxEntity e = SandboxEntity.FromBody(rb);
            return e != null && e.IsRegistered ? e : null;
        }

        private void ClearSelection()
        {
            _firstId = 0;
            if (selectionMarker != null && selectionMarker.gameObject.activeSelf) selectionMarker.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ Settings

        public override int SettingCount => 4;

        public override ToolSettingInfo GetSettingInfo(int index)
        {
            switch (index)
            {
                case 0: return ToolSettingInfo.Choice("Кнопка вперёд", BindingChoices);
                case 1: return ToolSettingInfo.Choice("Кнопка назад", BindingChoices);
                case 2: return ToolSettingInfo.Stepper("Скорость", SpeedMin, SpeedMax, 100f, "0", " °/с");
                default: return ToolSettingInfo.Stepper("Сила мотора", ForceMin, ForceMax, 50f, "0", "");
            }
        }

        public override float GetSettingValue(int index)
        {
            switch (index)
            {
                case 0: return _forward;
                case 1: return _backward;
                case 2: return _speed;
                default: return _force;
            }
        }

        public override void SetSettingValue(int index, float value)
        {
            switch (index)
            {
                case 0: _forward = Mathf.Clamp(Mathf.RoundToInt(value), 0, InputBindings.Count); break;
                case 1: _backward = Mathf.Clamp(Mathf.RoundToInt(value), 0, InputBindings.Count); break;
                case 2: _speed = Mathf.Clamp(value, SpeedMin, SpeedMax); break;
                default: _force = Mathf.Clamp(value, ForceMin, ForceMax); break;
            }
        }
    }
}
