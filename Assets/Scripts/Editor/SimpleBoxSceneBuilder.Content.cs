using System;
using System.Collections.Generic;
using System.IO;
using SimpleBox.Controls;
using SimpleBox.Gameplay;
using SimpleBox.Hud;
using SimpleBox.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Stage 10 content: extra props, paint palette, baked prop icons and the spawn menu (Q-menu).
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private const string IconFolder = "Assets/Data/Icons";
        private const int IconLayer = 31;
        private const int IconSize = 128;

        private static readonly string[] PaletteNames =
            { "White", "Black", "Grey", "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink", "Brown" };

        private static readonly Color[] PaletteColors =
        {
            new Color(0.95f, 0.95f, 0.95f), new Color(0.08f, 0.08f, 0.09f), new Color(0.5f, 0.52f, 0.55f),
            new Color(0.85f, 0.15f, 0.12f), new Color(0.95f, 0.5f, 0.1f), new Color(0.95f, 0.85f, 0.15f),
            new Color(0.2f, 0.7f, 0.25f), new Color(0.2f, 0.8f, 0.9f), new Color(0.15f, 0.3f, 0.85f),
            new Color(0.55f, 0.25f, 0.8f), new Color(0.95f, 0.45f, 0.7f), new Color(0.45f, 0.28f, 0.15f)
        };

        // ------------------------------------------------------------------ Extra props

        /// <summary>Creates the extra prop prefabs (kept if they exist) and appends them to the catalog.</summary>
        private static void AddExtraProps(PropCatalog catalog, int propsLayer)
        {
            AddProp(catalog, "plank", "Доска", "Prop_Plank", PrimitiveType.Cube, new Vector3(2f, 0.1f, 0.4f), 8f,
                new Color(0.7f, 0.5f, 0.3f), propsLayer);
            AddProp(catalog, "beam", "Балка", "Prop_Beam", PrimitiveType.Cube, new Vector3(3f, 0.2f, 0.2f), 12f,
                new Color(0.55f, 0.57f, 0.6f), propsLayer);
            AddProp(catalog, "plate", "Плита", "Prop_Plate", PrimitiveType.Cube, new Vector3(2f, 0.1f, 2f), 20f,
                new Color(0.6f, 0.62f, 0.65f), propsLayer);
            AddProp(catalog, "bigcrate", "Большой ящик", "Prop_BigCrate", PrimitiveType.Cube, new Vector3(2f, 2f, 2f), 40f,
                new Color(0.8f, 0.5f, 0.2f), propsLayer);
            AddProp(catalog, "wheel", "Колесо", "Prop_Wheel", PrimitiveType.Sphere, Vector3.one * 1.2f, 15f,
                new Color(0.15f, 0.15f, 0.17f), propsLayer);
            AddProp(catalog, "smallball", "Мячик", "Prop_SmallBall", PrimitiveType.Sphere, Vector3.one * 0.4f, 2f,
                new Color(0.9f, 0.3f, 0.3f), propsLayer);
        }

        private static void AddProp(PropCatalog catalog, string id, string displayName, string prefabName, PrimitiveType type,
            Vector3 scale, float mass, Color color, int propsLayer)
        {
            GameObject prefab = GetOrCreatePropPrefab(prefabName, type, scale, mass, propsLayer, GetOrCreateMaterial(prefabName, color));
            EnsurePropNetworkComponents(prefab);
            EnsureCatalogEntry(catalog, id, displayName, prefab);
        }

        // ------------------------------------------------------------------ Palette

        private static Material[] GetOrCreatePalette()
        {
            var palette = new Material[PaletteColors.Length];
            for (int i = 0; i < palette.Length; i++)
                palette[i] = GetOrCreateMaterial($"Paint_{i:00}_{PaletteNames[i]}", PaletteColors[i]);
            return palette;
        }

        private static void AssignPalette(SandboxActions actions, Material[] palette)
        {
            var so = new SerializedObject(actions);
            SerializedProperty p = so.FindProperty("colorPalette");
            p.arraySize = palette.Length;
            for (int i = 0; i < palette.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = palette[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ Icons

        /// <summary>
        /// Renders a 128x128 transparent icon for every catalog entry without one (isolated layer, temporary camera),
        /// saves it as a Sprite in Assets/Data/Icons and assigns it. Existing icons are kept.
        /// </summary>
        private static void BakeMissingIcons(PropCatalog catalog)
        {
            var so = new SerializedObject(catalog);
            SerializedProperty props = so.FindProperty("props");
            var pending = new List<int>();
            for (int i = 0; i < props.arraySize; i++)
            {
                SerializedProperty e = props.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("icon").objectReferenceValue == null &&
                    e.FindPropertyRelative("prefab").objectReferenceValue != null)
                    pending.Add(i);
            }
            if (pending.Count == 0) return;

            EnsureFolder(IconFolder);

            var camGo = new GameObject("IconCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << IconLayer;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;

            var lightGo = new GameObject("IconLight") { hideFlags = HideFlags.HideAndDontSave };
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.cullingMask = 1 << IconLayer;
            lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            var rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;
            var written = new List<(int entry, string path)>();

            try
            {
                for (int k = 0; k < pending.Count; k++)
                {
                    SerializedProperty e = props.GetArrayElementAtIndex(pending[k]);
                    var prefab = (GameObject)e.FindPropertyRelative("prefab").objectReferenceValue;
                    string id = e.FindPropertyRelative("id").stringValue;
                    if (string.IsNullOrEmpty(id)) id = prefab.name.ToLowerInvariant();

                    GameObject instance = Object.Instantiate(prefab, new Vector3(0f, -500f, 0f), prefab.transform.rotation);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        SetLayerRecursive(instance.transform, IconLayer);
                        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0) continue;
                        Bounds b = renderers[0].bounds;
                        for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);

                        float radius = Mathf.Max(b.extents.magnitude, 0.05f);
                        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                        Vector3 dir = new Vector3(1f, 0.8f, -1.2f).normalized;
                        camGo.transform.position = b.center + dir * distance;
                        camGo.transform.LookAt(b.center);

                        cam.Render();
                        RenderTexture previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                        tex.Apply();
                        RenderTexture.active = previous;

                        string path = $"{IconFolder}/{id}.png";
                        File.WriteAllBytes(path, tex.EncodeToPNG());
                        written.Add((pending[k], path));
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SimpleBox] Icon baking failed ({ex.Message}); the spawn menu will show names only.");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
            }

            if (written.Count == 0) return;
            AssetDatabase.Refresh();
            for (int i = 0; i < written.Count; i++)
            {
                (int entry, string path) = written[i];
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                props.GetArrayElementAtIndex(entry).FindPropertyRelative("icon").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        // ------------------------------------------------------------------ Spawn menu

        /// <summary>The prop panel becomes the opener; the menu holds a runtime-built grid of catalog props.</summary>
        private static void BuildSpawnMenu(MobileInputController input, PropSpawner spawner)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Opener: tap the prop panel (top right).
            Transform propPanel = ui.Find("PropPanel");
            TouchButton open = null;
            if (propPanel != null)
            {
                Image panelImage = propPanel.GetComponent<Image>();
                if (panelImage != null) panelImage.raycastTarget = true;
                open = propPanel.gameObject.AddComponent<TouchButton>();
                var openSo = new SerializedObject(open);
                openSo.FindProperty("targetGraphic").objectReferenceValue = panelImage;
                openSo.FindProperty("normalColor").colorValue = new Color(0f, 0f, 0f, 0.35f);
                openSo.FindProperty("pressedColor").colorValue = new Color(0f, 0f, 0f, 0.6f);
                openSo.ApplyModifiedPropertiesWithoutUndo();
            }

            RectTransform panel = CreateRect("SpawnMenu", ui);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(960f, 660f));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.85f), true).type = Image.Type.Sliced;

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-200f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(-70f, -10f);
            title.text = "Пропы";

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-70f, -55f), 90f, circle, font);

            RectTransform grid = CreateRect("Grid", panel);
            Stretch(grid, Vector2.zero, Vector2.one);
            grid.offsetMin = new Vector2(30f, 30f);
            grid.offsetMax = new Vector2(-30f, -110f);
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(165f, 175f);
            layout.spacing = new Vector2(14f, 14f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 5;
            layout.childAlignment = TextAnchor.UpperCenter;

            // Cell template (inactive): rounded background (button), icon, name.
            RectTransform cell = CreateRect("CellTemplate", grid);
            Image cellBg = AddImage(cell, rounded, new Color(1f, 1f, 1f, 0.12f), true);
            cellBg.type = Image.Type.Sliced;
            TouchButton cellButton = cell.gameObject.AddComponent<TouchButton>();
            var cellSo = new SerializedObject(cellButton);
            cellSo.FindProperty("targetGraphic").objectReferenceValue = cellBg;
            cellSo.FindProperty("normalColor").colorValue = new Color(1f, 1f, 1f, 0.12f);
            cellSo.FindProperty("pressedColor").colorValue = new Color(1f, 1f, 1f, 0.35f);
            cellSo.ApplyModifiedPropertiesWithoutUndo();

            RectTransform icon = CreateRect("Icon", cell);
            Place(icon, new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(118f, 118f));
            AddImage(icon, null, Color.white, false).preserveAspect = true;

            Text label = CreateLabel("Label", cell, font, 22, TextAnchor.LowerCenter);
            Stretch(label.rectTransform, Vector2.zero, new Vector2(1f, 0.3f));
            label.fontStyle = FontStyle.Normal;
            label.text = "Prop";
            cell.gameObject.SetActive(false);

            SpawnMenuPanel component = ui.gameObject.AddComponent<SpawnMenuPanel>();
            SetRefs(component,
                ("spawner", spawner),
                ("input", input),
                ("openButton", open),
                ("panelRoot", panel.gameObject),
                ("grid", grid),
                ("cellTemplate", cell.gameObject),
                ("closeButton", close));

            panel.gameObject.SetActive(false);
        }
    }
}
