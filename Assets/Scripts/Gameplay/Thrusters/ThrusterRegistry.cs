using System;
using System.Collections.Generic;
using SimpleBox.Core;
using SimpleBox.World;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Scene-wide list of attached thrusters: session limit (from the quality profile) and per-owner on/off.
    /// Reads no input: the global THRUST toggle comes from PlayerCommands through SandboxActions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThrusterRegistry : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private QualityManager quality;
        [Tooltip("Held contraption bindings: thrusters with a binding fire while their owner holds it.")]
        [SerializeField] private InputBindings bindings;

        private List<PropThruster> _thrusters;
        private int _maxThrusters;
        private bool _anyOn;
        private bool _batch;
        private bool _valid;

        /// <summary>Arg: true if at least one thruster in the session is on.</summary>
        public event Action<bool> StateChanged;
        /// <summary>Args: count, max.</summary>
        public event Action<int, int> CountChanged;

        public bool IsValid => _valid;
        public bool AnyOn => _anyOn;
        public int Count => _thrusters != null ? _thrusters.Count : 0;
        public int MaxThrusters => _maxThrusters;
        public bool IsFull => !_valid || _thrusters.Count >= _maxThrusters;

        private void Awake()
        {
            if (quality == null || quality.Profile == null || bindings == null)
            {
                Debug.LogError($"[{nameof(ThrusterRegistry)}] '{name}': Quality Manager (with a profile) / Bindings not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            _maxThrusters = quality.Profile.maxThrusters;
            _thrusters = new List<PropThruster>(_maxThrusters);
            _valid = true;
        }

        public bool IsBindingHeld(int ownerId, int binding) => bindings != null && bindings.IsHeld(ownerId, binding);

        public bool AnyOnOwnedBy(int ownerId)
        {
            if (!_valid) return false;
            for (int i = 0; i < _thrusters.Count; i++)
            {
                PropThruster t = _thrusters[i];
                if (t != null && t.OwnerId == ownerId && t.IsOn) return true;
            }
            return false;
        }

        public void SetAllOwnedBy(int ownerId, bool on)
        {
            if (!_valid) return;
            _batch = true;
            for (int i = 0; i < _thrusters.Count; i++)
            {
                PropThruster t = _thrusters[i];
                if (t == null || t.OwnerId != ownerId) continue;
                t.SetOn(on);
                if (on && t.Host != null && t.Host.Body != null) t.Host.Body.WakeUp();
            }
            _batch = false;
            RecomputeState();
        }

        internal bool Register(PropThruster thruster)
        {
            if (!_valid || _thrusters.Count >= _maxThrusters) return false;
            _thrusters.Add(thruster);
            CountChanged?.Invoke(_thrusters.Count, _maxThrusters);
            return true;
        }

        internal void Unregister(PropThruster thruster)
        {
            if (_thrusters == null) return;
            int index = _thrusters.IndexOf(thruster);
            if (index < 0) return;
            _thrusters.RemoveAt(index);
            CountChanged?.Invoke(_thrusters.Count, _maxThrusters);
            RecomputeState();
        }

        internal void NotifyStateChanged()
        {
            if (!_batch) RecomputeState();
        }

        private void RecomputeState()
        {
            bool any = false;
            for (int i = 0; i < _thrusters.Count; i++)
            {
                if (_thrusters[i] != null && _thrusters[i].IsOn)
                {
                    any = true;
                    break;
                }
            }
            if (any == _anyOn) return;
            _anyOn = any;
            StateChanged?.Invoke(any);
        }
    }
}
