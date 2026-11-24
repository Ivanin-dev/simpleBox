using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Implemented by the network layer on a connected (non-host) client. SandboxActions forwards every
    /// local request here instead of executing it; the host runs the matching Execute* with the sender's owner id
    /// and reports failures back through SandboxActions.ReportRemoteResult.
    /// </summary>
    public interface IActionTransport
    {
        void SendSpawnProp(int typeIndex, Vector3 position, Quaternion rotation);
        void SendRemoveEntity(int entityId);
        void SendWeld(int aId, int bId, WeldSettings settings);
        void SendRemoveConstraintsOf(int entityId);
        void SendAttachThruster(int entityId, Vector3 localPosition, Quaternion localRotation, float thrustForce, int binding, bool startOn);
        void SendCreateCable(int aId, Vector3 anchorA, int bId, Vector3 anchorB, ConstraintKind kind, float length);
        void SendCreateAxis(int aId, Vector3 anchorA, Vector3 axisA, int bId, AxisMotorSettings motor);
        void SendCreateHydraulic(int aId, Vector3 anchorA, int bId, Vector3 anchorB, HydraulicSettings settings);
        void SendBindings(int mask);
        void SendSetColor(int entityId, int colorIndex);
        void SendDetonate(int entityId);
        void SendCaptureRequest(int rootEntityId, float pivotYaw, int tag);
        void SendSpawnBlueprint(string json, Vector3 pivotPosition, float pivotYaw, bool liftToFit, bool replaceOwned);
        void SendSetEntityThrusters(int entityId, bool on);
        void SendToggleAllThrusters();
        void SendUndo();
        void SendRemoveAllOwned();
    }

    /// <summary>
    /// Network-side state of an entity (implemented by NetworkEntity). The authority calls OnAttachmentsChanged
    /// whenever thrusters are added / removed / switched, so remote peers can rebuild their visuals.
    /// </summary>
    public interface IEntityReplication
    {
        bool IsNetworkSpawned { get; }
        void OnAttachmentsChanged();
        void OnColorChanged();
    }
}
