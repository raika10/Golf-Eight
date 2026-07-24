using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GolfEight.Network
{
    /// GameManager がロビー状態（GameState.Lobby）の間だけ出す「ゲームスタート」バナー。
    /// 開始はサーバー権威（GameManager.RequestStartGame は [Server] 属性）なので、
    /// 押せるのはホスト（サーバー）だけにする。ゲスト側はホスト待ちのラベルを表示する。
    /// UI は実行時にコードで構築する（LoadingScreen と同じ方針）。GameScene への手作業配置は不要。
    public class LobbyStartUI : MonoBehaviour
    {
        private GameManager gameManager;
        private GameObject panel;
        private Button startButton;
        private TextMeshProUGUI hostLabel;
        private TextMeshProUGUI guestLabel;

        /// バナー一式を実行時に生成して返す。
        public static LobbyStartUI Create(TMP_FontAsset fontOverride = null)
        {
            var go = new GameObject("LobbyStartUI");
            var ui = go.AddComponent<LobbyStartUI>();
            ui.Build(RuntimeUiFont.Resolve(fontOverride));
            return ui;
        }

        private void Update()
        {
            if (gameManager == null)
            {
                gameManager = FindFirstObjectByType<GameManager>();
            }

            bool isLobby = gameManager != null && gameManager.State == GameManager.GameState.Lobby;
            panel.SetActive(isLobby);
            if (!isLobby)
            {
                return;
            }

            bool isHost = InstanceFinder.IsServerStarted;
            startButton.gameObject.SetActive(isHost);
            hostLabel.gameObject.SetActive(isHost);
            guestLabel.gameObject.SetActive(!isHost);
        }

        private void HandleStartClicked()
        {
            if (gameManager != null)
            {
                gameManager.RequestStartGame();
            }
        }

        private void Build(TMP_FontAsset font)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 通常のゲームUIより手前、LoadingScreen(1000)よりは奥

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // --- バナー本体（画面下寄り中央。プレイ画面を覆いすぎない小さめのパネル）---
            panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(canvas.transform, false);
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0, 120);
            panelRect.sizeDelta = new Vector2(520, 160);

            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.06f, 0.08f, 0.75f);

            // --- ホスト用ラベル（ボタンの上に一言添える）---
            hostLabel = CreateLabel(panelRect, "全員揃ったらスタート", 30, new Vector2(0, 45), new Vector2(460, 50), font);

            // --- ゲスト用ラベル（ホストの開始待ち）---
            guestLabel = CreateLabel(panelRect, "ホストの開始を待っています…", 30, new Vector2(0, 0), new Vector2(460, 60), font);

            // --- スタートボタン（ホストのみ表示・押下可）---
            var buttonGo = new GameObject("StartButton", typeof(RectTransform));
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.SetParent(panelRect, false);
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0, 16);
            buttonRect.sizeDelta = new Vector2(300, 72);

            var buttonImage = buttonGo.AddComponent<Image>();
            buttonImage.color = new Color(0.16f, 0.55f, 0.28f, 1f);

            startButton = buttonGo.AddComponent<Button>();
            startButton.targetGraphic = buttonImage;
            startButton.onClick.AddListener(HandleStartClicked);

            CreateLabel(buttonRect, "ゲームスタート", 30, Vector2.zero, new Vector2(280, 60), font, stretch: true);
        }

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string text, float size, Vector2 anchoredPosition, Vector2 sizeDelta, TMP_FontAsset font, bool stretch = false)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = sizeDelta;
            }

            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }
    }
}
