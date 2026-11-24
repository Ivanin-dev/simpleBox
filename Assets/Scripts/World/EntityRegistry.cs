using System;
using System.Collections.Generic;
using SimpleBox.Core;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Single source of truth for world objects: entities, attachments and constraints, all with ids from one
    /// never-reused id space (Undo, network and save reference these ids).
    /// Every removal goes through here: EntityRemoving event (physguns drop) -> attachments and constraints removed
    /// -> unregistered -> instance recycled to its pool or destroyed.
    /// Containers are pre-sized from the quality profile; registration / lookup / removal allocate nothing.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class EntityRegistry : MonoBehaviour
    {
        public enum RemovedKind { None, Entity, Attachment, Constraint }

        [Header("References (required)")]
        [SerializeField] private QualityManager quality;

        private Dictionary<int, SandboxEntity> _entities;
        private List<SandboxEntity> _ordered;               // registration order (oldest first)
        private Dictionary<int, IEntityAttachment> _attachments;
        private Dictionary<int, SandboxConstraint> _constraintsById;
        private List<SandboxConstraint> _constraints;
        private Dictionary<int, int> _ownerCounts;

        private int _nextId = 1;
        private bool _valid;
        private bool _shuttingDown;

        /// <summary>Entity registered (spawned or hand-placed).</summary>
        public event Action<SandboxEntity> EntityAdded;
        /// <summary>Entity is about to be removed (still fully valid). Physguns drop it here.</summary>
        public event Action<SandboxEntity> EntityRemoving;
        /// <summary>Args: owner id, entities owned now.</summary>
        public event Action<int, int> OwnerCountChanged;
        /// <summary>Constraint created (authority).</summary>
        public event Action<SandboxConstraint> ConstraintAdded;
        /// <summary>Constraint removed (broken, undone, or its entity removed).</summary>
        public event Action<SandboxConstraint> ConstraintRemoved;

        public bool IsValid => _valid;
        public int EntityCount => _ordered != null ? _ordered.Count : 0;
        public int ConstraintCount => _constraints != null ? _constraints.Count : 0;
        public int AttachmentCount => _attachments != null ? _attachments.Count : 0;
        public SandboxEntity GetEntityAt(int index) => _ordered[index];
        public SandboxConstraint GetConstraintAt(int index) => _constraints[index];

        private void Awake()
        {
            if (quality == null)
            {
                Debug.LogError($"[{nameof(EntityRegistry)}] '{name}': Quality Manager not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            QualityProfile p = quality.Profile;
            if (p == null)
            {
                Debug.LogError($"[{nameof(EntityRegistry)}] '{name}': Quality Manager has no active profile. Component disabled.", this);
                enabled = false;
                return;
            }

            int entityCapacity = p.maxEntitiesTotal + 32; // + hand-placed props
            _entities = new Dictionary<int, SandboxEntity>(entityCapacity);
            _ordered = new List<SandboxEntity>(entityCapacity);
            _attachments = new Dictionary<int, IEntityAttachment>(p.maxThrusters + 8);
            _constraintsById = new Dictionary<int, SandboxConstraint>(p.maxConstraintsTotal + 8);
            _constraints = new List<SandboxConstraint>(p.maxConstraintsTotal + 8);
            _ownerCounts = new Dictionary<int, int>(16);
            _valid = true;
        }

        private void OnApplicationQuit() => _shuttingDown = true;
        private void OnDestroy() => _shuttingDown = true;

        private void Update()
        {
            // Prune constraints whose joint broke (breakForce) or was destroyed elsewhere.
            for (int i = _constraints.Count - 1; i >= 0; i--)
            {
                if (i >= _constraints.Count) continue;
                SandboxConstraint c = _constraints[i];
                if (!c.IsAlive) RemoveConstraintInternal(c, false);
            }
        }

        public int NewId() => _nextId++;

        // ------------------------------------------------------------------ Entities

        public void RegisterEntity(SandboxEntity entity, int ownerId, string typeId)
        {
            if (!_valid || entity == null || entity.IsRegistered) return;

            entity.Id = NewId();
            entity.OwnerId = ownerId;
            entity.TypeId = typeId ?? string.Empty;
            entity.Registry = this;
            entity.RemovalInProgress = false;
            entity.IsMirror = false;
            entity.HeldBy = SandboxEntity.NoHolder;

            _entities.Add(entity.Id, entity);
            _ordered.Add(entity);
            ChangeOwnerCount(ownerId, 1);

            Rigidbody rb = entity.Body;
            if (rb != null)
            {
                QualityProfile p = quality.Profile;
                rb.sleepThreshold = p.sleepThreshold;
                rb.maxDepenetrationVelocity = p.maxDepenetrationVelocity;
                rb.maxAngularVelocity = p.maxAngularSpeed;
            }

            EntityAdded?.Invoke(entity);
        }

        // ------------------------------------------------------------------ Network mirrors (clients)

        /// <summary>
        /// Client side: registers a copy of a host entity with the host's id / owner / type, so local tools can
        /// target it by id. No id is allocated and the Rigidbody is left to the network layer.
        /// </summary>
        public void RegisterMirror(SandboxEntity entity, int id, int ownerId, string typeId)
        {
            if (!_valid || entity == null || id == 0) return;
            if (entity.IsRegistered) UnregisterSilently(entity);
            if (_entities.ContainsKey(id)) return;

            entity.Id = id;
            entity.OwnerId = ownerId;
            entity.TypeId = typeId ?? string.Empty;
            entity.Registry = this;
            entity.RemovalInProgress = false;
            entity.IsMirror = true;
            entity.SetPartsKinematic(true); // ragdoll bones: host simulates, NetworkTransforms move the copies
            entity.HeldBy = SandboxEntity.NoHolder;

            _entities.Add(id, entity);
            _ordered.Add(entity);
            ChangeOwnerCount(ownerId, 1);
            EntityAdded?.Invoke(entity);
        }

        /// <summary>Client side: the host despawned the entity (or we left the session).</summary>
        public void UnregisterMirror(SandboxEntity entity)
        {
            if (!_valid || entity == null || !entity.IsRegistered) return;
            entity.RemovalInProgress = true;
            EntityRemoving?.Invoke(entity);
            entity.Attachments.Clear();
            entity.Constraints.Clear();
            Unregister(entity);
            entity.IsMirror = false;
        }

        /// <summary>
        /// Before joining a session as a client: removes everything spawned locally while offline and unregisters
        /// hand-placed world entities (they come back as mirrors from the host).
        /// </summary>
        public void ClearLocalWorldForClient()
        {
            for (int i = _ordered.Count - 1; i >= 0; i--)
            {
                if (i >= _ordered.Count) continue;
                SandboxEntity e = _ordered[i];
                if (e == null) continue;
                if (e.Pool != null) RemoveEntity(e);       // offline-spawned prop: back to the pool
                else UnregisterSilently(e);                 // hand-placed: keep the object, drop local registration
            }
        }

        /// <summary>Drops the registration without events or disposal (object stays as is).</summary>
        private void UnregisterSilently(SandboxEntity entity)
        {
            DetachAll(entity, true);
            _entities.Remove(entity.Id);
            _ordered.Remove(entity);
            ChangeOwnerCount(entity.OwnerId, -1);
            entity.Id = 0;
            entity.HeldBy = SandboxEntity.NoHolder;
        }

        public bool TryGetEntity(int id, out SandboxEntity entity)
        {
            entity = null;
            return _valid && id != 0 && _entities.TryGetValue(id, out entity) && entity != null;
        }

        public int CountOwnedBy(int ownerId)
        {
            return _valid && _ownerCounts.TryGetValue(ownerId, out int count) ? count : 0;
        }

        /// <summary>Oldest entity of the owner that nobody is holding, or null.</summary>
        public SandboxEntity FindOldestRemovable(int ownerId)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                SandboxEntity e = _ordered[i];
                if (e != null && e.OwnerId == ownerId && !e.IsHeld) return e;
            }
            return null;
        }

        /// <summary>Number of entities of this owner with this catalog type id (linear scan; spawn requests only).</summary>
        public int CountOwnedBy(int ownerId, string typeId)
        {
            int count = 0;
            for (int i = 0; i < _ordered.Count; i++)
            {
                SandboxEntity e = _ordered[i];
                if (e != null && e.OwnerId == ownerId && string.Equals(e.TypeId, typeId)) count++;
            }
            return count;
        }

        /// <summary>Oldest unheld entity of the owner with this catalog type id, or null.</summary>
        public SandboxEntity FindOldestRemovable(int ownerId, string typeId)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                SandboxEntity e = _ordered[i];
                if (e != null && e.OwnerId == ownerId && !e.IsHeld && string.Equals(e.TypeId, typeId)) return e;
            }
            return null;
        }

        /// <summary>Removes the entity with everything attached. Call from normal game code only (not OnDisable/physics callbacks).</summary>
        public bool RemoveEntity(SandboxEntity entity)
        {
            if (!_valid || entity == null || !entity.IsRegistered || entity.Registry != this) return false;

            entity.RemovalInProgress = true;
            EntityRemoving?.Invoke(entity);
            DetachAll(entity, true);
            Unregister(entity);

            if (entity.Pool != null)
            {
                entity.Pool.Recycle(entity);
            }
            else
            {
                entity.gameObject.SetActive(false); // leaves physics / raycasts this frame
                Destroy(entity.gameObject);
            }
            return true;
        }

        /// <summary>Removes every entity of the owner. Returns the number removed.</summary>
        public int RemoveAllOwnedBy(int ownerId)
        {
            int removed = 0;
            for (int i = _ordered.Count - 1; i >= 0; i--)
            {
                if (i >= _ordered.Count) continue;
                SandboxEntity e = _ordered[i];
                if (e != null && e.OwnerId == ownerId && RemoveEntity(e)) removed++;
            }
            return removed;
        }

        /// <summary>Called by SandboxEntity.OnDisable / OnDestroy when someone bypassed RemoveEntity.</summary>
        internal void HandleExternalRemoval(SandboxEntity entity, bool destroyed)
        {
            if (_shuttingDown || !_valid || !entity.IsRegistered) return;

            entity.RemovalInProgress = true;
            EntityRemoving?.Invoke(entity);
            DetachAll(entity, false);
            Unregister(entity);
            if (entity.Pool != null) entity.Pool.HandleExternalLoss(entity, destroyed);
        }

        private void Unregister(SandboxEntity entity)
        {
            _entities.Remove(entity.Id);
            _ordered.Remove(entity);
            ChangeOwnerCount(entity.OwnerId, -1);
            entity.Id = 0;
            entity.HeldBy = SandboxEntity.NoHolder;
        }

        private void ChangeOwnerCount(int ownerId, int delta)
        {
            _ownerCounts.TryGetValue(ownerId, out int count);
            count = Mathf.Max(0, count + delta);
            _ownerCounts[ownerId] = count;
            OwnerCountChanged?.Invoke(ownerId, count);
        }

        private void DetachAll(SandboxEntity entity, bool immediate)
        {
            List<IEntityAttachment> attachments = entity.Attachments;
            for (int i = attachments.Count - 1; i >= 0; i--)
            {
                IEntityAttachment a = attachments[i];
                if (a == null) continue;
                _attachments.Remove(a.AttachmentId);
                a.Detach(immediate);
            }
            attachments.Clear();

            List<SandboxConstraint> constraints = entity.Constraints;
            for (int i = constraints.Count - 1; i >= 0; i--)
            {
                if (i >= constraints.Count) continue;
                RemoveConstraintInternal(constraints[i], immediate);
            }
        }

        // ------------------------------------------------------------------ Attachments

        /// <summary>Returns the new attachment id.</summary>
        public int RegisterAttachment(IEntityAttachment attachment, SandboxEntity host)
        {
            int id = NewId();
            _attachments.Add(id, attachment);
            host.Attachments.Add(attachment);
            return id;
        }

        public bool RemoveAttachment(int attachmentId)
        {
            if (!_attachments.TryGetValue(attachmentId, out IEntityAttachment a)) return false;
            _attachments.Remove(attachmentId);
            if (a.Host != null) a.Host.Attachments.Remove(a);
            a.Detach(true);
            return true;
        }

        /// <summary>Attachment destroyed with its object without going through RemoveAttachment.</summary>
        internal void HandleAttachmentDestroyed(IEntityAttachment attachment)
        {
            if (_shuttingDown || !_valid) return;
            _attachments.Remove(attachment.AttachmentId);
            if (attachment.Host != null) attachment.Host.Attachments.Remove(attachment);
        }

        // ------------------------------------------------------------------ Constraints

        public void AddConstraint(SandboxConstraint constraint)
        {
            constraint.Id = NewId();
            _constraintsById.Add(constraint.Id, constraint);
            _constraints.Add(constraint);
            constraint.A.Constraints.Add(constraint);
            if (constraint.B != null) constraint.B.Constraints.Add(constraint);
            ConstraintAdded?.Invoke(constraint);
        }

        public bool RemoveConstraint(int constraintId)
        {
            if (!_constraintsById.TryGetValue(constraintId, out SandboxConstraint c)) return false;
            RemoveConstraintInternal(c, true);
            return true;
        }

        /// <summary>Removes every constraint touching the entity. Returns the number removed.</summary>
        public int RemoveConstraintsOf(SandboxEntity entity)
        {
            int removed = 0;
            List<SandboxConstraint> list = entity.Constraints;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                RemoveConstraintInternal(list[i], true);
                removed++;
            }
            return removed;
        }

        /// <summary>b == null asks about a world weld.</summary>
        public bool HasConstraintBetween(SandboxEntity a, SandboxEntity b)
        {
            List<SandboxConstraint> list = a.Constraints;
            for (int i = 0; i < list.Count; i++)
            {
                SandboxConstraint c = list[i];
                if (c.IsAlive && c.Other(a) == b) return true;
            }
            return false;
        }

        private void RemoveConstraintInternal(SandboxConstraint c, bool immediate)
        {
            if (!_constraintsById.Remove(c.Id)) return;
            _constraints.Remove(c);
            if (c.A != null) c.A.Constraints.Remove(c);
            if (c.B != null) c.B.Constraints.Remove(c);

            if (c.Joint != null)
            {
                if (immediate) DestroyImmediate(c.Joint);
                else Destroy(c.Joint);
            }

            WakeIfActive(c.A);
            WakeIfActive(c.B);
            ConstraintRemoved?.Invoke(c);
        }

        private static void WakeIfActive(SandboxEntity e)
        {
            if (e != null && e.gameObject.activeInHierarchy && e.Body != null && !e.Body.isKinematic) e.Body.WakeUp();
        }

        // ------------------------------------------------------------------ Generic removal (Undo)

        /// <summary>Removes whatever the id refers to (entity, attachment or constraint).</summary>
        public RemovedKind RemoveById(int id)
        {
            if (TryGetEntity(id, out SandboxEntity e)) return RemoveEntity(e) ? RemovedKind.Entity : RemovedKind.None;
            if (RemoveAttachment(id)) return RemovedKind.Attachment;
            if (RemoveConstraint(id)) return RemovedKind.Constraint;
            return RemovedKind.None;
        }

        public bool Exists(int id)
        {
            return _valid && (_entities.ContainsKey(id) || _attachments.ContainsKey(id) || _constraintsById.ContainsKey(id));
        }
    }
}
