using UnityEngine;

namespace SimpleBox.Gameplay
{
    /// <summary>
    /// Multiplayer client side of the physgun: the local PhysicsGun forwards every command here (network avatar),
    /// the host simulates the held prop and reports the holding state back.
    /// </summary>
    public interface IPhysgunRemote
    {
        bool IsHolding { get; }
        void ToggleGrab();
        void ToggleFreeze();
        void RotateHeld(Vector2 degrees);
        void AdjustHoldDistance(float meters);
        void SetRotationSnap(float degrees);
        void Drop();
    }
}
