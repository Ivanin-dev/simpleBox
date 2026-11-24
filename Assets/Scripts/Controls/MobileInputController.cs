using UnityEngine;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace SimpleBox.Controls
{
    /// <summary>
    /// Single input facade for gameplay. Gameplay code never touches UI widgets directly.
    /// Touch isolation (fingerId) is handled per widget: VirtualJoystick, TouchLookZone, TouchButton.
    /// Every Consume* method must be called at most once per frame by exactly one consumer.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class MobileInputController : MonoBehaviour
    {
        [Header("Core widgets (required)")]
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private TouchLookZone lookZone;
        [SerializeField] private TouchButton jumpButton;

        [Header("Prop widgets (required by PropSpawner)")]
        [SerializeField] private TouchButton spawnButton;
        [SerializeField] private TouchButton nextPropButton;

        [Header("Tool widgets (required by ToolManager)")]
        [SerializeField] private TouchButton useToolButton;
        [SerializeField] private TouchButton nextToolButton;
        [Tooltip("Secondary tool action (freeze / reset / toggle, tool-specific).")]
        [SerializeField] private TouchButton altToolButton;
        [Tooltip("Mobile: a tap on the look zone also triggers Use Tool. Off by default to avoid accidental removals. " +
                 "In the editor/desktop a mouse click on the look zone always triggers Use Tool.")]
        [SerializeField] private bool touchTapUsesTool;

        [Header("Thruster widgets (required by ThrusterRegistry)")]
        [Tooltip("Global on/off for all thrusters.")]
        [SerializeField] private TouchButton thrustToggleButton;

        [Header("Session commands (required by PlayerCommands)")]
        [SerializeField] private TouchButton undoButton;
        [Tooltip("Remove all of the player's entities (PlayerCommands asks for a second press to confirm).")]
        [SerializeField] private TouchButton clearButton;

        [Header("Held-prop widgets (required by PhysicsGunTool; shown only while holding)")]
        [Tooltip("While held: swipes on the look zone rotate the held prop instead of the camera.")]
        [SerializeField] private TouchButton rotateButton;
        [Tooltip("Vertical drag strip: up = push the held prop away, down = pull it closer.")]
        [SerializeField] private TouchLookZone distanceZone;

        [Header("Flight widgets (required for noclip by PlayerController)")]
        [SerializeField] private TouchButton flyButton;
        [Tooltip("Descend while flying (ascend = JUMP held).")]
        [SerializeField] private TouchButton downButton;

        [Header("Seat (required by SeatController; shown when aiming at a seat or seated)")]
        [SerializeField] private TouchButton sitButton;

        [Header("Contraption bindings 1-4 (required by PlayerCommands)")]
        [SerializeField] private TouchButton[] bindingButtons = new TouchButton[0];

        [Header("Look")]
        [Tooltip("Degrees of rotation for a swipe across the full screen height.")]
        [SerializeField, Min(1f)] private float lookSensitivity = 180f;
        [SerializeField] private bool invertY;

        [Header("Held prop")]
        [Tooltip("Degrees the held prop turns for a swipe across the full screen height.")]
        [SerializeField, Min(1f)] private float heldRotationSensitivity = 270f;
        [Tooltip("Meters the hold distance changes for a drag across the full screen height on the distance strip.")]
        [SerializeField, Min(0.1f)] private float distanceSensitivity = 8f;
        [Tooltip("Meters per mouse wheel notch (desktop).")]
        [SerializeField, Min(0.05f)] private float wheelDistanceStep = 0.5f;

        [Header("Editor-only keyboard / mouse fallback (mobile-only game: not compiled into builds)")]
        [Tooltip("WASD move, Space jump/fly up, LeftCtrl fly down, V noclip, R/G spawn, Q spawn menu, wheel cycle " +
                 "(hold distance while holding), RMB held = rotate held prop, 1-9 select prop, Tab / Shift+Tab cycle tool, " +
                 "F1-F10 select tool, E = sit / exit, Esc = settings, arrows = bindings 1-4, click on look zone = Use Tool, X = tool Alt, T = thrust on/off, Z = undo, Backspace = clear.")]
        [SerializeField] private bool keyboardFallback = true;

        private bool _valid;
        private bool _propWidgetsValid;
        private bool _toolWidgetsValid;
        private bool _thrustWidgetsValid;
        private bool _commandWidgetsValid;
        private bool _holdWidgetsValid;
        private bool _flyWidgetsValid;
        private bool _bindingWidgetsValid;
        private bool _seatWidgetsValid;

        public bool HasCommandWidgets => _commandWidgetsValid;
        public bool HasHoldWidgets => _holdWidgetsValid;
        public bool HasFlyWidgets => _flyWidgetsValid;
        public bool HasBindingWidgets => _bindingWidgetsValid;
        public bool HasSeatWidgets => _seatWidgetsValid;

        /// <summary>
        /// Set by PhysicsGunTool while a prop is held. Routes the mouse wheel to hold distance and enables
        /// look capture by the rotate modifier.
        /// </summary>
        public bool HoldMode { get; set; }

        /// <summary>Set by UI while a text field is focused: keyboard shortcuts are ignored (typing a session code).</summary>
        public bool KeyboardBlocked { get; set; }

        /// <summary>Set by a full-screen menu (settings) while open: game shortcuts are ignored, its own toggle key still works.</summary>
        public bool MenuOpen { get; set; }

        /// <summary>Set by the main menu while it is shown: game shortcuts are ignored (independent of MenuOpen).</summary>
        public bool MainMenuOpen { get; set; }

        private bool KeyboardActive => keyboardFallback && !KeyboardBlocked && !MenuOpen && !MainMenuOpen;

        private float _lookScale = 1f;

        /// <summary>User setting: multiplier on the Inspector look sensitivity (0.1..5).</summary>
        public float LookSensitivityScale
        {
            get => _lookScale;
            set => _lookScale = Mathf.Clamp(value, 0.1f, 5f);
        }

        /// <summary>User setting: inverted vertical look.</summary>
        public bool InvertY
        {
            get => invertY;
            set => invertY = value;
        }

        public bool IsValid => _valid;
        public bool HasPropWidgets => _propWidgetsValid;
        public bool HasToolWidgets => _toolWidgetsValid;
        public bool HasThrustWidgets => _thrustWidgetsValid;

        private void Awake()
        {
            _valid = joystick != null && lookZone != null &&
                     jumpButton != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Core widget references (Joystick/LookZone/Jump) " +
                               $"are not all assigned on '{name}'. Touch input disabled.", this);
            }

            _propWidgetsValid = spawnButton != null && nextPropButton != null;
            if (!_propWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Spawn Button / Next Prop Button not assigned on '{name}'.", this);
            }

            _toolWidgetsValid = useToolButton != null && nextToolButton != null && altToolButton != null;
            if (!_toolWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Use Tool / Next Tool / Alt Tool Button not assigned on '{name}'.", this);
            }

            _thrustWidgetsValid = thrustToggleButton != null;
            if (!_thrustWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Thrust Toggle Button not assigned on '{name}'.", this);
            }

            _commandWidgetsValid = undoButton != null && clearButton != null;
            if (!_commandWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Undo Button / Clear Button not assigned on '{name}'.", this);
            }

            _holdWidgetsValid = rotateButton != null && distanceZone != null;
            if (!_holdWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Rotate Button / Distance Zone not assigned on '{name}'.", this);
            }

            _flyWidgetsValid = flyButton != null && downButton != null;
            if (!_flyWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Fly Button / Down Button not assigned on '{name}'.", this);
            }

            _seatWidgetsValid = sitButton != null;
            if (!_seatWidgetsValid)
                Debug.LogError($"[{nameof(MobileInputController)}] Sit Button not assigned on '{name}'.", this);

            _bindingWidgetsValid = bindingButtons != null && bindingButtons.Length == 4;
            for (int i = 0; _bindingWidgetsValid && i < bindingButtons.Length; i++)
                if (bindingButtons[i] == null) _bindingWidgetsValid = false;
            if (!_bindingWidgetsValid)
            {
                Debug.LogError($"[{nameof(MobileInputController)}] Binding Buttons must contain 4 assigned buttons on '{name}'.", this);
            }
        }

        // ------------------------------------------------------------------ Movement / look

        /// <summary>Movement vector, x = strafe, y = forward, magnitude 0..1.</summary>
        public Vector2 Move
        {
            get
            {
                Vector2 move = _valid ? joystick.Value : Vector2.zero;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
                if (KeyboardActive && move.sqrMagnitude < 0.0001f)
                {
                    Keyboard kb = Keyboard.current;
                    if (kb != null)
                    {
                        move.x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                        move.y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                        if (move.sqrMagnitude > 1f) move.Normalize();
                    }
                }
#endif
                return move;
            }
        }

        /// <summary>
        /// Look delta in degrees since last call: x = yaw (+right), y = pitch (+down, matches Euler X).
        /// </summary>
        public Vector2 ConsumeLookDegrees()
        {
            if (!_valid || LookCaptured) return Vector2.zero; // the held-prop rotation reads the delta instead

            Vector2 px = lookZone.ConsumeDelta();
            float k = lookSensitivity * _lookScale / Mathf.Max(1, Screen.height);
            px.x *= k;
            px.y *= invertY ? k : -k;
            return px;
        }

        // ------------------------------------------------------------------ Held prop

        /// <summary>ROTATE button held, or RMB held (desktop).</summary>
        public bool RotateModifierHeld
        {
            get
            {
                bool held = _holdWidgetsValid && rotateButton.IsHeld;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
                if (KeyboardActive && Mouse.current != null && Mouse.current.rightButton.isPressed) held = true;
#endif
                return held;
            }
        }

        /// <summary>True while look swipes go to the held prop instead of the camera.</summary>
        public bool LookCaptured => HoldMode && RotateModifierHeld;

        /// <summary>Held-prop rotation in degrees since last call: x = around world up (+right), y = around camera right (+down).</summary>
        public Vector2 ConsumeHeldRotationDegrees()
        {
            if (!_valid || !LookCaptured) return Vector2.zero;

            Vector2 px = lookZone.ConsumeDelta();
            float k = heldRotationSensitivity / Mathf.Max(1, Screen.height);
            px.x *= k;
            px.y *= -k;
            return px;
        }

        /// <summary>Hold distance change in meters since last call (distance strip + mouse wheel). Zero outside HoldMode.</summary>
        public float ConsumeHoldDistanceDelta()
        {
            float meters = 0f;
            if (_holdWidgetsValid)
            {
                float px = distanceZone.ConsumeDelta().y; // always consume so nothing is applied late
                meters = px * (distanceSensitivity / Mathf.Max(1, Screen.height));
            }
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (KeyboardActive && HoldMode && Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (wheel > 0.01f) meters += wheelDistanceStep;
                else if (wheel < -0.01f) meters -= wheelDistanceStep;
            }
#endif
            return HoldMode ? meters : 0f;
        }

        // ------------------------------------------------------------------ Flight

        /// <summary>FLY button or V.</summary>
        public bool ConsumeFlyToggle() => (_flyWidgetsValid && flyButton.ConsumePress()) | KeyDown(KeyId.Fly);

        /// <summary>JUMP held or Space held.</summary>
        public bool FlyUpHeld
        {
            get
            {
                bool held = _valid && jumpButton.IsHeld;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
                if (KeyboardActive && Keyboard.current != null && Keyboard.current.spaceKey.isPressed) held = true;
#endif
                return held;
            }
        }

        /// <summary>DOWN held or LeftCtrl held.</summary>
        public bool FlyDownHeld
        {
            get
            {
                bool held = _flyWidgetsValid && downButton.IsHeld;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
                if (KeyboardActive && Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed) held = true;
#endif
                return held;
            }
        }

        // ------------------------------------------------------------------ Buttons

        public bool ConsumeJump() => (_valid && jumpButton.ConsumePress()) | KeyDown(KeyId.Jump);
        public bool JumpHeld => _valid && jumpButton.IsHeld;

        public bool ConsumeSpawn() => (_propWidgetsValid && spawnButton.ConsumePress()) | KeyDown(KeyId.Spawn);

        /// <summary>+1 next, -1 previous, 0 none. Sources: Next Prop button, Q, mouse wheel.</summary>
        public int ConsumePropCycle()
        {
            int dir = (_propWidgetsValid && nextPropButton.ConsumePress()) ? 1 : 0;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (KeyboardActive)
            {
                Keyboard kb = Keyboard.current;
                // Q opens the spawn menu (ConsumeSpawnMenu); the wheel cycles props.

                Mouse mouse = Mouse.current;
                if (mouse != null && !HoldMode) // while holding, the wheel controls hold distance
                {
                    float wheel = mouse.scroll.ReadValue().y;
                    if (wheel < -0.01f) dir = 1;
                    else if (wheel > 0.01f) dir = -1;
                }
            }
#endif
            return dir;
        }

        /// <summary>Direct selection index from keys 1..9 (0..8), or -1.</summary>
        public int ConsumePropSelect()
        {
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (!KeyboardActive) return -1;
            Keyboard kb = Keyboard.current;
            if (kb == null) return -1;
            for (int i = 0; i < 9; i++)
            {
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) return i;
            }
#endif
            return -1;
        }

        // ------------------------------------------------------------------ Tools

        /// <summary>Use Tool pressed this frame: Use button, or a tap/click on the look zone (see touchTapUsesTool).</summary>
        public bool ConsumeUseTool()
        {
            bool pressed = _toolWidgetsValid && useToolButton.ConsumePress();

            // Always consume the tap so it never fires late.
            bool tap = _valid && lookZone.ConsumeTap();
            bool tapAllowed = touchTapUsesTool;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            tapAllowed |= keyboardFallback;
#endif
            return pressed | (tap && tapAllowed);
        }

        public bool UseToolHeld => _toolWidgetsValid && useToolButton.IsHeld;

        /// <summary>Secondary tool action: ALT button or X.</summary>
        public bool ConsumeToolSecondary() => (_toolWidgetsValid && altToolButton.ConsumePress()) | KeyDown(KeyId.ToolAlt);

        /// <summary>Global thruster on/off: THRUST button or T.</summary>
        public bool ConsumeThrustToggle() => (_thrustWidgetsValid && thrustToggleButton.ConsumePress()) | KeyDown(KeyId.Thrust);

        /// <summary>+1 next, -1 previous, 0 none. Sources: Next Tool button, Tab, Shift+Tab.</summary>
        public int ConsumeToolCycle()
        {
            int dir = (_toolWidgetsValid && nextToolButton.ConsumePress()) ? 1 : 0;
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (KeyboardActive)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.tabKey.wasPressedThisFrame) dir = kb.shiftKey.isPressed ? -1 : 1;
            }
#endif
            return dir;
        }

        /// <summary>Direct tool index from F1..F6 (0..5), or -1. Digits stay reserved for prop selection.</summary>
        public int ConsumeToolSelect()
        {
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (!KeyboardActive) return -1;
            Keyboard kb = Keyboard.current;
            if (kb == null) return -1;
            if (kb.f1Key.wasPressedThisFrame) return 0;
            if (kb.f2Key.wasPressedThisFrame) return 1;
            if (kb.f3Key.wasPressedThisFrame) return 2;
            if (kb.f4Key.wasPressedThisFrame) return 3;
            if (kb.f5Key.wasPressedThisFrame) return 4;
            if (kb.f6Key.wasPressedThisFrame) return 5;
            if (kb.f7Key.wasPressedThisFrame) return 6;
            if (kb.f8Key.wasPressedThisFrame) return 7;
            if (kb.f9Key.wasPressedThisFrame) return 8;
            if (kb.f10Key.wasPressedThisFrame) return 9;
#endif
            return -1;
        }

        // ------------------------------------------------------------------ Contraption bindings

        /// <summary>Held bindings as a bit mask (bit 0 = binding 1). Buttons 1-4 or arrow keys Up/Down/Left/Right.</summary>
        public int BindingMask
        {
            get
            {
                int mask = 0;
                if (_bindingWidgetsValid)
                {
                    for (int i = 0; i < bindingButtons.Length; i++)
                        if (bindingButtons[i].IsHeld) mask |= 1 << i;
                }
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
                Keyboard kb = Keyboard.current;
                if (KeyboardActive && kb != null)
                {
                    if (kb.upArrowKey.isPressed) mask |= 1;
                    if (kb.downArrowKey.isPressed) mask |= 2;
                    if (kb.leftArrowKey.isPressed) mask |= 4;
                    if (kb.rightArrowKey.isPressed) mask |= 8;
                }
#endif
                return mask;
            }
        }

        // ------------------------------------------------------------------ Session commands

        public bool ConsumeUndo() => (_commandWidgetsValid && undoButton.ConsumePress()) | KeyDown(KeyId.Undo);
        public bool ConsumeClear() => (_commandWidgetsValid && clearButton.ConsumePress()) | KeyDown(KeyId.Clear);

        /// <summary>SIT / EXIT button or E.</summary>
        public bool ConsumeSit() => (_seatWidgetsValid && sitButton.ConsumePress()) | KeyDown(KeyId.Sit);

        /// <summary>Spawn menu toggle key (Q). The on-screen opener is the prop panel itself (SpawnMenuPanel).</summary>
        public bool ConsumeSpawnMenu() => KeyDown(KeyId.SpawnMenu);

        /// <summary>Settings menu toggle key (Esc). The on-screen opener is the settings button (SettingsPanel).</summary>
        public bool ConsumeSettingsMenu()
        {
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (!keyboardFallback || KeyboardBlocked) return false; // not while typing; works while MenuOpen
            Keyboard kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        private enum KeyId { Jump, Spawn, ToolAlt, Thrust, Undo, Clear, Fly, Sit, SpawnMenu }

        private bool KeyDown(KeyId id)
        {
#if ENABLE_INPUT_SYSTEM && UNITY_EDITOR
            if (!KeyboardActive) return false;
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;
            switch (id)
            {
                case KeyId.Jump: return kb.spaceKey.wasPressedThisFrame;
                case KeyId.Spawn: return kb.rKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame;
                case KeyId.ToolAlt: return kb.xKey.wasPressedThisFrame;
                case KeyId.Thrust: return kb.tKey.wasPressedThisFrame;
                case KeyId.Undo: return kb.zKey.wasPressedThisFrame;
                case KeyId.Clear: return kb.backspaceKey.wasPressedThisFrame;
                case KeyId.Fly: return kb.vKey.wasPressedThisFrame;
                case KeyId.Sit: return kb.eKey.wasPressedThisFrame;
                case KeyId.SpawnMenu: return kb.qKey.wasPressedThisFrame;
            }
#endif
            return false;
        }
    }
}
