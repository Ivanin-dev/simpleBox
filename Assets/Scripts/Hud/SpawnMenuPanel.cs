using SimpleBox.Core;
using SimpleBox.Controls;
using SimpleBox.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// Spawn menu (GMod Q-menu): a grid of every catalog prop with its icon and name. Opened by tapping the prop panel
    /// or Q; tapping a cell selects that prop and spawns it in front of you, then the menu closes.
    /// Cells are built once from the catalog at Start (a new catalog entry appears without editing the UI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnMenuPanel : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private PropSpawner spawner;
        [SerializeField] private MobileInputController input;
        [Tooltip("Opens / closes the menu (the prop panel in the top-right corner).")]
        [SerializeField] private TouchButton openButton;
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Parent of the cells (GridLayoutGroup).")]
        [SerializeField] private RectTransform grid;
        [Tooltip("Inactive cell: TouchButton on the root, children 'Icon' (Image) and 'Label' (Text).")]
        [SerializeField] private GameObject cellTemplate;

        [Header("References (optional)")]
        [SerializeField] private TouchButton closeButton;

        private TouchButton[] _cells = new TouchButton[0];
        private int[] _cellTypeIndex = new int[0];
        private bool _valid;

        private void Awake()
        {
            _valid = spawner != null && input != null && openButton != null && panelRoot != null && grid != null && cellTemplate != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(SpawnMenuPanel)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            cellTemplate.SetActive(false);
            panelRoot.SetActive(false);
        }

        private void Start()
        {
            if (!_valid || spawner.Catalog == null) return;

            int count = spawner.Catalog.Count;
            _cells = new TouchButton[count];
            _cellTypeIndex = new int[count];
            int built = 0;
            for (int i = 0; i < count; i++)
            {
                PropDefinition def = spawner.Catalog.Get(i);
                if (def == null || def.prefab == null || string.IsNullOrEmpty(def.id)) continue;

                GameObject cell = Instantiate(cellTemplate, grid);
                cell.name = "Cell_" + def.id;
                cell.SetActive(true);

                Transform iconT = cell.transform.Find("Icon");
                Image icon = iconT != null ? iconT.GetComponent<Image>() : null;
                if (icon != null)
                {
                    icon.sprite = def.icon;
                    icon.enabled = def.icon != null;
                }
                Transform labelT = cell.transform.Find("Label");
                Text label = labelT != null ? labelT.GetComponent<Text>() : null;
                if (label != null) label.text = Loc.T(def.displayName);

                _cells[built] = cell.GetComponent<TouchButton>();
                _cellTypeIndex[built] = i;
                built++;
            }
            if (built < count)
            {
                System.Array.Resize(ref _cells, built);
                System.Array.Resize(ref _cellTypeIndex, built);
            }
        }

        private void Update()
        {
            bool toggle = openButton.ConsumePress() | input.ConsumeSpawnMenu();
            if (toggle) panelRoot.SetActive(!panelRoot.activeSelf);
            if (closeButton != null && closeButton.isActiveAndEnabled && closeButton.ConsumePress()) panelRoot.SetActive(false);
            if (!panelRoot.activeSelf) return;

            for (int i = 0; i < _cells.Length; i++)
            {
                TouchButton cell = _cells[i];
                if (cell == null || !cell.ConsumePress()) continue;
                spawner.SelectProp(_cellTypeIndex[i]);
                spawner.RequestSpawn();
                panelRoot.SetActive(false);
                return;
            }
        }
    }
}
