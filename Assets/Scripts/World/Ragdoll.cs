using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Ragdoll entity (on the root bone, next to SandboxEntity). Bones are child Rigidbodies joined by CharacterJoints,
    /// each with an EntityPart (tools target the whole ragdoll) and a RagdollBone (collision sounds go to the root's ImpactSound).
    /// OnEnable: bones of one ragdoll ignore each other (fewer contacts, no self-jitter on mobile; Unity resets ignore
    /// pairs when colliders are deactivated, so this runs on every activation from the pool) and get extra solver iterations.
    /// Clients (mirror entity): every bone is kinematic, NetworkTransforms on the bones move them.
    /// Pose reset on spawn: PropSpawner calls SandboxEntity.ResetParts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SandboxEntity))]
    public sealed class Ragdoll : MonoBehaviour
    {
        [Tooltip("Solver iterations of every bone (joint chains need more than the project default).")]
        [SerializeField, Range(4, 20)] private int solverIterations = 8;
        [SerializeField, Range(1, 10)] private int solverVelocityIterations = 2;

        private SandboxEntity _entity;
        private Collider[] _colliders;
        private bool _valid;

        public SandboxEntity Entity => _entity;

        private void Awake()
        {
            _entity = GetComponent<SandboxEntity>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _entity.CacheParts();

            if (_entity.PartBodies.Length == 0)
            {
                Debug.LogError($"[{nameof(Ragdoll)}] '{name}': no child Rigidbodies (bones). Component disabled.", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < _entity.PartBodies.Length; i++)
            {
                Rigidbody bone = _entity.PartBodies[i];
                if (bone.GetComponent<Joint>() == null)
                    Debug.LogWarning($"[{nameof(Ragdoll)}] '{name}': bone '{bone.name}' has no joint; it will fall off.", bone);
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid) return;

            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider a = _colliders[i];
                if (a == null || a.isTrigger) continue;
                for (int k = i + 1; k < _colliders.Length; k++)
                {
                    Collider b = _colliders[k];
                    if (b == null || b.isTrigger) continue;
                    Physics.IgnoreCollision(a, b, true);
                }
            }

            ConfigureSolver(_entity.Body);
            Rigidbody[] bones = _entity.PartBodies;
            for (int i = 0; i < bones.Length; i++) ConfigureSolver(bones[i]);
        }

        private void ConfigureSolver(Rigidbody rb)
        {
            if (rb == null) return;
            rb.solverIterations = solverIterations;
            rb.solverVelocityIterations = solverVelocityIterations;
        }
    }
}
