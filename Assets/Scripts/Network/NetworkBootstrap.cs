using FishNet;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfEight.Network
{
    /// GameScene 起動時に、TitleScene が PlayerPrefs に残した設定（IsHost / IP_address / Port）を読み、
    /// FishNet(Tugboat) の接続を自動で開始する本番の入口。検証用の NetTestHud（手動IMGUI）を置き換える。
    /// 接続に失敗、または接続後に切断されたら GameState=-1 を書いて TitleScene へ戻し、
    /// Title 側でエラー表示を出せるようにする（設計ドキュメントの GameState 規約に準拠）。
    ///
    /// 前提：GameScene に NetworkManager（Tugboat 設定済み）が置かれていること。
    public class NetworkBootstrap : MonoBehaviour
    {
        [Tooltip("接続失敗時に戻るシーン名（Build Settings に登録が必要）。")]
        [SerializeField] private string titleSceneName = "TitleScene";

        [Tooltip("ホストが自分自身のサーバーへ繋ぐときの宛先。通常はループバックのままでよい。")]
        [SerializeField] private string hostLoopbackAddress = "127.0.0.1";

        [Tooltip("接続待ちの上限秒数。これを超えても繋がらなければ失敗として Title へ戻す（0以下で無効）。")]
        [SerializeField] private float connectionTimeoutSeconds = 15f;

        [Tooltip("ローディング画面。未割当なら実行時に自動生成する（通常は未割当のままでよい）。")]
        [SerializeField] private LoadingScreen loadingScreen;

        [Tooltip("ローディング文字のフォント。未割当ならシーン内の日本語対応フォントを自動採用する。")]
        [SerializeField] private TMP_FontAsset loadingFont;

        [Tooltip("ロビーの「ゲームスタート」バナー。未割当なら実行時に自動生成する（通常は未割当のままでよい）。")]
        [SerializeField] private LobbyStartUI lobbyStartUI;

        private bool isHost;
        private bool returningToTitle; // Title へ戻す処理の多重実行を防ぐ
        private bool clientConnected;  // ローカルクライアントの接続が確立したか
        private bool loadingHidden;    // カメラが出てローディングを消し終えたか
        private float elapsed;         // 接続開始からの経過秒（タイムアウト判定用）

        private void Start()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                Debug.LogError("[NetworkBootstrap] NetworkManager がシーンにありません。GameScene に NetworkManager を置いてください。", this);
                return;
            }

            isHost = TitlePrefs.IsHost();

            // 接続待ちの黒画面（GameScene はカメラが無いので何も映らない）を隠すため、
            // 接続を始める前にローディング画面を出す。未割当なら実行時に生成する。
            if (loadingScreen == null)
            {
                loadingScreen = LoadingScreen.Create(loadingFont);
            }
            loadingScreen.OnCancelRequested += HandleCancelRequested;
            loadingScreen.Show(isHost ? "サーバーを起動中" : "接続中");

            // ロビーの「ゲームスタート」バナーも同様に自動生成する。中身はロビー状態になってから
            // GameManager を見つけて出す（未接続の間は何も表示しない）。
            if (lobbyStartUI == null)
            {
                lobbyStartUI = LobbyStartUI.Create(loadingFont);
            }

            // 起動の直前に購読する。StartConnection より前に購読しておかないと、
            // 早い段階の状態変化（即時失敗など）を取りこぼす可能性があるため。
            InstanceFinder.ClientManager.OnClientConnectionState += HandleClientState;
            InstanceFinder.ServerManager.OnServerConnectionState += HandleServerState;

            ushort port = TitlePrefs.GetPort();
            if (isHost)
            {
                // ホスト：まずサーバーを起動する。起動できたら（HandleServerState=Started）ローカルクライアントも繋ぐ。
                InstanceFinder.ServerManager.StartConnection(port);
            }
            else
            {
                // ゲスト：Title で入力されたホストIPへ接続する。
                InstanceFinder.ClientManager.StartConnection(TitlePrefs.GetIpAddress(), port);
            }
        }

        private void Update()
        {
            if (returningToTitle)
            {
                return;
            }

            // 接続確立後、プレイヤー（＝子カメラ）がスポーンして画面が映るようになったら
            // ローディングを消す。カメラが1台でも描画中になった時点を「準備完了」とみなす。
            // GameScene には元々カメラが無いので、この判定は接続後にしか真にならない。
            if (clientConnected && !loadingHidden)
            {
                if (Camera.allCamerasCount > 0)
                {
                    if (loadingScreen != null)
                    {
                        loadingScreen.Hide();
                    }
                    loadingHidden = true;
                }
                return;
            }

            // まだ接続できていない間はタイムアウトを監視する。FishNet 側が先に切断(Stopped)を
            // 出せばそちらで Title へ戻るが、無応答で固まったままのケースの保険として上限を設ける。
            if (!clientConnected && connectionTimeoutSeconds > 0f)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= connectionTimeoutSeconds)
                {
                    Debug.LogWarning("[NetworkBootstrap] 接続がタイムアウトしました。Title へ戻ります。", this);
                    FailToTitle();
                }
            }
        }

        private void OnDestroy()
        {
            if (InstanceFinder.ClientManager != null)
            {
                InstanceFinder.ClientManager.OnClientConnectionState -= HandleClientState;
            }
            if (InstanceFinder.ServerManager != null)
            {
                InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerState;
            }
            if (loadingScreen != null)
            {
                loadingScreen.OnCancelRequested -= HandleCancelRequested;
            }
        }

        /// ローディング画面のキャンセルボタンが押されたとき。接続を諦めて Title へ戻る。
        /// ユーザーが自分で中断しただけなので「接続失敗(-1)」ではなく通常状態で戻す。
        private void HandleCancelRequested()
        {
            ReturnToTitle(TitlePrefs.GameStateNormal);
        }

        private void HandleServerState(ServerConnectionStateArgs args)
        {
            if (!isHost)
            {
                return;
            }

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                // サーバーが立ち上がったので、ホスト自身もローカルクライアントとして参加する。
                InstanceFinder.ClientManager.StartConnection(hostLoopbackAddress, TitlePrefs.GetPort());
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // ポート使用中などでサーバーが起動できなかった／落ちた。ホストは続行不能なので Title へ。
                FailToTitle();
            }
        }

        private void HandleClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                // 接続確立。前回の失敗フラグ(-1)が残っていても、繋がった時点で通常状態に戻す。
                TitlePrefs.SetGameState(TitlePrefs.GameStateNormal);
                clientConnected = true;
                // カメラが出るまではまだ黒画面なので、表示は残したままメッセージだけ切り替える。
                if (loadingScreen != null)
                {
                    loadingScreen.SetMessage("プレイヤーを準備中");
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // ゲスト：ホストへ繋がらなかった／繋がらなくなった。どちらも Title へ戻して -1 表示。
                // ホスト：自分のサーバーを止めた結果ローカルクライアントも止まるだけなので、
                //         ここでは無視して HandleServerState 側に任せる（二重で Title へ戻さない）。
                if (!isHost)
                {
                    FailToTitle();
                }
            }
        }

        private void FailToTitle()
        {
            // 接続失敗として Title へ戻す（Title 側が -1 を読んでエラー表示する）。
            ReturnToTitle(TitlePrefs.GameStateConnectionFailed);
        }

        /// ゴール画面などから「意図的にTitleへ戻る」ときに呼ぶ。GameStateはNormalのまま戻す。
        public void ReturnToTitleManually()
        {
            ReturnToTitle(TitlePrefs.GameStateNormal);
        }

        /// Title へ戻す共通処理。gameState に書き込む値で「失敗(-1)」か「通常(0=キャンセル)」かを分ける。
        private void ReturnToTitle(int gameState)
        {
            if (returningToTitle)
            {
                return;
            }
            returningToTitle = true;

            if (loadingScreen != null)
            {
                loadingScreen.Hide();
            }

            TitlePrefs.SetGameState(gameState);

            // 残っている接続を確実に閉じてからシーンを戻す（ソケットの取りこぼしを防ぐ）。
            if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
            {
                InstanceFinder.ClientManager.StopConnection();
            }
            if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
            {
                InstanceFinder.ServerManager.StopConnection(true);
            }

            if (string.IsNullOrEmpty(titleSceneName))
            {
                Debug.LogError("[NetworkBootstrap] titleSceneName が未設定のため Title へ戻れません。", this);
                return;
            }
            SceneManager.LoadScene(titleSceneName);
        }
    }
}
