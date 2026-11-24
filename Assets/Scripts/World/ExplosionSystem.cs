using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Authority-side explosions. Requests are queued (from collisions, the detonator, chain reactions) and processed
    /// in Update: the explosive entity is removed through EntityRegistry (constraints, thrusters, pool), then
    /// OverlapSphereNonAlloc pushes every dynamic body in range (VelocityChange, falloff to the radius) and schedules
    /// explosives it reaches. Exploded fires for effects / sound / network (clients only get the effect).
    /// Fixed buffers: no allocation per explosion except a rare GetComponent for chained explosives.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExplosionSystem : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;

        [Header("Physics")]
        [Tooltip("Layers pushed by explosions (props). The player is pushed separately by ExplosionFx.")]
        [SerializeField] private LayerMask affectMask = ~0;
        [SerializeField, Range(8, 256)] private int maxBodiesPerExplosion = 64;

        private struct Pending
        {
            public ExplosiveProp Prop;
            public int EntityId;
            public float Time;
        }

        private readonly List<Pending> _queue = new List<Pending>(16);
        private Collider[] _hits;
        private Rigidbody[] _pushed;
        private bool _valid;
        private bool _subscribed;

        /// <summary>Args: position, radius.</summary>
        public event Action<Vector3, float> Exploded;

        private void Awake()
        {
            if (registry == null)
            {
                Debug.LogError($"[{nameof(ExplosionSystem)}] '{name}': Registry not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            _hits = new Collider[maxBodiesPerExplosion];
            _pushed = new Rigidbody[maxBodiesPerExplosion];
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.EntityAdded += OnEntityAdded;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (registry != null) registry.EntityAdded -= OnEntityAdded;
            _subscribed = false;
        }

        private void OnEntityAdded(SandboxEntity entity)
        {
            ExplosiveProp explosive = entity.GetComponent<ExplosiveProp>(); // once per registration
            if (explosive != null) explosive.System = this;
        }

        /// <summary>Queues the explosive (ignored if already queued).</summary>
        public void Request(ExplosiveProp prop, float delay)
        {
            if (!_valid || prop == null || prop.Entity == null || !prop.Entity.IsRegistered) return;
            int id = prop.Entity.Id;
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].EntityId == id) return;
            _queue.Add(new Pending { Prop = prop, EntityId = id, Time = Time.time + delay });
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _queue.Count; i++)
            {
                Pending p = _queue[i];
                if (p.Time > now) continue;
                _queue.RemoveAt(i);
                i--;
                Detonate(p);
            }
        }

        private void Detonate(Pending p)
        {
            ExplosiveProp prop = p.Prop;
            if (prop == null || prop.Entity == null || prop.Entity.Id != p.EntityId) return; // removed / recycled meanwhile

            Vector3 center = prop.transform.position;
            float radius = prop.Radius;
            float strength = prop.Strength;
            float upwards = prop.Upwards;
            float chainDelay = prop.ChainDelay;

            registry.RemoveEntity(prop.Entity); // first: it must not push itself or chain into itself

            int count = Physics.OverlapSphereNonAlloc(center, radius, _hits, affectMask, QueryTriggerInteraction.Ignore);
            int pushed = 0;
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb = _hits[i].attachedRigidbody;
                _hits[i] = null;
                if (rb == null || rb.isKinematic || AlreadyPushed(rb, pushed)) continue;
                if (pushed < _pushed.Length) _pushed[pushed++] = rb;

                rb.AddExplosionForce(strength, center, radius, upwards, ForceMode.VelocityChange);
                rb.WakeUp();

                ExplosiveProp other = rb.GetComponent<ExplosiveProp>();
                if (other != null) Request(other, chainDelay);
            }
            for (int i = 0; i < pushed; i++) _pushed[i] = null;

            Exploded?.Invoke(center, radius);
        }

        private bool AlreadyPushed(Rigidbody rb, int count)
        {
            for (int i = 0; i < count; i++)
                if (_pushed[i] == rb) return true;
            return false;
        }
    }
}
