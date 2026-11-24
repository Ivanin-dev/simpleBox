using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Draws ropes and elastics as 2-point LineRenderers between their anchors.
    /// Authority / single player: entries follow EntityRegistry.ConstraintAdded / ConstraintRemoved.
    /// Multiplayer client (no constraints locally): entries are pushed by the network layer (SetReplicated).
    /// LateUpdate resolves both ends by entity id (dictionary lookup) and writes 2 positions per line: no allocation.
    /// LineRenderers are pooled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CableVisuals : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private Material lineMaterial;

        [Header("Look")]
        [SerializeField, Min(0.005f)] private float ropeWidth = 0.05f;
        [SerializeField] private Color ropeColor = new Color(0.25f, 0.2f, 0.15f);
        [SerializeField] private Color elasticColor = new Color(0.9f, 0.3f, 0.2f);
        [SerializeField] private Color hydraulicColor = new Color(0.55f, 0.58f, 0.62f);
        [SerializeField, Min(0.005f)] private float hydraulicWidth = 0.1f;

        private struct Entry
        {
            public int Id;
            public int AId;
            public int BId;          // 0 = world
            public Vector3 AnchorA;  // local to A
            public Vector3 AnchorB;  // local to B, or world point
            public LineRenderer Line;
        }

        private readonly List<Entry> _entries = new List<Entry>(32);
        private readonly Stack<LineRenderer> _pool = new Stack<LineRenderer>(32);
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            _valid = registry != null && lineMaterial != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(CableVisuals)}] '{name}': Registry / Line Material not assigned. Component disabled.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.ConstraintAdded += OnConstraintAdded;
            registry.ConstraintRemoved += OnConstraintRemoved;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (registry != null)
            {
                registry.ConstraintAdded -= OnConstraintAdded;
                registry.ConstraintRemoved -= OnConstraintRemoved;
            }
            _subscribed = false;
        }

        private void OnConstraintAdded(SandboxConstraint c)
        {
            if (!c.IsCable) return;
            Add(c.Id, c.A.Id, c.B != null ? c.B.Id : 0, c.AnchorA, c.AnchorB, c.Kind);
        }

        private void OnConstraintRemoved(SandboxConstraint c)
        {
            if (c.IsCable) Remove(c.Id);
        }

        // ------------------------------------------------------------------ Network (client side)

        /// <summary>Client: removes every replicated cable (before re-adding the current list).</summary>
        public void ClearAll()
        {
            for (int i = _entries.Count - 1; i >= 0; i--) Release(_entries[i].Line);
            _entries.Clear();
        }

        public void Add(int id, int aId, int bId, Vector3 anchorA, Vector3 anchorB, ConstraintKind kind)
        {
            if (!_valid) return;
            LineRenderer line = _pool.Count > 0 ? _pool.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            Color color = kind == ConstraintKind.Elastic ? elasticColor : kind == ConstraintKind.Hydraulic ? hydraulicColor : ropeColor;
            float width = kind == ConstraintKind.Hydraulic ? hydraulicWidth : ropeWidth;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            _entries.Add(new Entry { Id = id, AId = aId, BId = bId, AnchorA = anchorA, AnchorB = anchorB, Line = line });
        }

        public void Remove(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id != id) continue;
                Release(_entries[i].Line);
                _entries.RemoveAt(i);
                return;
            }
        }

        // ------------------------------------------------------------------ Drawing

        private void LateUpdate()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                bool visible = TryResolve(e.AId, e.AnchorA, out Vector3 a);
                Vector3 b = e.AnchorB;
                if (visible && e.BId != 0) visible = TryResolve(e.BId, e.AnchorB, out b);

                if (e.Line.enabled != visible) e.Line.enabled = visible;
                if (!visible) continue;
                e.Line.SetPosition(0, a);
                e.Line.SetPosition(1, b);
            }
        }

        private bool TryResolve(int entityId, Vector3 local, out Vector3 world)
        {
            if (registry.TryGetEntity(entityId, out SandboxEntity entity) && entity.gameObject.activeInHierarchy)
            {
                world = entity.transform.TransformPoint(local);
                return true;
            }
            world = default;
            return false;
        }

        private LineRenderer CreateLine()
        {
            var go = new GameObject("Cable");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = ropeWidth;
            line.endWidth = ropeWidth;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void Release(LineRenderer line)
        {
            if (line == null) return;
            line.gameObject.SetActive(false);
            _pool.Push(line);
        }
    }
}
