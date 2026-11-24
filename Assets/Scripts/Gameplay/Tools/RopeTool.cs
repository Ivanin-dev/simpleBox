using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Rope / Elastic, two steps like Weld but with exact attachment points:
    /// USE #1 on a prop: remember entity + local point. USE #2 on another prop (or static geometry when allowed):
    /// SandboxActions.CreateCable. Length = distance between the points * (1 + slack).
    /// Rope = max length (slack allowed); Elastic = spring back to that length.
    /// ALT: with a selection -> reset; without -> remove all constraints of the aimed prop.
    /// </summary>
    public sealed class RopeTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("Marker shown at the first point.")]
        [SerializeField] private Transform selectionMarker;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 20f;
        [Tooltip("Everything that can stop the ray (environment + props). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose Rigidbodies can be tied. Props only.")]
        [SerializeField] private LayerMask targetMask;
        [Tooltip("Static layers the second point may be on.")]
        [SerializeField] private LayerMask worldMask;
        [SerializeField, Min(0f)] private float cooldown = 0.3f;

        private const string HintIdle = "USE: точка 1 на пропе   ALT: снять связи с пропа";
        private const string HintFirst = "Точка 1 выбрана — выберите точку 2 (проп или мир)   ALT: сброс";
        private const string HintDone = "Связано";
        private const string HintSelf = "Нельзя связать объект с самим собой";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintInvalid = "Сюда привязать нельзя";
        private const string HintLost = "Объект 1 удалён — сброс";
        private const string HintReset = "Сброс";
        private const string HintRemoved = "Связи сняты";
        private const string HintNothing = "У пропа нет связей";
        private const string HintAlready = "Эти объекты уже связаны";
        private const string HintLimit = "Лимит связей";
        private const string HintNotAllowed = "Это чужой проп";

        private static readonly string[] ModeChoices = { "Верёвка", "Резинка" };
        private static readonly string[] SlackChoices = { "0%", "10%", "25%", "50%" };
        private static readonly float[] SlackValues = { 0f, 0.1f, 0.25f, 0.5f };

        private int _mode;
        private int _slackIndex = 1;
        private bool _allowWorld = true;

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
            else if (_allowWorld && hit.rigidbody == null && (worldMask.value & (1 << hit.collider.gameObject.layer)) != 0)
            {
                secondId = 0;
                anchorB = hit.point; // world point
            }
            else
            {
                SetHint(HintInvalid);
                return;
            }

            Vector3 firstWorld = first.transform.TransformPoint(_firstLocal);
            float length = Vector3.Distance(firstWorld, hit.point) * (1f + SlackValues[_slackIndex]);
            ConstraintKind kind = _mode == 0 ? ConstraintKind.Rope : ConstraintKind.Elastic;

            ActionResult result = actions.CreateCable(_firstId, _firstLocal, secondId, anchorB, kind, length);
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

        /// <summary>Layer filter + entity lookup (user action only).</summary>
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

        public override int SettingCount => 3;

        public override ToolSettingInfo GetSettingInfo(int index)
        {
            switch (index)
            {
                case 0: return ToolSettingInfo.Choice("Тип", ModeChoices);
                case 1: return ToolSettingInfo.Choice("Запас длины", SlackChoices);
                default: return ToolSettingInfo.Toggle("Привязка к миру");
            }
        }

        public override float GetSettingValue(int index)
        {
            switch (index)
            {
                case 0: return _mode;
                case 1: return _slackIndex;
                default: return _allowWorld ? 1f : 0f;
            }
        }

        public override void SetSettingValue(int index, float value)
        {
            switch (index)
            {
                case 0: _mode = Mathf.Clamp(Mathf.RoundToInt(value), 0, ModeChoices.Length - 1); break;
                case 1: _slackIndex = Mathf.Clamp(Mathf.RoundToInt(value), 0, SlackValues.Length - 1); break;
                default: _allowWorld = value >= 0.5f && worldMask.value != 0; break;
            }
        }
    }
}
