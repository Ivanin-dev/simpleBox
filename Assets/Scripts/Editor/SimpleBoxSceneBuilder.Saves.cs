using SimpleBox.Controls;
using SimpleBox.Hud;
using SimpleBox.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>Save menu: SAVE button (top-left row) + panel with the autosave row and 3 slots (SAVE / LOAD).</summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private static void BuildSaveMenu(MobileInputController input, SaveService saves)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            TouchButton toggle = CreateButton("SaveButton", "SAVE", ui, new Vector2(0f, 1f), new Vector2(250f, -240f), 130f, circle, font); // under CLEAR, clear of the tool panel
            toggle.transform.Find("Label").GetComponent<Text>().fontSize = 26;

            RectTransform panel = CreateRect("SaveMenuPanel", ui);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(820f, 560f));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.85f), true).type = Image.Type.Sliced; // blocks touches behind

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-200f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(-70f, -10f);
            title.text = "Сохранения";

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-70f, -55f), 90f, circle, font);

            SaveMenuPanel component = ui.gameObject.AddComponent<SaveMenuPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("saves").objectReferenceValue = saves;
            so.FindProperty("toggleButton").objectReferenceValue = toggle;
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            so.FindProperty("closeButton").objectReferenceValue = close;

            SerializedProperty rows = so.FindProperty("rows");
            rows.arraySize = SaveService.SlotCount;
            for (int slot = 0; slot < SaveService.SlotCount; slot++)
            {
                RectTransform row = CreateRect($"Slot{slot}", panel);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(-40f, 100f);
                row.anchoredPosition = new Vector2(0f, -110f - slot * 110f);

                Text label = CreateLabel("Label", row, font, 26, TextAnchor.MiddleLeft);
                Stretch(label.rectTransform, Vector2.zero, new Vector2(0.62f, 1f));
                label.fontStyle = FontStyle.Normal;
                label.text = "—";

                TouchButton save = null;
                if (slot != SaveService.AutosaveSlot)
                {
                    save = CreateButton("Save", "SAVE", row, new Vector2(0.72f, 0.5f), Vector2.zero, 90f, circle, font);
                    save.transform.Find("Label").GetComponent<Text>().fontSize = 20;
                }
                TouchButton load = CreateButton("Load", "LOAD", row, new Vector2(0.9f, 0.5f), Vector2.zero, 90f, circle, font);
                load.transform.Find("Label").GetComponent<Text>().fontSize = 20;

                SerializedProperty e = rows.GetArrayElementAtIndex(slot);
                e.FindPropertyRelative("label").objectReferenceValue = label;
                e.FindPropertyRelative("saveButton").objectReferenceValue = save;
                e.FindPropertyRelative("loadButton").objectReferenceValue = load;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
        }
    }
}
