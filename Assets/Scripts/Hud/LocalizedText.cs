using SimpleBox.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Static label translated by Loc: the text set in the scene (Russian) is the key. Re-applied on language change,
    /// but only while the label still shows the last applied text, so labels that code rewrites are left alone.
    /// Added by the scene builder to every scene-built Text with Russian content.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Tooltip("Russian source text (filled from the label on first use if empty).")]
        [SerializeField] private string key = string.Empty;

        private Text _text;
        private string _applied;
        private bool _subscribed;

        private void Awake()
        {
            _text = GetComponent<Text>();
            if (string.IsNullOrEmpty(key)) key = _text.text;
            _applied = _text.text;
        }

        private void OnEnable()
        {
            Apply();
            if (_subscribed) return;
            Loc.Changed += Apply;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (_subscribed) Loc.Changed -= Apply;
            _subscribed = false;
        }

        private void Apply()
        {
            if (_text == null || string.IsNullOrEmpty(key)) return;
            if (_text.text != _applied && _text.text != key) return; // rewritten by code
            string value = Loc.T(key);
            _text.text = value;
            _applied = value;
        }

        /// <summary>Editor/builder: sets the source text.</summary>
        public void SetKey(string source) => key = source;
    }
}
