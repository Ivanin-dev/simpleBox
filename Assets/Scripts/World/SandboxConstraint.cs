using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Something attached to one entity (thruster now; lights, balloons later).
    /// Registered in EntityRegistry with an id from the shared id space (Undo uses it).
    /// </summary>
    public interface IEntityAttachment
    {
        int AttachmentId { get; }
        int OwnerId { get; }
        SandboxEntity Host { get; }

        /// <summary>
        /// Remove the attachment. immediate = called from normal game code (may change active state);
        /// false = called during deactivation / destruction (deferred Destroy only).
        /// </summary>
        void Detach(bool immediate);
    }

    /// <summary>Owner of pooled entities (PropSpawner). EntityRegistry hands removed entities back to it.</summary>
    public interface IEntityPool
    {
        /// <summary>Entity was removed through the registry: deactivate and keep for reuse.</summary>
        void Recycle(SandboxEntity entity);

        /// <summary>Entity was disabled/destroyed by external code. Only deferred-safe work allowed.</summary>
        void HandleExternalLoss(SandboxEntity entity, bool destroyed);
    }

    /// <summary>Constraint types.</summary>
    public enum ConstraintKind
    {
        Weld = 0,
        /// <summary>Max distance (slack allowed): ConfigurableJoint with a linear limit.</summary>
        Rope = 1,
        /// <summary>Spring pulling back to the rest length: SpringJoint.</summary>
        Elastic = 2,
        /// <summary>Hinge (wheel / axle) with an optional motor driven by bindings: HingeJoint.</summary>
        Axis = 3,
        /// <summary>Piston: distance between the anchors is driven by bindings (SpringJoint with min = max = current length).</summary>
        Hydraulic = 4
    }

    /// <summary>Motor of an Axis constraint: spins while its owner holds a binding.</summary>
    public struct AxisMotorSettings
    {
        /// <summary>Binding (1..4) that spins forward; 0 = none.</summary>
        public int ForwardBinding;
        /// <summary>Binding (1..4) that spins backward; 0 = none.</summary>
        public int BackwardBinding;
        /// <summary>Degrees per second.</summary>
        public float Speed;
        /// <summary>Max motor force.</summary>
        public float Force;

        public bool HasMotor => ForwardBinding != 0 || BackwardBinding != 0;

        public AxisMotorSettings(int forward, int backward, float speed, float force)
        {
            ForwardBinding = forward;
            BackwardBinding = backward;
            Speed = speed;
            Force = force;
        }
    }

    /// <summary>
    /// A joint between entity A and entity B (B == null: the world). The Joint component lives on A.
    /// Plain C# object owned by EntityRegistry. IsAlive becomes false when the joint breaks (breakForce)
    /// or is destroyed; the registry prunes dead constraints every frame.
    /// AnchorA is local to A; AnchorB is local to B (or a world point when B is null); Length = rope / rest length.
    /// These values are what replication and save files carry.
    /// </summary>
    public sealed class SandboxConstraint
    {
        public int Id { get; internal set; }
        public readonly ConstraintKind Kind;
        public readonly int OwnerId;
        public readonly SandboxEntity A;
        public readonly SandboxEntity B;   // null = world
        public readonly Joint Joint;
        public readonly Vector3 AnchorA;
        public readonly Vector3 AnchorB;
        public readonly float Length;
        public readonly AxisMotorSettings Motor;
        public readonly HydraulicSettings Hydraulic;

        /// <summary>Hydraulic: current target length (changed by HydraulicDriver).</summary>
        public float CurrentLength { get; internal set; }

        public SandboxConstraint(ConstraintKind kind, int ownerId, SandboxEntity a, SandboxEntity b, Joint joint)
            : this(kind, ownerId, a, b, joint, Vector3.zero, Vector3.zero, 0f, default)
        {
        }

        public SandboxConstraint(ConstraintKind kind, int ownerId, SandboxEntity a, SandboxEntity b, Joint joint,
            Vector3 anchorA, Vector3 anchorB, float length, AxisMotorSettings motor)
        {
            Kind = kind;
            OwnerId = ownerId;
            A = a;
            B = b;
            Joint = joint;
            AnchorA = anchorA;
            AnchorB = anchorB;
            Length = length;
            Motor = motor;
            CurrentLength = length;
        }

        public bool IsAlive => Joint != null;
        public bool IsWorldAnchored => B == null;
        /// <summary>Drawn as a line between the anchors (rope, elastic, piston).</summary>
        public bool IsCable => Kind == ConstraintKind.Rope || Kind == ConstraintKind.Elastic || Kind == ConstraintKind.Hydraulic;

        /// <summary>Hydraulic constructor.</summary>
        public SandboxConstraint(int ownerId, SandboxEntity a, SandboxEntity b, Joint joint, Vector3 anchorA, Vector3 anchorB,
            float length, HydraulicSettings hydraulic)
            : this(ConstraintKind.Hydraulic, ownerId, a, b, joint, anchorA, anchorB, length, default)
        {
            Hydraulic = hydraulic;
        }
        public SandboxEntity Other(SandboxEntity self) => self == A ? B : A;
    }

    /// <summary>Piston parameters: bindings, length range and speed.</summary>
    public struct HydraulicSettings
    {
        /// <summary>Binding (1..4) that extends; 0 = none.</summary>
        public int ExtendBinding;
        /// <summary>Binding (1..4) that retracts; 0 = none.</summary>
        public int RetractBinding;
        public float MinLength;
        public float MaxLength;
        /// <summary>Meters per second.</summary>
        public float Speed;

        public HydraulicSettings(int extend, int retract, float minLength, float maxLength, float speed)
        {
            ExtendBinding = extend;
            RetractBinding = retract;
            MinLength = minLength;
            MaxLength = maxLength;
            Speed = speed;
        }
    }

    /// <summary>Weld joint parameters (also the data a network request / save file carries).</summary>
    public struct WeldSettings
    {
        /// <summary>Newtons, 0 = unbreakable.</summary>
        public float BreakForce;
        /// <summary>Newton-meters, 0 = unbreakable.</summary>
        public float BreakTorque;
        public bool EnablePreprocessing;

        public WeldSettings(float breakForce, float breakTorque, bool enablePreprocessing)
        {
            BreakForce = breakForce;
            BreakTorque = breakTorque;
            EnablePreprocessing = enablePreprocessing;
        }
    }

    /// <summary>Creates the Unity joint for each constraint kind. One method per kind.</summary>
    public static class ConstraintFactory
    {
        /// <summary>
        /// FixedJoint on A locking the current relative pose. enableCollision = false so welded colliders do not fight.
        /// b == null welds A to the world.
        /// </summary>
        public static FixedJoint CreateWeld(Rigidbody a, Rigidbody b, in WeldSettings s)
        {
            FixedJoint joint = a.gameObject.AddComponent<FixedJoint>();
            joint.autoConfigureConnectedAnchor = true;
            joint.connectedBody = b;
            joint.breakForce = s.BreakForce > 0f ? s.BreakForce : Mathf.Infinity;
            joint.breakTorque = s.BreakTorque > 0f ? s.BreakTorque : Mathf.Infinity;
            joint.enableCollision = false;
            joint.enablePreprocessing = s.EnablePreprocessing;
            return joint;
        }

        /// <summary>
        /// Rope: A's anchor may not move farther than length from B's anchor (slack allowed, rotation free).
        /// anchorB is local to b, or a world point when b is null. Bodies on a rope still collide with each other.
        /// </summary>
        public static ConfigurableJoint CreateRope(Rigidbody a, Vector3 anchorA, Rigidbody b, Vector3 anchorB, float length)
        {
            ConfigurableJoint joint = a.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = b;
            joint.anchor = anchorA;
            joint.connectedAnchor = anchorB;
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.linearLimit = new SoftJointLimit { limit = Mathf.Max(0.05f, length), contactDistance = 0.05f };
            joint.enableCollision = true;
            joint.enablePreprocessing = false;
            return joint;
        }

        /// <summary>
        /// Elastic: a spring pulls the anchors back when they are farther apart than the rest length.
        /// Spring / damper scale with the lighter body's mass so light and heavy props behave alike.
        /// </summary>
        public static SpringJoint CreateElastic(Rigidbody a, Vector3 anchorA, Rigidbody b, Vector3 anchorB, float restLength,
            float stiffness, float damping)
        {
            float mass = b != null ? Mathf.Min(a.mass, b.mass) : a.mass;
            SpringJoint joint = a.gameObject.AddComponent<SpringJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = b;
            joint.anchor = anchorA;
            joint.connectedAnchor = anchorB;
            joint.minDistance = 0f;
            joint.maxDistance = Mathf.Max(0f, restLength);
            joint.spring = stiffness * mass;
            joint.damper = damping * mass;
            joint.tolerance = 0.01f;
            joint.enableCollision = true;
            joint.enablePreprocessing = false;
            return joint;
        }

        /// <summary>
        /// Axis: HingeJoint on A around axisA (local to A) through anchorA, connected to B in the current pose
        /// (b == null: hinged to the world). A and B do not collide with each other (wheel touching the chassis).
        /// </summary>
        public static HingeJoint CreateAxis(Rigidbody a, Vector3 anchorA, Vector3 axisA, Rigidbody b, in AxisMotorSettings motor)
        {
            HingeJoint joint = a.gameObject.AddComponent<HingeJoint>();
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = anchorA;
            joint.axis = axisA.sqrMagnitude > 1e-6f ? axisA.normalized : Vector3.right;
            joint.connectedBody = b;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.useMotor = false;
            joint.motor = new JointMotor { targetVelocity = 0f, force = motor.Force, freeSpin = true };
            return joint;
        }

        /// <summary>
        /// Piston: SpringJoint that holds the anchors at exactly 'length' (min = max distance), stiff enough to lift.
        /// HydraulicDriver changes min / max distance to extend or retract. Bodies on a piston do not collide.
        /// </summary>
        public static SpringJoint CreateHydraulic(Rigidbody a, Vector3 anchorA, Rigidbody b, Vector3 anchorB, float length,
            float stiffness, float damping)
        {
            float mass = b != null ? Mathf.Min(a.mass, b.mass) : a.mass;
            SpringJoint joint = a.gameObject.AddComponent<SpringJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = b;
            joint.anchor = anchorA;
            joint.connectedAnchor = anchorB;
            joint.minDistance = length;
            joint.maxDistance = length;
            joint.spring = stiffness * mass;
            joint.damper = damping * mass;
            joint.tolerance = 0.005f;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            return joint;
        }
    }
}
