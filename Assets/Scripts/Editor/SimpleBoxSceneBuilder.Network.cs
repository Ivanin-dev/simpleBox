using System.Collections.Generic;
using SimpleBox.Controls;
using SimpleBox.Gameplay;
using SimpleBox.Hud;
using SimpleBox.Net;
using SimpleBox.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Network part of the scene builder: NetworkManager (UnityTransport, player prefab, prefab list, scene management off),
    /// NetworkGameplay + NetworkSessionService, network components on prop prefabs, the player avatar prefab
    /// and the session UI (NET button + panel).
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private static void BuildNetwork(SystemsRefs sys, GameObject player, MobileInputController input, PropCatalog catalog, int playerLayer)
        {
            GameObject avatar = GetOrCreateAvatarPrefab(playerLayer);
            EnsureAvatarNameTag(avatar);

            var prefabs = new List<GameObject> { avatar };
            for (int i = 0; i < catalog.Count; i++)
            {
                PropDefinition def = catalog.Get(i);
                if (def == null || def.prefab == null) continue;
                EnsurePropNetworkComponents(def.prefab);
                prefabs.Add(def.prefab);
            }
            NetworkPrefabsList list = GetOrCreateNetworkPrefabsList(prefabs);

            var go = new GameObject("NetworkManager");
            Undo.RegisterCreatedObjectUndo(go, "Create NetworkManager");
            NetworkManager network = go.AddComponent<NetworkManager>();
            UnityTransport transport = go.AddComponent<UnityTransport>();
            transport.MaxPayloadSize = 256 * 1024; // blueprints (saves / duplicator) travel as JSON in one RPC

            if (network.NetworkConfig == null) network.NetworkConfig = new NetworkConfig();
            NetworkConfig config = network.NetworkConfig;
            config.NetworkTransport = transport;
            config.PlayerPrefab = avatar;
            config.EnableSceneManagement = false; // same scene on every peer; in-scene props are matched by id, no scene reload
            if (config.Prefabs == null) config.Prefabs = new NetworkPrefabs();
            config.Prefabs.NetworkPrefabsLists.RemoveAll(l => l == null || (l != list && !IsDefaultList(l) && OverlapsList(l, list)));
            if (!config.Prefabs.NetworkPrefabsLists.Contains(list)) config.Prefabs.NetworkPrefabsLists.Add(list);
            EditorUtility.SetDirty(network);

            PhysicsGun localGun = player.GetComponent<PhysicsGun>();
            Transform aim = localGun != null ? new SerializedObject(localGun).FindProperty("aimOrigin").objectReferenceValue as Transform : null;

            NetworkGameplay gameplay = go.AddComponent<NetworkGameplay>();
            SetRefs(gameplay,
                ("actions", sys.Actions),
                ("registry", sys.Registry),
                ("physicsGuard", sys.Guard),
                ("localPhysgun", localGun),
                ("localPlayer", player.transform),
                ("localAim", aim));

            NetworkSessionService session = go.AddComponent<NetworkSessionService>();

            // In-scene network object with scene-wide replicated state (cables). Root level: not under a plain parent.
            var worldStateGo = new GameObject("NetworkWorldState");
            Undo.RegisterCreatedObjectUndo(worldStateGo, "Create NetworkWorldState");
            NetworkObject worldStateNo = worldStateGo.AddComponent<NetworkObject>();
            worldStateNo.AutoObjectParentSync = false;
            NetworkWorldState worldState = worldStateGo.AddComponent<NetworkWorldState>();
            SetRefs(worldState, ("registry", sys.Registry), ("cableVisuals", sys.Cables),
                ("explosions", sys.Explosions), ("explosionFx", sys.ExplosionFx));
            BuildSessionUi(input, session);
            BuildPlayersPanel(input, gameplay);
        }

        /// <summary>NetworkObject + NetworkTransform (server authority, no scale) + NetworkRigidbody + NetworkEntity.</summary>
        private static void EnsurePropNetworkComponents(GameObject prefabAsset)
        {
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path)) return;
            NetworkObject existingNo = prefabAsset.GetComponent<NetworkObject>();
            if (existingNo != null && !existingNo.AutoObjectParentSync && prefabAsset.GetComponent<NetworkTransform>() != null &&
                prefabAsset.GetComponent<NetworkRigidbody>() != null && prefabAsset.GetComponent<NetworkEntity>() != null)
                return;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                NetworkObject no = root.GetComponent<NetworkObject>();
                if (no == null) no = root.AddComponent<NetworkObject>();
                no.AutoObjectParentSync = false; // the pool reparents props; NGO must not revert it

                NetworkTransform nt = root.GetComponent<NetworkTransform>();
                if (nt == null) nt = root.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                nt.SyncScaleX = false;
                nt.SyncScaleY = false;
                nt.SyncScaleZ = false;

                if (root.GetComponent<NetworkRigidbody>() == null) root.AddComponent<NetworkRigidbody>();
                if (root.GetComponent<NetworkEntity>() == null) root.AddComponent<NetworkEntity>(); // adds SandboxEntity

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Player avatar: owner-authoritative NetworkTransform, capsule body (collider + visual), aim proxy and a
        /// runtime-configured proxy PhysicsGun. Existing prefab is kept.
        /// </summary>
        private static GameObject GetOrCreateAvatarPrefab(int playerLayer)
        {
            string path = $"{PrefabFolder}/NetworkPlayer.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            EnsureFolder(PrefabFolder);

            var root = new GameObject("NetworkPlayer") { layer = playerLayer };
            root.AddComponent<NetworkObject>();
            NetworkTransform nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncScaleX = false;
            nt.SyncScaleY = false;
            nt.SyncScaleZ = false;

            CapsuleCollider body = root.AddComponent<CapsuleCollider>();
            body.height = 1.8f;
            body.radius = 0.35f;
            body.center = new Vector3(0f, 0.9f, 0f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.layer = playerLayer;
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Material mat = GetOrCreateMaterial("Avatar", new Color(0.9f, 0.85f, 0.3f));
            if (mat != null) visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var aim = new GameObject("AimProxy") { layer = playerLayer };
            aim.transform.SetParent(root.transform, false);
            aim.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            PhysicsGun gun = root.AddComponent<PhysicsGun>();
            var gunSo = new SerializedObject(gun);
            gunSo.FindProperty("configuredAtRuntime").boolValue = true;
            gunSo.ApplyModifiedPropertiesWithoutUndo();

            NetworkPlayerAvatar avatar = root.AddComponent<NetworkPlayerAvatar>();
            SetRefs(avatar,
                ("visual", visual),
                ("bodyCollider", body),
                ("aimProxy", aim.transform),
                ("proxyPhysgun", gun));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Uses Netcode's own auto-maintained default list (DefaultNetworkPrefabs, IsDefault = true): it already collects
        /// every prefab with a NetworkObject. A second list with the same prefabs causes "duplicate GlobalObjectIdHash"
        /// errors. A list is created only if Netcode has not generated one; missing prefabs are added to it.
        /// </summary>
        private static NetworkPrefabsList GetOrCreateNetworkPrefabsList(List<GameObject> prefabs)
        {
            NetworkPrefabsList list = null;
            string[] guids = AssetDatabase.FindAssets("t:NetworkPrefabsList");
            for (int i = 0; i < guids.Length && list == null; i++)
            {
                var candidate = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate != null && IsDefaultList(candidate)) list = candidate;
            }

            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                MarkDefaultList(list);
                AssetDatabase.CreateAsset(list, "Assets/DefaultNetworkPrefabs.asset");
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject p = prefabs[i];
                if (p != null && !list.Contains(p)) list.Add(new NetworkPrefab { Prefab = p });
            }
            EditorUtility.SetDirty(list);
            return list;
        }

        /// <summary>NetworkPrefabsList.IsDefault is internal: read / write it as a serialized field.</summary>
        private static bool IsDefaultList(NetworkPrefabsList list)
        {
            SerializedProperty p = new SerializedObject(list).FindProperty("IsDefault");
            return p != null && p.boolValue;
        }

        private static void MarkDefaultList(NetworkPrefabsList list)
        {
            var so = new SerializedObject(list);
            SerializedProperty p = so.FindProperty("IsDefault");
            if (p == null) return;
            p.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>True if any prefab of "other" is also in "reference" (such a list would only create duplicates).</summary>
        private static bool OverlapsList(NetworkPrefabsList other, NetworkPrefabsList reference)
        {
            var items = other.PrefabList;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].Prefab != null && reference.Contains(items[i].Prefab)) return true;
            }
            return false;
        }

        private static void BuildSessionUi(MobileInputController input, NetworkSessionService session)
        {
            Transform ui = input.transform.Find("SafeArea");
            if (ui == null) ui = input.transform;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            TouchButton toggle = CreateButton("NetButton", "NET", ui, new Vector2(0f, 1f), new Vector2(550f, -100f), 130f, circle, font);
            toggle.transform.Find("Label").GetComponent<Text>().fontSize = 26;

            RectTransform panel = CreateRect("SessionPanel", ui);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 520f));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.85f), true).type = Image.Type.Sliced;

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleCenter);
            Stretch(title.rectTransform, new Vector2(0f, 0.84f), new Vector2(1f, 1f));
            title.text = "Мультиплеер";

            // Code field.
            RectTransform field = CreateRect("CodeInput", panel);
            Place(field, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(520f, 90f));
            Image fieldBg = AddImage(field, rounded, new Color(1f, 1f, 1f, 0.92f), true);
            fieldBg.type = Image.Type.Sliced;

            Text fieldText = CreateLabel("Text", field, font, 40, TextAnchor.MiddleCenter);
            Stretch(fieldText.rectTransform, Vector2.zero, Vector2.one);
            fieldText.color = Color.black;
            fieldText.supportRichText = false;

            Text placeholder = CreateLabel("Placeholder", field, font, 34, TextAnchor.MiddleCenter);
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one);
            placeholder.color = new Color(0f, 0f, 0f, 0.4f);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.text = "Код сессии";

            InputField inputField = field.gameObject.AddComponent<InputField>();
            inputField.targetGraphic = fieldBg;
            inputField.textComponent = fieldText;
            inputField.placeholder = placeholder;
            inputField.characterLimit = 12;
            inputField.contentType = InputField.ContentType.Alphanumeric;

            Vector2 center = new Vector2(0.5f, 0.5f);
            TouchButton host = CreateButton("HostButton", "HOST", panel, center, new Vector2(-220f, -20f), 160f, circle, font);
            TouchButton join = CreateButton("JoinButton", "JOIN", panel, center, new Vector2(0f, -20f), 160f, circle, font);
            TouchButton leave = CreateButton("LeaveButton", "LEAVE", panel, center, new Vector2(220f, -20f), 160f, circle, font);

            Text status = CreateLabel("Status", panel, font, 26, TextAnchor.MiddleCenter);
            Stretch(status.rectTransform, new Vector2(0f, 0.15f), new Vector2(1f, 0.3f));
            status.fontStyle = FontStyle.Normal;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.text = "Не в сети";

            Text code = CreateLabel("Code", panel, font, 48, TextAnchor.MiddleCenter);
            Stretch(code.rectTransform, new Vector2(0f, 0.02f), new Vector2(1f, 0.15f));
            code.color = new Color(0.5f, 1f, 0.6f);
            code.text = string.Empty;

            // Lives on the always-active SafeArea: Update must run while the panel is hidden (NET opens it).
            SessionPanel component = ui.gameObject.AddComponent<SessionPanel>();
            SetRefs(component,
                ("session", session),
                ("toggleButton", toggle),
                ("panelRoot", panel.gameObject),
                ("codeInput", inputField),
                ("hostButton", host),
                ("joinButton", join),
                ("leaveButton", leave),
                ("statusText", status),
                ("codeText", code),
                ("input", input));

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-60f, -50f), 80f, circle, font);
            SetRefs(component, ("closeButton", close));

            panel.gameObject.SetActive(false);
        }

        private static void EnsureSceneInBuildSettings(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("[SimpleBox] Сцена не сохранена: сохраните её и пересоберите, чтобы она попала в Build Settings.");
                return;
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scene.path) return;
            }

            // First = startup scene, unless the Boot (loading) scene is already first: then the game scene goes right after it.
            int insertAt = scenes.Length > 0 && scenes[0].path.EndsWith("/Boot.unity") ? 1 : 0;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
            list.Insert(insertAt, new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
