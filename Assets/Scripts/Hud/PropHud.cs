using SimpleBox.Gameplay;
using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Shows the selected prop (name + optional icon) and the "active / max" counter.
    /// Event-driven: labels change only when the spawner reports a change. Counter strings are cached,
    /// so updates allocate nothing. A failed spawn flashes a message in place of the name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropHud : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private PropSpawner spawner;
        [SerializeField] private Text nameLabel;
        [SerializeField] private Text counterLabel;

        [Header("References (optional)")]
        [Tooltip("Hidden when the selected prop has no icon.")]
        [SerializeField] private Image iconImage;

        [Header("Feedback")]
        [SerializeField] private string noSpaceMessage = "НЕТ МЕСТА";
        [SerializeField] private string limitMessage = "ЛИМИТ";
        [SerializeField, Min(0.1f)] private float failMessageDuration = 0.8f;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color failColor = new Color(1f, 0.35f, 0.3f);
        [SerializeField] private Color counterFullColor = new Color(1f, 0.75f, 0.3f);

        private const string NoSelection = "—";

        private string[] _counterCache;
        private float _failTimer;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            string missing = string.Empty;
            if (spawner == null) missing += " Spawner";
            if (nameLabel == null) missing += " NameLabel";
            if (counterLabel == null) missing += " CounterLabel";
            if (missing.Length > 0)
            {
                Debug.LogError($"[{nameof(PropHud)}] '{name}': required references not assigned:{missing}. Component disabled.", this);
                enabled = false;
                return;
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            spawner.SelectionChanged += OnSelectionChanged;
            spawner.ActiveCountChanged += OnActiveCountChanged;
            spawner.SpawnFailed += OnSpawnFailed;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed || spawner == null) return;
            spawner.SelectionChanged -= OnSelectionChanged;
            spawner.ActiveCountChanged -= OnActiveCountChanged;
            spawner.SpawnFailed -= OnSpawnFailed;
            _subscribed = false;
        }

        private void Start()
        {
            if (!_valid) return;
            ShowSelected();
            ShowCounter(spawner.ActiveCount, spawner.MaxActiveProps);
        }

        private void Update()
        {
            if (_failTimer <= 0f) return;
            _failTimer -= Time.unscaledDeltaTime;
            if (_failTimer <= 0f) ShowSelected();
        }

        private void OnSelectionChanged(int index)
        {
            if (_failTimer <= 0f) ShowSelected();
            else UpdateIcon(); // name is restored when the fail message expires
        }

        private void OnActiveCountChanged(int active, int max) => ShowCounter(active, max);

        private void OnSpawnFailed(ActionResult result)
        {
            if (result == ActionResult.Invalid) return; // cooldown
            nameLabel.text = result == ActionResult.OwnerLimit || result == ActionResult.ServerLimit ? limitMessage : noSpaceMessage;
            nameLabel.color = failColor;
            _failTimer = failMessageDuration;
        }

        private void ShowSelected()
        {
            _failTimer = 0f;
            PropDefinition def = spawner.IsValid ? spawner.SelectedDefinition : null;
            nameLabel.text = def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : NoSelection;
            nameLabel.color = normalColor;
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            if (iconImage == null) return;
            PropDefinition def = spawner.IsValid ? spawner.SelectedDefinition : null;
            Sprite sprite = def != null ? def.icon : null;
            iconImage.sprite = sprite;
            iconImage.enabled = sprite != null;
        }

        private void ShowCounter(int active, int max)
        {
            if (_counterCache == null || _counterCache.Length != max + 1) BuildCounterCache(max);

            counterLabel.text = active >= 0 && active <= max ? _counterCache[active] : NoSelection;
            counterLabel.color = active >= max ? counterFullColor : normalColor;
        }

        private void BuildCounterCache(int max)
        {
            if (max < 0) max = 0;
            _counterCache = new string[max + 1];
            for (int i = 0; i <= max; i++) _counterCache[i] = i + "/" + max;
        }
    }
}
