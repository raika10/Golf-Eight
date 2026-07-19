using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace GolfEight.Network
{
    /// サーバー専用のスポーン係。サーバー起動時にボールを、クライアント接続ごとにプレイヤーをスポーンする。
    /// プレイヤーは接続してきた順に別々のスタート地点へ配置する（net-player-spawn）。
    /// 割り当ての判断はサーバー側だけで行うので、クライアントが自分の位置を決めることはない。
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

        // スポーン済みのボール。接続してきたプレイヤーへ順に「担当ボール」として割り当てる
        //（自分のボールを見分ける透過シルエット用。誰でも誰のボールを打てる仕様自体は変えない）。
        private readonly List<NetworkObject> spawnedBalls = new List<NetworkObject>();

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

                NetworkObject ballObject = ball.GetComponent<NetworkObject>();
                if (ballObject != null)
                {
                    // 何番目のボールかで色を決める。プレイヤーには同じ番号のボールを割り当てるので、
                    // 「赤い人の球は赤」と一致する。
                    BallNetworkSync ballSync = ball.GetComponent<BallNetworkSync>();
                    if (ballSync != null)
                    {
                        ballSync.SetColorIndex(spawnedBalls.Count);
                    }
                    spawnedBalls.Add(ballObject);
                }
            }
        }

        private void SpawnPlayer(NetworkConnection conn)
        {
            if (playerPrefab == null || playerStartPoints == null || playerStartPoints.Length == 0)
            {
                Debug.LogWarning("[NetworkSpawner] playerPrefab / playerStartPoints が未設定です。", this);
                return;
            }

            // 何人目の接続かを確定させ、スタート地点とボールの両方の割り当てに使う。
            int playerIndex = nextPlayerSpawnIndex;
            nextPlayerSpawnIndex++;

            // 接続順に別々のスタート地点を割り当てる。地点が足りなければ先頭から巡回して使い回す。
            Transform start = playerStartPoints[playerIndex % playerStartPoints.Length];
            if (start == null)
            {
                Debug.LogWarning("[NetworkSpawner] playerStartPoints の要素に未設定(null)があります。", this);
                return;
            }

            GameObject player = Instantiate(playerPrefab, start.position, start.rotation);
            InstanceFinder.ServerManager.Spawn(player, conn);

            AssignPlayerIdentity(player, playerIndex);
        }

        /// 全ボールをスポーン地点へ戻して初期状態にする（再戦時にサーバーから呼ぶ）。
        /// スポーンし直さないのは、ボールに紐づく色や担当プレイヤーの割り当てを維持するため。
        public void ResetBallsForNewMatch()
        {
            if (ballSpawnPoints == null || ballSpawnPoints.Length == 0)
            {
                return;
            }
            for (int i = 0; i < spawnedBalls.Count; i++)
            {
                if (spawnedBalls[i] == null)
                {
                    continue;
                }
                Transform point = ballSpawnPoints[i % ballSpawnPoints.Length];
                GolfBall ball = spawnedBalls[i].GetComponent<GolfBall>();
                if (point != null && ball != null)
                {
                    ball.ResetForNewMatch(point.position);
                }
            }
        }

        /// 接続順に対応するスタート地点。再戦でプレイヤーを戻すときに各クライアントが参照する。
        /// スタート地点はシーンに置かれているので、クライアントからも同じものが引ける。
        public Transform GetPlayerStart(int playerIndex)
        {
            if (playerStartPoints == null || playerStartPoints.Length == 0 || playerIndex < 0)
            {
                return null;
            }
            return playerStartPoints[playerIndex % playerStartPoints.Length];
        }

        /// 接続順に応じて、プレイヤーの識別（色・表示名）と担当ボールを割り当てる。
        /// 担当ボールの用途は「自分のボールを見分ける透過シルエット」と勝者判定で、
        /// 打てるボールを制限するものではない（誰でも誰のボールを打てる）。
        private void AssignPlayerIdentity(GameObject player, int playerIndex)
        {
            PlayerNetworkSync sync = player.GetComponent<PlayerNetworkSync>();
            if (sync == null)
            {
                return;
            }

            // 色と表示名はボールの有無に関係なく決まるので先に渡す
            sync.SetPlayerIndex(playerIndex);

            if (spawnedBalls.Count == 0)
            {
                return;
            }
            NetworkObject ball = spawnedBalls[playerIndex % spawnedBalls.Count];
            if (ball != null)
            {
                sync.SetAssignedBall(ball);
            }
        }
    }
}
