using SimpleBox.Gameplay;
using SimpleBox.World;
using Unity.Netcode;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>
    /// One per connected player (NetworkManager Player Prefab).
    /// Owner: follows the local player rig (owner-authoritative NetworkTransform), hides its own body, and is the
    /// client's gateway to the host: IActionTransport (sandbox requests) and IPhysgunRemote (physgun commands).
    /// Host: executes the owner's requests with the sender's owner id and drives a proxy PhysicsGun whose aim comes
    /// from the owner (unreliable aim stream). Other peers: see a capsule body with a collider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerAvatar : NetworkBehaviour, IActionTransport, IPhysgunRemote
    {
        [Header("References (required)")]
        [Tooltip("Body visual, hidden for the owner.")]
        [SerializeField] private GameObject visual;
        [Tooltip("Body collider, disabled for the owner (the local CharacterController is the owner's body).")]
        [SerializeField] private Collider bodyCollider;
        [Tooltip("Host: aim of this player's proxy physgun (updated from the owner's camera).")]
        [SerializeField] private Transform aimProxy;
        [Tooltip("Host: physgun used on behalf of this player (Configured At Runtime = on).")]
        [SerializeField] private PhysicsGun proxyPhysgun;

        [Header("Rates")]
        [Tooltip("Aim / hold-adjust messages per second from the owner.")]
        [SerializeField, Min(1f)] private float sendRate = 15f;

        private NetworkGameplay _gameplay;
        private float _nextSendTime;
        private bool _remoteHolding;     // owner side: holding state reported by the host
        private bool _lastSentHolding;   // host side
        private Vector2 _pendingRotation;
        private float _pendingDistance;
        private bool _valid;

        private void Awake()
        {
            _valid = visual != null && bodyCollider != null && aimProxy != null && proxyPhysgun != null;
            if (!_valid)
                Debug.LogError($"[{nameof(NetworkPlayerAvatar)}] '{name}': Visual / Body Collider / Aim Proxy / Proxy Physgun not assigned.", this);
        }

        public override void OnNetworkSpawn()
        {
            _gameplay = NetworkManager.GetComponent<NetworkGameplay>();
            if (!_valid || _gameplay == null)
            {
                if (_gameplay == null) Debug.LogError($"[{nameof(NetworkPlayerAvatar)}] NetworkGameplay not found on the NetworkManager object.", this);
                return;
            }

            if (IsOwner)
            {
                visual.SetActive(false);
                bodyCollider.enabled = false;
                _gameplay.BindLocalAvatar(this);
            }

            if (IsServer && !IsOwner)
            {
                PhysicsGun local = _gameplay.LocalPhysgun;
                proxyPhysgun.ConfigureRuntime(_gameplay.Registry, _gameplay.Actions, aimProxy, (int)OwnerClientId,
                    local.RaycastMask, local.GrabbableMask);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_gameplay != null && IsOwner) _gameplay.UnbindLocalAvatar(this);
            if (proxyPhysgun != null && proxyPhysgun.enabled) proxyPhysgun.Drop();
        }

        private void LateUpdate()
        {
            if (!_valid || !IsSpawned || _gameplay == null) return;

            if (IsOwner)
            {
                Transform rig = _gameplay.LocalPlayer;
                transform.SetPositionAndRotation(rig.position, rig.rotation);

                if (!IsServer && Time.unscaledTime >= _nextSendTime)
                {
                    _nextSendTime = Time.unscaledTime + 1f / sendRate;
                    Transform aim = _gameplay.LocalAim;
                    AimRpc(aim.position, aim.rotation);
                    if (_pendingRotation != Vector2.zero || _pendingDistance != 0f)
                    {
                        HoldAdjustRpc(_pendingRotation, _pendingDistance);
                        _pendingRotation = Vector2.zero;
                        _pendingDistance = 0f;
                    }
                }
            }
            else if (IsServer && proxyPhysgun.enabled)
            {
                bool holding = proxyPhysgun.IsHolding;
                if (holding != _lastSentHolding)
                {
                    _lastSentHolding = holding;
                    HoldStateRpc(holding);
                }
            }
        }

        // ================================================================== IActionTransport (owner -> host)

        public void SendSpawnProp(int typeIndex, Vector3 position, Quaternion rotation) => SpawnPropRpc(typeIndex, position, rotation);
        public void SendRemoveEntity(int entityId) => RemoveEntityRpc(entityId);
        public void SendWeld(int aId, int bId, WeldSettings s) => WeldRpc(aId, bId, s.BreakForce, s.BreakTorque, s.EnablePreprocessing);
        public void SendRemoveConstraintsOf(int entityId) => RemoveConstraintsRpc(entityId);
        public void SendAttachThruster(int entityId, Vector3 lp, Quaternion lr, float force, int binding, bool startOn) => AttachThrusterRpc(entityId, lp, lr, force, binding, startOn);
        public void SendCreateCable(int aId, Vector3 anchorA, int bId, Vector3 anchorB, ConstraintKind kind, float length) => CreateCableRpc(aId, anchorA, bId, anchorB, kind, length);
        public void SendCreateAxis(int aId, Vector3 anchorA, Vector3 axisA, int bId, AxisMotorSettings m) => CreateAxisRpc(aId, anchorA, axisA, bId, m.ForwardBinding, m.BackwardBinding, m.Speed, m.Force);
        public void SendBindings(int mask) => BindingsRpc(mask);
        public void SendSetColor(int entityId, int colorIndex) => SetColorRpc(entityId, colorIndex);
        public void SendDetonate(int entityId) => DetonateRpc(entityId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DetonateRpc(int entityId, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteDetonate(Sender(rpcParams), entityId));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetColorRpc(int entityId, int colorIndex, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteSetColor(Sender(rpcParams), entityId, colorIndex));
        public void SendCaptureRequest(int rootEntityId, float pivotYaw, int tag) => CaptureRpc(rootEntityId, pivotYaw, tag);
        public void SendSpawnBlueprint(string json, Vector3 pivotPosition, float pivotYaw, bool liftToFit, bool replaceOwned)
            => SpawnBlueprintRpc(json, pivotPosition, pivotYaw, liftToFit, replaceOwned);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CaptureRpc(int rootEntityId, float pivotYaw, int tag, RpcParams rpcParams = default)
            => CaptureResultRpc(tag, Actions.ExecuteCapture(Sender(rpcParams), rootEntityId, pivotYaw));

        [Rpc(SendTo.Owner)]
        private void CaptureResultRpc(int tag, string json)
        {
            if (_gameplay != null) _gameplay.Actions.ReportCapture(tag, json);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SpawnBlueprintRpc(string json, Vector3 pivotPosition, float pivotYaw, bool liftToFit, bool replaceOwned,
            RpcParams rpcParams = default)
            => Reply(Actions.ExecuteSpawnBlueprintJson(Sender(rpcParams), json, pivotPosition, pivotYaw, liftToFit, replaceOwned, out _));
        public void SendCreateHydraulic(int aId, Vector3 anchorA, int bId, Vector3 anchorB, HydraulicSettings h)
            => CreateHydraulicRpc(aId, anchorA, bId, anchorB, h.ExtendBinding, h.RetractBinding, h.MinLength, h.MaxLength, h.Speed);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CreateHydraulicRpc(int aId, Vector3 anchorA, int bId, Vector3 anchorB, int extend, int retract,
            float minLength, float maxLength, float speed, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteCreateHydraulic(Sender(rpcParams), aId, anchorA, bId, anchorB,
                new HydraulicSettings(extend, retract, minLength, maxLength, speed)));
        public void SendSetEntityThrusters(int entityId, bool on) => SetEntityThrustersRpc(entityId, on);
        public void SendToggleAllThrusters() => ToggleAllThrustersRpc();
        public void SendUndo() => UndoRpc();
        public void SendRemoveAllOwned() => RemoveAllOwnedRpc();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SpawnPropRpc(int typeIndex, Vector3 position, Quaternion rotation, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteSpawnProp(Sender(rpcParams), typeIndex, position, rotation));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RemoveEntityRpc(int entityId, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteRemoveEntity(Sender(rpcParams), entityId));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WeldRpc(int aId, int bId, float breakForce, float breakTorque, bool preprocessing, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteWeld(Sender(rpcParams), aId, bId, new WeldSettings(breakForce, breakTorque, preprocessing)));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RemoveConstraintsRpc(int entityId, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteRemoveConstraintsOf(Sender(rpcParams), entityId));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AttachThrusterRpc(int entityId, Vector3 localPosition, Quaternion localRotation, float force, int binding, bool startOn, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteAttachThruster(Sender(rpcParams), entityId, localPosition, localRotation, force, binding, startOn));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CreateCableRpc(int aId, Vector3 anchorA, int bId, Vector3 anchorB, ConstraintKind kind, float length, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteCreateCable(Sender(rpcParams), aId, anchorA, bId, anchorB, kind, length));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CreateAxisRpc(int aId, Vector3 anchorA, Vector3 axisA, int bId, int forward, int backward, float speed, float force, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteCreateAxis(Sender(rpcParams), aId, anchorA, axisA, bId, new AxisMotorSettings(forward, backward, speed, force)));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BindingsRpc(int mask, RpcParams rpcParams = default)
            => Actions.ExecuteSetBindings(Sender(rpcParams), mask);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetEntityThrustersRpc(int entityId, bool on, RpcParams rpcParams = default)
            => Reply(Actions.ExecuteSetEntityThrusters(Sender(rpcParams), entityId, on));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ToggleAllThrustersRpc(RpcParams rpcParams = default)
            => Reply(Actions.ExecuteToggleAllThrusters(Sender(rpcParams), out _));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UndoRpc(RpcParams rpcParams = default)
            => Reply(Actions.ExecuteUndo(Sender(rpcParams), out _));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RemoveAllOwnedRpc(RpcParams rpcParams = default)
            => Reply(Actions.ExecuteRemoveAllOwned(Sender(rpcParams), out _));

        [Rpc(SendTo.Owner)]
        private void ResultRpc(ActionResult result)
        {
            if (_gameplay != null) _gameplay.Actions.ReportRemoteResult(result);
        }

        private SandboxActions Actions => _gameplay.Actions;

        private static int Sender(RpcParams rpcParams) => (int)rpcParams.Receive.SenderClientId;

        private void Reply(ActionResult result)
        {
            if (result != ActionResult.Ok) ResultRpc(result);
        }

        // ================================================================== IPhysgunRemote (owner -> host)

        public bool IsHolding => _remoteHolding;
        public void ToggleGrab() => PhysgunGrabRpc();
        public void ToggleFreeze() => PhysgunFreezeRpc();
        public void RotateHeld(Vector2 degrees) => _pendingRotation += degrees;
        public void AdjustHoldDistance(float meters) => _pendingDistance += meters;
        public void SetRotationSnap(float degrees) => PhysgunSnapRpc(degrees);
        public void Drop() => PhysgunDropRpc();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void AimRpc(Vector3 position, Quaternion rotation) => aimProxy.SetPositionAndRotation(position, rotation);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HoldAdjustRpc(Vector2 rotation, float distance)
        {
            if (!proxyPhysgun.enabled) return;
            proxyPhysgun.RotateHeld(rotation);
            if (distance != 0f) proxyPhysgun.AdjustHoldDistance(distance);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PhysgunGrabRpc()
        {
            if (proxyPhysgun.enabled) proxyPhysgun.ToggleGrab();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PhysgunFreezeRpc()
        {
            if (proxyPhysgun.enabled) proxyPhysgun.ToggleFreeze();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PhysgunSnapRpc(float degrees)
        {
            if (proxyPhysgun.enabled) proxyPhysgun.RotationSnap = degrees;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PhysgunDropRpc()
        {
            if (proxyPhysgun.enabled) proxyPhysgun.Drop();
        }

        [Rpc(SendTo.Owner)]
        private void HoldStateRpc(bool holding) => _remoteHolding = holding;
    }
}
