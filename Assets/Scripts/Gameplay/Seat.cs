using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Marks a prop as a seat (on the seat prefab root, next to the Rigidbody). SitPoint must be an unscaled child:
    /// the player rig is parented to it while seated, so it moves with the contraption.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Seat : MonoBehaviour
    {
        [Tooltip("Unscaled child where the seated player's root is placed.")]
        [SerializeField] private Transform sitPoint;

        public Transform SitPoint => sitPoint != null ? sitPoint : transform;

        /// <summary>Taken by the local player (single player / own client).</summary>
        public bool Occupied { get; internal set; }

        private void OnDisable() => Occupied = false;
    }
}
