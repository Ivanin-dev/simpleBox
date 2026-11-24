using SimpleBox.Core;
using System;
using SimpleBox.Controls;
using SimpleBox.Gameplay;
using SimpleBox.Gameplay.Tools;
using SimpleBox.World;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.Hud
{
    /// <summary>
    /// First-launch tutorial: a hint card with one step at a time and a pulsing ring over the control to press.
    /// Steps complete by doing the thing (walk, look around, spawn a prop, grab it with the physgun, switch tool);
    /// the last step is a short summary closed by OK or a timeout. Completion (or SKIP) is saved in PlayerPrefs;
    /// Restart() (settings menu) shows it again. Detection is polled with cached values (no allocations).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnboardingGuide : MonoBehaviour
    {
        private const string DoneKey = "sb.onboarding.done";

        private enum Step { Move = 0, Look, Spawn, Grab, Tool, Finish }
        private const int StepCount = 6;

        private static readonly string[] Texts =
        {
            "Иди: джойстик в левой половине экрана",
            "Осмотрись: веди пальцем по правой половине экрана",
            "Создай проп: кнопка SPAWN. Тап по панели пропа справа вверху — выбор пропа",
            "Физган: наведи прицел на проп и нажми USE — проп в руках. USE ещё раз — отпустить, ALT — заморозить",
            "Смени инструмент: кнопка TOOL. Подсказка инструмента — внизу экрана",
            "Готово! UNDO отменяет, SAVE — сохранения, MENU — настройки и повтор обучения"
        };
        private static readonly string[] Counters = { "1/6", "2/6", "3/6", "4/6", "5/6", "6/6" };

        [Header("References (required)")]
        [SerializeField] private GameObject cardRoot;
        [SerializeField] private Text hintLabel;
        [SerializeField] private Text counterLabel;
        [SerializeField] private TouchButton skipButton;
        [SerializeField] private Transform player;
        [SerializeField] private Transform aimOrigin;
        [SerializeField] private EntityRegistry registry;
        [SerializeField] private SandboxActions actions;
        [SerializeField] private PhysicsGun physicsGun;
        [SerializeField] private ToolManager toolManager;

        [Header("Highlight (optional)")]
        [Tooltip("Ring image moved over the control of the current step.")]
        [SerializeField] private RectTransform highlight;
        [Tooltip("Index = step: Move, Look, Spawn, Grab, Tool, Finish. Empty = no ring for that step.")]
        [SerializeField] private RectTransform[] stepTargets = new RectTransform[StepCount];
        [SerializeField, Min(0f)] private float highlightPadding = 30f;

        [Header("Start")]
        [Tooltip("Off when a main menu starts the tutorial (StartIfFirstRun) after the player presses Play.")]
        [SerializeField] private bool autoStart = true;

        [Header("Thresholds")]
        [SerializeField, Min(0.5f)] private float moveDistance = 3f;
        [SerializeField, Min(10f)] private float lookDegrees = 90f;
        [SerializeField, Min(1f)] private float finishSeconds = 8f;
        [Tooltip("Pause after a step completes before the next one appears.")]
        [SerializeField, Min(0f)] private float stepDelay = 0.6f;

        private Image _highlightImage;
        private bool _valid;
        private bool _active;
        private int _step;
        private float _stepTime;
        private float _pendingUntil = -1f;
        private Vector3 _startPosition;
        private Vector3 _lastForward;
        private float _lookAccumulated;
        private bool _spawned;
        private int _ownedAtStart;
        private bool _toolChanged;
        private bool _subscribed;

        public bool IsActive => _active;

        private void Awake()
        {
            _valid = cardRoot != null && hintLabel != null && counterLabel != null && skipButton != null && player != null &&
                     aimOrigin != null && registry != null && actions != null && physicsGun != null && toolManager != null;
            if (!_valid)
            {
                Debug.LogError($"[{nameof(OnboardingGuide)}] '{name}': required references not assigned. Component disabled.", this);
                enabled = false;
                return;
            }
            if (stepTargets == null || stepTargets.Length != StepCount) Array.Resize(ref stepTargets, StepCount);
            if (highlight != null) _highlightImage = highlight.GetComponent<Image>();
            cardRoot.SetActive(false);
            SetHighlight(null);
        }

        private void OnEnable()
        {
            if (!_valid || _subscribed) return;
            registry.OwnerCountChanged += OnOwnerCountChanged;
            toolManager.ActiveToolChanged += OnToolChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            if (registry != null) registry.OwnerCountChanged -= OnOwnerCountChanged;
            if (toolManager != null) toolManager.ActiveToolChanged -= OnToolChanged;
            _subscribed = false;
        }

        private void Start()
        {
            if (autoStart) StartIfFirstRun();
        }

        /// <summary>Shows the tutorial if it was never completed or skipped (does nothing while it is running).</summary>
        public void StartIfFirstRun()
        {
            if (_valid && !_active && PlayerPrefs.GetInt(DoneKey, 0) == 0) Begin();
        }

        /// <summary>Shows the tutorial again from the first step (settings menu).</summary>
        public void Restart()
        {
            if (!_valid) return;
            PlayerPrefs.SetInt(DoneKey, 0);
            PlayerPrefs.Save();
            Begin();
        }

        private void Begin()
        {
            _active = true;
            cardRoot.SetActive(true);
            EnterStep(0);
        }

        private void Complete()
        {
            _active = false;
            cardRoot.SetActive(false);
            SetHighlight(null);
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
        }

        private void EnterStep(int step)
        {
            _step = step;
            _stepTime = Time.unscaledTime;
            _pendingUntil = -1f;
            hintLabel.text = Loc.T(Texts[step]);
            counterLabel.text = Counters[step];
            SetHighlight(stepTargets[step]);

            switch ((Step)step)
            {
                case Step.Move: _startPosition = player.position; break;
                case Step.Look:
                    _lastForward = aimOrigin.forward;
                    _lookAccumulated = 0f;
                    break;
                case Step.Spawn:
                    _spawned = false;
                    _ownedAtStart = registry.CountOwnedBy(actions.LocalOwnerId);
                    break;
                case Step.Tool: _toolChanged = false; break;
            }
        }

        private void Update()
        {
            if (!_active) return;

            if (skipButton.ConsumePress())
            {
                Complete();
                return;
            }

            UpdateHighlight();

            float now = Time.unscaledTime;
            if (_pendingUntil >= 0f)
            {
                if (now < _pendingUntil) return;
                if (_step + 1 >= StepCount) Complete();
                else EnterStep(_step + 1);
                return;
            }

            if (IsStepDone(now)) _pendingUntil = now + stepDelay;
        }

        private bool IsStepDone(float now)
        {
            switch ((Step)_step)
            {
                case Step.Move:
                    Vector3 d = player.position - _startPosition;
                    d.y = 0f;
                    return d.sqrMagnitude >= moveDistance * moveDistance;
                case Step.Look:
                    Vector3 f = aimOrigin.forward;
                    _lookAccumulated += Vector3.Angle(_lastForward, f);
                    _lastForward = f;
                    return _lookAccumulated >= lookDegrees;
                case Step.Spawn: return _spawned;
                case Step.Grab: return physicsGun.IsHolding;
                case Step.Tool: return _toolChanged;
                case Step.Finish: return now - _stepTime >= finishSeconds;
            }
            return true;
        }

        private void OnOwnerCountChanged(int ownerId, int count)
        {
            if (_active && ownerId == actions.LocalOwnerId && count > _ownedAtStart) _spawned = true;
        }

        private void OnToolChanged(int index)
        {
            if (_active) _toolChanged = true;
        }

        // ------------------------------------------------------------------ Highlight

        private RectTransform _target;

        private void SetHighlight(RectTransform target)
        {
            _target = target;
            if (highlight != null) highlight.gameObject.SetActive(target != null);
        }

        private void UpdateHighlight()
        {
            if (highlight == null || _target == null) return;
            bool visible = _target.gameObject.activeInHierarchy;
            if (highlight.gameObject.activeSelf != visible) highlight.gameObject.SetActive(visible);
            if (!visible) return;

            highlight.position = _target.TransformPoint(_target.rect.center);
            Vector2 size = _target.rect.size;
            Vector3 s = _target.lossyScale;
            Vector3 hs = highlight.lossyScale;
            float kx = hs.x > 1e-5f ? s.x / hs.x : 1f;
            float ky = hs.y > 1e-5f ? s.y / hs.y : 1f;
            highlight.sizeDelta = new Vector2(size.x * kx + highlightPadding, size.y * ky + highlightPadding);

            if (_highlightImage != null)
            {
                Color c = _highlightImage.color;
                c.a = 0.3f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f);
                _highlightImage.color = c;
            }
        }
    }
}
