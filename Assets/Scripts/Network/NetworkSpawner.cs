using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace GolfEight.Network
{
    /// サーバー専用のスポーン係。サーバー起動時にボールを、クライアント接続ごとにプレイヤーをスポーンする。
    /// 全員同じ位置からスタートする仕様（ONLINE_DESIGN.md）に合わせ、プレイヤーのスタート地点は1つだけ持つ。
    public class NetworkSpawner : MonoBehaviour
    {
        [Header("ボール")]
        [SerializeField] private GameObject ballPrefab;
        [SerializeField] private Transform[] ballSpawnPoints;

        [Header("プレイヤー")]
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("プレイヤーのスタート地点。接続してきた順に別々の地点へ割り当てる（net-player-spawn）。\n人数より地点が少ない場合は先頭から巡回して使い回す。")]
        [SerializeField] private Transform[] playerStartPoints;

        // 次に接続してきたプレイヤーへ割り当てるスタート地点のインデックス（サーバー側だけで進む＝権威）。
        private int nextPlayerSpawnIndex;

        // Start（Awakeの後）で購読する。OnEnable だとシーン内の初期化順によっては
        // NetworkManager がまだ InstanceFinder に登録されておらず取得できないことがあるため。
        private void Start()
        {
            if (InstanceFinder.ServerManager == null)
            {
                Debug.LogError("[NetworkSpawner] ServerManager が見つかりません。シーンに NetworkManager があるか確認してください。", this);
                return;
            }
            InstanceFinder.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            InstanceFinder.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
        }

        private void OnDestroy()
        {
            if (InstanceFinder.ServerManager != null)
            {
                InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
                InstanceFinder.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
            }
        }

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
            {
                return;
            }
            SpawnBalls();
        }

        private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Started)
            {
                return;
            }
            SpawnPlayer(conn);
        }

        private void SpawnBalls()
        {
            if (ballPrefab == null)
            {
                Debug.LogWarning("[NetworkSpawner] ballPrefab が未設定です。", this);
                return;
            }
            foreach (Transform point in ballSpawnPoints)
            {
                if (point == null)
                {
                    continue;
                }
                GameObject ball = Instantiate(ballPrefab, point.position, point.rotation);
                InstanceFinder.ServerManager.Spawn(ball);
            }
        }

        private void SpawnPlayer(NetworkConnection conn)
        {
            if (playerPrefab == null || playerStartPoints == null || playerStartPoints.Length == 0)
            {
                Debug.LogWarning("[NetworkSpawner] playerPrefab / playerStartPoints が未設定です。", this);
                return;
            }

            // 接続順に別々のスタート地点を割り当てる。地点が足りなければ先頭から巡回して使い回す。
            Transform start = playerStartPoints[nextPlayerSpawnIndex % playerStartPoints.Length];
            nextPlayerSpawnIndex++;
            if (start == null)
            {
                Debug.LogWarning("[NetworkSpawner] playerStartPoints の要素に未設定(null)があります。", this);
                return;
            }

            GameObject player = Instantiate(playerPrefab, start.position, start.rotation);
            InstanceFinder.ServerManager.Spawn(player, conn);
        }
    }
}
