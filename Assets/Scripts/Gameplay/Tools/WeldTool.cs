using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Two-step weld request.
    /// USE #1 on a prop: remember its entity id (marker shown at the hit point).
    /// USE #2 on another prop: SandboxActions.Weld(a, b). On static geometry (no Rigidbody, worldMask) when
    /// allowWorldWeld: weld to the world (b = 0).
    /// ALT: with a selection -> reset; without -> remove all constraints of the aimed prop.
    /// Protection: no self-weld, duplicate pair / per-entity / session limits enforced by SandboxActions,
    /// cooldown after each weld, selection dropped automatically when entity 1 no longer exists
    /// (removed, undone, recycled: ids are never reused).
    /// </summary>
    public sealed class WeldTool : BaseTool
    {
        [Header("References (required)")]
        [Tooltip("Camera transform: ray origin and direction.")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("Small marker object (no collider) shown at the first selected point.")]
        [SerializeField] private Transform selectionMarker;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 15f;
        [Tooltip("Everything that can stop the ray (environment + props). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose Rigidbodies can be welded. Props only.")]
        [SerializeField] private LayerMask weldableMask;
        [Tooltip("Static layers a prop can be welded to (second click).")]
        [SerializeField] private LayerMask worldMask;
        [SerializeField] private bool allowWorldWeld = true;

        [Header("Joint")]
        [Tooltip("Newtons. 0 = unbreakable.")]
        [SerializeField, Min(0f)] private float breakForce = 30000f;
        [Tooltip("Newton-meters. 0 = unbreakable.")]
        [SerializeField, Min(0f)] private float breakTorque = 30000f;
        [Tooltip("Off is more stable for chains of joints and conflicting constraints.")]
        [SerializeField] private bool enablePreprocessing;

        [Header("Behaviour")]
        [SerializeField, Min(0f)] private float cooldown = 0.3f;

        private const string HintIdle = "USE: выбрать объект 1   ALT: снять сварку с пропа";
        private const string HintFirstSelected = "Объект 1 выбран — выберите второй (ALT — сброс)";
        private const string HintWelded = "Сварено";
        private const string HintWeldedWorld = "Приварено к миру";
        private const string HintSelf = "Нельзя сварить объект с самим собой";
        private const string HintAlready = "Эти объекты уже сварены";
        private const string HintEntityLimit = "Лимит сварок у пропа";
        private const string HintServerLimit = "Лимит сварок на сцене";
        private const string HintNotAllowed = "Это чужой проп";
        private const string HintReset = "Сброс";
        private const string HintFirstLost = "Объект 1 удалён — сброс";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintInvalidSecond = "Сюда приварить нельзя";
        private const string HintUnwelded = "Сварка снята";
        private const string HintNothingToUnweld = "У пропа нет сварки";

        // Strength setting: Normal = Inspector values, Strong = x4, Unbreakable = 0 (WeldSettings treats 0 as infinite).
        private static readonly string[] StrengthChoices = { "Обычная", "Прочная", "Неразрывная" };
        private static readonly float[] StrengthMultipliers = { 1f, 4f, 0f };

        private int _strengthIndex;
        private int _firstId;
        private Vector3 _firstLocalPoint;
        private float _nextUseTime;

        public bool HasSelection => _firstId != 0;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (weldableMask.value == 0) error = "Weldable Mask is empty.";
            else if ((weldableMask.value & (1 << aimOrigin.root.gameObject.layer)) != 0)
                error = $"Weldable Mask contains the player layer '{LayerMask.LayerToName(aimOrigin.root.gameObject.layer)}'.";
            else if (allowWorldWeld && worldMask.value == 0) error = "World Mask is empty while Allow World Weld is on.";
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
                SetHint(HintFirstLost);
                return;
            }

            if (selectionMarker != null) selectionMarker.position = first.transform.TransformPoint(_firstLocalPoint);
        }

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;

            if (_firstId != 0 && !actions.TryGetEntity(_firstId, out _))
            {
                ClearSelection();
                SetHint(HintFirstLost);
                return;
            }

            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range,
                    raycastMask, QueryTriggerInteraction.Ignore))
            {
                SetHint(HintNoTarget);
                return;
            }

            SandboxEntity aimed = GetWeldableEntity(hit.rigidbody);

            // ---- step 1
            if (_firstId == 0)
            {
                if (aimed == null)
                {
                    SetHint(HintNoTarget);
                    return;
                }
                Select(aimed, hit.point);
                SetHint(HintFirstSelected);
                return;
            }

            // ---- step 2
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
            else if (hit.rigidbody == null && allowWorldWeld && (worldMask.value & (1 << hit.collider.gameObject.layer)) != 0)
            {
                secondId = 0; // world
            }
            else
            {
                SetHint(HintInvalidSecond);
                return;
            }

            float strengthMul = StrengthMultipliers[_strengthIndex];
            var settings = new WeldSettings(breakForce * strengthMul, breakTorque * strengthMul, enablePreprocessing);
            ActionResult result = actions.Weld(_firstId, secondId, settings);
            ClearSelection();

            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending:
                    _nextUseTime = now + cooldown;
                    SetHint(secondId != 0 ? HintWelded : HintWeldedWorld);
                    break;
                case ActionResult.AlreadyExists: SetHint(HintAlready); break;
                case ActionResult.EntityLimit: SetHint(HintEntityLimit); break;
                case ActionResult.ServerLimit: SetHint(HintServerLimit); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                case ActionResult.Invalid: SetHint(HintSelf); break;
                default: SetHint(HintFirstLost); break;
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

            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range,
                    raycastMask, QueryTriggerInteraction.Ignore))
            {
                SetHint(HintNoTarget);
                return;
            }

            SandboxEntity aimed = GetWeldableEntity(hit.rigidbody);
            if (aimed == null)
            {
                SetHint(HintNoTarget);
                return;
            }

            ActionResult result = actions.RemoveConstraintsOf(aimed.Id);
            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending: SetHint(HintUnwelded); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                default: SetHint(HintNothingToUnweld); break;
            }
        }

        // ------------------------------------------------------------------ Settings

        public override int SettingCount => 2;

        public override ToolSettingInfo GetSettingInfo(int index)
        {
            return index == 0
                ? ToolSettingInfo.Choice("Прочность", StrengthChoices)
                : ToolSettingInfo.Toggle("Сварка к миру");
        }

        public override float GetSettingValue(int index) => index == 0 ? _strengthIndex : (allowWorldWeld ? 1f : 0f);

        public override void SetSettingValue(int index, float value)
        {
            if (index == 0)
            {
                _strengthIndex = Mathf.Clamp(Mathf.RoundToInt(value), 0, StrengthChoices.Length - 1);
                return;
            }
            bool allow = value >= 0.5f;
            if (allow && worldMask.value == 0) return; // world weld impossible without a world mask
            allowWorldWeld = allow;
        }

        /// <summary>Layer filter + entity lookup (user action only).</summary>
        private SandboxEntity GetWeldableEntity(Rigidbody rb)
        {
            if (rb == null || (weldableMask.value & (1 << rb.gameObject.layer)) == 0) return null;
            SandboxEntity entity = SandboxEntity.FromBody(rb);
            return entity != null && entity.IsRegistered ? entity : null;
        }

        private void Select(SandboxEntity entity, Vector3 point)
        {
            _firstId = entity.Id;
            _firstLocalPoint = entity.transform.InverseTransformPoint(point);

            if (selectionMarker != null)
            {
                selectionMarker.position = point;
                selectionMarker.gameObject.SetActive(true);
            }
        }

        private void ClearSelection()
        {
            _firstId = 0;
            if (selectionMarker != null && selectionMarker.gameObject.activeSelf) selectionMarker.gameObject.SetActive(false);
        }
    }
}
