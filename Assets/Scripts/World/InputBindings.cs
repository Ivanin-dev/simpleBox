using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Contraption controls (GMod numpad style): 4 bindable buttons per player. Thrusters and axis motors
    /// reference a binding number (1..4) and react while their owner holds it.
    /// State lives on the authority as a bit mask per owner (bit 0 = binding 1). Locally it comes from
    /// PlayerCommands; on a multiplayer client the mask is sent to the host through SandboxActions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputBindings : MonoBehaviour
    {
        public const int Count = 4;

        private readonly Dictionary<int, int> _masks = new Dictionary<int, int>(8);

        /// <summary>Args: owner, new mask.</summary>
        public event Action<int, int> MaskChanged;

        /// <summary>binding = 1..4. 0 (none) is never held.</summary>
        public bool IsHeld(int ownerId, int binding)
        {
            if (binding < 1 || binding > Count) return false;
            return _masks.TryGetValue(ownerId, out int mask) && (mask & (1 << (binding - 1))) != 0;
        }

        public int GetMask(int ownerId) => _masks.TryGetValue(ownerId, out int mask) ? mask : 0;

        /// <summary>Authority: sets the owner's held bindings.</summary>
        public void SetMask(int ownerId, int mask)
        {
            mask &= (1 << Count) - 1;
            if (GetMask(ownerId) == mask) return;
            _masks[ownerId] = mask;
            MaskChanged?.Invoke(ownerId, mask);
        }

        public void ClearOwner(int ownerId) => SetMask(ownerId, 0);
    }
}
