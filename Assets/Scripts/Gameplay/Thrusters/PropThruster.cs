using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Thruster attachment (child of an entity's Rigidbody root). Created only by SandboxActions.ExecuteAttachThruster.
    /// Orientation: transform.forward = nozzle direction (out of the surface). Thrust pushes along -forward (into the prop).
    /// Force (FixedUpdate only): min(thrustForce, mass * maxAcceleration) * throttle; throttle spools 0 -> 1 over spoolTime;
    /// no thrust above maxSpeedAlongThrust or while the body is kinematic (frozen).
    /// The prefab must contain NO colliders (they would join the prop's compound collider and move its center of mass).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropThruster : MonoBehaviour, IEntityAttachment
    {
        [Header("Thrust")]
        [SerializeField, Min(0f)] private float thrustForce = 150f;
        [Tooltip("Cap: force never exceeds mass * this (m/s²). Stops light props from rocketing away.")]
        [SerializeField, Min(0.1f)] private float maxAcceleration = 25f;
        [Tooltip("No thrust while the body already moves faster than this along the thrust direction (m/s).")]
        [SerializeField, Min(0.1f)] private float maxSpeedAlongThrust = 15f;
        [Tooltip("Time to reach full thrust after switching on (s). Prevents a first-step kick.")]
        [SerializeField, Min(0f)] private float spoolTime = 0.3f;
        [Tooltip("true: push through the center of mass (no spin). false: push at the mount point (GMod-like torque).")]
        [SerializeField] private bool applyAtCenterOfMass;

        [Header("Effects (optional)")]
        [SerializeField] private GameObject flame;
        [SerializeField] private ParticleSystem exhaust;
        [SerializeField] private AudioSource loopAudio;

        private SandboxEntity _host;
        private Rigidbody _body;
        private ThrusterRegistry _thrusters;
        private EntityRegistry _entities;
        private Transform _transform;
        private bool _attached;
        private bool _on;
        private bool _firing;
        private int _binding;
        private float _throttle;

        public int AttachmentId { get; private set; }
        public int OwnerId { get; private set; } = SandboxEntity.WorldOwner;
        public SandboxEntity Host => _host;

        /// <summary>Toggle state (ALT / THRUST).</summary>
        public bool IsOn => _on;

        /// <summary>Binding 1..4 that fires the thruster while held; 0 = none.</summary>
        public int Binding => _binding;

        /// <summary>Actually pushing: toggled on, or its binding is held by the owner.</summary>
        public bool IsFiring => _firing;

        /// <summary>Thrust of this instance, N (saved by blueprints).</summary>
        public float ThrustForce => thrustForce;

        private void Awake()
        {
            _transform = transform;
            ApplyEffects(false, true);
        }

        /// <summary>Registers with both registries. Returns false (detached) at the thruster limit.</summary>
        internal bool Attach(SandboxEntity host, int ownerId, ThrusterRegistry thrusters, EntityRegistry entities,
            float force, int binding, bool startOn)
        {
            if (_attached || host == null || thrusters == null || entities == null) return false;
            if (!thrusters.Register(this)) return false;

            _host = host;
            _body = host.Body;
            _thrusters = thrusters;
            _entities = entities;
            OwnerId = ownerId;
            if (force > 0f) thrustForce = force; // per-instance value from the tool settings
            _binding = Mathf.Clamp(binding, 0, InputBindings.Count);
            AttachmentId = entities.RegisterAttachment(this, host);
            _attached = true;
            SetOn(startOn);
            NotifyReplication(host);
            return true;
        }

        public void SetOn(bool on)
        {
            if (!_attached) on = false;
            if (_on == on) return;

            _on = on;
            if (_thrusters != null) _thrusters.NotifyStateChanged();
            UpdateFiring();
        }

        /// <summary>Recomputes "firing" (toggle or held binding); effects and replication change only on transitions.</summary>
        private void UpdateFiring()
        {
            bool firing = _attached && (_on || (_binding > 0 && _thrusters != null && _thrusters.IsBindingHeld(OwnerId, _binding)));
            if (firing == _firing) return;
            _firing = firing;
            if (!firing) _throttle = 0f;
            ApplyEffects(firing, true);
            NotifyReplication(_host);
        }

        /// <summary>
        /// Client side visual copy of a host thruster (created by NetworkEntity): never registers, never applies force,
        /// only shows / hides the flame and effects.
        /// </summary>
        public void SetVisualOnly(bool on)
        {
            if (_attached) return;
            ApplyEffects(on, true);
        }

        private static void NotifyReplication(SandboxEntity host)
        {
            if (host != null && host.Replication != null) host.Replication.OnAttachmentsChanged();
        }

        private void FixedUpdate()
        {
            if (!_attached) return; // visual-only copies on clients never push
            UpdateFiring();
            if (!_firing || _body == null) return;
            if (_body.isKinematic)
            {
                _throttle = 0f;
                return;
            }

            _throttle = spoolTime > 0f ? Mathf.MoveTowards(_throttle, 1f, Time.fixedDeltaTime / spoolTime) : 1f;

            Vector3 dir = -_transform.forward;
            if (Vector3.Dot(_body.linearVelocity, dir) >= maxSpeedAlongThrust) return;

            float magnitude = Mathf.Min(thrustForce, _body.mass * maxAcceleration) * _throttle;
            Vector3 force = dir * magnitude;

            if (applyAtCenterOfMass) _body.AddForce(force, ForceMode.Force);
            else _body.AddForceAtPosition(force, _transform.position, ForceMode.Force);
        }

        /// <summary>Called by EntityRegistry (removal, undo). The registry has already dropped its own reference.</summary>
        public void Detach(bool immediate)
        {
            if (!_attached) return;
            _attached = false;
            _on = false;
            _firing = false;
            _throttle = 0f;
            ApplyEffects(false, immediate);

            ThrusterRegistry thrusters = _thrusters;
            SandboxEntity host = _host;
            _thrusters = null;
            _entities = null;
            _host = null;
            _body = null;
            if (thrusters != null) thrusters.Unregister(this);

            if (immediate)
            {
                gameObject.SetActive(false);
                NotifyReplication(host); // host list no longer contains this thruster
            }
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            if (exhaust != null) exhaust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (loopAudio != null) loopAudio.Stop();
        }

        private void OnDestroy()
        {
            // Destroyed with its prop without going through the registry.
            if (!_attached) return;
            _attached = false;
            if (_thrusters != null) _thrusters.Unregister(this);
            if (_entities != null) _entities.HandleAttachmentDestroyed(this);
        }

        private void ApplyEffects(bool on, bool allowActiveChange)
        {
            if (flame != null && allowActiveChange && flame.activeSelf != on) flame.SetActive(on);

            if (exhaust != null)
            {
                if (on) exhaust.Play(true);
                else exhaust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (loopAudio != null)
            {
                if (on && !loopAudio.isPlaying) loopAudio.Play();
                else if (!on) loopAudio.Stop();
            }
        }
    }
}
