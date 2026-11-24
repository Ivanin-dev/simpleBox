using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SimpleBox.Core
{
    /// <summary>
    /// Loading screen (Boot scene, build index 0): loads the game scene asynchronously with a progress bar and a random
    /// tip, keeps the screen up for at least Min Show Time, then activates the game scene (single mode).
    /// The game scene's own startup (pools, network manager) runs during activation while this screen is still shown.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootLoader : MonoBehaviour
    {
        [Header("References (required)")]
        [Tooltip("Image Type = Filled, Horizontal.")]
        [SerializeField] private Image progressFill;

        [Header("References (optional)")]
        [SerializeField] private Text tipLabel;
        [SerializeField] private Text versionLabel;
        [SerializeField] private Text errorLabel;

        [Header("Loading")]
        [Tooltip("Name of the game scene (must be in Build Settings).")]
        [SerializeField] private string gameScene = string.Empty;
        [SerializeField, Min(0f)] private float minShowTime = 1.2f;
        [Tooltip("Progress bar speed (fraction per second) so it never jumps.")]
        [SerializeField, Min(0.1f)] private float fillSpeed = 1.5f;
        [SerializeField, Min(15)] private int bootFrameRate = 60;

        [Header("Tips")]
        [SerializeField] private string[] tips =
        {
            "Физган: USE хватает проп, ALT замораживает его в воздухе",
            "Сварка соединяет два пропа в одну конструкцию",
            "Двигатели и оси можно привязать к кнопкам 1–4",
            "Сядь на стул — джойстик будет управлять кнопками 1–4",
            "Дубликатор копирует целую конструкцию",
            "Взрывная бочка детонирует от удара или детонатора",
            "UNDO отменяет последнее действие",
            "Настройки графики и управления — кнопка MENU"
        };

        private bool _valid;

        private void Awake()
        {
            _valid = progressFill != null && !string.IsNullOrWhiteSpace(gameScene);
            if (!_valid)
            {
                Debug.LogError($"[{nameof(BootLoader)}] '{name}': Progress Fill / Game Scene not assigned. Component disabled.", this);
                ShowError(Loc.T("Загрузчик не настроен"));
                enabled = false;
                return;
            }
            Application.targetFrameRate = bootFrameRate; // QualityManager sets the real value in the game scene
            progressFill.fillAmount = 0f;
            if (errorLabel != null) errorLabel.gameObject.SetActive(false);
            if (versionLabel != null) versionLabel.text = "v" + Application.version;
            if (tipLabel != null && tips != null && tips.Length > 0) tipLabel.text = Loc.T(tips[Random.Range(0, tips.Length)]);
        }

        private void Start()
        {
            if (!_valid) return;
            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                Debug.LogError($"[{nameof(BootLoader)}] Scene '{gameScene}' is not in Build Settings.", this);
                ShowError(Loc.T("Сцена игры не найдена: ") + gameScene);
                return;
            }
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            float start = Time.unscaledTime;
            AsyncOperation op = SceneManager.LoadSceneAsync(gameScene, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            float shown = 0f;
            while (true)
            {
                // Async progress stops at 0.9 until activation is allowed.
                float target = Mathf.Clamp01(op.progress / 0.9f);
                shown = Mathf.MoveTowards(shown, target, fillSpeed * Time.unscaledDeltaTime);
                progressFill.fillAmount = shown;

                if (op.progress >= 0.9f && shown >= 0.999f && Time.unscaledTime - start >= minShowTime) break;
                yield return null;
            }

            op.allowSceneActivation = true;
        }

        private void ShowError(string message)
        {
            if (errorLabel == null) return;
            errorLabel.gameObject.SetActive(true);
            errorLabel.text = message;
        }
    }
}
