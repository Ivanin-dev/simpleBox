using System;
using SimpleBox.Controls;
using SimpleBox.Core;
using SimpleBox.Gameplay;
using SimpleBox.Gameplay.Tools;
using SimpleBox.Hud;
using SimpleBox.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Object = UnityEngine.Object;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Menu: SimpleBox / Build Test Scene.
    /// Builds Environment, Props, Player (camera rig + controller + physgun), MobileUI and EventSystem
    /// in the active scene and wires every Inspector reference. Single Undo step (Ctrl+Z).
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string MaterialFolder = "Assets/Materials";
        private const string CatalogFolder = "Assets/Data";
        private const int UILayer = 5;

        private static readonly string[] ManagedRoots = { "Systems", "NetworkManager", "NetworkWorldState", "Environment", "Props", "Player", "MobileUI", "Effects" };

        [MenuItem("SimpleBox/Build Test Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SimpleBox", "Выйдите из Play Mode перед сборкой сцены.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();

            bool hasManaged = false;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (Array.IndexOf(ManagedRoots, go.name) >= 0) { hasManaged = true; break; }
            }

            if (hasManaged && !EditorUtility.DisplayDialog("SimpleBox",
                    "В сцене уже есть Environment / Props / Player / MobileUI.\nУдалить их и собрать заново?",
                    "Пересобрать", "Отмена"))
            {
                return;
            }

            int playerLayer = EnsureLayer("Player", 6);
            int propsLayer = EnsureLayer("Props", 7);
            if (playerLayer < 0 || propsLayer < 0)
            {
                Debug.LogError("[SimpleBox] Нет свободных User Layer для 'Player'/'Props'.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build SimpleBox Test Scene");

            if (hasManaged)
            {
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (Array.IndexOf(ManagedRoots, go.name) >= 0) Undo.DestroyObjectImmediate(go);
                }
            }

            EnsureDirectionalLight(scene);
            SystemsRefs sys = BuildSystems();
            BuildEnvironment();
            PropCatalog catalog = BuildProps(propsLayer, sys.Registry);
            AddExtraProps(catalog, propsLayer);
            AddExplosiveProp(catalog, propsLayer);
            AddRagdollProp(catalog, propsLayer);
            EnsureImpactSounds(catalog);
            BakeMissingIcons(catalog);
            MobileInputController input = BuildUI(out PropHud hud, out ToolHud toolHud, out ToastHud toastHud,
                out Graphic crosshair, out Text thrustLabel, out ContextHud contextHud, out ToolSettingsPanel settingsPanel);
            EnsureEventSystem(scene);
            GameObject player = BuildPlayer(scene, input, catalog, sys, playerLayer, propsLayer);
            PropSpawner spawner = player.GetComponent<PropSpawner>();
            SetRefs(hud, ("spawner", spawner));
            BuildSpawnMenu(input, spawner);

            SetRefs(sys.Actions,
                ("registry", sys.Registry),
                ("spawner", spawner),
                ("thrusters", sys.Thrusters),
                ("quality", sys.Quality),
                ("thrusterPrefab", GetOrCreateThrusterPrefab()),
                ("bindings", sys.Bindings));
            SetRefs(sys.Commands, ("input", input), ("actions", sys.Actions));
            AssignPalette(sys.Actions, GetOrCreatePalette());
            SetRefs(sys.Actions, ("explosions", sys.Explosions));
            PlayerController playerController = player.GetComponent<PlayerController>();
            SetRefs(toastHud, ("commands", sys.Commands), ("player", playerController), ("saves", sys.Saves));
            BuildSaveMenu(input, sys.Saves);
            // Seats: sit / exit, joystick driving merged into bindings.
            SeatController seatController = player.AddComponent<SeatController>();
            PhysicsGun playerGun = player.GetComponent<PhysicsGun>();
            SetRefs(seatController,
                ("input", input),
                ("player", playerController),
                ("aimOrigin", new SerializedObject(playerGun).FindProperty("aimOrigin").objectReferenceValue),
                ("registry", sys.Registry));
            var seatSo = new SerializedObject(seatController);
            seatSo.FindProperty("raycastMask").intValue = ~((1 << playerLayer) | (1 << UILayer) | (1 << 2));
            seatSo.ApplyModifiedPropertiesWithoutUndo();
            SetRefs(sys.Commands, ("seat", seatController));

            SetRefs(contextHud, ("physicsGun", playerGun), ("player", playerController), ("seat", seatController));
            SetRefs(sys.ExplosionFx, ("player", playerController));

            ToolManager toolManager = BuildTools(player, input, crosshair, sys, playerLayer, propsLayer);
            SetRefs(toolHud,
                ("toolManager", toolManager),
                ("thrusterRegistry", sys.Thrusters),
                ("thrustIndicator", thrustLabel));
            SetRefs(settingsPanel, ("toolManager", toolManager));

            BuildNetwork(sys, player, input, catalog, playerLayer);
            EnsureSceneInBuildSettings(scene);

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;

            Debug.Log("[SimpleBox] Сцена собрана. Сохраните её (Ctrl+S) и нажмите Play. " +
                      "В редакторе: мышь по зонам экрана, WASD / Space, " +
                      "R или G (спавн), колесо / 1-9 (выбор пропа), Tab / F1-F10 (инструмент), Q (меню пропов), стрелки (кнопки 1-4), E (сесть / встать), клик по правой половине (применить), " +
                      "X (ALT инструмента), T (тяга вкл/выкл), Z (отмена), Backspace ×2 (удалить все свои), V (полёт), " +
                      "ПКМ + мышь (вращать проп в руках), колесо в руках (дистанция).");
        }

        // ------------------------------------------------------------------ Systems

        private struct SystemsRefs
        {
            public QualityManager Quality;
            public EntityRegistry Registry;
            public ThrusterRegistry Thrusters;
            public SandboxActions Actions;
            public PhysicsGuard Guard;
            public PlayerCommands Commands;
            public InputBindings Bindings;
            public AxisMotorDriver Motors;
            public CableVisuals Cables;
            public HydraulicDriver Hydraulics;
            public SaveService Saves;
            public AudioManager Audio;
            public ExplosionSystem Explosions;
            public ExplosionFx ExplosionFx;
        }

        /// <summary>"Systems" root: quality, world registry, actions (filled after the spawner exists), guard, commands.</summary>
        private static SystemsRefs BuildSystems()
        {
            var root = new GameObject("Systems");
            Undo.RegisterCreatedObjectUndo(root, "Create Systems");

            var sys = new SystemsRefs
            {
                Quality = root.AddComponent<QualityManager>(),
                Registry = root.AddComponent<EntityRegistry>(),
                Thrusters = root.AddComponent<ThrusterRegistry>(),
                Actions = root.AddComponent<SandboxActions>(),
                Guard = root.AddComponent<PhysicsGuard>(),
                Commands = root.AddComponent<PlayerCommands>(),
                Bindings = root.AddComponent<InputBindings>(),
                Motors = root.AddComponent<AxisMotorDriver>(),
                Cables = root.AddComponent<CableVisuals>(),
                Hydraulics = root.AddComponent<HydraulicDriver>(),
                Saves = root.AddComponent<SaveService>(),
                Audio = root.AddComponent<AudioManager>(),
                Explosions = root.AddComponent<ExplosionSystem>(),
                ExplosionFx = root.AddComponent<ExplosionFx>()
            };

            SetRefs(sys.Quality,
                ("minimum", GetOrCreateQualityProfile("Quality_Minimum", false)),
                ("recommended", GetOrCreateQualityProfile("Quality_Recommended", true)));
            SetRefs(sys.Registry, ("quality", sys.Quality));
            SetRefs(sys.Thrusters, ("quality", sys.Quality), ("bindings", sys.Bindings));
            SetRefs(sys.Guard, ("registry", sys.Registry), ("quality", sys.Quality));
            SetRefs(sys.Motors, ("registry", sys.Registry), ("bindings", sys.Bindings));
            SetRefs(sys.Cables, ("registry", sys.Registry), ("lineMaterial", GetOrCreateCableMaterial()));
            SetRefs(sys.Hydraulics, ("registry", sys.Registry), ("bindings", sys.Bindings));
            SetRefs(sys.Saves, ("actions", sys.Actions));
            SetRefs(sys.Audio, ("registry", sys.Registry));
            SetRefs(sys.Explosions, ("registry", sys.Registry));
            SetRefs(sys.ExplosionFx, ("explosions", sys.Explosions), ("audioManager", sys.Audio), ("effectTemplate", CreateExplosionEffect()));
            return sys;
        }

        /// <summary>Existing assets are kept (user tuning preserved).</summary>
        private static QualityProfile GetOrCreateQualityProfile(string name, bool recommended)
        {
            string path = $"{CatalogFolder}/{name}.asset";
            QualityProfile existing = AssetDatabase.LoadAssetAtPath<QualityProfile>(path);
            if (existing != null) return existing;

            EnsureFolder(CatalogFolder);
            QualityProfile p = ScriptableObject.CreateInstance<QualityProfile>();
            if (recommended)
            {
                p.targetFrameRate = 60;
                p.fixedTimestep = 0.02f;
                p.solverIterations = 8;
                p.solverVelocityIterations = 2;
                p.sleepThreshold = 0.005f;
                p.maxDepenetrationVelocity = 4f;
                p.maxEntitiesTotal = 150;
                p.maxEntitiesPerOwner = 40;
                p.maxThrusters = 32;
                p.maxConstraintsPerEntity = 8;
                p.maxConstraintsTotal = 150;
                p.maxLinearSpeed = 60f;
                p.maxAngularSpeed = 25f;
            }
            else
            {
                p.targetFrameRate = 30;
                p.fixedTimestep = 1f / 30f;
                p.solverIterations = 6;
                p.solverVelocityIterations = 1;
                p.sleepThreshold = 0.01f;
                p.maxDepenetrationVelocity = 3f;
                p.maxEntitiesTotal = 60;
                p.maxEntitiesPerOwner = 25;
                p.maxThrusters = 12;
                p.maxConstraintsPerEntity = 6;
                p.maxConstraintsTotal = 50;
                p.maxLinearSpeed = 50f;
                p.maxAngularSpeed = 20f;
            }
            p.maximumDeltaTime = 0.1f;
            p.qualityLevel = -1;
            AssetDatabase.CreateAsset(p, path);
            return p;
        }

        // ------------------------------------------------------------------ Layers

        private static int EnsureLayer(string layerName, int preferredSlot)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            int slot = -1;
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredSlot).stringValue))
            {
                slot = preferredSlot;
            }
            else
            {
                for (int i = 8; i < 32; i++)
                {
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { slot = i; break; }
                }
            }

            if (slot < 0) return -1;

            layers.GetArrayElementAtIndex(slot).stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        // ------------------------------------------------------------------ Environment

        private static void EnsureDirectionalLight(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional) return;
                }
            }

            var go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Create Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void BuildEnvironment()
        {
            var root = new GameObject("Environment");
            Undo.RegisterCreatedObjectUndo(root, "Create Environment");
            root.isStatic = true;

            Material groundMat = GetOrCreateMaterial("Ground", new Color(0.55f, 0.57f, 0.6f));
            Material wallMat = GetOrCreateMaterial("Wall", new Color(0.35f, 0.38f, 0.42f));

            CreateStaticBox("Ground", root.transform, new Vector3(0f, -0.5f, 0f), Vector3.zero, new Vector3(40f, 1f, 40f), groundMat);
            CreateStaticBox("Wall", root.transform, new Vector3(0f, 1.5f, 10f), Vector3.zero, new Vector3(12f, 3f, 0.5f), wallMat);
            CreateStaticBox("Ramp", root.transform, new Vector3(-7f, 0.6f, 2f), new Vector3(-15f, 0f, 0f), new Vector3(3f, 0.3f, 6f), wallMat);
        }

        private static void CreateStaticBox(string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); // BoxCollider included
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.isStatic = true;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ Props

        private static PropCatalog BuildProps(int propsLayer, EntityRegistry registry)
        {
            var root = new GameObject("Props");
            Undo.RegisterCreatedObjectUndo(root, "Create Props");

            GameObject crate = GetOrCreatePropPrefab("Prop_Crate", PrimitiveType.Cube, Vector3.one, 10f, propsLayer,
                GetOrCreateMaterial("Prop_Crate", new Color(0.85f, 0.55f, 0.25f)));
            GameObject ball = GetOrCreatePropPrefab("Prop_Ball", PrimitiveType.Sphere, Vector3.one * 0.8f, 5f, propsLayer,
                GetOrCreateMaterial("Prop_Ball", new Color(0.25f, 0.6f, 0.9f)));
            GameObject barrel = GetOrCreatePropPrefab("Prop_Barrel", PrimitiveType.Capsule, new Vector3(0.6f, 0.6f, 0.6f), 8f, propsLayer,
                GetOrCreateMaterial("Prop_Barrel", new Color(0.4f, 0.75f, 0.35f)));

            // Network components on prop prefabs (inert offline). Must happen before scene instances are created.
            EnsurePropNetworkComponents(crate);
            EnsurePropNetworkComponents(ball);
            EnsurePropNetworkComponents(barrel);
            GameObject seat = GetOrCreateSeatPrefab(propsLayer);
            EnsurePropNetworkComponents(seat);

            PropCatalog catalog = GetOrCreateCatalog(
                ("crate", "Ящик", crate),
                ("ball", "Мяч", ball),
                ("barrel", "Бочка", barrel),
                ("seat", "Кресло", seat));
            EnsureCatalogEntry(catalog, "seat", "Кресло", seat); // catalogs created before the seat existed

            // Hand-placed props: world-owned entities registered by themselves at Start.
            SpawnProp(crate, root.transform, new Vector3(-2f, 0.5f, 2f), registry, "crate");
            SpawnProp(crate, root.transform, new Vector3(-2f, 1.5f, 2f), registry, "crate");
            SpawnProp(crate, root.transform, new Vector3(0f, 0.5f, 3f), registry, "crate");
            SpawnProp(crate, root.transform, new Vector3(2.5f, 0.5f, 2f), registry, "crate");
            SpawnProp(ball, root.transform, new Vector3(1f, 0.4f, 0.5f), registry, "ball");
            SpawnProp(ball, root.transform, new Vector3(-1f, 0.4f, 5f), registry, "ball");
            SpawnProp(barrel, root.transform, new Vector3(3.5f, 0.6f, 4f), registry, "barrel");

            return catalog;
        }

        private static PropCatalog GetOrCreateCatalog(params (string id, string displayName, GameObject prefab)[] entries)
        {
            const string path = CatalogFolder + "/PropCatalog.asset";
            PropCatalog catalog = AssetDatabase.LoadAssetAtPath<PropCatalog>(path);
            if (catalog != null)
            {
                FillMissingTypeIds(catalog); // catalogs created before type ids existed
                return catalog;              // keep user edits
            }

            EnsureFolder(CatalogFolder);
            catalog = ScriptableObject.CreateInstance<PropCatalog>();
            AssetDatabase.CreateAsset(catalog, path);

            var so = new SerializedObject(catalog);
            SerializedProperty props = so.FindProperty("props");
            props.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty e = props.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").stringValue = entries[i].id;
                e.FindPropertyRelative("displayName").stringValue = entries[i].displayName;
                e.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
                e.FindPropertyRelative("icon").objectReferenceValue = null;
                e.FindPropertyRelative("boundsMode").enumValueIndex = (int)PropBoundsMode.AutoFromColliders;
                e.FindPropertyRelative("manualCenter").vector3Value = Vector3.zero;
                e.FindPropertyRelative("manualHalfExtents").vector3Value = new Vector3(0.5f, 0.5f, 0.5f);
                e.FindPropertyRelative("prewarmCount").intValue = 2;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static GameObject GetOrCreatePropPrefab(string name, PrimitiveType type, Vector3 scale, float mass, int layer, Material mat)
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            EnsureFolder(PrefabFolder);

            GameObject go = GameObject.CreatePrimitive(type); // Box/Sphere/Capsule collider only
            go.name = name;
            go.layer = layer;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Appends an entry with this type id if the catalog does not have one (user entries are kept).</summary>
        private static void EnsureCatalogEntry(PropCatalog catalog, string id, string displayName, GameObject prefab)
        {
            var so = new SerializedObject(catalog);
            SerializedProperty props = so.FindProperty("props");
            for (int i = 0; i < props.arraySize; i++)
            {
                if (props.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id) return;
            }

            int index = props.arraySize;
            props.arraySize = index + 1;
            SerializedProperty e = props.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("id").stringValue = id;
            e.FindPropertyRelative("displayName").stringValue = displayName;
            e.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            e.FindPropertyRelative("icon").objectReferenceValue = null;
            e.FindPropertyRelative("boundsMode").enumValueIndex = (int)PropBoundsMode.AutoFromColliders;
            e.FindPropertyRelative("manualCenter").vector3Value = Vector3.zero;
            e.FindPropertyRelative("manualHalfExtents").vector3Value = new Vector3(0.5f, 0.5f, 0.5f);
            e.FindPropertyRelative("prewarmCount").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Seat: unscaled root (Rigidbody + Seat) with box colliders on children (base + back) and an unscaled SitPoint,
        /// so the parented player is never scaled. Existing prefab is kept.
        /// </summary>
        private static GameObject GetOrCreateSeatPrefab(int propsLayer)
        {
            string path = $"{PrefabFolder}/Prop_Seat.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            EnsureFolder(PrefabFolder);
            Material mat = GetOrCreateMaterial("Prop_Seat", new Color(0.2f, 0.3f, 0.6f));

            var root = new GameObject("Prop_Seat") { layer = propsLayer };
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 15f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            GameObject seatBase = GameObject.CreatePrimitive(PrimitiveType.Cube); // BoxCollider joins the compound
            seatBase.name = "Base";
            seatBase.layer = propsLayer;
            seatBase.transform.SetParent(root.transform, false);
            seatBase.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f);
            if (mat != null) seatBase.GetComponent<MeshRenderer>().sharedMaterial = mat;

            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back";
            back.layer = propsLayer;
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0f, 0.45f, -0.33f);
            back.transform.localScale = new Vector3(0.8f, 0.6f, 0.14f);
            if (mat != null) back.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var sitPoint = new GameObject("SitPoint") { layer = propsLayer };
            sitPoint.transform.SetParent(root.transform, false);
            sitPoint.transform.localPosition = new Vector3(0f, 0.15f, 0f);

            Seat seat = root.AddComponent<Seat>();
            SetRefs(seat, ("sitPoint", sitPoint.transform));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void FillMissingTypeIds(PropCatalog catalog)
        {
            var so = new SerializedObject(catalog);
            SerializedProperty props = so.FindProperty("props");
            bool changed = false;
            for (int i = 0; i < props.arraySize; i++)
            {
                SerializedProperty e = props.GetArrayElementAtIndex(i);
                SerializedProperty id = e.FindPropertyRelative("id");
                if (!string.IsNullOrWhiteSpace(id.stringValue)) continue;

                var prefab = e.FindPropertyRelative("prefab").objectReferenceValue as GameObject;
                string baseName = prefab != null ? prefab.name : $"prop{i}";
                if (baseName.StartsWith("Prop_")) baseName = baseName.Substring(5);
                id.stringValue = baseName.ToLowerInvariant();
                changed = true;
            }
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            Debug.Log("[SimpleBox] PropCatalog: empty type ids filled from prefab names. Check them in the catalog asset.", catalog);
        }

        private static void SpawnProp(GameObject prefab, Transform parent, Vector3 pos, EntityRegistry registry, string typeId)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = pos;
            SandboxEntity entity = go.GetComponent<SandboxEntity>(); // present when the prefab has NetworkEntity
            if (entity == null) entity = go.AddComponent<SandboxEntity>();
            var so = new SerializedObject(entity);
            so.FindProperty("sceneRegistry").objectReferenceValue = registry;
            so.FindProperty("sceneTypeId").stringValue = typeId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ UI

        private static MobileInputController BuildUI(out PropHud hud, out ToolHud toolHud, out ToastHud toastHud,
            out Graphic crosshairGraphic, out Text thrustLabel, out ContextHud contextHud, out ToolSettingsPanel settingsPanel)
        {
            var root = new GameObject("MobileUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(root, "Create MobileUI");
            root.layer = UILayer;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Every HUD element lives inside the safe area (notches, cut-outs, gesture bars).
            RectTransform safeArea = CreateRect("SafeArea", root.transform);
            Stretch(safeArea, Vector2.zero, Vector2.one);
            safeArea.gameObject.AddComponent<SafeAreaFitter>();
            Transform ui = safeArea;

            // Order matters: LookZone first (behind), buttons last (on top).
            RectTransform look = CreateRect("LookZone", ui);
            Stretch(look, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            AddInvisibleHitArea(look);
            TouchLookZone lookZone = look.gameObject.AddComponent<TouchLookZone>();

            RectTransform joyZone = CreateRect("JoystickZone", ui);
            Stretch(joyZone, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            AddInvisibleHitArea(joyZone);
            VirtualJoystick joystick = joyZone.gameObject.AddComponent<VirtualJoystick>();

            RectTransform bg = CreateRect("JoystickBackground", joyZone);
            Place(bg, new Vector2(0.3f, 0.3f), Vector2.zero, new Vector2(260f, 260f));
            AddImage(bg, circle, new Color(1f, 1f, 1f, 0.25f), false);

            RectTransform handle = CreateRect("JoystickHandle", bg);
            Place(handle, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            AddImage(handle, circle, new Color(1f, 1f, 1f, 0.6f), false);

            SetRefs(joystick, ("background", bg), ("handle", handle));

            RectTransform crosshair = CreateRect("Crosshair", ui);
            Place(crosshair, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            crosshairGraphic = AddImage(crosshair, circle, new Color(1f, 1f, 1f, 0.9f), false);

            // Prop HUD (top-right): icon, selected prop name, "active/max" counter. Not a raycast target.
            RectTransform panel = CreateRect("PropPanel", ui);
            panel.anchorMin = new Vector2(1f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.sizeDelta = new Vector2(460f, 110f);
            panel.anchoredPosition = new Vector2(-30f, -30f);
            AddImage(panel, AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"), new Color(0f, 0f, 0f, 0.35f), false)
                .type = Image.Type.Sliced;

            RectTransform iconRt = CreateRect("PropIcon", panel);
            Place(iconRt, new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(84f, 84f));
            Image icon = AddImage(iconRt, null, Color.white, false);
            icon.preserveAspect = true;
            icon.enabled = false;

            Text propName = CreateLabel("PropName", panel, font, 34, TextAnchor.MiddleLeft);
            Stretch(propName.rectTransform, Vector2.zero, Vector2.one);
            propName.rectTransform.offsetMin = new Vector2(115f, 0f);
            propName.rectTransform.offsetMax = new Vector2(-130f, 0f);
            propName.text = "—";

            Text counter = CreateLabel("PropCounter", panel, font, 34, TextAnchor.MiddleRight);
            Stretch(counter.rectTransform, Vector2.zero, Vector2.one);
            counter.rectTransform.offsetMin = new Vector2(320f, 0f);
            counter.rectTransform.offsetMax = new Vector2(-20f, 0f);
            counter.text = "0/30";

            hud = panel.gameObject.AddComponent<PropHud>();
            SetRefs(hud, ("nameLabel", propName), ("counterLabel", counter), ("iconImage", icon));

            // Tool HUD (top-center): active tool name. Not a raycast target.
            RectTransform toolPanel = CreateRect("ToolPanel", ui);
            toolPanel.anchorMin = new Vector2(0.5f, 1f);
            toolPanel.anchorMax = new Vector2(0.5f, 1f);
            toolPanel.pivot = new Vector2(0.5f, 1f);
            toolPanel.sizeDelta = new Vector2(760f, 120f);
            toolPanel.anchoredPosition = new Vector2(0f, -30f);
            AddImage(toolPanel, AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"), new Color(0f, 0f, 0f, 0.35f), false)
                .type = Image.Type.Sliced;

            Text toolName = CreateLabel("ToolName", toolPanel, font, 34, TextAnchor.MiddleCenter);
            Stretch(toolName.rectTransform, new Vector2(0f, 0.5f), Vector2.one);
            toolName.text = "—";

            Text toolHint = CreateLabel("ToolHint", toolPanel, font, 22, TextAnchor.MiddleCenter);
            Stretch(toolHint.rectTransform, Vector2.zero, new Vector2(1f, 0.5f));
            toolHint.fontStyle = FontStyle.Normal;
            toolHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            toolHint.text = string.Empty;

            toolHud = toolPanel.gameObject.AddComponent<ToolHud>();
            SetRefs(toolHud, ("toolLabel", toolName), ("hintLabel", toolHint));

            // Toast (below the tool panel): Undo / Clear / Thrust messages. Not a raycast target.
            RectTransform toastRt = CreateRect("Toast", ui);
            Place(toastRt, new Vector2(0.5f, 1f), new Vector2(0f, -320f), new Vector2(900f, 60f)); // below the top-right button row
            Text toastText = CreateLabel("ToastText", toastRt, font, 30, TextAnchor.MiddleCenter);
            Stretch(toastText.rectTransform, Vector2.zero, Vector2.one);
            toastText.text = string.Empty;
            toastHud = toastRt.gameObject.AddComponent<ToastHud>();
            SetRefs(toastHud, ("label", toastText));

            // Buttons last = drawn above LookZone.
            Vector2 bottomRight = new Vector2(1f, 0f);
            Vector2 topRight = new Vector2(1f, 1f);
            // Right thumb cluster: USE (main action) largest, JUMP and ALT next to it.
            TouchButton jump = CreateButton("JumpButton", "JUMP", ui, bottomRight, new Vector2(-150f, 150f), 170f, circle, font);
            TouchButton useTool = CreateButton("UseToolButton", "USE", ui, bottomRight, new Vector2(-380f, 180f), 200f, circle, font);
            TouchButton altTool = CreateButton("AltToolButton", "ALT", ui, bottomRight, new Vector2(-150f, 370f), 160f, circle, font);
            TouchButton spawn = CreateButton("SpawnButton", "SPAWN", ui, bottomRight, new Vector2(-400f, 410f), 170f, circle, font);
            TouchButton nextProp = CreateButton("NextPropButton", "NEXT", ui, topRight, new Vector2(-100f, -220f), 140f, circle, font);
            TouchButton nextTool = CreateButton("NextToolButton", "TOOL", ui, topRight, new Vector2(-260f, -220f), 140f, circle, font);
            TouchButton thrust = CreateButton("ThrustButton", "THRUST", ui, topRight, new Vector2(-420f, -220f), 140f, circle, font);
            thrustLabel = thrust.transform.Find("Label").GetComponent<Text>();
            thrustLabel.fontSize = 24;

            // Session commands: top-left corner (above the joystick zone, away from the thumb).
            Vector2 topLeft = new Vector2(0f, 1f);
            TouchButton undo = CreateButton("UndoButton", "UNDO", ui, topLeft, new Vector2(100f, -100f), 130f, circle, font);
            TouchButton clear = CreateButton("ClearButton", "CLEAR", ui, topLeft, new Vector2(250f, -100f), 130f, circle, font);
            undo.transform.Find("Label").GetComponent<Text>().fontSize = 26;
            clear.transform.Find("Label").GetComponent<Text>().fontSize = 26;

            // Flight: FLY toggle next to UNDO/CLEAR, DOWN above the right cluster (shown only while flying).
            TouchButton fly = CreateButton("FlyButton", "FLY", ui, topLeft, new Vector2(400f, -100f), 130f, circle, font);
            fly.transform.Find("Label").GetComponent<Text>().fontSize = 26;
            TouchButton down = CreateButton("DownButton", "DOWN", ui, bottomRight, new Vector2(-150f, 560f), 150f, circle, font);
            down.transform.Find("Label").GetComponent<Text>().fontSize = 26;

            // Held prop: ROTATE (hold) and the distance strip (shown only while holding).
            TouchButton rotate = CreateButton("RotateButton", "ROTATE", ui, bottomRight, new Vector2(-610f, 200f), 160f, circle, font);
            rotate.transform.Find("Label").GetComponent<Text>().fontSize = 24;
            TouchLookZone distance = CreateDistanceStrip(ui, font);

            // Tool options: SET button + panel (topmost, blocks touches behind it while open).
            TouchButton settingsOpen = CreateButton("SettingsButton", "SET", ui, topRight, new Vector2(-580f, -220f), 140f, circle, font);
            settingsPanel = CreateSettingsPanel(ui, circle, font, settingsOpen);

            contextHud = root.AddComponent<ContextHud>();
            var contextSo = new SerializedObject(contextHud);
            SerializedProperty holdControls = contextSo.FindProperty("holdControls");
            holdControls.arraySize = 2;
            holdControls.GetArrayElementAtIndex(0).objectReferenceValue = rotate.gameObject;
            holdControls.GetArrayElementAtIndex(1).objectReferenceValue = distance.gameObject;
            SerializedProperty flightControls = contextSo.FindProperty("flightControls");
            flightControls.arraySize = 1;
            flightControls.GetArrayElementAtIndex(0).objectReferenceValue = down.gameObject;
            contextSo.FindProperty("jumpLabel").objectReferenceValue = jump.transform.Find("Label").GetComponent<Text>();
            contextSo.FindProperty("flyIndicator").objectReferenceValue = fly.GetComponent<Image>();
            contextSo.ApplyModifiedPropertiesWithoutUndo();

            MobileInputController input = root.AddComponent<MobileInputController>();
            SetRefs(input,
                ("joystick", joystick),
                ("lookZone", lookZone),
                ("jumpButton", jump),
                ("spawnButton", spawn),
                ("nextPropButton", nextProp),
                ("useToolButton", useTool),
                ("nextToolButton", nextTool),
                ("altToolButton", altTool),
                ("thrustToggleButton", thrust),
                ("undoButton", undo),
                ("clearButton", clear),
                ("rotateButton", rotate),
                ("distanceZone", distance),
                ("flyButton", fly),
                ("downButton", down));

            // SIT / EXIT: bottom center, shown only when aiming at a seat or seated (ContextHud).
            TouchButton sit = CreateButton("SitButton", "SIT", ui, new Vector2(0.5f, 0f), new Vector2(0f, 140f), 150f, circle, font);
            Text sitLabel = sit.transform.Find("Label").GetComponent<Text>();
            sitLabel.fontSize = 28;
            SetRefs(input, ("sitButton", sit));
            SetRefs(contextHud, ("sitButton", sit.gameObject), ("sitLabel", sitLabel));

            // Contraption bindings 1-4: column at the left edge (above the joystick zone), held while driving.
            var inputSo = new SerializedObject(input);
            SerializedProperty bindingButtons = inputSo.FindProperty("bindingButtons");
            bindingButtons.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                TouchButton b = CreateButton($"BindingButton{i + 1}", (i + 1).ToString(), ui, new Vector2(0f, 0.5f),
                    new Vector2(85f, 225f - i * 140f), 120f, circle, font);
                bindingButtons.GetArrayElementAtIndex(i).objectReferenceValue = b;
            }
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            return input;
        }

        /// <summary>Vertical drag strip for the held prop's distance (TouchLookZone reused as a 1D drag source).</summary>
        private static TouchLookZone CreateDistanceStrip(Transform parent, Font font)
        {
            RectTransform strip = CreateRect("DistanceStrip", parent);
            Place(strip, new Vector2(1f, 0.5f), new Vector2(-620f, 50f), new Vector2(110f, 360f)); // between ROTATE and SET
            AddImage(strip, AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"), new Color(0f, 0f, 0f, 0.3f), true)
                .type = Image.Type.Sliced;
            TouchLookZone zone = strip.gameObject.AddComponent<TouchLookZone>();

            Text far = CreateLabel("Far", strip, font, 44, TextAnchor.UpperCenter);
            Stretch(far.rectTransform, Vector2.zero, Vector2.one);
            far.text = "+";
            Text near = CreateLabel("Near", strip, font, 44, TextAnchor.LowerCenter);
            Stretch(near.rectTransform, Vector2.zero, Vector2.one);
            near.text = "-";
            return zone;
        }

        private const int SettingRowCount = 4;

        private static ToolSettingsPanel CreateSettingsPanel(Transform parent, Sprite circle, Font font, TouchButton openButton)
        {
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            RectTransform panel = CreateRect("ToolSettingsPanel", parent);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(780f, 140f + SettingRowCount * 100f));
            AddImage(panel, rounded, new Color(0f, 0f, 0f, 0.8f), true).type = Image.Type.Sliced; // raycast target: blocks touches behind

            Text title = CreateLabel("Title", panel, font, 36, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-200f, 90f);
            title.rectTransform.anchoredPosition = new Vector2(-70f, -10f);
            title.text = "—";

            TouchButton close = CreateButton("CloseButton", "OK", panel, new Vector2(1f, 1f), new Vector2(-70f, -55f), 90f, circle, font);

            // Lives on the always-active parent: it must run Update while the panel itself is hidden.
            ToolSettingsPanel component = parent.gameObject.AddComponent<ToolSettingsPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("openButton").objectReferenceValue = openButton;
            so.FindProperty("closeButton").objectReferenceValue = close;

            SerializedProperty rows = so.FindProperty("rows");
            rows.arraySize = SettingRowCount;
            for (int i = 0; i < SettingRowCount; i++)
            {
                RectTransform row = CreateRect($"Row{i}", panel);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(-40f, 90f);
                row.anchoredPosition = new Vector2(0f, -110f - i * 100f);

                Text label = CreateLabel("Label", row, font, 30, TextAnchor.MiddleLeft);
                Stretch(label.rectTransform, Vector2.zero, new Vector2(0.45f, 1f));
                label.fontStyle = FontStyle.Normal;

                TouchButton minus = CreateButton("Minus", "-", row, new Vector2(0.53f, 0.5f), Vector2.zero, 80f, circle, font);
                Text value = CreateLabel("Value", row, font, 30, TextAnchor.MiddleCenter);
                Stretch(value.rectTransform, new Vector2(0.6f, 0f), new Vector2(0.86f, 1f));
                TouchButton plus = CreateButton("Plus", "+", row, new Vector2(0.93f, 0.5f), Vector2.zero, 80f, circle, font);

                SerializedProperty e = rows.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
                e.FindPropertyRelative("label").objectReferenceValue = label;
                e.FindPropertyRelative("value").objectReferenceValue = value;
                e.FindPropertyRelative("minus").objectReferenceValue = minus;
                e.FindPropertyRelative("plus").objectReferenceValue = plus;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.gameObject.SetActive(false);
            return component;
        }

        private static Text CreateLabel(string name, Transform parent, Font font, int size, TextAnchor alignment)
        {
            RectTransform rt = CreateRect(name, parent);
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static TouchButton CreateButton(string name, string label, Transform parent, Vector2 anchor, Vector2 position, float size, Sprite sprite, Font font)
        {
            RectTransform rt = CreateRect(name, parent);
            Place(rt, anchor, position, new Vector2(size, size));
            Image img = AddImage(rt, sprite, new Color(0f, 0f, 0f, 0.35f), true);

            TouchButton button = rt.gameObject.AddComponent<TouchButton>();
            var so = new SerializedObject(button);
            so.FindProperty("targetGraphic").objectReferenceValue = img;
            so.FindProperty("normalColor").colorValue = new Color(0f, 0f, 0f, 0.35f);
            so.FindProperty("pressedColor").colorValue = new Color(0f, 0f, 0f, 0.65f);
            so.ApplyModifiedPropertiesWithoutUndo();

            RectTransform textRt = CreateRect("Label", rt);
            Stretch(textRt, Vector2.zero, Vector2.one);
            Text text = textRt.gameObject.AddComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 30;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 0.9f);
            text.raycastTarget = false;

            return button;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }

        private static Image AddImage(RectTransform rt, Sprite sprite, Color color, bool raycastTarget)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycastTarget;
            return img;
        }

        private static void AddInvisibleHitArea(RectTransform rt)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;
            rt.GetComponent<CanvasRenderer>().cullTransparentMesh = true; // no draw call, still receives raycasts
        }

        // ------------------------------------------------------------------ EventSystem

        private static void EnsureEventSystem(Scene scene)
        {
            EventSystem es = FindInScene<EventSystem>(scene);
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
                es = go.GetComponent<EventSystem>();
            }

#if ENABLE_INPUT_SYSTEM
            StandaloneInputModule legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Undo.DestroyObjectImmediate(legacy);

            InputSystemUIInputModule module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = Undo.AddComponent<InputSystemUIInputModule>(es.gameObject);
            if (module.actionsAsset == null) module.AssignDefaultActions();
#else
            if (es.GetComponent<StandaloneInputModule>() == null) Undo.AddComponent<StandaloneInputModule>(es.gameObject);
#endif
        }

        // ------------------------------------------------------------------ Player

        private static GameObject BuildPlayer(Scene scene, MobileInputController input, PropCatalog catalog, SystemsRefs sys, int playerLayer, int propsLayer)
        {
            var player = new GameObject("Player");
            Undo.RegisterCreatedObjectUndo(player, "Create Player");
            player.layer = playerLayer;
            player.transform.position = new Vector3(0f, 0.05f, -4f);

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.05f;
            cc.slopeLimit = 45f;

            var pivot = new GameObject("CameraPivot");
            pivot.layer = playerLayer;
            pivot.transform.SetParent(player.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            Camera cam = FindSceneCamera(scene);
            if (cam == null)
            {
                var camGo = new GameObject("PlayerCamera", typeof(Camera));
                Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }

            Undo.SetTransformParent(cam.transform, pivot.transform, "Parent Camera");
            Undo.RecordObject(cam.transform, "Reset Camera Transform");
            Undo.RecordObject(cam, "Configure Camera");
            Undo.RecordObject(cam.gameObject, "Rename Camera");
            cam.gameObject.name = "PlayerCamera";
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.transform.localScale = Vector3.one;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 70f;

            if (cam.GetComponent<UniversalAdditionalCameraData>() == null)
                Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
            if (cam.GetComponent<AudioListener>() == null)
                Undo.AddComponent<AudioListener>(cam.gameObject);

            PlayerController controller = player.AddComponent<PlayerController>();
            SetRefs(controller, ("input", input), ("cameraPivot", pivot.transform), ("actions", sys.Actions),
                ("cameraTransform", cam.transform));
            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("flightExitBlockMask").intValue = ~((1 << playerLayer) | (1 << UILayer) | (1 << 2));
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            PhysicsGun gun = player.AddComponent<PhysicsGun>();
            SetRefs(gun, ("aimOrigin", cam.transform), ("registry", sys.Registry), ("actions", sys.Actions));
            var so = new SerializedObject(gun);
            so.FindProperty("raycastMask").intValue = ~(1 << playerLayer);
            so.FindProperty("grabbableMask").intValue = 1 << propsLayer;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Environment = every layer except Player, UI and Ignore Raycast (Props included).
            int environmentAndProps = ~((1 << playerLayer) | (1 << UILayer) | (1 << 2));

            PropSpawner spawner = player.AddComponent<PropSpawner>();
            SetRefs(spawner,
                ("input", input),
                ("aimOrigin", cam.transform),
                ("catalog", catalog),
                ("actions", sys.Actions),
                ("registry", sys.Registry),
                ("quality", sys.Quality));
            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("placementMask").intValue = environmentAndProps;
            spawnerSo.FindProperty("overlapMask").intValue = environmentAndProps | (1 << playerLayer);
            spawnerSo.FindProperty("propLayers").intValue = 1 << propsLayer;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            return player;
        }

        // ------------------------------------------------------------------ Tools

        private static ToolManager BuildTools(GameObject player, MobileInputController input, Graphic crosshair,
            SystemsRefs sys, int playerLayer, int propsLayer)
        {
            PhysicsGun gun = player.GetComponent<PhysicsGun>();
            int environmentAndProps = ~((1 << playerLayer) | (1 << UILayer) | (1 << 2));
            int environmentOnly = environmentAndProps & ~(1 << propsLayer);
            Transform aim = gun != null ? new SerializedObject(gun).FindProperty("aimOrigin").objectReferenceValue as Transform : null;

            var toolsRoot = new GameObject("Tools");
            toolsRoot.layer = playerLayer;
            toolsRoot.transform.SetParent(player.transform, false);

            var physgunGo = new GameObject("Tool_Physgun");
            physgunGo.layer = playerLayer;
            physgunGo.transform.SetParent(toolsRoot.transform, false);
            PhysicsGunTool physgunTool = physgunGo.AddComponent<PhysicsGunTool>();
            SetRefs(physgunTool, ("physicsGun", gun), ("input", input));
            SetString(physgunTool, "displayName", "Физган");

            var removerGo = new GameObject("Tool_Remover");
            removerGo.layer = playerLayer;
            removerGo.transform.SetParent(toolsRoot.transform, false);
            RemoverTool remover = removerGo.AddComponent<RemoverTool>();
            AudioSource audio = removerGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            SetRefs(remover,
                ("aimOrigin", aim),
                ("actions", sys.Actions),
                ("crosshair", crosshair),
                ("removeEffect", CreateRemoveEffect()),
                ("audioSource", audio));
            SetString(remover, "displayName", "Удалитель");
            var so = new SerializedObject(remover);
            so.FindProperty("raycastMask").intValue = environmentAndProps;
            so.FindProperty("removableMask").intValue = 1 << propsLayer;
            so.ApplyModifiedPropertiesWithoutUndo();

            var thrusterGo = new GameObject("Tool_Thruster");
            thrusterGo.layer = playerLayer;
            thrusterGo.transform.SetParent(toolsRoot.transform, false);
            ThrusterTool thrusterTool = thrusterGo.AddComponent<ThrusterTool>();
            SetRefs(thrusterTool,
                ("aimOrigin", aim),
                ("actions", sys.Actions));
            SetString(thrusterTool, "displayName", "Двигатель");
            var thrusterSo = new SerializedObject(thrusterTool);
            thrusterSo.FindProperty("raycastMask").intValue = environmentAndProps;
            thrusterSo.FindProperty("attachMask").intValue = 1 << propsLayer;
            thrusterSo.ApplyModifiedPropertiesWithoutUndo();

            // Weld.
            var weldGo = new GameObject("Tool_Weld");
            weldGo.layer = playerLayer;
            weldGo.transform.SetParent(toolsRoot.transform, false);
            WeldTool weldTool = weldGo.AddComponent<WeldTool>();
            SetRefs(weldTool,
                ("aimOrigin", aim),
                ("actions", sys.Actions),
                ("selectionMarker", CreateWeldMarker()));
            SetString(weldTool, "displayName", "Сварка");
            var weldSo = new SerializedObject(weldTool);
            weldSo.FindProperty("raycastMask").intValue = environmentAndProps;
            weldSo.FindProperty("weldableMask").intValue = 1 << propsLayer;
            weldSo.FindProperty("worldMask").intValue = environmentOnly;
            weldSo.ApplyModifiedPropertiesWithoutUndo();

            // Rope / Elastic.
            var ropeGo = new GameObject("Tool_Rope");
            ropeGo.layer = playerLayer;
            ropeGo.transform.SetParent(toolsRoot.transform, false);
            RopeTool ropeTool = ropeGo.AddComponent<RopeTool>();
            SetRefs(ropeTool, ("aimOrigin", aim), ("actions", sys.Actions), ("selectionMarker", CreateWeldMarker()));
            SetString(ropeTool, "displayName", "Верёвка");
            SetPairMasks(ropeTool, environmentAndProps, 1 << propsLayer, environmentOnly);

            // Axis / wheel with motor.
            var axisGo = new GameObject("Tool_Axis");
            axisGo.layer = playerLayer;
            axisGo.transform.SetParent(toolsRoot.transform, false);
            AxisTool axisTool = axisGo.AddComponent<AxisTool>();
            SetRefs(axisTool, ("aimOrigin", aim), ("actions", sys.Actions), ("selectionMarker", CreateWeldMarker()));
            SetString(axisTool, "displayName", "Ось / колесо");
            SetPairMasks(axisTool, environmentAndProps, 1 << propsLayer, environmentOnly);

            // Piston.
            // Detonator.
            var detonatorGo = new GameObject("Tool_Detonator");
            detonatorGo.layer = playerLayer;
            detonatorGo.transform.SetParent(toolsRoot.transform, false);
            DetonatorTool detonatorTool = detonatorGo.AddComponent<DetonatorTool>();
            SetRefs(detonatorTool, ("aimOrigin", aim), ("actions", sys.Actions));
            SetString(detonatorTool, "displayName", "Детонатор");
            var detSo = new SerializedObject(detonatorTool);
            detSo.FindProperty("raycastMask").intValue = environmentAndProps;
            detSo.FindProperty("targetMask").intValue = 1 << propsLayer;
            detSo.ApplyModifiedPropertiesWithoutUndo();

            // Paint.
            var colorGo = new GameObject("Tool_Color");
            colorGo.layer = playerLayer;
            colorGo.transform.SetParent(toolsRoot.transform, false);
            ColorTool colorTool = colorGo.AddComponent<ColorTool>();
            SetRefs(colorTool, ("aimOrigin", aim), ("actions", sys.Actions), ("swatch", crosshair));
            SetString(colorTool, "displayName", "Цвет");
            var colorSo = new SerializedObject(colorTool);
            colorSo.FindProperty("raycastMask").intValue = environmentAndProps;
            colorSo.FindProperty("targetMask").intValue = 1 << propsLayer;
            colorSo.ApplyModifiedPropertiesWithoutUndo();

            // Duplicator.
            var duplicatorGo = new GameObject("Tool_Duplicator");
            duplicatorGo.layer = playerLayer;
            duplicatorGo.transform.SetParent(toolsRoot.transform, false);
            DuplicatorTool duplicatorTool = duplicatorGo.AddComponent<DuplicatorTool>();
            SetRefs(duplicatorTool, ("aimOrigin", aim), ("actions", sys.Actions));
            SetString(duplicatorTool, "displayName", "Дубликатор");
            var dupSo = new SerializedObject(duplicatorTool);
            dupSo.FindProperty("raycastMask").intValue = environmentAndProps;
            dupSo.FindProperty("targetMask").intValue = 1 << propsLayer;
            dupSo.ApplyModifiedPropertiesWithoutUndo();

            var hydraulicGo = new GameObject("Tool_Hydraulic");
            hydraulicGo.layer = playerLayer;
            hydraulicGo.transform.SetParent(toolsRoot.transform, false);
            HydraulicTool hydraulicTool = hydraulicGo.AddComponent<HydraulicTool>();
            SetRefs(hydraulicTool, ("aimOrigin", aim), ("actions", sys.Actions), ("selectionMarker", CreateWeldMarker()));
            SetString(hydraulicTool, "displayName", "Поршень");
            SetPairMasks(hydraulicTool, environmentAndProps, 1 << propsLayer, environmentOnly);

            ToolManager manager = player.AddComponent<ToolManager>();
            var managerSo = new SerializedObject(manager);
            managerSo.FindProperty("input").objectReferenceValue = input;
            SerializedProperty tools = managerSo.FindProperty("tools");
            tools.arraySize = 10;
            tools.GetArrayElementAtIndex(6).objectReferenceValue = hydraulicTool;
            tools.GetArrayElementAtIndex(7).objectReferenceValue = duplicatorTool;
            tools.GetArrayElementAtIndex(8).objectReferenceValue = colorTool;
            tools.GetArrayElementAtIndex(9).objectReferenceValue = detonatorTool;
            tools.GetArrayElementAtIndex(0).objectReferenceValue = physgunTool;
            tools.GetArrayElementAtIndex(1).objectReferenceValue = remover;
            tools.GetArrayElementAtIndex(2).objectReferenceValue = thrusterTool;
            tools.GetArrayElementAtIndex(3).objectReferenceValue = weldTool;
            tools.GetArrayElementAtIndex(4).objectReferenceValue = ropeTool;
            tools.GetArrayElementAtIndex(5).objectReferenceValue = axisTool;
            managerSo.FindProperty("startToolIndex").intValue = 0;
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            return manager;
        }

        private static ParticleSystem CreateRemoveEffect()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
            if (mat == null) return null; // effect is optional

            var go = new GameObject("RemoveEffect");
            go.transform.SetParent(GetOrCreateEffectsRoot(), false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 0.45f;
            main.startSpeed = 3.5f;
            main.startSize = 0.12f;
            main.startColor = new Color(1f, 0.45f, 0.25f);
            main.gravityModifier = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        private static Transform GetOrCreateEffectsRoot()
        {
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == "Effects") return go.transform;
            }
            var root = new GameObject("Effects");
            Undo.RegisterCreatedObjectUndo(root, "Create Effects");
            return root.transform;
        }

        private static Transform CreateWeldMarker()
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "WeldMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>()); // must not be hit by any ray
            marker.transform.SetParent(GetOrCreateEffectsRoot(), false);
            marker.transform.localScale = Vector3.one * 0.12f;
            Material mat = GetOrCreateUnlitMaterial("WeldMarker", new Color(0.2f, 0.9f, 1f));
            if (mat != null) marker.GetComponent<MeshRenderer>().sharedMaterial = mat;
            marker.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.SetActive(false);
            return marker.transform;
        }

        /// <summary>
        /// Thruster prefab: root (PropThruster) + Body (cylinder along +Z) + Flame (inactive, unlit). No colliders.
        /// Existing prefab is kept (user edits preserved).
        /// </summary>
        private static PropThruster GetOrCreateThrusterPrefab()
        {
            string path = $"{PrefabFolder}/Thruster.prefab";
            PropThruster existing = AssetDatabase.LoadAssetAtPath<PropThruster>(path);
            if (existing != null) return existing;

            EnsureFolder(PrefabFolder);

            var root = new GameObject("Thruster");
            PropThruster thruster = root.AddComponent<PropThruster>();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis Y -> Z (nozzle direction)
            body.transform.localScale = new Vector3(0.25f, 0.12f, 0.25f);  // 0.24 m long
            body.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            Material bodyMat = GetOrCreateMaterial("Thruster", new Color(0.3f, 0.32f, 0.35f));
            if (bodyMat != null) body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "Flame";
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.transform.SetParent(root.transform, false);
            flame.transform.localScale = new Vector3(0.16f, 0.16f, 0.5f);
            flame.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            Material flameMat = GetOrCreateUnlitMaterial("ThrusterFlame", new Color(1f, 0.55f, 0.15f));
            if (flameMat != null) flame.GetComponent<MeshRenderer>().sharedMaterial = flameMat;
            flame.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flame.SetActive(false);

            var so = new SerializedObject(thruster);
            so.FindProperty("flame").objectReferenceValue = flame;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<PropThruster>();
        }

        private static Material GetOrCreateUnlitMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            EnsureFolder(MaterialFolder);
            mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Line material that multiplies by vertex color (LineRenderer start/end colors): URP Particles/Unlit.</summary>
        private static Material GetOrCreateCableMaterial()
        {
            string path = $"{MaterialFolder}/Cable.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            EnsureFolder(MaterialFolder);
            mat = new Material(shader) { name = "Cable" };
            mat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>raycastMask / targetMask / worldMask of the two-point constraint tools (Rope, Axis).</summary>
        private static void SetPairMasks(Object tool, int raycast, int target, int world)
        {
            var so = new SerializedObject(tool);
            so.FindProperty("raycastMask").intValue = raycast;
            so.FindProperty("targetMask").intValue = target;
            so.FindProperty("worldMask").intValue = world;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Camera FindSceneCamera(Scene scene)
        {
            Camera main = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                {
                    if (c.CompareTag("MainCamera")) return c;
                    if (main == null) main = c;
                }
            }
            return main;
        }

        // ------------------------------------------------------------------ Helpers

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        private static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach ((string field, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(field);
                if (p == null)
                {
                    Debug.LogError($"[SimpleBox] Поле '{field}' не найдено в {target.GetType().Name}.", target);
                    continue;
                }
                p.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            EnsureFolder(MaterialFolder);
            mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
