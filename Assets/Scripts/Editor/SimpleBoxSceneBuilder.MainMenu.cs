using SimpleBox.Controls;
using SimpleBox.Gameplay;
using SimpleBox.Hud;
using SimpleBox.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Main menu overlay: its own canvas under MobileUI (sorting 10, above the HUD), orbiting menu camera under
    /// Environment, CanvasGroup on the HUD safe area, and nested canvases (sorting 30, ignore parent groups) on the
    /// settings / saves / session panels so the menu can open them while the HUD is hidden. Runs last (after network UI).
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private const int MenuSortingOrder = 10;
        private const int PanelSortingOrder = 30;

        private static void BuildMainMenu(MobileInputController input, PhysicsGun playerGun, SystemsRefs sys, Transform environment)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null)
            {
                Debug.LogError("[SimpleBox] MobileUI/SafeArea not found: main menu not built.");
                return;
            }

            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            CanvasGroup hudGroup = ui.GetComponent<CanvasGroup>();
            if (hudGroup == null) hudGroup = ui.gameObject.AddComponent<CanvasGroup>();

            SettingsPanel settingsPanel = ui.GetComponent<SettingsPanel>();
            SaveMenuPanel saveMenu = ui.GetComponent<SaveMenuPanel>();
            SessionPanel sessionPanel = ui.GetComponent<SessionPanel>();
            OnboardingGuide onboarding = ui.GetComponent<OnboardingGuide>();
            NetworkSessionService session = Object.FindAnyObjectByType<NetworkSessionService>();
            if (settingsPanel == null || saveMenu == null || sessionPanel == null || session == null)
            {
                Debug.LogError("[SimpleBox] Settings / save / session panels or NetworkSessionService missing: main menu not built.");
                return;
            }

            MakeOverlayPanel(ui.Find("GameSettingsPanel"));
            MakeOverlayPanel(ui.Find("SaveMenuPanel"));
            MakeOverlayPanel(ui.Find("SessionPanel"));
            MakeOverlayPanel(ui.Find("PlayersPanel"));

            // "В МЕНЮ" in the settings panel.
            Transform settingsRoot = ui.Find("GameSettingsPanel");
            if (settingsRoot != null)
            {
                TouchButton exit = CreateButton("ExitToMenuButton", "В МЕНЮ", settingsRoot, new Vector2(1f, 0f),
                    new Vector2(-300f, 55f), 90f, circle, font);
                exit.transform.Find("Label").GetComponent<Text>().fontSize = 16;
                SetRefs(settingsPanel, ("exitToMenuButton", exit));
            }
            if (onboarding != null)
            {
                var onboardingSo = new SerializedObject(onboarding);
                onboardingSo.FindProperty("autoStart").boolValue = false; // started by the menu after ИГРАТЬ
                onboardingSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---- Cameras
            Camera playerCamera = null;
            var aim = new SerializedObject(playerGun).FindProperty("aimOrigin").objectReferenceValue as Transform;
            if (aim != null) playerCamera = aim.GetComponent<Camera>();

            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(environment, false);
            Camera menuCamera = camGo.AddComponent<Camera>();
            menuCamera.fieldOfView = 50f;
            menuCamera.nearClipPlane = 0.3f;
            menuCamera.farClipPlane = 300f;
            menuCamera.enabled = false;
            if (playerCamera != null)
            {
                menuCamera.clearFlags = playerCamera.clearFlags;
                menuCamera.backgroundColor = playerCamera.backgroundColor;
                menuCamera.cullingMask = playerCamera.cullingMask;
            }

            // ---- Menu canvas (full screen, above the HUD)
            RectTransform menuCanvasRt = CreateRect("MainMenuUI", input.transform);
            Stretch(menuCanvasRt, Vector2.zero, Vector2.one);
            Canvas menuCanvas = menuCanvasRt.gameObject.AddComponent<Canvas>();
            menuCanvas.overrideSorting = true;
            menuCanvas.sortingOrder = MenuSortingOrder;
            menuCanvasRt.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform safe = CreateRect("MenuSafeArea", menuCanvasRt);
            Stretch(safe, Vector2.zero, Vector2.one);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Full-screen catcher: touches never reach the hidden HUD (it is non-raycastable anyway) or fall through.
            RectTransform blocker = CreateRect("Blocker", menuCanvasRt);
            Stretch(blocker, Vector2.zero, Vector2.one);
            AddImage(blocker, null, new Color(0f, 0f, 0f, 0.15f), true);
            blocker.SetAsFirstSibling();

            RectTransform left = CreateRect("LeftPanel", safe);
            Stretch(left, Vector2.zero, new Vector2(0.4f, 1f));
            AddImage(left, null, new Color(0.04f, 0.05f, 0.07f, 0.82f), false);

            Text title = CreateLabel("Title", left, font, 96, TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(700f, 130f));
            title.text = "SimpleBox";

            Text subtitle = CreateLabel("Subtitle", left, font, 28, TextAnchor.MiddleCenter);
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -215f), new Vector2(700f, 40f));
            subtitle.fontStyle = FontStyle.Normal;
            subtitle.color = new Color(1f, 1f, 1f, 0.55f);
            subtitle.text = "физическая песочница";

            TouchButton play = CreateWideButton("PlayButton", "ИГРАТЬ", left, -330f, rounded, circle, font, new Color(1f, 0.72f, 0.2f, 0.9f));
            TouchButton multiplayer = CreateWideButton("MultiplayerButton", "МУЛЬТИПЛЕЕР", left, -445f, rounded, circle, font, null);
            TouchButton load = CreateWideButton("LoadButton", "ЗАГРУЗИТЬ", left, -560f, rounded, circle, font, null);
            TouchButton settings = CreateWideButton("SettingsButton", "НАСТРОЙКИ", left, -675f, rounded, circle, font, null);
            TouchButton about = CreateWideButton("AboutButton", "О ИГРЕ", left, -790f, rounded, circle, font, null);
            Text playLabel = play.transform.Find("Label").GetComponent<Text>();
            playLabel.color = new Color(0.1f, 0.08f, 0.02f);

            // Nickname field (bottom of the left panel).
            Text nickCaption = CreateLabel("NicknameCaption", left, font, 24, TextAnchor.MiddleLeft);
            Place(nickCaption.rectTransform, new Vector2(0.5f, 0f), new Vector2(-150f, 175f), new Vector2(220f, 40f));
            nickCaption.fontStyle = FontStyle.Normal;
            nickCaption.color = new Color(1f, 1f, 1f, 0.6f);
            nickCaption.text = "Ник";
            InputField nickname = CreateTextField("NicknameInput", left, new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                new Vector2(520f, 80f), rounded, font, "Твой ник");

            Text version = CreateLabel("Version", safe, font, 22, TextAnchor.LowerRight);
            Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-150f, 30f), new Vector2(260f, 36f));
            version.fontStyle = FontStyle.Normal;
            version.color = new Color(1f, 1f, 1f, 0.45f);
            version.text = "v0.0.0";

            // ---- About
            RectTransform aboutRoot = CreateRect("AboutPanel", safe);
            Place(aboutRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 460f));
            AddImage(aboutRoot, rounded, new Color(0f, 0f, 0f, 0.9f), true).type = Image.Type.Sliced;
            Text aboutTitle = CreateLabel("Title", aboutRoot, font, 44, TextAnchor.MiddleCenter);
            Place(aboutTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(700f, 70f));
            aboutTitle.text = "SimpleBox";
            Text aboutText = CreateLabel("Text", aboutRoot, font, 26, TextAnchor.MiddleCenter);
            Place(aboutText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(720f, 140f));
            aboutText.fontStyle = FontStyle.Normal;
            aboutText.horizontalOverflow = HorizontalWrapMode.Wrap;
            aboutText.text = "Физическая песочница для телефона.\nСтрой, взрывай, катайся — один или с друзьями.";
            TouchButton privacy = CreateWideButton("PrivacyButton", "ПОЛИТИКА КОНФИДЕНЦИАЛЬНОСТИ", aboutRoot, -330f, rounded, circle, font, null);
            privacy.transform.Find("Label").GetComponent<Text>().fontSize = 22;
            ((RectTransform)privacy.transform).sizeDelta = new Vector2(600f, 80f);
            TouchButton aboutClose = CreateButton("CloseButton", "OK", aboutRoot, new Vector2(1f, 1f), new Vector2(-60f, -55f), 80f, circle, font);
            aboutRoot.gameObject.SetActive(false);

            MainMenu menu = menuCanvasRt.gameObject.AddComponent<MainMenu>();
            SetRefs(menu,
                ("menuRoot", safe.gameObject),
                ("hudGroup", hudGroup),
                ("input", input),
                ("playButton", play),
                ("playLabel", playLabel),
                ("multiplayerButton", multiplayer),
                ("loadButton", load),
                ("settingsButton", settings),
                ("settingsPanel", settingsPanel),
                ("saveMenu", saveMenu),
                ("sessionPanel", sessionPanel),
                ("session", session),
                ("saves", sys.Saves),
                ("registry", sys.Registry),
                ("actions", sys.Actions),
                ("aboutButton", about),
                ("aboutRoot", aboutRoot.gameObject),
                ("aboutCloseButton", aboutClose),
                ("privacyButton", privacy),
                ("versionLabel", version),
                ("nicknameField", nickname),
                ("onboarding", onboarding),
                ("physicsGun", playerGun),
                ("playerCamera", playerCamera),
                ("menuCamera", menuCamera));
            // The blocker belongs to the menu: hidden together with it.
            blocker.SetParent(safe, false);
            blocker.SetAsFirstSibling();
        }

        /// <summary>Own canvas above the main menu and a group that ignores the hidden HUD group.</summary>
        private static void MakeOverlayPanel(Transform panel)
        {
            if (panel == null) return;
            GameObject go = panel.gameObject;
            // A nested Canvas keeps Override Sorting only if it is set while the object is active: Unity treats an
            // inactive canvas as a root and drops the flag (the panel then rendered below the menu canvas and its
            // full-screen blocker swallowed the panel's buttons).
            bool wasActive = go.activeSelf;
            if (!wasActive) go.SetActive(true);
            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = PanelSortingOrder;
            EditorUtility.SetDirty(canvas);
            if (!wasActive) go.SetActive(false);
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;

            // Modal: a full-screen dim catcher behind the panel content, so menu / HUD buttons around the panel
            // cannot be pressed (no second window opening on top, no presses queued for later).
            if (panel.Find("ModalBlocker") == null)
            {
                RectTransform blocker = CreateRect("ModalBlocker", panel);
                Place(blocker, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10000f, 10000f));
                AddImage(blocker, null, new Color(0f, 0f, 0f, 0.45f), true);
                blocker.SetAsFirstSibling();
            }
        }

        /// <summary>Wide rounded TouchButton anchored at the top center of the parent.</summary>
        private static TouchButton CreateWideButton(string name, string label, Transform parent, float y, Sprite rounded, Sprite circle,
            Font font, Color? color)
        {
            TouchButton button = CreateButton(name, label, parent, new Vector2(0.5f, 1f), new Vector2(0f, y), 100f, circle, font);
            var rt = (RectTransform)button.transform;
            rt.sizeDelta = new Vector2(520f, 96f);
            Image img = button.GetComponent<Image>();
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            Color normal = color ?? new Color(1f, 1f, 1f, 0.12f);
            img.color = normal;
            var so = new SerializedObject(button);
            so.FindProperty("normalColor").colorValue = normal;
            so.FindProperty("pressedColor").colorValue = new Color(normal.r * 0.7f, normal.g * 0.7f, normal.b * 0.7f, Mathf.Min(1f, normal.a + 0.2f));
            so.ApplyModifiedPropertiesWithoutUndo();
            button.transform.Find("Label").GetComponent<Text>().fontSize = 34;
            return button;
        }

        private static InputField CreateTextField(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size,
            Sprite rounded, Font font, string placeholderText)
        {
            RectTransform field = CreateRect(name, parent);
            Place(field, anchor, position, size);
            Image bg = AddImage(field, rounded, new Color(1f, 1f, 1f, 0.92f), true);
            bg.type = Image.Type.Sliced;

            Text text = CreateLabel("Text", field, font, 32, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one);
            text.color = Color.black;
            text.fontStyle = FontStyle.Normal;
            text.supportRichText = false;

            Text placeholder = CreateLabel("Placeholder", field, font, 30, TextAnchor.MiddleCenter);
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one);
            placeholder.color = new Color(0f, 0f, 0f, 0.4f);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.text = placeholderText;

            InputField input = field.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }
    }
}
