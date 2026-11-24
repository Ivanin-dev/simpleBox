using System;
using SimpleBox.Controls;
using SimpleBox.Gameplay;
using UnityEngine;

namespace SimpleBox.World
{
    /// <summary>
    /// Session-level player commands that work with any tool: UNDO (Z), CLEAR (Backspace, press twice to confirm),
    /// THRUST global toggle (T). Turns input into SandboxActions requests and reports a short message for the HUD.
    /// Messages are constant strings (no allocation).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCommands : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private MobileInputController input;
        [SerializeField] private SandboxActions actions;

        [Header("References (optional)")]
        [Tooltip("While seated the joystick is merged into the bindings (forward 1, back 2, left 3, right 4).")]
        [SerializeField] private SeatController seat;

        [Header("Clear confirmation")]
        [Tooltip("Second CLEAR press must come within this time.")]
        [SerializeField, Min(0.2f)] private float clearConfirmWindow = 2f;

        private const string MsgUndoEntity = "Отменено: проп";
        private const string MsgUndoAttachment = "Отменено: двигатель";
        private const string MsgUndoConstraint = "Отменено: сварка";
        private const string MsgNothingToUndo = "Нечего отменять";
        private const string MsgClearArm = "Нажмите CLEAR ещё раз — удалить все ваши пропы";
        private const string MsgCleared = "Все ваши пропы удалены";
        private const string MsgThrustOn = "Тяга: ВКЛ";
        private const string MsgThrustOff = "Тяга: ВЫКЛ";
        private const string MsgUndoSent = "Отмена…";
        private const string MsgThrustSent = "Тяга: переключено";
        private const string MsgRemoteNotAllowed = "Сервер: это чужой объект";
        private const string MsgRemoteLimit = "Сервер: лимит";
        private const string MsgRemoteNoSpace = "Сервер: нет места";
        private const string MsgRemoteNotFound = "Сервер: объект уже удалён";
        private const string MsgRemoteAlready = "Сервер: уже сварены";
        private const string MsgRemoteRejected = "Сервер отклонил действие";

        private float _clearArmedUntil = -1f;
        private bool _valid;
        private bool _subscribed;
        private int _lastBindingMask;

        /// <summary>Arg: message for the toast HUD.</summary>
        public event Action<string> Message;

        private void Awake()
        {
            if (input == null || actions == null)
            {
                Fail("Input / Actions not assigned.");
                return;
            }
            if (!input.HasCommandWidgets || !input.HasThrustWidgets || !input.HasBindingWidgets)
            {
                Fail("MobileInputController has no Undo / Clear / Thrust / Binding buttons assigned.");
                return;
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            actions.RemoteResult += OnRemoteResult;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (actions != null) actions.RemoteResult -= OnRemoteResult;
            _subscribed = false;
        }

        /// <summary>Multiplayer client: the host refused one of our requests.</summary>
        private void OnRemoteResult(ActionResult result)
        {
            switch (result)
            {
                case ActionResult.NotAllowed: Message?.Invoke(MsgRemoteNotAllowed); break;
                case ActionResult.OwnerLimit:
                case ActionResult.EntityLimit:
                case ActionResult.ServerLimit: Message?.Invoke(MsgRemoteLimit); break;
                case ActionResult.NoSpace: Message?.Invoke(MsgRemoteNoSpace); break;
                case ActionResult.NotFound: Message?.Invoke(MsgRemoteNotFound); break;
                case ActionResult.AlreadyExists: Message?.Invoke(MsgRemoteAlready); break;
                case ActionResult.NothingToUndo: Message?.Invoke(MsgNothingToUndo); break;
                default: Message?.Invoke(MsgRemoteRejected); break;
            }
        }

        private void Update()
        {
            if (!_valid) return;
            if (input.ConsumeUndo()) Undo();
            if (input.ConsumeClear()) Clear();
            if (input.ConsumeThrustToggle()) ToggleThrust();

            // Contraption bindings: send only when the held set changes.
            int mask = input.BindingMask | (seat != null ? seat.DriveMask : 0);
            if (mask != _lastBindingMask)
            {
                _lastBindingMask = mask;
                actions.SetBindings(mask);
            }
        }

        public void Undo()
        {
            ActionResult result = actions.Undo(out EntityRegistry.RemovedKind kind);
            if (result == ActionResult.Pending)
            {
                Message?.Invoke(MsgUndoSent);
                return;
            }
            if (result != ActionResult.Ok)
            {
                Message?.Invoke(MsgNothingToUndo);
                return;
            }
            switch (kind)
            {
                case EntityRegistry.RemovedKind.Entity: Message?.Invoke(MsgUndoEntity); break;
                case EntityRegistry.RemovedKind.Attachment: Message?.Invoke(MsgUndoAttachment); break;
                case EntityRegistry.RemovedKind.Constraint: Message?.Invoke(MsgUndoConstraint); break;
            }
        }

        public void Clear()
        {
            float now = Time.unscaledTime;
            if (now > _clearArmedUntil)
            {
                _clearArmedUntil = now + clearConfirmWindow;
                Message?.Invoke(MsgClearArm);
                return;
            }

            _clearArmedUntil = -1f;
            actions.RemoveAllOwned(out _);
            Message?.Invoke(MsgCleared);
        }

        public void ToggleThrust()
        {
            ActionResult result = actions.ToggleAllThrusters(out bool nowOn);
            if (result == ActionResult.Ok) Message?.Invoke(nowOn ? MsgThrustOn : MsgThrustOff);
            else if (result == ActionResult.Pending) Message?.Invoke(MsgThrustSent);
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[{nameof(PlayerCommands)}] '{name}': {reason} Component disabled.", this);
            _valid = false;
            enabled = false;
        }
    }
}
