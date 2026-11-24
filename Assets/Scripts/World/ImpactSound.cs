using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Collision sound of a prop (on prop prefabs, next to the Rigidbody). AudioManager wires itself in when the entity
    /// registers. Plays only above minSpeed relative velocity, with a per-prop cooldown; AudioManager rate-limits globally.
    /// Note: on multiplayer clients props are kinematic copies and produce no collision events (no impact sounds there).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ImpactSound : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float minSpeed = 1.5f;
        [SerializeField, Min(0.2f)] private float maxSpeed = 10f;
        [SerializeField, Min(0f)] private float cooldown = 0.08f;
        [Tooltip("Props heavier than this use the heavy impact sound.")]
        [SerializeField, Min(0f)] private float heavyMass = 15f;

        internal AudioManager Audio;

        private Rigidbody _body;
        private float _nextTime;

        private void Awake() => _body = GetComponent<Rigidbody>();

        private void OnCollisionEnter(Collision collision) => Report(collision);

        /// <summary>Plays the impact for a collision of this prop or of one of its parts (ragdoll bones relay here).</summary>
        public void Report(Collision collision)
        {
            if (Audio == null) return;
            float now = Time.time;
            if (now < _nextTime) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minSpeed) return;
            _nextTime = now + cooldown;

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            float intensity = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
            Audio.PlayImpact(point, intensity, _body != null && _body.mass >= heavyMass);
        }
    }
}
