using System.Collections;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace GolfEight.Network
{
    /// ゲーム全体の状態機械（Lobby→Countdown→Playing→Finished）をサーバー権威で進める。
    /// 既存の TimerUI / CountdownUI はローカルの見た目駆動のまま流用し、ObserversRpc で開始タイミングだけ全クライアントに揃える。
    public class GameManager : NetworkBehaviour
    {
        public enum GameState
        {
            Lobby,
            Countdown,
            Playing,
            Finished,
        }

        [Header("時間")]
        [SerializeField] private float gameTimeLimit = 300f;   // 制限時間（秒）
        [SerializeField] private float countdownDuration = 4f; // CountdownUI の "3","2","1","Start!" 分（秒）。CountdownUI 自体は変更せず、サーバーの待ち時間をこれに合わせる

        [Header("参照（既存スクリプトをそのまま駆動する）")]
        [SerializeField] private TimerUI timerUI;
        [SerializeField] private CountdownUI countdownUI;
        [SerializeField] private GolfHole[] golfHoles;

        private readonly SyncVar<GameState> state = new SyncVar<GameState>(GameState.Lobby);

        /// 現在のゲーム状態（全クライアントで参照可能）。
        public GameState State => state.Value;

        private void Awake()
        {
            state.OnChange += OnStateChanged;
        }

        private void OnStateChanged(GameState prev, GameState next, bool asServer)
        {
            ApplyLocalPlayerInputLock();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyLocalPlayerInputLock();
        }

        /// ゲームが始まる（Playing になる）まで、この端末で操作しているプレイヤーの行動を止める。
        /// ロビーで勝手に歩き回ったり、カウントダウン中に動いたりできないようにするため。
        /// Playing 以外（Lobby / Countdown / Finished）はすべてロック対象。
        /// プレイヤーは実行時にスポーンするので、スポーン側（PlayerNetworkSync）からも呼ぶ。
        public void ApplyLocalPlayerInputLock()
        {
            bool locked = state.Value != GameState.Playing;
            foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (pc.IsLocalPlayer)
                {
                    pc.SetActionLocked(locked);
                    return;
                }
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            state.Value = GameState.Lobby;
            HookHoles();
        }

        /// ロビー状態からゲーム開始をリクエストする（NetworkSpawner やデバッグ用UIから呼ぶ想定）。
        [Server]
        public void RequestStartGame()
        {
            if (state.Value != GameState.Lobby)
            {
                return;
            }

            // カップは迷路と一緒に実行時生成されるため、接続後・開始時点で改めて拾い直す
            //（Inspector の golfHoles では実行時生成のカップを参照できない）。
            HookHoles();
            StartCoroutine(ServerGameFlow());
        }

        /// カップイン通知を購読する。カップは MazeGenerator がゴールのマスに実行時生成するので、
        /// シーンから探して拾う（Inspector で明示指定されたものも併せて対象にする）。
        /// カップイン判定そのものはサーバーでしか走らない（クライアントのボールは kinematic で
        /// GolfHole.HandleBall が即 return するため）。ここでの購読はサーバーの勝敗判定専用。
        [Server]
        private void HookHoles()
        {
            foreach (GolfHole hole in FindObjectsByType<GolfHole>(FindObjectsSortMode.None))
            {
                // 二重登録を避けるため、必ず外してから付ける
                hole.onBallHoled.RemoveListener(OnBallHoled);
                hole.onBallHoled.AddListener(OnBallHoled);
            }

            if (golfHoles == null)
            {
                return;
            }
            foreach (GolfHole hole in golfHoles)
            {
                if (hole != null)
                {
                    hole.onBallHoled.RemoveListener(OnBallHoled);
                    hole.onBallHoled.AddListener(OnBallHoled);
                }
            }
        }

        private IEnumerator ServerGameFlow()
        {
            state.Value = GameState.Countdown;
            StartCountdown_ObserversRpc();
            yield return new WaitForSeconds(countdownDuration);

            // カウントダウン中に誰かがゲームを終わらせるような操作は無い前提だが、念のため状態を確認する
            if (state.Value != GameState.Countdown)
            {
                yield break;
            }

            state.Value = GameState.Playing;
            StartTimer_ObserversRpc();
            yield return new WaitForSeconds(gameTimeLimit);

            if (state.Value == GameState.Playing)
            {
                FinishGame();
            }
        }

        private void OnBallHoled(GolfBall ball)
        {
            if (state.Value != GameState.Playing)
            {
                return;
            }
            FinishGame();
        }

        [Server]
        private void FinishGame()
        {
            state.Value = GameState.Finished;
            StopTimer_ObserversRpc();
            ShowGoal_ObserversRpc();
        }

        /// ゴール表示を全クライアントへ配る。
        /// カップイン判定はサーバーでしか走らない（クライアントのボールは kinematic）ため、
        /// これが無いとホストの画面にしかゴールUIが出ない。
        [ObserversRpc]
        private void ShowGoal_ObserversRpc()
        {
            if (IsServerStarted)
            {
                return; // サーバーは GolfHole から直接 GoalUIManager が呼ばれて表示済み
            }
            GoalUIManager goalUI = FindFirstObjectByType<GoalUIManager>();
            if (goalUI != null)
            {
                goalUI.ShowGoal();
            }
        }

        [ObserversRpc]
        private void StartCountdown_ObserversRpc()
        {
            if (countdownUI != null)
            {
                countdownUI.StartCoroutine(countdownUI.PlayCountdown());
            }
        }

        [ObserversRpc]
        private void StartTimer_ObserversRpc()
        {
            if (timerUI != null)
            {
                timerUI.StartTimer();
            }
        }

        [ObserversRpc]
        private void StopTimer_ObserversRpc()
        {
            if (timerUI != null)
            {
                timerUI.StopTimer();
            }
        }
    }
}
