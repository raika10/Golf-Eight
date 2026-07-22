using TMPro;
using UnityEngine;

namespace GolfEight.Network
{
    /// 実行時にコード生成するUI（LoadingScreen / LobbyStartUI など）が共通で使うフォント解決。
    /// 優先は明示指定 → シーンに読み込まれている日本語対応フォント（Noto/JP を名前に含むもの）
    /// → TMP のデフォルト → 見つかった最初のもの。これで日本語テキストが文字化けしない。
    public static class RuntimeUiFont
    {
        public static TMP_FontAsset Resolve(TMP_FontAsset preferred)
        {
            if (preferred != null)
            {
                return preferred;
            }

            TMP_FontAsset firstFound = null;
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null)
                {
                    continue;
                }
                string lower = font.name.ToLowerInvariant();
                if (lower.Contains("noto") || lower.Contains("jp") || lower.Contains("japan"))
                {
                    return font;
                }
                if (firstFound == null)
                {
                    firstFound = font;
                }
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }
            return firstFound;
        }
    }
}
