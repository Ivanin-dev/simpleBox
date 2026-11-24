using System;
using Unity.Netcode;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>Replicated state of one thruster on an entity (local pose relative to the entity root + on/off).</summary>
    public struct ThrusterVisualState : INetworkSerializable, IEquatable<ThrusterVisualState>
    {
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public bool On;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LocalPosition);
            serializer.SerializeValue(ref LocalRotation);
            serializer.SerializeValue(ref On);
        }

        public bool Equals(ThrusterVisualState other)
        {
            return LocalPosition == other.LocalPosition && LocalRotation == other.LocalRotation && On == other.On;
        }

        public override bool Equals(object obj) => obj is ThrusterVisualState other && Equals(other);

        public override int GetHashCode() => LocalPosition.GetHashCode() ^ (LocalRotation.GetHashCode() << 2) ^ (On ? 1 : 0);
    }
}
