using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Explosive prop (barrel). Explodes on a hard impact (relative speed >= impactSpeed), when detonated, or when caught
    /// in another explosion (chain, after chainDelay). Only the authority reacts: multiplayer clients hold kinematic
    /// copies. ExplosionSystem wires itself in when the entity registers; the explosion is queued and processed in
    /// Update (never inside a physics callback).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SandboxEntity))]
    public sealed class ExplosiveProp : MonoBehaviour
    {
        [Header("Blast")]
        [SerializeField, Min(0.5f)] private float radius = 6f;
        [Tooltip("Velocity change at the center (m/s), falling off to 0 at the radius. Mass-independent.")]
        [SerializeField, Min(0f)] private float strength = 16f;
        [Tooltip("Upward bias of the push (AddExplosionForce upwardsModifier).")]
        [SerializeField, Min(0f)] private float upwards = 0.6f;

        [Header("Triggers")]
        [SerializeField, Min(1f)] private float impactSpeed = 9f;
        [SerializeField, Min(0f)] private float chainDelay = 0.15f;
        [Tooltip("No impact trigger for this long after spawning (spawn / paste settling).")]
        [SerializeField, Min(0f)] private float armDelay = 1f;

        internal ExplosionSystem System;

        private SandboxEntity _entity;
        private float _armedAt;

        public float Radius => radius;
        public float Strength => strength;
        public float Upwards => upwards;
        public float ChainDelay => chainDelay;
        public SandboxEntity Entity => _entity;

        private void Awake() => _entity = GetComponent<SandboxEntity>();

        private void OnEnable() => _armedAt = Time.time + armDelay;

        private void OnCollisionEnter(Collision collision)
        {
            if (System == null || _entity.IsMirror || !_entity.IsRegistered || Time.time < _armedAt) return;
            if (collision.relativeVelocity.sqrMagnitude < impactSpeed * impactSpeed) return;
            System.Request(this, 0f);
        }
    }
}
