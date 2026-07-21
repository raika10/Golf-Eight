using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
        private RagdollController ragdollController;
        private Camera playerCamera;
        private AudioListener playerAudioListener;

        // このプレイヤーに割り当てられたボール。サーバー（NetworkSpawner）が接続順に決めて全員へ配る。
        // 用途は「自分のボールを見分ける透過シルエット」。誰でも誰のボールを打てる仕様自体は変えない。
        private readonly SyncVar<NetworkObject> assignedBall = new SyncVar<NetworkObject>();

        // 接続順のインデックス。色（担当ボールと共通）と勝敗表示の名前に使う。
        private readonly SyncVar<int> playerIndex = new SyncVar<int>(-1);

        // このプレイヤーの表示名。所有クライアントが Title で入力した値（PlayerPrefs "Name"）を
        // サーバーへ送り、サーバーが SyncVar 経由で全員へ配る。空なら色ベースの既定名にフォールバックする。
        private readonly SyncVar<string> playerName = new SyncVar<string>(string.Empty);

        /// 接続順のインデックス（-1 は未割り当て）。勝者表示などに使う。
        public int PlayerIndex => playerIndex.Value;

        /// Title で入力されたプレイヤー名（未受信・未入力なら空文字）。勝者表示などに使う。
        public string PlayerName => playerName.Value;

        /// このプレイヤーの担当ボール。勝者判定（入ったボールの持ち主）で使う。
        public NetworkObject AssignedBall => assignedBall.Value;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            ragdollController = GetComponent<RagdollController>();
            playerCamera = GetComponentInChildren<Camera>(includeInactive: true);
            if (playerCamera != null)
            {
                playerAudioListener = playerCamera.GetComponent<AudioListener>();
            }
            // 割り当てはサーバーから遅れて届くことがあるので、届いた時点でも反映できるようにしておく
            assignedBall.OnChange += OnAssignedBallChanged;
            playerIndex.OnChange += OnPlayerIndexChanged;
        }

        /// サーバーがこのプレイヤーの接続順インデックスを設定する（NetworkSpawner から呼ぶ）。
        [Server]
        public void SetPlayerIndex(int index)
        {
            playerIndex.Value = index;
        }

        private void OnPlayerIndexChanged(int prev, int next, bool asServer)
        {
            ApplyColor();
        }

        /// 自分が操作しているプレイヤーをスタート地点へ戻す（再戦時に各クライアントが自分の分を実行する）。
        /// 位置の権威は所有者にあるので、サーバーが直接動かしても所有者の値で上書きされてしまう。
        /// そのため「各自が自分のプレイヤーを戻す」形にしている。
        public void ResetToStartPoint()
        {
            if (!IsOwner)
            {
                return;
            }

            NetworkSpawner spawner = FindFirstObjectByType<NetworkSpawner>();
            Transform start = spawner != null ? spawner.GetPlayerStart(playerIndex.Value) : null;
            if (start == null)
            {
                return;
            }

            // 吹っ飛ばされて倒れたままなら、起き上がらせつつスタート地点へ戻す
            if (ragdollController != null && ragdollController.IsDown)
            {
                ragdollController.RecoverAt(start.position);
            }

            // CharacterController が有効なままだと transform を直接動かしても戻されるので、一時的に切る
            CharacterController cc = GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null)
            {
                cc.enabled = false;
            }
            transform.SetPositionAndRotation(start.position, start.rotation);
            if (cc != null)
            {
                cc.enabled = ccWasEnabled;
            }
        }

        /// プレイヤーのモデルを担当ボールと同じ色にする。全クライアントで同じ色になる。
        private void ApplyColor()
        {
            if (playerIndex.Value < 0)
            {
                return;
            }
            Color color = PlayerColors.Get(playerIndex.Value);
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                PlayerColors.Apply(smr, color);
            }
        }

        /// サーバーがこのプレイヤーの担当ボールを設定する（NetworkSpawner から呼ぶ）。
        [Server]
        public void SetAssignedBall(NetworkObject ball)
        {
            assignedBall.Value = ball;
        }

        private void OnAssignedBallChanged(NetworkObject prev, NetworkObject next, bool asServer)
        {
            ApplyBallOwnership();
        }

        /// 自分が操作しているプレイヤーの担当ボールだけ、透過シルエットを出す。
        /// 各クライアントで所有プレイヤーは1人なので、結果として自分のボール1個だけが光る。
        private void ApplyBallOwnership()
        {
            if (!IsOwner)
            {
                return;
            }
            NetworkObject ballObject = assignedBall.Value;
            if (ballObject == null)
            {
                return;
            }

            // この端末で光らせるのは担当ボール1個だけ。他は明示的に落とす。
            // そうしないとプレハブの初期値や再割り当ての取りこぼしで複数のボールが光ってしまい、
            // 「どれが自分のボールか」という透過表示の意味が失われる。
            GolfBall mine = ballObject.GetComponent<GolfBall>();
            foreach (GolfBall ball in FindObjectsByType<GolfBall>(FindObjectsSortMode.None))
            {
                ball.SetOwnedByLocalPlayer(ball == mine);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyOwnership();
            // 壁を実際に壊すのはサーバーだけ。他端末は下の BroadcastWallDamage を受けて同じ破壊を再生する。
            ragdollController.WallDamageAuthority = IsServerStarted;
            // 割り当てが既に届いている場合はここで反映（届いていなければ OnChange 側で反映される）
            ApplyBallOwnership();
            ApplyColor();

            // 参加した時点のゲーム状態に合わせて操作ロックを反映する。
            // プレイヤーは実行時スポーンなので、GameManager 側の状態変化通知だけでは
            // 「スポーンした時点で既に開始前だった」ケースを取りこぼす。
            if (IsOwner)
            {
                GameManager gameManager = FindFirstObjectByType<GameManager>();
                if (gameManager != null)
                {
                    gameManager.ApplyLocalPlayerInputLock();
                }

                // Title で入力した自分の名前をサーバーへ知らせる。自分のプレイヤーだけが送るので、
                // 各クライアントが自分の PlayerPrefs("Name") を担当し、全員分がサーバーで揃う。
                SubmitName_ServerRpc(TitlePrefs.GetPlayerName());
            }
        }

        /// 所有クライアントが自分の表示名をサーバーへ登録する。サーバーが SyncVar で全員へ配る。
        [ServerRpc]
        private void SubmitName_ServerRpc(string name)
        {
            playerName.Value = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            ApplyOwnership();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // ラグドール由来（飛行中の貫通・着地後の骨の衝突）の壁破壊をサーバーで受け取り、全員へ配信する。
            ragdollController.WallDamageAuthority = true;
            ragdollController.OnWallDamaged += HandleRagdollWallDamaged;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ragdollController.OnWallDamaged -= HandleRagdollWallDamaged;
        }

        private void HandleRagdollWallDamaged(MazeWall wall, int damage, Vector3 impactPoint, Vector3 impactVelocity)
        {
            BroadcastWallDamage(wall.name, damage, impactPoint, impactVelocity);
        }

        private void ApplyOwnership()
        {
            playerController.SetLocalPlayer(IsOwner);
            // リモート（非所有）プレイヤーは本体位置・CharacterController を NetworkTransform（owner権威）へ委譲し、
            // ラグドールは見た目だけ再生する。これで吹っ飛ばし後の着地位置が owner のものに一本化され、
            // 撃ち合いを繰り返しても位置ズレが累積しない。
            ragdollController.NetworkRemote = !IsOwner;
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

            // 打てる状態かはサーバー側で必ず検証する。
            // クライアントのボールは kinematic で物理が回っていないため IsMoving が常に「止まっている」を返し、
            // 実際にはサーバー上で飛行中のボールでもクライアントからは打ててしまうため
            //（BallHitController の onlyHitRestingBall だけに任せられない）。
            if (ball.IsHoled || ball.IsMoving)
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

        /// スイングが当たった音（かきーん）を全端末で鳴らす。
        /// 打った本人はレスポンス優先で即ローカル再生し、他の端末へはここ経由で配る
        /// （ボール打撃の RequestHitBall は ServerRpc＝サーバーでしか走らないため、
        /// 　音を配る経路がこれとは別に必要になる）。
        [ServerRpc]
        public void RequestPlaySwingHitVoice()
        {
            BroadcastSwingHitVoice();
        }

        /// ExcludeOwner：打った本人は既にローカルで鳴らしているので二重再生しない。
        [ObserversRpc(ExcludeOwner = true)]
        private void BroadcastSwingHitVoice()
        {
            PlayerVoice voice = GetComponent<PlayerVoice>();
            if (voice != null)
            {
                voice.PlaySwingHitVoice();
            }
        }
    }
}
