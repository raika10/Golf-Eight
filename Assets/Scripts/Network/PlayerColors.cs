using UnityEngine;

namespace GolfEight.Network
{
    /// プレイヤーと、その担当ボールを見分けるための色。接続順のインデックスで引く。
    /// プレイヤー本体・担当ボール・勝敗表示のすべてでここを参照するので、
    /// 「赤い人の球は赤」「勝者表示も赤」と全画面で一貫する。
    public static class PlayerColors
    {
        private static readonly Color[] colors =
        {
            new Color(0.90f, 0.25f, 0.25f), // 赤
            new Color(0.25f, 0.50f, 0.95f), // 青
            new Color(0.30f, 0.80f, 0.35f), // 緑
            new Color(0.95f, 0.80f, 0.20f), // 黄
        };

        private static readonly string[] names = { "赤", "青", "緑", "黄" };

        /// 用意している色の数（＝想定する最大人数）。
        public static int Count => colors.Length;

        /// インデックスに対応する色。人数が色数を超えても落ちないよう巡回させる。
        public static Color Get(int index)
        {
            return colors[Wrap(index)];
        }

        /// 表示用の名前。名前の仕組みがまだ無いので「プレイヤー1（赤）」のように使う。
        public static string GetDisplayName(int index)
        {
            return $"プレイヤー{Wrap(index) + 1}（{names[Wrap(index)]}）";
        }

        private static int Wrap(int index)
        {
            int n = colors.Length;
            return ((index % n) + n) % n;
        }

        /// レンダラーへ色を適用する。ビルトインとURPで主色のプロパティ名が違うため両方に対応する。
        /// material（sharedMaterial ではない）を触るのでインスタンス化され、他のオブジェクトには影響しない。
        public static void Apply(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }
            Material mat = renderer.material;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }
    }
}
