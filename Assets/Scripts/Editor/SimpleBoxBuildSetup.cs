using System.Collections.Generic;
using SimpleBox.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Release setup (mobile only: Android + iOS):
    ///   SimpleBox → Build Boot Scene        — Assets/Scenes/Boot.unity with the loading screen, first in Build Settings,
    ///                                         loads the currently open game scene.
    ///   SimpleBox → Apply Mobile Player Settings — landscape, IL2CPP, ARM64, AAB, min OS versions, internet permission.
    /// </summary>
    public static class SimpleBoxBuildSetup
    {
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string DefaultBundleId = "com.ivanindev.simplebox";
        private const string ProductName = "SimpleBox";

        // ------------------------------------------------------------------ Boot scene

        [MenuItem("SimpleBox/Build Boot Scene")]
        public static void BuildBootScene()
        {
            Scene game = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(game.path) || game.path == BootScenePath || FindRoot(game, "Systems") == null)
            {
                EditorUtility.DisplayDialog("SimpleBox",
                    "Откройте сохранённую игровую сцену (собранную через Build Test Scene) и запустите команду снова.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string gamePath = game.path;
            string gameName = game.name;

            Scene boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLoadingScreen(gameName);
            EditorSceneManager.SaveScene(boot, BootScenePath);

            SetBuildOrder(BootScenePath, gamePath);
            EditorSceneManager.OpenScene(gamePath, OpenSceneMode.Single);
            Debug.Log($"[SimpleBox] Boot scene saved to {BootScenePath}; Build Settings: Boot (0) → {gameName} (1).");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == name) return go;
            }
            return null;
        }

        private static void SetBuildOrder(string bootPath, string gamePath)
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(bootPath, true),
                new EditorBuildSettingsScene(gamePath, true)
            };
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path != bootPath && s.path != gamePath) list.Add(s);
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void BuildLoadingScreen(string gameSceneName)
        {
            Color background = new Color(0.07f, 0.08f, 0.1f);

            // Camera only clears the screen (the canvas is overlay).
            var camGo = new GameObject("Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 0;

            var canvasGo = new GameObject("LoadingUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasGo.transform;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            Image bg = CreateImage("Background", root, null, background);
            Stretch(bg.rectTransform);

            Text title = CreateText("Title", root, font, 120, TextAnchor.MiddleCenter, "SimpleBox");
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 180f));

            Text subtitle = CreateText("Subtitle", root, font, 32, TextAnchor.MiddleCenter, "физическая песочница");
            subtitle.color = new Color(1f, 1f, 1f, 0.55f);
            subtitle.fontStyle = FontStyle.Normal;
            Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1400f, 60f));

            Image barBack = CreateImage("ProgressBack", root, rounded, new Color(1f, 1f, 1f, 0.12f));
            barBack.type = Image.Type.Sliced;
            Place(barBack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(900f, 28f));

            Image fill = CreateImage("ProgressFill", barBack.transform, null, new Color(1f, 0.78f, 0.25f));
            Stretch(fill.rectTransform);
            fill.rectTransform.offsetMin = new Vector2(4f, 4f);
            fill.rectTransform.offsetMax = new Vector2(-4f, -4f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;

            Text tip = CreateText("Tip", root, font, 30, TextAnchor.MiddleCenter, "—");
            tip.fontStyle = FontStyle.Normal;
            tip.color = new Color(1f, 1f, 1f, 0.8f);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            Place(tip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(1300f, 100f));

            Text error = CreateText("Error", root, font, 30, TextAnchor.MiddleCenter, string.Empty);
            error.color = new Color(1f, 0.4f, 0.35f);
            Place(error.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -320f), new Vector2(1300f, 60f));
            error.gameObject.SetActive(false);

            Text version = CreateText("Version", root, font, 24, TextAnchor.LowerRight, "v0.0.0");
            version.fontStyle = FontStyle.Normal;
            version.color = new Color(1f, 1f, 1f, 0.35f);
            Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(-160f, 40f), new Vector2(280f, 40f));

            SimpleBoxSceneBuilder.AddLocalizedTexts(root);

            BootLoader loader = canvasGo.AddComponent<BootLoader>();
            var so = new SerializedObject(loader);
            so.FindProperty("progressFill").objectReferenceValue = fill;
            so.FindProperty("tipLabel").objectReferenceValue = tip;
            so.FindProperty("versionLabel").objectReferenceValue = version;
            so.FindProperty("errorLabel").objectReferenceValue = error;
            so.FindProperty("gameScene").stringValue = gameSceneName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text CreateText(string name, Transform parent, Font font, int size, TextAnchor anchor, string value)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = value;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        // ------------------------------------------------------------------ Player settings

        [MenuItem("SimpleBox/Apply Mobile Player Settings")]
        public static void ApplyMobilePlayerSettings()
        {
            PlayerSettings.productName = ProductName;

            // Bundle id: set only while it is still Unity's default; the user's own id is kept.
            SetBundleIdIfDefault(NamedBuildTarget.Android);
            SetBundleIdIfDefault(NamedBuildTarget.iOS);

            // Landscape only, both directions.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.gcIncremental = true;
            PlayerSettings.SplashScreen.show = false; // own loading screen (Boot scene)
            PlayerSettings.statusBarHidden = true;

            // Android: 64-bit only (Google Play requirement), IL2CPP, App Bundle, Android 8.0+.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true; // multiplayer (relay)
            PlayerSettings.Android.renderOutsideSafeArea = true;   // HUD uses SafeAreaFitter
            EditorUserBuildSettings.buildAppBundle = true;

            // iOS: IL2CPP is the only backend; iOS 15+ (iPhone XS / XR and newer).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.requiresPersistentWiFi = false;

            AssetDatabase.SaveAssets();
            Debug.Log($"[SimpleBox] Mobile player settings applied. Bundle id: Android '{PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}', " +
                      $"iOS '{PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)}'. Version {PlayerSettings.bundleVersion}, " +
                      $"Android version code {PlayerSettings.Android.bundleVersionCode}.");
        }

        private static void SetBundleIdIfDefault(NamedBuildTarget target)
        {
            string current = PlayerSettings.GetApplicationIdentifier(target);
            if (string.IsNullOrEmpty(current) || current.StartsWith("com.DefaultCompany") || current.StartsWith("com.Company"))
                PlayerSettings.SetApplicationIdentifier(target, DefaultBundleId);
        }
    }
}
