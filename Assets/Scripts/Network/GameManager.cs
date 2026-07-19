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

        public override void OnStartServer()
        {
            base.OnStartServer();
            state.Value = GameState.Lobby;

            // カップイン判定はサーバーだけが状態遷移の起点になる。
            // クライアント側でも GolfHole の見た目（ボールが落ちる演出）は各自ローカルで独立に再生されるので、
            // ここでの購読はサーバーの勝敗判定専用（二重に状態遷移させないためサーバーのみ購読する）。
            foreach (GolfHole hole in golfHoles)
            {
                if (hole != null)
                {
                    hole.onBallHoled.AddListener(OnBallHoled);
                }
            }
        }

        /// ロビー状態からゲーム開始をリクエストする（NetworkSpawner やデバッグ用UIから呼ぶ想定）。
        [Server]
        public void RequestStartGame()
        {
            if (state.Value != GameState.Lobby)
            {
                return;
            }
            StartCoroutine(ServerGameFlow());
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
