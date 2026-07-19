using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace GolfEight.Network
{
    /// GolfBall にサーバー権威の物理を持たせるための薄いアダプタ。
    /// ONLINE_DESIGN.md の「ボール＝サーバー権威、NetworkTransformで補間」に対応する。
    /// サーバー（ホスト含む）だけが Rigidbody をシミュレートし、サーバーではないクライアントは
    /// isKinematic にして自前の物理シミュレーションを止め、NetworkTransform（Inspectorで別途追加）
    /// から受け取る位置・回転をそのまま反映するだけにする（二重シミュレーションによるズレを防ぐ）。
    /// 併せて、サーバー側のボール衝突（壁破壊・プレイヤーへの吹っ飛ばし）をGolfBallのイベント経由で受け取り、
    /// 他クライアントにも同じ結果を再生させる（壁・ラグドールはNetworkObjectではないためObserversRpcで名前ベースに同期する）。
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(GolfBall))]
    public class BallNetworkSync : NetworkBehaviour
    {
        private Rigidbody body;
        private GolfBall ball;

        // サーバーが判定した「ボールが動いているか」。
        // クライアントのボールは kinematic で物理が回らないため自前では判定できず、
        // これが無いと「止まっている球だけ打てる」判定が常に成立せず打てなくなる。
        private readonly SyncVar<bool> syncedIsMoving = new SyncVar<bool>();

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ball = GetComponent<GolfBall>();
            syncedIsMoving.OnChange += OnIsMovingChanged;
        }

        private void OnIsMovingChanged(bool prev, bool next, bool asServer)
        {
            ball.SetNetworkIsMoving(next);
        }

        private void Update()
        {
            // 権威側だけが判定を進め、変化したときだけ配る（SyncVar は同値なら送信しない）。
            if (!IsServerStarted)
            {
                return;
            }
            bool moving = ball.IsMoving;
            if (syncedIsMoving.Value != moving)
            {
                syncedIsMoving.Value = moving;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // 物理を回すのも、停止判定・場外リスポーン・衝突効果を決めるのもサーバーだけ。
            // クライアントは kinematic にして位置を NetworkTransform に委ねる。
            ball.StateAuthority = IsServerStarted;
            if (!IsServerStarted)
            {
                body.isKinematic = true;
            }
            // 接続時点の値を反映しておく（以降の変化は OnChange で届く）
            ball.SetNetworkIsMoving(syncedIsMoving.Value);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ball.StateAuthority = true;
            ball.OnWallDamaged += HandleWallDamaged;
            ball.OnPlayerImpact += HandlePlayerImpact;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ball.OnWallDamaged -= HandleWallDamaged;
            ball.OnPlayerImpact -= HandlePlayerImpact;
        }

        private void HandleWallDamaged(MazeWall wall, int damage, Vector3 point, Vector3 velocity)
        {
            BroadcastWallDamage(wall.name, damage, point, velocity);
        }

        [ObserversRpc]
        private void BroadcastWallDamage(string wallName, int damage, Vector3 point, Vector3 velocity)
        {
            if (IsServerStarted)
            {
                return; // サーバー自身はGolfBall.OnCollisionEnterで既に適用済み
            }
            MazeWall wall = MazeNetworkSync.FindWallByName(wallName);
            if (wall != null)
            {
                wall.TakeDamage(damage, point, velocity);
            }
        }

        private void HandlePlayerImpact(RagdollController rc, Vector3 relativeVelocity)
        {
            NetworkObject targetNob = rc.GetComponent<NetworkObject>();
            if (targetNob == null)
            {
                return;
            }
            BroadcastPlayerImpact(targetNob, relativeVelocity);
        }

        [ObserversRpc]
        private void BroadcastPlayerImpact(NetworkObject targetPlayer, Vector3 relativeVelocity)
        {
            if (IsServerStarted || targetPlayer == null)
            {
                return; // サーバー自身はGolfBall.OnCollisionEnterで既に適用済み
            }
            RagdollController rc = targetPlayer.GetComponent<RagdollController>();
            if (rc != null)
            {
                rc.ApplyBallImpact(relativeVelocity);
            }
        }
    }
}
