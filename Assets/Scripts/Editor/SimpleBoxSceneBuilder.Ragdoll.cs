using SimpleBox.Gameplay;
using SimpleBox.World;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace SimpleBox.EditorTools
{
    /// <summary>
    /// Stage 12: procedural ragdoll prop. Root = pelvis (Rigidbody, SandboxEntity, Ragdoll, ImpactSound; the network
    /// builder later adds NetworkObject / NetworkTransform / NetworkRigidbody / NetworkEntity to it). Bones are unscaled
    /// child bodies with primitive colliders, a CharacterJoint to the parent bone, EntityPart, RagdollBone and a
    /// local-space server NetworkTransform; meshes are separate scaled children without colliders.
    /// Standing pose with arms down (compact spawn box). Existing prefab is kept.
    /// </summary>
    public static partial class SimpleBoxSceneBuilder
    {
        private const string RagdollTypeId = "ragdoll";
        private const int RagdollMaxPerOwner = 3;

        private struct JointLimits
        {
            public Vector3 Axis;       // twist axis (bend axis for elbows/knees), bone-local
            public float LowTwist;
            public float HighTwist;
            public float Swing1;       // around the swing axis (forward): sideways
            public float Swing2;       // around the third axis: rotation along the limb

            public JointLimits(Vector3 axis, float lowTwist, float highTwist, float swing1, float swing2)
            {
                Axis = axis;
                LowTwist = lowTwist;
                HighTwist = highTwist;
                Swing1 = swing1;
                Swing2 = swing2;
            }
        }

        private static void AddRagdollProp(PropCatalog catalog, int propsLayer)
        {
            GameObject prefab = GetOrCreateRagdollPrefab(propsLayer);
            if (prefab == null) return;
            EnsurePropNetworkComponents(prefab);
            EnsureCatalogEntry(catalog, RagdollTypeId, "Регдолл", prefab);
            EnsureMaxPerOwner(catalog, RagdollTypeId, RagdollMaxPerOwner);
        }

        /// <summary>Sets maxPerOwner of the entry if it is still 0 (a value set by the user is kept).</summary>
        private static void EnsureMaxPerOwner(PropCatalog catalog, string id, int max)
        {
            var so = new SerializedObject(catalog);
            SerializedProperty props = so.FindProperty("props");
            for (int i = 0; i < props.arraySize; i++)
            {
                SerializedProperty e = props.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("id").stringValue != id) continue;
                SerializedProperty limit = e.FindPropertyRelative("maxPerOwner");
                if (limit.intValue != 0) return;
                limit.intValue = max;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
                return;
            }
        }

        private static GameObject GetOrCreateRagdollPrefab(int layer)
        {
            string path = $"{PrefabFolder}/Prop_Ragdoll.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            EnsureFolder(PrefabFolder);
            Material skin = GetOrCreateMaterial("Prop_Ragdoll", new Color(0.86f, 0.68f, 0.52f));
            Material cloth = GetOrCreateMaterial("Prop_RagdollCloth", new Color(0.25f, 0.35f, 0.55f));

            // ---- Root: pelvis
            var root = new GameObject("Prop_Ragdoll") { layer = layer };
            Rigidbody pelvisBody = AddBoneBody(root, 8f);
            var pelvisCol = root.AddComponent<BoxCollider>();
            pelvisCol.size = new Vector3(0.32f, 0.2f, 0.18f);
            AddVisual(root.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.32f, 0.2f, 0.18f), cloth, layer);

            // NetworkObject first: the bones carry NetworkTransforms (rest of the network set comes from EnsurePropNetworkComponents).
            Unity.Netcode.NetworkObject networkObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            networkObject.AutoObjectParentSync = false;

            SandboxEntity entity = root.AddComponent<SandboxEntity>();
            root.AddComponent<Ragdoll>();
            ImpactSound sound = root.AddComponent<ImpactSound>();

            // ---- Spine / head
            GameObject chest = AddBone("Chest", root.transform, new Vector3(0f, 0.1f, 0f), 10f, pelvisBody,
                new JointLimits(Vector3.right, -35f, 15f, 15f, 20f), entity, sound, layer);
            AddBox(chest, new Vector3(0f, 0.22f, 0f), new Vector3(0.38f, 0.42f, 0.2f), cloth, layer);

            GameObject head = AddBone("Head", chest.transform, new Vector3(0f, 0.44f, 0f), 4f, chest.GetComponent<Rigidbody>(),
                new JointLimits(Vector3.right, -40f, 30f, 30f, 40f), entity, sound, layer);
            var headCol = head.AddComponent<SphereCollider>();
            headCol.center = new Vector3(0f, 0.13f, 0f);
            headCol.radius = 0.12f;
            AddVisual(head.transform, PrimitiveType.Sphere, headCol.center, Vector3.one * 0.24f, skin, layer);

            // ---- Arms (hanging down). Positive twist about +X = limb swings forward.
            for (int side = -1; side <= 1; side += 2)
            {
                string s = side < 0 ? "L" : "R";
                GameObject upperArm = AddBone($"UpperArm{s}", chest.transform, new Vector3(0.25f * side, 0.38f, 0f), 2f,
                    chest.GetComponent<Rigidbody>(), new JointLimits(Vector3.right, -45f, 140f, 70f, 30f), entity, sound, layer);
                AddCapsule(upperArm, 0.32f, 0.06f, cloth, layer);

                GameObject lowerArm = AddBone($"LowerArm{s}", upperArm.transform, new Vector3(0f, -0.32f, 0f), 1.5f,
                    upperArm.GetComponent<Rigidbody>(), new JointLimits(Vector3.right, 0f, 120f, 5f, 5f), entity, sound, layer);
                AddCapsule(lowerArm, 0.3f, 0.05f, skin, layer);

                GameObject upperLeg = AddBone($"UpperLeg{s}", root.transform, new Vector3(0.1f * side, -0.1f, 0f), 5f,
                    pelvisBody, new JointLimits(Vector3.right, -20f, 90f, 30f, 15f), entity, sound, layer);
                AddCapsule(upperLeg, 0.44f, 0.08f, cloth, layer);

                // Knee: shin bends back = negative twist about +X.
                GameObject lowerLeg = AddBone($"LowerLeg{s}", upperLeg.transform, new Vector3(0f, -0.44f, 0f), 3.5f,
                    upperLeg.GetComponent<Rigidbody>(), new JointLimits(Vector3.right, -120f, 0f, 5f, 5f), entity, sound, layer);
                AddCapsule(lowerLeg, 0.44f, 0.065f, cloth, layer);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Rigidbody AddBoneBody(GameObject go, float mass)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            return rb;
        }

        private static GameObject AddBone(string name, Transform parent, Vector3 localPosition, float mass, Rigidbody connected,
            JointLimits limits, SandboxEntity entity, ImpactSound sound, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            AddBoneBody(go, mass);

            CharacterJoint joint = go.AddComponent<CharacterJoint>();
            joint.connectedBody = connected;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;
            joint.axis = limits.Axis;
            joint.swingAxis = Vector3.forward;
            joint.lowTwistLimit = Limit(limits.LowTwist);
            joint.highTwistLimit = Limit(limits.HighTwist);
            joint.swing1Limit = Limit(limits.Swing1);
            joint.swing2Limit = Limit(limits.Swing2);
            joint.enableProjection = true; // keeps the chain from stretching under physgun / explosions
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 10f;
            joint.enablePreprocessing = false; // more stable with conflicting constraints (welds / ropes on the pelvis)
            joint.enableCollision = false;

            EntityPart part = go.AddComponent<EntityPart>();
            part.SetEntity(entity);
            RagdollBone bone = go.AddComponent<RagdollBone>();
            bone.SetRootSound(sound);

            // Client copies follow the host bone by bone (pose relative to the parent bone).
            NetworkTransform nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            nt.InLocalSpace = true;
            nt.SyncScaleX = false;
            nt.SyncScaleY = false;
            nt.SyncScaleZ = false;
            return go;
        }

        private static SoftJointLimit Limit(float degrees) => new SoftJointLimit { limit = degrees };

        private static void AddBox(GameObject bone, Vector3 center, Vector3 size, Material mat, int layer)
        {
            var col = bone.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;
            AddVisual(bone.transform, PrimitiveType.Cube, center, size, mat, layer);
        }

        /// <summary>Capsule hanging down from the joint (bone origin at the top end).</summary>
        private static void AddCapsule(GameObject bone, float height, float radius, Material mat, int layer)
        {
            var col = bone.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.height = height;
            col.radius = radius;
            col.center = new Vector3(0f, -height * 0.5f, 0f);
            // Unity capsule mesh: height 2, radius 0.5.
            AddVisual(bone.transform, PrimitiveType.Capsule, col.center, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat, layer);
        }

        /// <summary>Mesh-only child (collider removed): scaling it never scales a bone.</summary>
        private static void AddVisual(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material mat, int layer)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = "Mesh";
            visual.layer = layer;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localScale = scale;
            if (mat != null) visual.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
