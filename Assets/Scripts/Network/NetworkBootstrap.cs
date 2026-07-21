using FishNet;
using FishNet.Transporting;
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

        private bool isHost;
        private bool returningToTitle; // Title へ戻す処理の多重実行を防ぐ

        private void Start()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                Debug.LogError("[NetworkBootstrap] NetworkManager がシーンにありません。GameScene に NetworkManager を置いてください。", this);
                return;
            }

            isHost = TitlePrefs.IsHost();

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
            if (returningToTitle)
            {
                return;
            }
            returningToTitle = true;

            TitlePrefs.SetGameState(TitlePrefs.GameStateConnectionFailed);

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
