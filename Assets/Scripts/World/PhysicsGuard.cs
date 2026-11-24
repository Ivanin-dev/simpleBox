using SimpleBox.Core;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Protects the simulation from "explosions" and broken bodies. Each physics step checks a slice of registered
    /// entities (round-robin, entitiesPerStep at a time):
    /// - position / rotation / velocity NaN or Infinity -> velocity zeroed, or the entity removed if its pose is broken;
    /// - below killPlaneY (fell out of the map) -> removed;
    /// - linear / angular speed above the profile caps -> clamped.
    /// Removal goes through EntityRegistry (physgun drop, attachments, constraints, pooling).
    /// Global settings that prevent jams from exploding (depenetration velocity, solver iterations, sleep threshold)
    /// are applied by QualityManager and per body by EntityRegistry.RegisterEntity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhysicsGuard : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private QualityManager quality;

        [Header("Checks")]
        [Tooltip("Entities below this height are removed.")]
        [SerializeField] private float killPlaneY = -50f;
        [Tooltip("Entities checked per physics step (cost is bounded regardless of prop count).")]
        [SerializeField, Min(1)] private int entitiesPerStep = 32;
        [SerializeField] private bool logRemovals = true;

        private const int RemovalBufferSize = 8;

        private readonly SandboxEntity[] _toRemove = new SandboxEntity[RemovalBufferSize];
        private float _maxLinearSpeed;
        private float _maxAngularSpeed;
        private int _cursor;
        private bool _valid;

        private void Awake()
        {
            if (registry == null || quality == null || quality.Profile == null)
            {
                Debug.LogError($"[{nameof(PhysicsGuard)}] '{name}': Registry / Quality Manager not assigned (or no profile). Component disabled.", this);
                enabled = false;
                return;
            }
            _maxLinearSpeed = quality.Profile.maxLinearSpeed;
            _maxAngularSpeed = quality.Profile.maxAngularSpeed;
            _valid = true;
        }

        private void FixedUpdate()
        {
            if (!_valid) return;

            int count = registry.EntityCount;
            if (count == 0) return;

            int checks = entitiesPerStep < count ? entitiesPerStep : count;
            float maxLinSq = _maxLinearSpeed * _maxLinearSpeed;
            float maxAngSq = _maxAngularSpeed * _maxAngularSpeed;
            int removeCount = 0;

            for (int k = 0; k < checks; k++)
            {
                if (_cursor >= count) _cursor = 0;
                SandboxEntity entity = registry.GetEntityAt(_cursor++);
                if (entity == null) continue;

                Rigidbody rb = entity.Body;
                if (rb == null) continue;

                Vector3 position = rb.position;
                if (!IsFinite(position) || !IsFinite(rb.rotation) || position.y < killPlaneY)
                {
                    if (removeCount < RemovalBufferSize) _toRemove[removeCount++] = entity;
                    continue;
                }

                ClampBody(rb, maxLinSq, maxAngSq);

                // Ragdoll bones: a broken bone pose removes the whole entity, speeds are clamped like the root.
                Rigidbody[] parts = entity.PartBodies;
                for (int p = 0; p < parts.Length; p++)
                {
                    Rigidbody part = parts[p];
                    if (part == null) continue;
                    if (!IsFinite(part.position) || !IsFinite(part.rotation))
                    {
                        if (removeCount < RemovalBufferSize) _toRemove[removeCount++] = entity;
                        break;
                    }
                    ClampBody(part, maxLinSq, maxAngSq);
                }
            }

            for (int i = 0; i < removeCount; i++)
            {
                SandboxEntity entity = _toRemove[i];
                _toRemove[i] = null;
                if (entity == null || !entity.IsRegistered) continue;
                if (logRemovals)
                    Debug.LogWarning($"[{nameof(PhysicsGuard)}] Removed entity #{entity.Id} '{entity.name}' (broken pose or below kill plane).", entity);
                registry.RemoveEntity(entity);
            }
        }

        private void ClampBody(Rigidbody rb, float maxLinSq, float maxAngSq)
        {
            if (rb.isKinematic) return;

            Vector3 v = rb.linearVelocity;
            Vector3 w = rb.angularVelocity;
            if (!IsFinite(v) || !IsFinite(w))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                return;
            }

            if (v.sqrMagnitude > maxLinSq) rb.linearVelocity = v.normalized * _maxLinearSpeed;
            if (w.sqrMagnitude > maxAngSq) rb.angularVelocity = w.normalized * _maxAngularSpeed;
        }

        private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        private static bool IsFinite(Quaternion q)
        {
            return IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);
        }

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
