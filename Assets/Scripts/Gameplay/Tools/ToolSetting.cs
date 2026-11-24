namespace SimpleBox.Gameplay.Tools
{
    public enum ToolSettingKind
    {
        /// <summary>Numeric value from Min to Max in fixed Steps (changed with − / +).</summary>
        Stepper = 0,
        /// <summary>0 = off, 1 = on.</summary>
        Toggle = 1,
        /// <summary>Index into Choices.</summary>
        Choice = 2
    }

    /// <summary>
    /// Description of one tool setting. The settings panel builds its row from this; values are floats
    /// (stepper value, toggle 0/1, choice index), so they can travel in action requests and save files as plain data.
    /// Strings and the Choices array must be constants / static readonly (no per-frame allocation).
    /// </summary>
    public readonly struct ToolSettingInfo
    {
        public readonly ToolSettingKind Kind;
        public readonly string Label;
        public readonly float Min;
        public readonly float Max;
        public readonly float Step;
        /// <summary>Format for stepper values, e.g. "0" or "0.0".</summary>
        public readonly string Format;
        /// <summary>Suffix for stepper values, e.g. " Н".</summary>
        public readonly string Unit;
        public readonly string[] Choices;

        private ToolSettingInfo(ToolSettingKind kind, string label, float min, float max, float step,
            string format, string unit, string[] choices)
        {
            Kind = kind;
            Label = label;
            Min = min;
            Max = max;
            Step = step;
            Format = format;
            Unit = unit;
            Choices = choices;
        }

        public static ToolSettingInfo Stepper(string label, float min, float max, float step, string format, string unit)
            => new ToolSettingInfo(ToolSettingKind.Stepper, label, min, max, step, format, unit, null);

        public static ToolSettingInfo Toggle(string label)
            => new ToolSettingInfo(ToolSettingKind.Toggle, label, 0f, 1f, 1f, null, null, null);

        public static ToolSettingInfo Choice(string label, string[] choices)
            => new ToolSettingInfo(ToolSettingKind.Choice, label, 0f, choices.Length - 1, 1f, null, null, choices);

        /// <summary>Number of distinct values (used to pre-build value labels).</summary>
        public int ValueCount
        {
            get
            {
                switch (Kind)
                {
                    case ToolSettingKind.Toggle: return 2;
                    case ToolSettingKind.Choice: return Choices != null ? Choices.Length : 0;
                    default: return Step > 0f ? (int)System.Math.Round((Max - Min) / Step) + 1 : 1;
                }
            }
        }

        /// <summary>Value -> index in [0, ValueCount).</summary>
        public int ToIndex(float value)
        {
            int count = ValueCount;
            int index = Kind == ToolSettingKind.Stepper && Step > 0f
                ? (int)System.Math.Round((value - Min) / Step)
                : (int)System.Math.Round(value);
            return index < 0 ? 0 : (index >= count ? count - 1 : index);
        }

        /// <summary>Index -> value.</summary>
        public float FromIndex(int index)
        {
            return Kind == ToolSettingKind.Stepper ? Min + index * Step : index;
        }
    }
}
