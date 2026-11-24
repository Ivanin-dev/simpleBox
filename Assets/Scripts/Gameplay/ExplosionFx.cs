using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Explosion presentation on every peer: pooled particle burst, explosion sound, and knockback of the local player
    /// (CharacterController is not a rigidbody, so explosions push it here). On the authority it listens to
    /// ExplosionSystem.Exploded; multiplayer clients receive Play() from the network layer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExplosionFx : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private ExplosionSystem explosions;
        [SerializeField] private AudioManager audioManager;
        [SerializeField] private PlayerController player;

        [Header("References (optional)")]
        [Tooltip("Scene ParticleSystem used as the effect (cloned into a small pool).")]
        [SerializeField] private ParticleSystem effectTemplate;

        [Header("Player knockback")]
        [Tooltip("Velocity change for the local player at the center (m/s), falling off to 0 at the radius.")]
        [SerializeField, Min(0f)] private float playerKnockback = 12f;
        [SerializeField, Range(1, 8)] private int poolSize = 3;

        private ParticleSystem[] _pool = new ParticleSystem[0];
        private int _next;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            _valid = explosions != null && audioManager != null && player != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(ExplosionFx)}] '{name}': Explosions / Audio / Player not assigned. Component disabled.", this);
                enabled = false;
                return;
            }

            if (effectTemplate != null)
            {
                _pool = new ParticleSystem[poolSize];
                _pool[0] = effectTemplate;
                for (int i = 1; i < poolSize; i++)
                    _pool[i] = Instantiate(effectTemplate, effectTemplate.transform.parent);
            }
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            explosions.Exploded += Play;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (explosions != null) explosions.Exploded -= Play;
            _subscribed = false;
        }

        public void Play(Vector3 center, float radius)
        {
            if (!_valid) return;

            if (_pool.Length > 0)
            {
                ParticleSystem ps = _pool[_next];
                _next = (_next + 1) % _pool.Length;
                ps.transform.position = center;
                ps.Play(true);
            }

            audioManager.PlayExplosion(center);

            Vector3 body = player.transform.position + Vector3.up * 0.9f;
            Vector3 offset = body - center;
            float distance = offset.magnitude;
            if (distance >= radius || distance < 1e-3f) return;
            float falloff = 1f - distance / radius;
            player.AddImpulse(offset / distance * (playerKnockback * falloff) + Vector3.up * (playerKnockback * 0.3f * falloff));
        }
    }
}
