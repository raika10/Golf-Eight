using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace GolfEight.Network
{
    /// PlayerController の isLocalPlayer を FishNet の所有権（IsOwner）に合わせて切り替えるだけの薄いアダプタ。
    /// ONLINE_DESIGN.md の「IsOwnerに置き換えればほぼ流用可」に対応する最小実装で、PlayerController.cs 自体は変更しない。
    /// 併せて、Player プレハブが各インスタンスに専用カメラ／AudioListenerを持つ構成のため、
    /// 所有者以外のインスタンスではそれらを無効化する（有効なままだと全クライアントで複数カメラが競合し、
    /// 誰か1人の視点だけが全員に表示されてしまう）。
    [RequireComponent(typeof(PlayerController))]
    public class PlayerNetworkSync : NetworkBehaviour
    {
        private PlayerController playerController;
        private Camera playerCamera;
        private AudioListener playerAudioListener;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerCamera = GetComponentInChildren<Camera>(includeInactive: true);
            if (playerCamera != null)
            {
                playerAudioListener = playerCamera.GetComponent<AudioListener>();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyOwnership();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            ApplyOwnership();
        }

        private void ApplyOwnership()
        {
            playerController.SetLocalPlayer(IsOwner);
            if (playerCamera != null)
            {
                playerCamera.enabled = IsOwner;
            }
            if (playerAudioListener != null)
            {
                playerAudioListener.enabled = IsOwner;
            }
        }

        // ── net-ball-hit：スイング操作をサーバー権威にする ──────────────────────
        // BallHitController（ローカル操作者のクライアントでしか動かない）はここを経由してサーバーに依頼する。
        // GolfBall/MazeWall/RagdollController 自体はFishNetを知らない既存スクリプトのまま。

        /// ボールを打つ（GolfBall.Hit）。サーバーの実体だけが物理を実行し、結果はNetworkTransformで自動同期される。
        [ServerRpc]
        public void RequestHitBall(NetworkObject ballObject, Vector3 direction, float power)
        {
            if (ballObject == null)
            {
                return;
            }
            GolfBall ball = ballObject.GetComponent<GolfBall>();
            if (ball == null)
            {
                return;
            }
            RagdollController selfRagdoll = GetComponent<RagdollController>();
            ball.Hit(direction, power, selfRagdoll);
        }

        /// スイングで前方の壁を壊す（MazeWall.TakeDamage）。壁はNetworkObjectではないため名前をキーに同期する。
        [ServerRpc]
        public void RequestBreakWall(string wallName, int damage, Vector3 impactPoint, Vector3 impactVelocity)
        {
            MazeWall wall = MazeNetworkSync.FindWallByName(wallName);
            if (wall == null)
            {
                return;
            }
            wall.TakeDamage(damage, impactPoint, impactVelocity);
            BroadcastWallDamage(wallName, damage, impactPoint, impactVelocity);
        }

        [ObserversRpc]
        private void BroadcastWallDamage(string wallName, int damage, Vector3 impactPoint, Vector3 impactVelocity)
        {
            if (IsServerStarted)
            {
                return; // サーバー自身は上のRequestBreakWallで既に適用済み
            }
            MazeWall wall = MazeNetworkSync.FindWallByName(wallName);
            if (wall != null)
            {
                wall.TakeDamage(damage, impactPoint, impactVelocity);
            }
        }

        /// スイングで相手プレイヤーを吹っ飛ばす（IKnockbackable/RagdollController.Fling）。
        [ServerRpc]
        public void RequestFlingPlayer(NetworkObject targetPlayer, Vector3 direction, float power)
        {
            if (targetPlayer == null)
            {
                return;
            }
            RagdollController rc = targetPlayer.GetComponent<RagdollController>();
            if (rc == null || rc.IsDown)
            {
                return;
            }
            ApplyFling(rc, direction, power);
            BroadcastPlayerFling(targetPlayer, direction, power);
        }

        [ObserversRpc]
        private void BroadcastPlayerFling(NetworkObject targetPlayer, Vector3 direction, float power)
        {
            if (IsServerStarted || targetPlayer == null)
            {
                return; // サーバー自身は上のRequestFlingPlayerで既に適用済み
            }
            RagdollController rc = targetPlayer.GetComponent<RagdollController>();
            if (rc != null && !rc.IsDown)
            {
                ApplyFling(rc, direction, power);
            }
        }

        private static void ApplyFling(RagdollController rc, Vector3 direction, float power)
        {
            IKnockbackable knockable = rc.GetComponent<IKnockbackable>();
            if (knockable != null)
            {
                knockable.ApplyKnockback(direction, power);
            }
            else
            {
                rc.Fling(direction * power);
            }
        }
    }
}
