using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Piston, two steps: USE #1 point on a prop, USE #2 point on another prop or static geometry.
    /// The piston starts at the current distance; while "Выдвинуть" is held it extends up to +Stroke,
    /// while "Втянуть" is held it retracts down to half the start length.
    /// ALT: with a selection -> reset; without -> remove all constraints of the aimed prop.
    /// </summary>
    public sealed class HydraulicTool : BaseTool
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

        private const string HintIdle = "USE: точка 1 на пропе, затем точка 2   ALT: снять связи";
        private const string HintFirst = "Точка 1 выбрана — выберите точку 2 (проп или мир)   ALT: сброс";
        private const string HintDone = "Поршень создан";
        private const string HintSelf = "Нельзя прикрепить объект к самому себе";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintInvalid = "Сюда прикрепить нельзя";
        private const string HintLost = "Объект 1 удалён — сброс";
        private const string HintReset = "Сброс";
        private const string HintRemoved = "Связи сняты";
        private const string HintNothing = "У пропа нет связей";
        private const string HintAlready = "Эти объекты уже связаны";
        private const string HintLimit = "Лимит связей";
        private const string HintNotAllowed = "Это чужой проп";
        private const string HintTooShort = "Точки слишком близко";

        private static readonly string[] BindingChoices = { "Нет", "1", "2", "3", "4" };

        private int _extend = 1;
        private int _retract = 2;
        private float _stroke = 1.5f;
        private float _speed = 1f;

        private int _firstId;
        private Vector3 _firstLocal;
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
            if (selectionMarker != null) selectionMarker.position = first.transform.TransformPoint(_firstLocal);
        }

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;
            if (!Raycast(out RaycastHit hit))
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
                _firstId = aimed.Id;
                _firstLocal = aimed.transform.InverseTransformPoint(hit.point);
                if (selectionMarker != null)
                {
                    selectionMarker.position = hit.point;
                    selectionMarker.gameObject.SetActive(true);
                }
                SetHint(HintFirst);
                return;
            }

            if (!actions.TryGetEntity(_firstId, out SandboxEntity first))
            {
                ClearSelection();
                SetHint(HintLost);
                return;
            }

            int secondId;
            Vector3 anchorB;
            if (aimed != null)
            {
                if (aimed.Id == _firstId)
                {
                    SetHint(HintSelf);
                    return;
                }
                secondId = aimed.Id;
                anchorB = aimed.transform.InverseTransformPoint(hit.point);
            }
            else if (hit.rigidbody == null && (worldMask.value & (1 << hit.collider.gameObject.layer)) != 0)
            {
                secondId = 0;
                anchorB = hit.point;
            }
            else
            {
                SetHint(HintInvalid);
                return;
            }

            float length = Vector3.Distance(first.transform.TransformPoint(_firstLocal), hit.point);
            if (length < 0.1f)
            {
                SetHint(HintTooShort);
                return;
            }

            var settings = new HydraulicSettings(_extend, _retract, length * 0.5f, length + _stroke, _speed);
            ActionResult result = actions.CreateHydraulic(_firstId, _firstLocal, secondId, anchorB, settings);
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
                case ActionResult.Invalid: SetHint(HintTooShort); break;
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
            if (!Raycast(out RaycastHit hit) || GetTarget(hit.rigidbody) is not SandboxEntity aimed)
            {
                SetHint(HintNoTarget);
                return;
            }
            ActionResult result = actions.RemoveConstraintsOf(aimed.Id);
            SetHint(result == ActionResult.Ok || result == ActionResult.Pending ? HintRemoved
                : result == ActionResult.NotAllowed ? HintNotAllowed : HintNothing);
        }

        private bool Raycast(out RaycastHit hit)
        {
            return Physics.Raycast(aimOrigin.position, aimOrigin.forward, out hit, range, raycastMask, QueryTriggerInteraction.Ignore);
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
                case 0: return ToolSettingInfo.Choice("Кнопка выдвинуть", BindingChoices);
                case 1: return ToolSettingInfo.Choice("Кнопка втянуть", BindingChoices);
                case 2: return ToolSettingInfo.Stepper("Ход", 0.5f, 4f, 0.5f, "0.0", " м");
                default: return ToolSettingInfo.Stepper("Скорость", 0.25f, 3f, 0.25f, "0.00", " м/с");
            }
        }

        public override float GetSettingValue(int index)
        {
            switch (index)
            {
                case 0: return _extend;
                case 1: return _retract;
                case 2: return _stroke;
                default: return _speed;
            }
        }

        public override void SetSettingValue(int index, float value)
        {
            switch (index)
            {
                case 0: _extend = Mathf.Clamp(Mathf.RoundToInt(value), 0, InputBindings.Count); break;
                case 1: _retract = Mathf.Clamp(Mathf.RoundToInt(value), 0, InputBindings.Count); break;
                case 2: _stroke = Mathf.Clamp(value, 0.5f, 4f); break;
                default: _speed = Mathf.Clamp(value, 0.25f, 3f); break;
            }
        }
    }
}
