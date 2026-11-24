using System;
using SimpleBox.Controls;
using UnityEngine;

namespace SimpleBox.Gameplay.Tools
{
    /// <summary>
    /// Owns the tool list, switches the active tool (Next Tool button, Tab / Shift+Tab, F1..F6)
    /// and forwards Use (button / desktop click) to it. Only the active tool receives Tick / OnPrimary / FixedTick.
    /// Adding a tool: create a BaseTool subclass, put it on a child of "Tools", add it to the Tools array.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolManager : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private MobileInputController input;
        [Tooltip("Order = cycle order. F1..F6 select indices 0..5.")]
        [SerializeField] private BaseTool[] tools = Array.Empty<BaseTool>();

        [Header("Startup")]
        [SerializeField, Min(0)] private int startToolIndex;

        private int _active = -1;
        private bool _valid;

        /// <summary>Arg: active tool index (or -1).</summary>
        public event Action<int> ActiveToolChanged;

        public bool IsValid => _valid;
        public int ActiveIndex => _active;
        public BaseTool ActiveTool => _active >= 0 ? tools[_active] : null;
        public int ToolCount => tools.Length;
        public BaseTool GetTool(int index) => tools[index];

        private void Awake()
        {
            if (input == null)
            {
                Fail("Input not assigned.");
                return;
            }
            if (!input.HasToolWidgets)
            {
                Fail("MobileInputController has no Use Tool / Next Tool / Alt Tool buttons assigned.");
                return;
            }
            if (tools == null || tools.Length == 0)
            {
                Fail("Tools array is empty.");
                return;
            }
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] == null)
                {
                    Fail($"Tools[{i}] is not assigned.");
                    return;
                }
            }
            _valid = true;
        }

        private void Start()
        {
            if (!_valid) return;

            // Tools validated themselves in their own Awake (which has run by now).
            int first = startToolIndex < tools.Length && tools[startToolIndex].IsReady
                ? startToolIndex
                : FindNextReady(-1, 1);

            if (first < 0)
            {
                Fail("No tool is ready (see tool errors above).");
                return;
            }
            SelectTool(first);
        }

        private void OnDisable()
        {
            if (_active >= 0) tools[_active].Unequip();
            _active = -1;
        }

        private void Update()
        {
            int cycle = input.ConsumeToolCycle();
            if (cycle != 0) CycleTool(cycle);

            int direct = input.ConsumeToolSelect();
            if (direct >= 0) SelectTool(direct);

            bool usePressed = input.ConsumeUseTool();
            bool altPressed = input.ConsumeToolSecondary();
            if (_active < 0) return;

            BaseTool tool = tools[_active];
            tool.Tick(Time.deltaTime, input.UseToolHeld);
            if (usePressed) tool.OnPrimary();
            if (altPressed && tool.IsEquipped) tool.OnSecondary();
        }

        private void FixedUpdate()
        {
            if (_active >= 0) tools[_active].FixedTick(Time.fixedDeltaTime);
        }

        public bool SelectTool(int index)
        {
            if (!_valid || index < 0 || index >= tools.Length || !tools[index].IsReady) return false;
            if (index == _active) return true;

            if (_active >= 0) tools[_active].Unequip();
            _active = index;
            tools[_active].Equip();
            ActiveToolChanged?.Invoke(_active);
            return true;
        }

        public void NextTool() => CycleTool(1);
        public void PreviousTool() => CycleTool(-1);

        private void CycleTool(int direction)
        {
            if (!_valid) return;
            int next = FindNextReady(_active, direction > 0 ? 1 : -1);
            if (next >= 0) SelectTool(next);
        }

        private int FindNextReady(int from, int direction)
        {
            int count = tools.Length;
            for (int step = 1; step <= count; step++)
            {
                int i = ((from + direction * step) % count + count) % count;
                if (tools[i].IsReady) return i;
            }
            return -1;
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[{nameof(ToolManager)}] '{name}': {reason} Component disabled.", this);
            _valid = false;
            enabled = false;
        }
    }
}
