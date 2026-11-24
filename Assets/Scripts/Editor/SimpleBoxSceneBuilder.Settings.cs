using System.Collections.Generic;
using SimpleBox.Controls;
using SimpleBox.Core;
using SimpleBox.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Release phase: GameSettings on the Systems root, MENU button + settings panel (one stepper row per setting),
    /// FPS counter label at the top center.
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private static void BuildGameSettings(MobileInputController input, QualityManager quality)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // On-screen controls scaled by the Control Size setting: every button placed directly in the safe area
            // (collected before the menu's own button and panel are created).
            var controls = new List<RectTransform>();
            for (int i = 0; i < ui.childCount; i++)
            {
                Transform child = ui.GetChild(i);
                if (child.GetComponent<TouchButton>() != null) controls.Add((RectTransform)child);
            }

            // ---- FPS counter
            Text fpsText = CreateLabel("FpsCounter", ui, font, 26, TextAnchor.MiddleCenter);
            Place(fpsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(200f, 40f));
            fpsText.text = "-- FPS";
            FpsCounter counter = fpsText.gameObject.AddComponent<FpsCounter>();
            SetRefs(counter, ("label", fpsText));
            fpsText.gameObject.SetActive(false);

            // ---- Settings service
            GameSettings settings = quality.gameObject.AddComponent<GameSettings>();
            var settingsSo = new SerializedObject(settings);
            settingsSo.FindProperty("quality").objectReferenceValue = quality;
            settingsSo.FindProperty("input").objectReferenceValue = input;
            settingsSo.FindProperty("fpsCounter").objectReferenceValue = fpsText.gameObject;
            SerializedProperty scaled = settingsSo.FindProperty("scaledControls");
            scaled.arraySize = controls.Count;
            for (int i = 0; i < controls.Count; i++) scaled.GetArrayElementAtIndex(i).objectReferenceValue = controls[i];
            settingsSo.ApplyModifiedPropertiesWithoutUndo();

            // ---- Menu button + panel
            TouchButton toggle = CreateButton("MenuButton", "MENU", ui, new Vector2(0f, 1f), new Vector2(400f, -240f), 130f, circle, font); // under FLY
            toggle.transform.Find("Label").GetComponent<Text>().fontSize = 26;

            const float rowHeight = 92f;
            int rowCount = GameSettings.SettingCount;
            RectTransform panel = CreateRect("GameSettingsPanel", ui);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(900f, 210f + rowCount * rowHeight));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.88f), true).type = Image.Type.Sliced; // blocks touches behind

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-200f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(-70f, -10f);
            title.text = "Настройки";

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-70f, -55f), 90f, circle, font);
            TouchButton reset = CreateButton("ResetButton", "СБРОС", panel, new Vector2(1f, 0f), new Vector2(-80f, 55f), 90f, circle, font);
            reset.transform.Find("Label").GetComponent<Text>().fontSize = 18;

            Text note = CreateLabel("RestartNote", panel, font, 22, TextAnchor.MiddleLeft);
            note.rectTransform.anchorMin = new Vector2(0f, 0f);
            note.rectTransform.anchorMax = new Vector2(1f, 0f);
            note.rectTransform.pivot = new Vector2(0.5f, 0f);
            note.rectTransform.sizeDelta = new Vector2(-430f, 60f); // clear of В МЕНЮ / ОБУЧЕНИЕ / СБРОС
            note.rectTransform.anchoredPosition = new Vector2(-185f, 25f);
            note.fontStyle = FontStyle.Normal;
            note.color = new Color(1f, 0.85f, 0.4f);
            note.text = string.Empty;
            note.gameObject.SetActive(false);

            SettingsPanel component = ui.gameObject.AddComponent<SettingsPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("input").objectReferenceValue = input;
            so.FindProperty("toggleButton").objectReferenceValue = toggle;
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("resetButton").objectReferenceValue = reset;
            so.FindProperty("noteLabel").objectReferenceValue = note;

            SerializedProperty rows = so.FindProperty("rows");
            rows.arraySize = rowCount;
            for (int i = 0; i < rowCount; i++)
            {
                var setting = (GameSettings.Setting)i;
                RectTransform row = CreateRect($"Row_{setting}", panel);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(-60f, rowHeight - 8f);
                row.anchoredPosition = new Vector2(0f, -100f - i * rowHeight);

                Text name = CreateLabel("Name", row, font, 28, TextAnchor.MiddleLeft);
                Stretch(name.rectTransform, Vector2.zero, new Vector2(0.5f, 1f));
                name.fontStyle = FontStyle.Normal;
                name.text = GameSettings.NameOf(setting);

                TouchButton prev = CreateButton("Prev", "<", row, new Vector2(0.56f, 0.5f), Vector2.zero, 76f, circle, font);
                Text value = CreateLabel("Value", row, font, 28, TextAnchor.MiddleCenter);
                Stretch(value.rectTransform, new Vector2(0.6f, 0f), new Vector2(0.88f, 1f));
                value.text = "—";
                TouchButton next = CreateButton("Next", ">", row, new Vector2(0.93f, 0.5f), Vector2.zero, 76f, circle, font);

                SerializedProperty e = rows.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("setting").enumValueIndex = i;
                e.FindPropertyRelative("valueLabel").objectReferenceValue = value;
                e.FindPropertyRelative("prevButton").objectReferenceValue = prev;
                e.FindPropertyRelative("nextButton").objectReferenceValue = next;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
        }
    }
}
