using SimpleBox.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleBox.EditorTools
{
    /// <summary>Localization: every scene-built Text with Russian content gets a LocalizedText (its text = key).</summary>
    public static partial class SimpleBoxSceneBuilder
    {
        internal static void AddLocalizedTexts(Transform root)
        {
            if (root == null) return;
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponent<LocalizedText>() != null || !HasCyrillic(text.text)) continue;
                LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
                localized.SetKey(text.text);
            }
        }

        private static bool HasCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= 'Ѐ' && s[i] <= 'ӿ') return true;
            }
            return false;
        }
    }
}
