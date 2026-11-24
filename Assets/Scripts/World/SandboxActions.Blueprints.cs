using System;
using System.Collections.Generic;
using SimpleBox.Core;
using SimpleBox.Gameplay;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Blueprints: capture a construction (or everything a player owns) to JSON and spawn it back.
    /// Used by saves (full world, pivot = identity, replace own entities) and the duplicator (group of the aimed prop,
    /// pivot = that prop + player yaw, lifted until it fits). Capture and spawn run on the authority; a multiplayer
    /// client requests them through IActionTransport (capture result comes back via ReportCapture).
    /// Spawning reuses the normal Execute* paths, so limits, ownership, Undo and replication all apply.
    /// User actions only (allocates).
    /// </summary>
    public sealed partial class SandboxActions
    {
        private const float LiftStep = 0.25f;
        private const int LiftAttempts = 12;

        /// <summary>Args: request tag, blueprint JSON (empty if nothing was captured).</summary>
        public event Action<int, string> CaptureReady;

        // ================================================================== Requests (local player)

        /// <summary>
        /// Captures the group connected to rootEntityId (0 = everything the local player owns, in world space).
        /// pivotYaw = player yaw for the duplicator. The result arrives through CaptureReady with the same tag
        /// (immediately offline / on the host, later on a client).
        /// </summary>
        public void RequestCapture(int rootEntityId, float pivotYaw, int tag)
        {
            if (_transport != null)
            {
                _transport.SendCaptureRequest(rootEntityId, pivotYaw, tag);
                return;
            }
            CaptureReady?.Invoke(tag, ExecuteCapture(_localOwner, rootEntityId, pivotYaw));
        }

        /// <summary>Network layer: the host returned a capture.</summary>
        public void ReportCapture(int tag, string json) => CaptureReady?.Invoke(tag, json ?? string.Empty);

        /// <summary>Spawns a blueprint at pivot (position, yaw). replaceOwned = remove own entities first (loading a save).</summary>
        public ActionResult SpawnBlueprint(string json, Vector3 pivotPosition, float pivotYaw, bool liftToFit, bool replaceOwned)
        {
            if (_transport != null)
            {
                _transport.SendSpawnBlueprint(json, pivotPosition, pivotYaw, liftToFit, replaceOwned);
                return ActionResult.Pending;
            }
            return ExecuteSpawnBlueprintJson(_localOwner, json, pivotPosition, pivotYaw, liftToFit, replaceOwned, out _);
        }

        // ================================================================== Authority

        /// <summary>Returns blueprint JSON, or an empty string when there is nothing to capture.</summary>
        public string ExecuteCapture(int owner, int rootEntityId, float pivotYaw)
        {
            if (!_valid) return string.Empty;

            var group = new List<SandboxEntity>();
            Vector3 pivotPos = Vector3.zero;
            Quaternion pivotRot = Quaternion.identity;

            if (rootEntityId == 0)
            {
                for (int i = 0; i < registry.EntityCount; i++)
                {
                    SandboxEntity e = registry.GetEntityAt(i);
                    if (e != null && e.OwnerId == owner && e.TypeId.Length > 0) group.Add(e);
                }
            }
            else
            {
                if (!registry.TryGetEntity(rootEntityId, out SandboxEntity root) || !CanModify(owner, root)) return string.Empty;
                CollectGroup(root, group);
                pivotPos = root.transform.position;
                pivotRot = Quaternion.Euler(0f, pivotYaw, 0f);
            }
            if (group.Count == 0) return string.Empty;

            Blueprint bp = BuildBlueprint(group, pivotPos, pivotRot, includeWorldConstraints: rootEntityId == 0);
            return JsonUtility.ToJson(bp);
        }

        public ActionResult ExecuteSpawnBlueprintJson(int owner, string json, Vector3 pivotPosition, float pivotYaw,
            bool liftToFit, bool replaceOwned, out int spawned)
        {
            spawned = 0;
            if (!_valid) return ActionResult.Unavailable;
            if (string.IsNullOrEmpty(json)) return ActionResult.Invalid;

            Blueprint bp;
            try
            {
                bp = JsonUtility.FromJson<Blueprint>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(SandboxActions)}] Blueprint parse failed: {e.Message}", this);
                return ActionResult.Invalid;
            }
            if (bp == null || bp.entities == null || bp.version > Blueprint.CurrentVersion) return ActionResult.Invalid;

            return ExecuteSpawnBlueprint(owner, bp, pivotPosition, Quaternion.Euler(0f, pivotYaw, 0f), liftToFit, replaceOwned, out spawned);
        }

        public ActionResult ExecuteSpawnBlueprint(int owner, Blueprint bp, Vector3 pivotPos, Quaternion pivotRot,
            bool liftToFit, bool replaceOwned, out int spawned)
        {
            spawned = 0;
            int count = bp.entities.Count;
            if (count == 0) return ActionResult.Invalid;

            if (replaceOwned) ExecuteRemoveAllOwned(owner, out _);

            QualityProfile p = quality.Profile;
            if (registry.CountOwnedBy(owner) + count > p.maxEntitiesPerOwner) return ActionResult.OwnerLimit;
            int playerEntities = registry.EntityCount - registry.CountOwnedBy(SandboxEntity.WorldOwner);
            if (playerEntities + count > p.maxEntitiesTotal) return ActionResult.ServerLimit;

            // Resolve types and world poses.
            var typeIndex = new int[count];
            var positions = new Vector3[count];
            var rotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                EntityRecord r = bp.entities[i];
                typeIndex[i] = r != null && spawner.TryGetTypeIndex(r.type, out int ti) ? ti : -1;
                if (r == null) continue;
                positions[i] = pivotPos + pivotRot * r.position;
                rotations[i] = pivotRot * r.rotation;
            }

            // Duplicator: lift the whole group until every part fits.
            if (liftToFit)
            {
                bool fits = false;
                for (int attempt = 0; attempt <= LiftAttempts && !fits; attempt++)
                {
                    Vector3 lift = Vector3.up * (LiftStep * attempt);
                    fits = true;
                    for (int i = 0; i < count && fits; i++)
                    {
                        if (typeIndex[i] >= 0 && !spawner.IsSpaceFree(typeIndex[i], positions[i] + lift, rotations[i])) fits = false;
                    }
                    if (!fits) continue;
                    for (int i = 0; i < count; i++) positions[i] += lift;
                }
                if (!fits) return ActionResult.NoSpace;
            }

            // Entities.
            var spawnedEntities = new SandboxEntity[count];
            for (int i = 0; i < count; i++)
            {
                if (typeIndex[i] < 0) continue; // unknown type (catalog changed): skipped
                if (spawner.SpawnAt(typeIndex[i], positions[i], rotations[i], owner, out SandboxEntity e, true) != ActionResult.Ok) continue;
                spawnedEntities[i] = e;
                History(owner).Push(e.Id);
                if (bp.entities[i].paint > 0) ExecuteSetColor(owner, e.Id, bp.entities[i].paint - 1);
                if (bp.entities[i].frozen)
                {
                    e.Body.linearVelocity = Vector3.zero;
                    e.Body.angularVelocity = Vector3.zero;
                    e.Body.isKinematic = true;
                }
                spawned++;
            }

            // Thrusters.
            if (bp.thrusters != null)
            {
                for (int i = 0; i < bp.thrusters.Count; i++)
                {
                    ThrusterRecord t = bp.thrusters[i];
                    SandboxEntity host = At(spawnedEntities, t.host);
                    if (host == null) continue;
                    ExecuteAttachThruster(owner, host.Id, t.localPosition, t.localRotation, t.force, t.binding, t.on);
                }
            }

            // Constraints (poses are already restored, so joints lock the saved relative arrangement).
            if (bp.constraints != null)
            {
                for (int i = 0; i < bp.constraints.Count; i++)
                {
                    ConstraintRecord c = bp.constraints[i];
                    SandboxEntity a = At(spawnedEntities, c.a);
                    if (a == null) continue;
                    SandboxEntity b = c.b >= 0 ? At(spawnedEntities, c.b) : null;
                    if (c.b >= 0 && b == null) continue;
                    int bId = b != null ? b.Id : 0;
                    Vector3 anchorB = b != null ? c.anchorB : pivotPos + pivotRot * c.anchorB;

                    switch ((ConstraintKind)c.kind)
                    {
                        case ConstraintKind.Weld:
                            ExecuteWeld(owner, a.Id, bId, new WeldSettings(c.breakForce, c.breakTorque, false));
                            break;
                        case ConstraintKind.Rope:
                        case ConstraintKind.Elastic:
                            ExecuteCreateCable(owner, a.Id, c.anchorA, bId, anchorB, (ConstraintKind)c.kind, c.length);
                            break;
                        case ConstraintKind.Axis:
                            ExecuteCreateAxis(owner, a.Id, c.anchorA, c.anchorB, bId,
                                new AxisMotorSettings(c.motorForward, c.motorBackward, c.motorSpeed, c.motorForce));
                            break;
                        case ConstraintKind.Hydraulic:
                            ExecuteCreateHydraulic(owner, a.Id, c.anchorA, bId, anchorB,
                                new HydraulicSettings(c.extend, c.retract, c.minLength, c.maxLength, c.speed));
                            break;
                    }
                }
            }

            return spawned > 0 ? ActionResult.Ok : ActionResult.Invalid;
        }

        // ================================================================== Helpers

        private static SandboxEntity At(SandboxEntity[] list, int index)
        {
            return index >= 0 && index < list.Length ? list[index] : null;
        }

        /// <summary>Breadth-first over constraints (world anchors excluded).</summary>
        private void CollectGroup(SandboxEntity root, List<SandboxEntity> group)
        {
            var visited = new HashSet<SandboxEntity> { root };
            var queue = new Queue<SandboxEntity>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                SandboxEntity e = queue.Dequeue();
                if (e.TypeId.Length > 0) group.Add(e);
                for (int i = 0; i < e.ConstraintCount; i++)
                {
                    SandboxEntity other = e.GetConstraint(i).Other(e);
                    if (other != null && other.IsRegistered && visited.Add(other)) queue.Enqueue(other);
                }
            }
        }

        private Blueprint BuildBlueprint(List<SandboxEntity> group, Vector3 pivotPos, Quaternion pivotRot, bool includeWorldConstraints)
        {
            Quaternion inverse = Quaternion.Inverse(pivotRot);
            var bp = new Blueprint { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            var index = new Dictionary<SandboxEntity, int>(group.Count);
            float minY = float.MaxValue;

            for (int i = 0; i < group.Count; i++)
            {
                SandboxEntity e = group[i];
                Transform t = e.transform;
                var record = new EntityRecord
                {
                    type = e.TypeId,
                    position = inverse * (t.position - pivotPos),
                    rotation = inverse * t.rotation,
                    frozen = e.Body != null && e.Body.isKinematic,
                    paint = e.ColorIndex + 1
                };
                index.Add(e, bp.entities.Count);
                bp.entities.Add(record);

                float extent = 0.5f;
                if (spawner.TryGetTypeIndex(e.TypeId, out int ti) && spawner.TryGetBounds(ti, out _, out Vector3 he))
                    extent = Mathf.Max(he.x, Mathf.Max(he.y, he.z));
                minY = Mathf.Min(minY, record.position.y - extent);

                for (int k = 0; k < e.AttachmentCount; k++)
                {
                    if (!(e.GetAttachment(k) is PropThruster thruster) || thruster == null) continue;
                    Transform tt = thruster.transform;
                    bp.thrusters.Add(new ThrusterRecord
                    {
                        host = index[e],
                        localPosition = tt.localPosition,
                        localRotation = tt.localRotation,
                        force = thruster.ThrustForce,
                        binding = thruster.Binding,
                        on = thruster.IsOn
                    });
                }
            }
            bp.minLocalY = minY == float.MaxValue ? 0f : minY;

            // Each constraint once: from A's side, when A is in the group.
            for (int i = 0; i < group.Count; i++)
            {
                SandboxEntity e = group[i];
                for (int k = 0; k < e.ConstraintCount; k++)
                {
                    SandboxConstraint c = e.GetConstraint(k);
                    if (c.A != e || !c.IsAlive) continue;
                    int bIndex = -1;
                    if (c.B != null)
                    {
                        if (!index.TryGetValue(c.B, out bIndex)) continue; // partner outside the group
                    }
                    else if (!includeWorldConstraints)
                    {
                        continue; // duplicator: copies are not anchored to the world
                    }

                    var r = new ConstraintRecord
                    {
                        kind = (int)c.Kind,
                        a = index[e],
                        b = bIndex,
                        anchorA = c.AnchorA,
                        anchorB = c.B != null || c.Kind == ConstraintKind.Axis ? c.AnchorB : inverse * (c.AnchorB - pivotPos),
                        length = c.Kind == ConstraintKind.Hydraulic ? c.CurrentLength : c.Length,
                        motorForward = c.Motor.ForwardBinding,
                        motorBackward = c.Motor.BackwardBinding,
                        motorSpeed = c.Motor.Speed,
                        motorForce = c.Motor.Force,
                        extend = c.Hydraulic.ExtendBinding,
                        retract = c.Hydraulic.RetractBinding,
                        minLength = c.Hydraulic.MinLength,
                        maxLength = c.Hydraulic.MaxLength,
                        speed = c.Hydraulic.Speed
                    };
                    if (c.Kind == ConstraintKind.Weld && c.Joint != null)
                    {
                        r.breakForce = float.IsInfinity(c.Joint.breakForce) ? 0f : c.Joint.breakForce;
                        r.breakTorque = float.IsInfinity(c.Joint.breakTorque) ? 0f : c.Joint.breakTorque;
                    }
                    bp.constraints.Add(r);
                }
            }
            return bp;
        }
    }
}
