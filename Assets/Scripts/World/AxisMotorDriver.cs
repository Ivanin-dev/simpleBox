using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Authority only: drives the motors of Axis constraints from their owner's held bindings.
    /// Forward binding held -> +Speed, backward -> -Speed, none / both -> motor off (free spin).
    /// The joint is written only when the state changes; bodies are woken when a motor starts.
    /// Clients have no constraints, so the loop is empty there.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AxisMotorDriver : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private InputBindings bindings;

        private bool _valid;

        private void Awake()
        {
            _valid = registry != null && bindings != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(AxisMotorDriver)}] '{name}': Registry / Bindings not assigned. Component disabled.", this);
                enabled = false;
            }
        }

        private void FixedUpdate()
        {
            int count = registry.ConstraintCount;
            for (int i = 0; i < count; i++)
            {
                SandboxConstraint c = registry.GetConstraintAt(i);
                if (c.Kind != ConstraintKind.Axis || !c.Motor.HasMotor || !(c.Joint is HingeJoint hinge)) continue;

                float dir = (bindings.IsHeld(c.OwnerId, c.Motor.ForwardBinding) ? 1f : 0f) -
                            (bindings.IsHeld(c.OwnerId, c.Motor.BackwardBinding) ? 1f : 0f);
                bool wantMotor = dir != 0f;

                if (hinge.useMotor != wantMotor)
                {
                    hinge.useMotor = wantMotor;
                    if (wantMotor) WakeBodies(c);
                }
                if (!wantMotor) continue;

                JointMotor motor = hinge.motor;
                float target = dir * c.Motor.Speed;
                if (motor.targetVelocity == target && motor.force == c.Motor.Force) continue;
                motor.targetVelocity = target;
                motor.force = c.Motor.Force;
                motor.freeSpin = false;
                hinge.motor = motor;
                WakeBodies(c);
            }
        }

        private static void WakeBodies(SandboxConstraint c)
        {
            if (c.A != null && c.A.Body != null) c.A.Body.WakeUp();
            if (c.B != null && c.B.Body != null) c.B.Body.WakeUp();
        }
    }
}
