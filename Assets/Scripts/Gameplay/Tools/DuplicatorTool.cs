using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Duplicator. ALT on a prop: copy it with everything welded / tied / hinged to it (and its thrusters) into the
    /// tool's clipboard (blueprint relative to the prop and the player's yaw). USE: paste the copy where you aim,
    /// resting on the surface, rotated with your view; it is lifted until it fits and placed through SandboxActions
    /// (limits, ownership, Undo, multiplayer). World anchors are not copied.
    /// </summary>
    public sealed class DuplicatorTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 25f;
        [Tooltip("Environment + props. Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose props can be copied. Props only.")]
        [SerializeField] private LayerMask targetMask;
        [SerializeField, Min(0f)] private float cooldown = 0.5f;

        private const int CaptureTag = 2;
        private const string HintEmpty = "ALT: скопировать постройку под прицелом";
        private const string HintCopying = "Копирование…";
        private const string HintCopied = "Скопировано. USE: вставить, ALT: скопировать другую";
        private const string HintNothing = "Нечего копировать";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintNoSurface = "Наведите на поверхность";
        private const string HintPasted = "Вставлено";
        private const string HintNoSpace = "Не помещается";
        private const string HintLimit = "Лимит пропов";
        private const string HintFailed = "Не удалось вставить";

        private string _clipboard = string.Empty;
        private float _minLocalY;
        private float _nextUseTime;
        private bool _subscribed;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (targetMask.value == 0) error = "Target Mask is empty.";
            return error == null;
        }

        protected override void Awake()
        {
            base.Awake();
            if (!IsReady) return;
            actions.CaptureReady += OnCaptureReady;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed && actions != null) actions.CaptureReady -= OnCaptureReady;
        }

        protected override void OnEquip() => SetHint(_clipboard.Length > 0 ? HintCopied : HintEmpty);

        public override void OnSecondary()
        {
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore))
            {
                SetHint(HintNoTarget);
                return;
            }
            Rigidbody rb = hit.rigidbody;
            if (rb == null || (targetMask.value & (1 << rb.gameObject.layer)) == 0)
            {
                SetHint(HintNoTarget);
                return;
            }
            SandboxEntity entity = SandboxEntity.FromBody(rb); // user action only
            if (entity == null || !entity.IsRegistered)
            {
                SetHint(HintNoTarget);
                return;
            }

            SetHint(HintCopying);
            actions.RequestCapture(entity.Id, aimOrigin.eulerAngles.y, CaptureTag);
        }

        private void OnCaptureReady(int tag, string json)
        {
            if (tag != CaptureTag) return;
            if (string.IsNullOrEmpty(json))
            {
                if (IsEquipped) SetHint(HintNothing);
                return;
            }

            Blueprint bp = JsonUtility.FromJson<Blueprint>(json);
            if (bp == null || bp.entities == null || bp.entities.Count == 0)
            {
                if (IsEquipped) SetHint(HintNothing);
                return;
            }
            _clipboard = json;
            _minLocalY = bp.minLocalY;
            if (IsEquipped) SetHint(HintCopied);
        }

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;
            if (_clipboard.Length == 0)
            {
                SetHint(HintEmpty);
                return;
            }
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range, raycastMask, QueryTriggerInteraction.Ignore))
            {
                SetHint(HintNoSurface);
                return;
            }

            _nextUseTime = now + cooldown;
            Vector3 pivot = hit.point + Vector3.up * (-_minLocalY + 0.05f); // rest the group's lowest point on the surface
            ActionResult result = actions.SpawnBlueprint(_clipboard, pivot, aimOrigin.eulerAngles.y, liftToFit: true, replaceOwned: false);
            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending: SetHint(HintPasted); break;
                case ActionResult.NoSpace: SetHint(HintNoSpace); break;
                case ActionResult.OwnerLimit:
                case ActionResult.ServerLimit: SetHint(HintLimit); break;
                default: SetHint(HintFailed); break;
            }
        }
    }
}
