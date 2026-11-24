using System;
using UnityEngine;

namespace SimpleBox.Gameplay
{
    public enum PropBoundsMode
    {
        /// <summary>Spawn box is computed from the prefab's Box/Sphere/Capsule colliders.</summary>
        AutoFromColliders = 0,
        /// <summary>Spawn box is taken from ManualCenter / ManualHalfExtents (prefab-root local space, root scale included).</summary>
        Manual = 1
    }

    /// <summary>One spawnable prop entry.</summary>
    [Serializable]
    public sealed class PropDefinition
    {
        [Tooltip("Stable type id (lowercase, unique, never change after release): save files, duplicator and network use it, not the list index.")]
        public string id = string.Empty;

        [Tooltip("Name shown in the HUD.")]
        public string displayName = "Prop";

        [Tooltip("Root must have a non-kinematic Rigidbody. Colliders: Box/Sphere/Capsule only. Layer: Props.")]
        public GameObject prefab;

        [Tooltip("Optional HUD icon.")]
        public Sprite icon;

        public PropBoundsMode boundsMode = PropBoundsMode.AutoFromColliders;

        [Tooltip("Manual mode: center of the spawn box relative to the prefab pivot (meters, root scale included).")]
        public Vector3 manualCenter = Vector3.zero;

        [Tooltip("Manual mode: half size of the spawn box (meters, root scale included).")]
        public Vector3 manualHalfExtents = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("Instances created inactive at startup (avoids Instantiate hitch on first spawns).")]
        [Min(0)] public int prewarmCount = 0;

        [Tooltip("Max live instances of this type per player (0 = only the general limit). Spawning over it recycles the player's oldest one. Heavy props such as ragdolls.")]
        [Min(0)] public int maxPerOwner = 0;
    }

    /// <summary>
    /// List of spawnable props. Create: Assets → Create → SimpleBox → Prop Catalog.
    /// Index in the list = selection index (keys 1..9 map to indices 0..8).
    /// </summary>
    [CreateAssetMenu(fileName = "PropCatalog", menuName = "SimpleBox/Prop Catalog")]
    public sealed class PropCatalog : ScriptableObject
    {
        [SerializeField] private PropDefinition[] props = Array.Empty<PropDefinition>();

        public int Count => props != null ? props.Length : 0;

        public PropDefinition Get(int index) => props[index];

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (props == null) return;
            for (int i = 0; i < props.Length; i++)
            {
                PropDefinition d = props[i];
                if (d == null) continue;
                if (string.IsNullOrWhiteSpace(d.id))
                    Debug.LogWarning($"[PropCatalog] Entry #{i} '{d.displayName}': id is empty.", this);
                for (int j = 0; j < i; j++)
                {
                    if (props[j] != null && !string.IsNullOrEmpty(d.id) && props[j].id == d.id)
                        Debug.LogWarning($"[PropCatalog] Duplicate id '{d.id}' (entries #{j} and #{i}).", this);
                }
                if (d.prefab == null) continue;
                if (d.prefab.GetComponent<Rigidbody>() == null)
                    Debug.LogWarning($"[PropCatalog] '{d.displayName}': prefab root has no Rigidbody.", this);
                if (d.prefab.GetComponentInChildren<MeshCollider>(true) != null)
                    Debug.LogWarning($"[PropCatalog] '{d.displayName}': MeshCollider found. Dynamic props must use Box/Sphere/Capsule only.", this);
            }
        }
#endif
    }
}
