using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>Detonator. USE on an explosive prop (barrel): it explodes (chain reactions included).</summary>
    public sealed class DetonatorTool : BaseTool
    {
        [Header("References (required)")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 40f;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private LayerMask targetMask;
        [SerializeField, Min(0f)] private float cooldown = 0.3f;

        private const string HintIdle = "USE: взорвать взрывную бочку под прицелом";
        private const string HintBoom = "Бум!";
        private const string HintNotExplosive = "Это не взрывчатка";
        private const string HintNoTarget = "Нужна взрывная бочка под прицелом";
        private const string HintNotAllowed = "Это чужой проп";

        private float _nextUseTime;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (targetMask.value == 0) error = "Target Mask is empty.";
            return error == null;
        }

        protected override void OnEquip() => SetHint(HintIdle);

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;

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

            _nextUseTime = now + cooldown;
            switch (actions.Detonate(entity.Id))
            {
                case ActionResult.Ok:
                case ActionResult.Pending: SetHint(HintBoom); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                case ActionResult.Invalid: SetHint(HintNotExplosive); break;
                default: SetHint(HintNoTarget); break;
            }
        }
    }
}
