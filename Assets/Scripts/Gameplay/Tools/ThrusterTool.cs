using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// USE: request a thruster on the entity under the crosshair at the hit point, nozzle along the surface normal
    /// (thrust pushes into the prop). ALT: toggle the aimed entity's thrusters.
    /// Global on/off for the player's thrusters: THRUST button / T (PlayerCommands).
    /// The tool only computes the entity-local pose; SandboxActions creates and registers the thruster.
    /// </summary>
    public sealed class ThrusterTool : BaseTool
    {
        [Header("References (required)")]
        [Tooltip("Camera transform: ray origin and direction.")]
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private SandboxActions actions;

        [Header("Targeting")]
        [SerializeField, Min(0.1f)] private float range = 15f;
        [Tooltip("Everything that can stop the ray (environment + props). Exclude the Player layer.")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [Tooltip("Layers whose Rigidbodies accept thrusters. Props only.")]
        [SerializeField] private LayerMask attachMask;

        [Header("Behaviour (also editable in the tool options panel)")]
        [Tooltip("Thrust of newly placed thrusters, N. Still capped per thruster by mass * maxAcceleration.")]
        [SerializeField, Range(ThrustMin, ThrustMax)] private float thrustForce = 150f;
        [Tooltip("New thruster starts firing immediately (quick testing).")]
        [SerializeField] private bool autoActivate = true;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.005f;
        [SerializeField, Min(0f)] private float cooldown = 0.2f;

        private const string HintIdle = "USE: поставить двигатель   ALT: вкл/выкл двигатели пропа   THRUST: все";
        private const string HintPlaced = "Двигатель установлен";
        private const string HintNoTarget = "Нужен проп под прицелом";
        private const string HintLimit = "Лимит двигателей на сцене";
        private const string HintNotAllowed = "Это чужой проп";
        private const string HintPropOn = "Двигатели пропа: ВКЛ";
        private const string HintPropOff = "Двигатели пропа: ВЫКЛ";
        private const string HintNoThrusters = "На пропе нет двигателей";

        private const float ThrustMin = 50f;
        private const float ThrustMax = 600f;
        private const float ThrustStep = 25f;

        private float _nextUseTime;
        private int _binding; // 0 = none, 1..4 = fires while held

        private static readonly string[] BindingChoices = { "Нет", "1", "2", "3", "4" };

        protected override bool ValidateReferences(out string error)
        {
            error = null;
            if (aimOrigin == null) error = "Aim Origin not assigned.";
            else if (actions == null) error = "Actions not assigned.";
            else if (attachMask.value == 0) error = "Attach Mask is empty.";
            return error == null;
        }

        protected override void OnEquip() => SetHint(HintIdle);

        public override void OnPrimary()
        {
            float now = Time.time;
            if (now < _nextUseTime) return;

            if (!TryGetTarget(out RaycastHit hit, out SandboxEntity entity))
            {
                SetHint(HintNoTarget);
                return;
            }

            _nextUseTime = now + cooldown;

            Vector3 normal = hit.normal;
            Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? aimOrigin.forward : Vector3.up;
            Quaternion worldRotation = Quaternion.LookRotation(normal, up);
            Vector3 worldPosition = hit.point + normal * surfaceOffset;

            Transform host = entity.transform;
            Vector3 localPosition = host.InverseTransformPoint(worldPosition);
            Quaternion localRotation = Quaternion.Inverse(host.rotation) * worldRotation;

            ActionResult result = actions.AttachThruster(entity.Id, localPosition, localRotation, thrustForce, _binding,
                autoActivate && _binding == 0); // a bound thruster waits for its button
            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending: SetHint(HintPlaced); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                case ActionResult.ServerLimit: SetHint(HintLimit); break;
                default: SetHint(HintNoTarget); break;
            }
        }

        public override void OnSecondary()
        {
            if (!TryGetTarget(out _, out SandboxEntity entity))
            {
                SetHint(HintNoTarget);
                return;
            }

            bool on = !AnyThrusterOn(entity);
            ActionResult result = actions.SetEntityThrusters(entity.Id, on);
            switch (result)
            {
                case ActionResult.Ok:
                case ActionResult.Pending: SetHint(on ? HintPropOn : HintPropOff); break;
                case ActionResult.NotAllowed: SetHint(HintNotAllowed); break;
                default: SetHint(HintNoThrusters); break;
            }
        }

        // ------------------------------------------------------------------ Settings

        public override int SettingCount => 3;

        public override ToolSettingInfo GetSettingInfo(int index)
        {
            switch (index)
            {
                case 0: return ToolSettingInfo.Stepper("Тяга", ThrustMin, ThrustMax, ThrustStep, "0", " Н");
                case 1: return ToolSettingInfo.Toggle("Автозапуск");
                default: return ToolSettingInfo.Choice("Кнопка (держать)", BindingChoices);
            }
        }

        public override float GetSettingValue(int index)
        {
            switch (index)
            {
                case 0: return thrustForce;
                case 1: return autoActivate ? 1f : 0f;
                default: return _binding;
            }
        }

        public override void SetSettingValue(int index, float value)
        {
            switch (index)
            {
                case 0: thrustForce = Mathf.Clamp(value, ThrustMin, ThrustMax); break;
                case 1: autoActivate = value >= 0.5f; break;
                default: _binding = Mathf.Clamp(Mathf.RoundToInt(value), 0, InputBindings.Count); break;
            }
        }
        private static bool AnyThrusterOn(SandboxEntity entity)
        {
            for (int i = 0; i < entity.AttachmentCount; i++)
            {
                if (entity.GetAttachment(i) is PropThruster t && t.IsOn) return true;
            }
            return false;
        }

        /// <summary>Raycast + layer filter; entity lookup only on user action.</summary>
        private bool TryGetTarget(out RaycastHit hit, out SandboxEntity entity)
        {
            entity = null;
            if (!Physics.Raycast(aimOrigin.position, aimOrigin.forward, out hit, range, raycastMask, QueryTriggerInteraction.Ignore))
                return false;

            Rigidbody rb = hit.rigidbody;
            if (rb == null || (attachMask.value & (1 << rb.gameObject.layer)) == 0) return false;

            entity = SandboxEntity.FromBody(rb);
            return entity != null && entity.IsRegistered;
        }
    }
}
