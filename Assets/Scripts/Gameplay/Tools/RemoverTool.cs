using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Removes the entity under the crosshair through SandboxActions.RemoveEntity.
    /// Targeting: ray from the camera along raycastMask (walls block it); the hit Rigidbody must be on removableMask.
    /// Colliders without a Rigidbody (floor, walls) and the player are never targets.
    /// Everything else (physgun drop, thrusters, welds, slot release / pooling) is done by EntityRegistry.
    /// Feedback: crosshair tint while a valid target is aimed at (color set only on change), optional particles/sound.
    /// </summary>
    public sealed class RemoverTool : BaseTool
    {
        [Header("References (required)")]
        [Tooltip("Camera transform: ray origin and direction.")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("Crosshair graphic tinted while a removable target is aimed at.")]
        [SerializeField] private Graphic crosshair;
        [Tooltip("Scene ParticleSystem reused for every removal (Simulation Space = World).")]
        [SerializeField] private ParticleSystem removeEffect;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip removeClip;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 25f;
        [Tooltip("Everything that can stop the ray (environment + props). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose Rigidbodies may be removed. Props only.")]
        [SerializeField] private LayerMask removableMask;
        [SerializeField, Min(0f)] private float cooldown = 0.15f;

        [Header("Feedback")]
        [SerializeField] private Color validTargetColor = new Color(1f, 0.3f, 0.25f, 1f);

        private const string HintText = "USE: удалить проп под прицелом";
        private const string HintNotEntity = "Этот объект нельзя удалить";
        private const string HintNotAllowed = "Это чужой проп";

        private Rigidbody _target;
        private Vector3 _targetPoint;
        private Color _crosshairDefaultColor;
        private bool _crosshairTinted;
        private float _nextUseTime;

        public Rigidbody CurrentTarget => _target;

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (removableMask.value == 0) error = "Removable Mask is empty.";
            else if ((removableMask.value & (1 << aimOrigin.root.gameObject.layer)) != 0)
                error = $"Removable Mask contains the player layer '{LayerMask.LayerToName(aimOrigin.root.gameObject.layer)}'.";
            return error == null;
        }

        protected override void OnEquip()
        {
            SetHint(HintText);
            if (crosshair != null) _crosshairDefaultColor = crosshair.color;
            _crosshairTinted = false;
            _target = null;
            RefreshTarget();
        }

        protected override void OnUnequip()
        {
            _target = null;
            SetCrosshairTint(false);
        }

        public override void Tick(float deltaTime, bool primaryHeld)
        {
            RefreshTarget();
        }

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;

            RefreshTarget();
            if (_target == null) return;

            SandboxEntity entity = SandboxEntity.FromBody(_target); // once per use, not per frame
            if (entity == null || !entity.IsRegistered)
            {
                SetHint(HintNotEntity);
                return;
            }

            Vector3 point = _targetPoint;
            _nextUseTime = now + cooldown;

            ActionResult result = actions.RemoveEntity(entity.Id);
            if (result == ActionResult.NotAllowed)
            {
                SetHint(HintNotAllowed);
                return;
            }
            if (result != ActionResult.Ok && result != ActionResult.Pending) return;

            _target = null;
            SetCrosshairTint(false);
            SetHint(HintText);
            PlayFeedback(point);
        }

        private void RefreshTarget()
        {
            Rigidbody found = null;
            if (Physics.Raycast(aimOrigin.position, aimOrigin.forward, out RaycastHit hit, range,
                    raycastMask, QueryTriggerInteraction.Ignore))
            {
                Rigidbody rb = hit.rigidbody;
                if (rb != null && (removableMask.value & (1 << rb.gameObject.layer)) != 0)
                {
                    found = rb;
                    _targetPoint = hit.point;
                }
            }

            if (found != _target)
            {
                _target = found;
                SetCrosshairTint(found != null);
            }
        }

        private void PlayFeedback(Vector3 point)
        {
            if (removeEffect != null)
            {
                removeEffect.transform.position = point;
                removeEffect.Play(true);
            }
            if (audioSource != null && removeClip != null) audioSource.PlayOneShot(removeClip);
        }

        private void SetCrosshairTint(bool tinted)
        {
            if (crosshair == null || tinted == _crosshairTinted) return;
            _crosshairTinted = tinted;
            crosshair.color = tinted ? validTargetColor : _crosshairDefaultColor;
        }
    }
}
