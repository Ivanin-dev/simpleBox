using SimpleBox.Gameplay;
using SimpleBox.World;
using UnityEditor;
using UnityEngine;

namespace SimpleBox.EditorTools
{
    /// <summary>Stage 11: explosive barrel, impact sounds on prop prefabs, explosion particle effect.</summary>
    public static partial class SimpleBoxSceneBuilder
    {
        /// <summary>Adds T to the prefab asset if missing (prefab contents edited and saved).</summary>
        private static void EnsurePrefabComponent<T>(GameObject prefabAsset) where T : Component
        {
            if (prefabAsset == null || prefabAsset.GetComponent<T>() != null) return;
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path)) return;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<T>() == null) root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddExplosiveProp(PropCatalog catalog, int propsLayer)
        {
            GameObject prefab = GetOrCreatePropPrefab("Prop_Explosive", PrimitiveType.Capsule, new Vector3(0.7f, 0.7f, 0.7f), 10f,
                propsLayer, GetOrCreateMaterial("Prop_Explosive", new Color(0.8f, 0.12f, 0.08f)));
            EnsurePropNetworkComponents(prefab);
            EnsurePrefabComponent<ExplosiveProp>(prefab);
            EnsureCatalogEntry(catalog, "explosive", "Взрывная бочка", prefab);
        }

        /// <summary>Every catalog prop gets an ImpactSound (collision sounds).</summary>
        private static void EnsureImpactSounds(PropCatalog catalog)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                PropDefinition def = catalog.Get(i);
                if (def != null && def.prefab != null) EnsurePrefabComponent<ImpactSound>(def.prefab);
            }
        }

        /// <summary>Fireball + smoke burst (URP ParticlesUnlit), world space, cloned into a pool by ExplosionFx.</summary>
        private static ParticleSystem CreateExplosionEffect()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
            if (mat == null) return null;

            var go = new GameObject("ExplosionEffect");
            go.transform.SetParent(GetOrCreateEffectsRoot(), false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.35f, 0.1f));
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 0.5f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }
    }
}
