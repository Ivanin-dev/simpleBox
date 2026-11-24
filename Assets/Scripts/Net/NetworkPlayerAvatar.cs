using SimpleBox.Core;
using SimpleBox.Gameplay;
using SimpleBox.World;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Authentication;
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

        [Header("References (optional)")]
        [Tooltip("Nickname above the body (hidden for the owner, faces the local camera).")]
        [SerializeField] private TextMesh nameTag;

        [Header("Rates")]
        [Tooltip("Aim / hold-adjust messages per second from the owner.")]
        [SerializeField, Min(1f)] private float sendRate = 15f;

        // Profile (server writes; everyone reads). Nickname is sanitized on the host.
        private readonly NetworkVariable<FixedString64Bytes> _nickname = new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<FixedString64Bytes> _authId = new NetworkVariable<FixedString64Bytes>();
        private string _nicknameCache = string.Empty;
        private string _authIdCache = string.Empty;
        private bool _hiddenByBlock;

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

            _nickname.OnValueChanged += OnNicknameChanged;
            _authId.OnValueChanged += OnAuthIdChanged;
            _nicknameCache = _nickname.Value.ToString();
            _authIdCache = _authId.Value.ToString();
            RefreshNameTag();
            _gameplay.RegisterPlayer(this);

            if (IsOwner)
            {
                visual.SetActive(false);
                bodyCollider.enabled = false;
                if (nameTag != null) nameTag.gameObject.SetActive(false);
                _gameplay.BindLocalAvatar(this);
                ProfileRpc(PlayerProfile.Nickname, LocalAuthId());
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
            _nickname.OnValueChanged -= OnNicknameChanged;
            _authId.OnValueChanged -= OnAuthIdChanged;
            if (_gameplay != null)
            {
                if (IsOwner) _gameplay.UnbindLocalAvatar(this);
                _gameplay.UnregisterPlayer(this);
            }
            if (proxyPhysgun != null && proxyPhysgun.enabled) proxyPhysgun.Drop();
        }

        private void LateUpdate()
        {
            if (!_valid || !IsSpawned || _gameplay == null) return;
            UpdateNameTag();

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

        // ================================================================== Profile (nickname, auth id)

        /// <summary>Nickname as shown to everyone ("Игрок N" until the owner's profile arrives).</summary>
        public string Nickname => _nicknameCache.Length > 0 ? _nicknameCache : Loc.T("Игрок ") + OwnerClientId;

        /// <summary>Unity Authentication player id (stable across sessions; used for block / report / kick).</summary>
        public string AuthId => _authIdCache;

        /// <summary>Owner id used by the sandbox (= NGO client id).</summary>
        public int PlayerOwnerId => (int)OwnerClientId;


        private static string LocalAuthId()
        {
            try
            {
                return AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn
                    ? AuthenticationService.Instance.PlayerId ?? string.Empty
                    : string.Empty;
            }
            catch (System.Exception)
            {
                return string.Empty; // services not initialized
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ProfileRpc(string nickname, string authId)
        {
            string clean = PlayerProfile.Sanitize(nickname);
            if (clean.Length == 0) clean = "Player " + OwnerClientId; // neutral: seen by everyone
            var name = new FixedString64Bytes();
            name.CopyFromTruncated(clean);
            var id = new FixedString64Bytes();
            id.CopyFromTruncated(authId ?? string.Empty);
            _nickname.Value = name;
            _authId.Value = id;

            // The host blocked this player earlier: not allowed back in.
            if (!IsOwner && _gameplay != null && BlockList.IsBlocked(authId)) _gameplay.Kick(this);
        }

        private void OnNicknameChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            _nicknameCache = current.ToString();
            RefreshNameTag();
            if (_gameplay != null) _gameplay.NotifyPlayersChanged();
        }

        private void OnAuthIdChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            _authIdCache = current.ToString();
            if (_gameplay != null) _gameplay.NotifyPlayersChanged(); // block state may apply now
        }

        private void RefreshNameTag()
        {
            if (nameTag != null) nameTag.text = Nickname;
        }

        /// <summary>Local view only: hides a blocked player's body and name.</summary>
        public void SetHiddenByBlock(bool hidden)
        {
            if (_hiddenByBlock == hidden || IsOwner) return;
            _hiddenByBlock = hidden;
            if (visual != null) visual.SetActive(!hidden);
            if (nameTag != null) nameTag.gameObject.SetActive(!hidden);
        }

        private void UpdateNameTag()
        {
            if (nameTag == null || IsOwner || _hiddenByBlock) return;
            Transform cam = _gameplay.LocalAim;
            Transform tag = nameTag.transform;
            Vector3 away = tag.position - cam.position;
            if (away.sqrMagnitude > 1e-4f) tag.rotation = Quaternion.LookRotation(away, Vector3.up);
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
