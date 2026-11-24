using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Ragdoll bone (child Rigidbody): relays its collisions to the root's ImpactSound, so a ragdoll sounds like one prop
    /// (shared cooldown). The reference is assigned by the scene builder; if empty it is found once in Awake.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RagdollBone : MonoBehaviour
    {
        [SerializeField] private ImpactSound rootSound;

        private void Awake()
        {
            if (rootSound == null) rootSound = GetComponentInParent<ImpactSound>(true);
            if (rootSound == null) enabled = false; // silent ragdoll is valid (no ImpactSound on the root)
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (enabled) rootSound.Report(collision);
        }

        /// <summary>Editor/builder: sets the root's ImpactSound.</summary>
        public void SetRootSound(ImpactSound sound) => rootSound = sound;
    }
}
