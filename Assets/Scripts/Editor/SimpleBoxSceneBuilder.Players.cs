using SimpleBox.Controls;
using SimpleBox.Hud;
using SimpleBox.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Multiplayer social: nickname tag on the avatar prefab, players panel (report / block / kick) and the ИГРОКИ
    /// button in the session panel.
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private const int PlayerRows = 6;

        /// <summary>Adds a billboard TextMesh above the avatar body if the prefab has none.</summary>
        private static void EnsureAvatarNameTag(GameObject prefabAsset)
        {
            if (prefabAsset == null || prefabAsset.transform.Find("NameTag") != null) return;
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var tagGo = new GameObject("NameTag") { layer = root.layer };
                tagGo.transform.SetParent(root.transform, false);
                tagGo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
                TextMesh text = tagGo.AddComponent<TextMesh>();
                text.font = font;
                text.fontSize = 64;
                text.characterSize = 0.035f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                text.text = "Игрок";
                if (font != null) tagGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                NetworkPlayerAvatar avatar = root.GetComponent<NetworkPlayerAvatar>();
                if (avatar != null) SetRefs(avatar, ("nameTag", text));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildPlayersPanel(MobileInputController input, NetworkGameplay gameplay)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            const float rowHeight = 92f;
            RectTransform panel = CreateRect("PlayersPanel", ui);
            Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060f, 230f + PlayerRows * rowHeight));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.88f), true).type = Image.Type.Sliced;

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-200f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(-70f, -10f);
            title.text = "Игроки";

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-70f, -55f), 90f, circle, font);

            Text empty = CreateLabel("Empty", panel, font, 28, TextAnchor.MiddleCenter);
            Stretch(empty.rectTransform, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f));
            empty.fontStyle = FontStyle.Normal;
            empty.text = "Нет игроков";
            empty.gameObject.SetActive(false);

            Text status = CreateLabel("Status", panel, font, 24, TextAnchor.MiddleCenter);
            status.rectTransform.anchorMin = new Vector2(0f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 0f);
            status.rectTransform.pivot = new Vector2(0.5f, 0f);
            status.rectTransform.sizeDelta = new Vector2(-60f, 80f);
            status.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            status.fontStyle = FontStyle.Normal;
            status.color = new Color(1f, 0.85f, 0.4f);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.text = string.Empty;

            PlayersPanel component = ui.gameObject.AddComponent<PlayersPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("gameplay").objectReferenceValue = gameplay;
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("emptyLabel").objectReferenceValue = empty;

            SerializedProperty rows = so.FindProperty("rows");
            rows.arraySize = PlayerRows;
            for (int i = 0; i < PlayerRows; i++)
            {
                RectTransform row = CreateRect($"Row{i}", panel);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(-60f, rowHeight - 8f);
                row.anchoredPosition = new Vector2(0f, -100f - i * rowHeight);
                AddImage(row, rounded, new Color(1f, 1f, 1f, 0.06f), false).type = Image.Type.Sliced;

                Text name = CreateLabel("Name", row, font, 30, TextAnchor.MiddleLeft);
                Stretch(name.rectTransform, Vector2.zero, new Vector2(0.5f, 1f));
                name.rectTransform.offsetMin = new Vector2(24f, 0f);
                name.fontStyle = FontStyle.Normal;
                name.text = "—";

                TouchButton report = CreateRowButton("Report", "ЖАЛОБА", row, 0.6f, rounded, circle, font);
                TouchButton block = CreateRowButton("Block", "БЛОК", row, 0.76f, rounded, circle, font);
                TouchButton kick = CreateRowButton("Kick", "КИК", row, 0.91f, rounded, circle, font);

                SerializedProperty e = rows.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
                e.FindPropertyRelative("nameLabel").objectReferenceValue = name;
                e.FindPropertyRelative("reportButton").objectReferenceValue = report;
                e.FindPropertyRelative("blockButton").objectReferenceValue = block;
                e.FindPropertyRelative("blockLabel").objectReferenceValue = block.transform.Find("Label").GetComponent<Text>();
                e.FindPropertyRelative("kickButton").objectReferenceValue = kick;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);

            // ИГРОКИ in the session panel (top-left corner, shown only while in a session).
            Transform sessionRoot = ui.Find("SessionPanel");
            SessionPanel sessionPanel = ui.GetComponent<SessionPanel>();
            if (sessionRoot != null && sessionPanel != null)
            {
                TouchButton players = CreateButton("PlayersButton", "ИГРОКИ", sessionRoot, new Vector2(0f, 1f), new Vector2(60f, -50f), 80f, circle, font);
                players.transform.Find("Label").GetComponent<Text>().fontSize = 15;
                players.gameObject.SetActive(false);
                SetRefs(sessionPanel, ("playersButton", players), ("playersPanel", component));
            }
        }

        private static TouchButton CreateRowButton(string name, string label, Transform row, float anchorX, Sprite rounded, Sprite circle, Font font)
        {
            TouchButton button = CreateButton(name, label, row, new Vector2(anchorX, 0.5f), Vector2.zero, 70f, circle, font);
            var rt = (RectTransform)button.transform;
            rt.sizeDelta = new Vector2(150f, 66f);
            Image img = button.GetComponent<Image>();
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            button.transform.Find("Label").GetComponent<Text>().fontSize = 20;
            return button;
        }
    }
}
