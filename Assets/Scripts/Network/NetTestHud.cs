using FishNet;
using UnityEngine;

namespace GolfEight.Network
{
    /// net-bootstrap 検証専用の最小接続UI（IMGUI）。TitleScene の RoomJoinValidator 等、本番のロビーUIとは無関係
    /// （ロビーUIとの結線は別フェーズで行う）。空のGameObjectに付けるだけで動く。
    /// Host: サーバーを起動し、自分もローカルクライアントとして接続する（ホスト型）。
    /// Join: 入力したIPへ、指定ポートで接続する。
    public class NetTestHud : MonoBehaviour
    {
        [SerializeField] private ushort port = 7770;
        [SerializeField] private GameManager gameManager;
        private string joinAddress = "127.0.0.1";

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 260, 150), GUI.skin.box);

            bool serverStarted = InstanceFinder.IsServerStarted;
            bool clientStarted = InstanceFinder.IsClientStarted;

            if (!serverStarted && !clientStarted)
            {
                if (GUILayout.Button("Host"))
                {
                    InstanceFinder.ServerManager.StartConnection(port);
                    InstanceFinder.ClientManager.StartConnection("127.0.0.1", port);
                }

                GUILayout.Space(8);
                GUILayout.Label("Host IP:");
                joinAddress = GUILayout.TextField(joinAddress);
                if (GUILayout.Button("Join"))
                {
                    InstanceFinder.ClientManager.StartConnection(joinAddress, port);
                }
            }
            else
            {
                GUILayout.Label(serverStarted ? "Hosting" : "Connected as client");

                // 検証用：サーバー側でだけ進行を操作できる（[Server]属性がクライアント呼び出しを弾く）
                if (serverStarted && gameManager != null)
                {
                    GUILayout.Label("State: " + gameManager.State);

                    // ロビーからだけ開始できる（RequestStartGame 側でも弾いている）
                    if (gameManager.State == GameManager.GameState.Lobby && GUILayout.Button("Start Game"))
                    {
                        gameManager.RequestStartGame();
                    }

                    // 決着後はロビーへ戻して再戦できる。クライアントは接続したまま残る
                    if (gameManager.State == GameManager.GameState.Finished && GUILayout.Button("Return to Lobby"))
                    {
                        gameManager.ReturnToLobby();
                    }
                }
            }

            GUILayout.EndArea();
        }
    }
}
