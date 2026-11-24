using SimpleBox.Controls;
using SimpleBox.Gameplay;
using SimpleBox.Gameplay.Tools;
using SimpleBox.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Release phase: first-launch tutorial (hint card + pulsing highlight over the step's control) and the
    /// "ОБУЧЕНИЕ" button in the settings panel that restarts it. Runs after the settings menu and tools exist.
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private static void BuildOnboarding(MobileInputController input, GameObject player, PhysicsGun playerGun,
            ToolManager toolManager, SystemsRefs sys)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Drawn above the HUD buttons but below the menus (settings / saves / spawn menu are opened on top).
            Transform settingsPanelRoot = ui.Find("GameSettingsPanel");
            int insertIndex = settingsPanelRoot != null ? settingsPanelRoot.GetSiblingIndex() : ui.childCount;

            // ---- Highlight ring (no raycasts: never blocks the control under it)
            RectTransform ring = CreateRect("OnboardingHighlight", ui);
            Place(ring, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 160f));
            AddImage(ring, rounded, new Color(1f, 0.85f, 0.2f, 0.4f), false).type = Image.Type.Sliced;
            ring.SetSiblingIndex(insertIndex);

            // ---- Hint card (top center, under the toast line; only the SKIP button takes touches)
            RectTransform card = CreateRect("OnboardingCard", ui);
            Place(card, new Vector2(0.5f, 1f), new Vector2(0f, -450f), new Vector2(840f, 150f));
            AddImage(card, rounded, new Color(0f, 0f, 0f, 0.7f), false).type = Image.Type.Sliced;
            card.SetSiblingIndex(insertIndex + 1);

            Text counter = CreateLabel("Counter", card, font, 22, TextAnchor.UpperLeft);
            Stretch(counter.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f));
            counter.rectTransform.offsetMin = new Vector2(24f, 14f);
            counter.rectTransform.offsetMax = new Vector2(-24f, -12f);
            counter.color = new Color(1f, 0.85f, 0.3f);
            counter.text = "1/6";

            Text hint = CreateLabel("Hint", card, font, 26, TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f));
            hint.rectTransform.offsetMin = new Vector2(24f, 10f);
            hint.rectTransform.offsetMax = new Vector2(-150f, -36f);
            hint.fontStyle = FontStyle.Normal;
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.text = "—";

            TouchButton skip = CreateButton("SkipButton", "SKIP", card, new Vector2(1f, 0.5f), new Vector2(-70f, 0f), 100f, circle, font);
            skip.transform.Find("Label").GetComponent<Text>().fontSize = 22;

            // ---- Step targets: Move, Look, Spawn, Grab, Tool, Finish
            RectTransform[] targets =
            {
                ui.Find("JoystickZone/JoystickBackground") as RectTransform,
                null,
                ui.Find("SpawnButton") as RectTransform,
                ui.Find("UseToolButton") as RectTransform,
                ui.Find("NextToolButton") as RectTransform,
                ui.Find("MenuButton") as RectTransform
            };

            OnboardingGuide guide = ui.gameObject.AddComponent<OnboardingGuide>();
            var so = new SerializedObject(guide);
            so.FindProperty("cardRoot").objectReferenceValue = card.gameObject;
            so.FindProperty("hintLabel").objectReferenceValue = hint;
            so.FindProperty("counterLabel").objectReferenceValue = counter;
            so.FindProperty("skipButton").objectReferenceValue = skip;
            so.FindProperty("player").objectReferenceValue = player.transform;
            so.FindProperty("aimOrigin").objectReferenceValue = new SerializedObject(playerGun).FindProperty("aimOrigin").objectReferenceValue;
            so.FindProperty("registry").objectReferenceValue = sys.Registry;
            so.FindProperty("actions").objectReferenceValue = sys.Actions;
            so.FindProperty("physicsGun").objectReferenceValue = playerGun;
            so.FindProperty("toolManager").objectReferenceValue = toolManager;
            so.FindProperty("highlight").objectReferenceValue = ring;
            SerializedProperty stepTargets = so.FindProperty("stepTargets");
            stepTargets.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++) stepTargets.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            card.gameObject.SetActive(false);
            ring.gameObject.SetActive(false);

            // ---- Settings panel: restart tutorial
            SettingsPanel settingsPanel = ui.GetComponent<SettingsPanel>();
            if (settingsPanelRoot != null && settingsPanel != null)
            {
                TouchButton tutorial = CreateButton("TutorialButton", "ОБУЧЕНИЕ", settingsPanelRoot, new Vector2(1f, 0f),
                    new Vector2(-190f, 55f), 90f, circle, font);
                tutorial.transform.Find("Label").GetComponent<Text>().fontSize = 14;
                SetRefs(settingsPanel, ("tutorialButton", tutorial), ("onboarding", guide));
            }
        }
    }
}
