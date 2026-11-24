using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Serializable description of a construction (save file and duplicator clipboard), JsonUtility-friendly.
    /// Poses are relative to a pivot (identity for full saves = world space; the copied prop's position and the
    /// player's yaw for the duplicator). Records reference each other by list index, never by runtime ids,
    /// and props by catalog type id, so a blueprint survives restarts, pooling and catalog reordering.
    /// </summary>
    [Serializable]
    public sealed class Blueprint
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAt = string.Empty;
        /// <summary>Lowest point of the group relative to the pivot (approximate), used to rest a paste on the surface.</summary>
        public float minLocalY;
        public List<EntityRecord> entities = new List<EntityRecord>();
        public List<ThrusterRecord> thrusters = new List<ThrusterRecord>();
        public List<ConstraintRecord> constraints = new List<ConstraintRecord>();
    }

    [Serializable]
    public sealed class EntityRecord
    {
        public string type;
        public Vector3 position;
        public Quaternion rotation;
        public bool frozen;
        /// <summary>Palette color index + 1 (0 = original materials; older saves without the field stay unpainted).</summary>
        public int paint;
    }

    [Serializable]
    public sealed class ThrusterRecord
    {
        /// <summary>Index into Blueprint.entities.</summary>
        public int host;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float force;
        public int binding;
        public bool on;
    }

    [Serializable]
    public sealed class ConstraintRecord
    {
        public int kind;
        /// <summary>Index into Blueprint.entities.</summary>
        public int a;
        /// <summary>Index into Blueprint.entities, or -1 for the world.</summary>
        public int b = -1;
        public Vector3 anchorA;
        /// <summary>Local to B, world point (pivot space) when b = -1, or the hinge axis for Axis.</summary>
        public Vector3 anchorB;
        public float length;

        // Weld
        public float breakForce;
        public float breakTorque;

        // Axis motor
        public int motorForward;
        public int motorBackward;
        public float motorSpeed;
        public float motorForce;

        // Hydraulic
        public int extend;
        public int retract;
        public float minLength;
        public float maxLength;
        public float speed;
    }
}
