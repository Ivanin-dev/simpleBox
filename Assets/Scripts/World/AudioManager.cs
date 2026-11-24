using UnityEngine;

namespace SimpleBox.World
{
    public enum SoundId
    {
        ImpactLight = 0,
        ImpactHeavy = 1,
        Explosion = 2
    }

    /// <summary>
    /// 3D one-shot sounds from a fixed pool of AudioSources (no Instantiate, no allocation while playing).
    /// Clips are synthesized at startup (decaying filtered noise), so the project needs no audio assets.
    /// Impacts are rate-limited (at most maxImpactsPerWindow per window) so a pile of 20 falling crates does not
    /// flood the mixer; explosions may steal the oldest source. Props get their ImpactSound wired here on registration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [Header("References (required)")]
        [SerializeField] private EntityRegistry registry;

        [Header("Pool")]
        [SerializeField, Range(4, 32)] private int voices = 12;
        [SerializeField, Min(5f)] private float maxDistance = 45f;

        [Header("Impacts")]
        [SerializeField, Range(1, 16)] private int maxImpactsPerWindow = 6;
        [SerializeField, Min(0.02f)] private float impactWindow = 0.1f;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float explosionVolume = 1f;

        private const int SampleRate = 22050;

        private AudioSource[] _sources;
        private float[] _startTimes;
        private AudioClip[] _clips;
        private int _next;
        private float _windowStart;
        private int _impactsInWindow;
        private bool _valid;
        private bool _subscribed;

        private void Awake()
        {
            if (registry == null)
            {
                Debug.LogError($"[{nameof(AudioManager)}] '{name}': Registry not assigned. Component disabled.", this);
                enabled = false;
                return;
            }

            _clips = new AudioClip[3];
            _clips[(int)SoundId.ImpactLight] = Synthesize("ImpactLight", 0.12f, 0.35f, 30f, 0.35f);
            _clips[(int)SoundId.ImpactHeavy] = Synthesize("ImpactHeavy", 0.22f, 0.12f, 18f, 0.6f);
            _clips[(int)SoundId.Explosion] = Synthesize("Explosion", 1.4f, 0.05f, 3.2f, 1f);

            _sources = new AudioSource[voices];
            _startTimes = new float[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = maxDistance;
                s.dopplerLevel = 0f;
                _sources[i] = s;
            }
            _valid = true;
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.EntityAdded += OnEntityAdded;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (registry != null) registry.EntityAdded -= OnEntityAdded;
            _subscribed = false;
        }

        /// <summary>Wires the prop's ImpactSound (GetComponent once per registration, not per collision).</summary>
        private void OnEntityAdded(SandboxEntity entity)
        {
            ImpactSound impact = entity.GetComponent<ImpactSound>();
            if (impact != null) impact.Audio = this;
        }

        /// <summary>intensity 0..1 scales volume and lowers pitch slightly for hard hits.</summary>
        public void PlayImpact(Vector3 position, float intensity, bool heavy)
        {
            if (!_valid) return;
            float now = Time.time;
            if (now - _windowStart > impactWindow)
            {
                _windowStart = now;
                _impactsInWindow = 0;
            }
            if (_impactsInWindow >= maxImpactsPerWindow) return;
            _impactsInWindow++;

            float volume = impactVolume * Mathf.Lerp(0.25f, 1f, intensity);
            float pitch = Random.Range(0.85f, 1.15f) * Mathf.Lerp(1.1f, 0.85f, intensity);
            Play(heavy ? SoundId.ImpactHeavy : SoundId.ImpactLight, position, volume, pitch, false);
        }

        public void PlayExplosion(Vector3 position)
        {
            if (_valid) Play(SoundId.Explosion, position, explosionVolume, Random.Range(0.9f, 1.05f), true);
        }

        private void Play(SoundId id, Vector3 position, float volume, float pitch, bool steal)
        {
            int index = -1;
            for (int k = 0; k < _sources.Length; k++)
            {
                int i = (_next + k) % _sources.Length;
                if (!_sources[i].isPlaying)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                if (!steal) return;
                index = 0; // steal the oldest voice
                for (int i = 1; i < _startTimes.Length; i++)
                    if (_startTimes[i] < _startTimes[index]) index = i;
            }

            _next = (index + 1) % _sources.Length;
            AudioSource s = _sources[index];
            s.transform.position = position;
            s.clip = _clips[(int)id];
            s.volume = volume;
            s.pitch = pitch;
            s.Play();
            _startTimes[index] = Time.time;
        }

        /// <summary>
        /// Noise burst with an exponential decay, low-pass filtered (lowPass 0..1: lower = duller) and a short attack.
        /// Startup only.
        /// </summary>
        private static AudioClip Synthesize(string clipName, float seconds, float lowPass, float decay, float gain)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(clipName.GetHashCode());
            float filtered = 0f;
            float filtered2 = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                filtered += lowPass * (noise - filtered);
                filtered2 += lowPass * (filtered - filtered2);
                float attack = Mathf.Clamp01(t / 0.004f);
                float envelope = attack * Mathf.Exp(-decay * t);
                data[i] = Mathf.Clamp(filtered2 * envelope * gain * 4f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(clipName, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
