using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Marks a child Rigidbody (ragdoll bone) as part of a SandboxEntity on an ancestor.
    /// Tools resolve the entity through SandboxEntity.FromBody, so any bone targets the whole ragdoll.
    /// The reference is assigned by the scene builder (prefab); if empty it is found once in Awake.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class EntityPart : MonoBehaviour
    {
        [SerializeField] private SandboxEntity entity;

        public SandboxEntity Entity => entity;

        private void Awake()
        {
            if (entity == null) entity = GetComponentInParent<SandboxEntity>(true);
            if (entity == null)
            {
                Debug.LogError($"[{nameof(EntityPart)}] '{name}': no SandboxEntity on an ancestor. Component disabled.", this);
                enabled = false;
            }
        }

        /// <summary>Editor/builder: sets the owning entity.</summary>
        public void SetEntity(SandboxEntity owner) => entity = owner;
    }
}
