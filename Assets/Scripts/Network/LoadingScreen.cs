using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GolfEight.Network
{
    /// カメラの無い GameScene で「接続待ち」中に全面表示するローディング画面。
    /// Screen Space - Overlay の Canvas はカメラが無くても描画されるので、
    /// 接続してプレイヤー（＝子カメラ）がスポーンするまでの黒画面をこれで隠す。
    ///
    /// UI は実行時にコードで丸ごと構築する（Create）ため、シーン側の準備・参照付けは不要。
    /// フォントはシーン内で使われている日本語対応フォントを自動採用するので「接続中...」も文字化けしない。
    ///
    /// 表示/非表示・メッセージ差し替えは NetworkBootstrap から Show/Hide/SetMessage で制御する。
    /// キャンセルボタンが押されたら OnCancelRequested を通知するだけで、実際の後始末は購読側に任せる。
    public class LoadingScreen : MonoBehaviour
    {
        // スピナー（回転風の1文字）を回すアニメ。ASCII なのでどのフォントでも必ず出る。
        private static readonly string[] SpinnerFrames = { "|", "/", "-", "\\" };

        private TextMeshProUGUI messageText;
        private TextMeshProUGUI spinnerText;

        private string baseMessage = "接続中";
        private float dotTimer;    // 「...」の増減用
        private int dotCount;      // 現在のドット数（0..3）
        private float spinTimer;   // スピナー切り替え用
        private int spinIndex;     // 現在のスピナーフレーム

        /// キャンセルボタンが押されたことを購読側（NetworkBootstrap）へ知らせる。
        public event Action OnCancelRequested;

        /// ローディング画面一式を実行時に生成して返す。生成直後は非表示。
        /// fontOverride を渡さなければシーン内の日本語対応フォントを自動で探して使う。
        public static LoadingScreen Create(TMP_FontAsset fontOverride = null)
        {
            var go = new GameObject("LoadingScreen");
            var loading = go.AddComponent<LoadingScreen>();
            loading.Build(RuntimeUiFont.Resolve(fontOverride));
            go.SetActive(false);
            return loading;
        }

        /// 表示する。message を渡すと基準メッセージ（末尾のドットアニメを除いた部分）を差し替える。
        public void Show(string message = null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                baseMessage = message;
            }
            dotCount = 0;
            dotTimer = 0f;
            spinIndex = 0;
            spinTimer = 0f;
            UpdateTexts();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// 基準メッセージだけ差し替える（表示状態は変えない）。
        public void SetMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            baseMessage = message;
            UpdateTexts();
        }

        /// 隠す。
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            // timeScale の影響を受けないよう unscaled を使う（決着時など timeScale=0 でも回すため）。
            float dt = Time.unscaledDeltaTime;

            spinTimer += dt;
            if (spinTimer >= 0.12f)
            {
                spinTimer = 0f;
                spinIndex = (spinIndex + 1) % SpinnerFrames.Length;
                if (spinnerText != null)
                {
                    spinnerText.text = SpinnerFrames[spinIndex];
                }
            }

            dotTimer += dt;
            if (dotTimer >= 0.4f)
            {
                dotTimer = 0f;
                dotCount = (dotCount + 1) % 4; // 0,1,2,3 を繰り返す
                UpdateTexts();
            }
        }

        private void UpdateTexts()
        {
            if (messageText != null)
            {
                messageText.text = baseMessage + new string('.', dotCount);
            }
            if (spinnerText != null)
            {
                spinnerText.text = SpinnerFrames[spinIndex];
            }
        }

        /// UI 階層を実行時に構築する。Canvas(Overlay) → 背景 → スピナー → メッセージ → キャンセルボタン。
        private void Build(TMP_FontAsset font)
        {
            // --- Canvas（カメラ不要で描画される Overlay。他UIより手前に出す）---
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // --- 背景（不透明。裏の空シーンを完全に隠す）---
            var bg = CreateChild("Background", canvas.transform);
            StretchFull(bg);
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = new Color(0.055f, 0.06f, 0.075f, 1f);

            // --- スピナー（回転風の1文字。画面中央やや上）---
            var spinnerGo = CreateChild("Spinner", canvas.transform);
            spinnerText = spinnerGo.gameObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(spinnerText, font, 96, FontStyles.Bold);
            spinnerText.text = SpinnerFrames[0];
            CenterAnchor(spinnerText.rectTransform, new Vector2(0, 60), new Vector2(400, 140));

            // --- メッセージ（「接続中...」。画面中央やや下）---
            var messageGo = CreateChild("Message", canvas.transform);
            messageText = messageGo.gameObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(messageText, font, 48, FontStyles.Normal);
            messageText.text = baseMessage;
            CenterAnchor(messageText.rectTransform, new Vector2(0, -40), new Vector2(900, 100));

            // --- キャンセルボタン（画面下部。接続不能時に待ちきらず戻れるように）---
            BuildCancelButton(canvas.transform, font);
        }

        private void BuildCancelButton(Transform parent, TMP_FontAsset font)
        {
            var buttonGo = CreateChild("CancelButton", parent);
            var rect = buttonGo;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0, 80);
            rect.sizeDelta = new Vector2(260, 72);

            var image = buttonGo.gameObject.AddComponent<Image>();
            image.color = new Color(0.2f, 0.22f, 0.26f, 1f);

            var button = buttonGo.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => OnCancelRequested?.Invoke());

            var labelGo = CreateChild("Text", buttonGo);
            StretchFull(labelGo);
            var label = labelGo.gameObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(label, font, 28, FontStyles.Normal);
            label.text = "キャンセル";
        }

        private static void ConfigureText(TextMeshProUGUI text, TMP_FontAsset font, float size, FontStyles style)
        {
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static RectTransform CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void CenterAnchor(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
