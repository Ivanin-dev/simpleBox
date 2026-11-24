using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Authority only: extends / retracts Hydraulic constraints while their owner holds the bound buttons.
    /// Length moves at Speed m/s inside [MinLength, MaxLength]; the SpringJoint target (min = max distance) follows.
    /// Clients have no constraints, so the loop is empty there.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HydraulicDriver : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private InputBindings bindings;

        private void Awake()
        {
            if (registry == null || bindings == null)
            {
                Debug.LogError($"[{nameof(HydraulicDriver)}] '{name}': Registry / Bindings not assigned. Component disabled.", this);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            int count = registry.ConstraintCount;
            for (int i = 0; i < count; i++)
            {
                SandboxConstraint c = registry.GetConstraintAt(i);
                if (c.Kind != ConstraintKind.Hydraulic || !(c.Joint is SpringJoint spring)) continue;

                HydraulicSettings h = c.Hydraulic;
                float dir = (bindings.IsHeld(c.OwnerId, h.ExtendBinding) ? 1f : 0f) -
                            (bindings.IsHeld(c.OwnerId, h.RetractBinding) ? 1f : 0f);
                if (dir == 0f) continue;

                float length = Mathf.Clamp(c.CurrentLength + dir * h.Speed * dt, h.MinLength, h.MaxLength);
                if (Mathf.Approximately(length, c.CurrentLength)) continue;

                c.CurrentLength = length;
                spring.minDistance = length;
                spring.maxDistance = length;
                if (c.A != null && c.A.Body != null) c.A.Body.WakeUp();
                if (c.B != null && c.B.Body != null) c.B.Body.WakeUp();
            }
        }
    }
}
