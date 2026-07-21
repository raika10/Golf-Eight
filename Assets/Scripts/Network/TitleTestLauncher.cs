using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfEight.Network
{
    /// TitleScene（本実装は別担当）の代役となるテスト用ランチャ。
    /// 本物の Title UI なしで GameScene の接続フロー（NetworkBootstrap）を検証するために使う。
    /// 空の GameObject に付けると簡易 IMGUI が出る。Host/Guest を選び、名前・ポート・（ゲストなら）IPを入力して
    /// 「開始」を押すと、TitlePrefs のキーで PlayerPrefs に保存し GameScene をロードする。
    ///
    /// 接続に失敗すると NetworkBootstrap が GameState=-1 を書いてこのシーンへ戻すので、
    /// 戻ってきたときのエラー表示（本物の Title が担当する部分）の見本もここで再現している。
    ///
    /// 使い方：適当なテスト用シーン（または TitleScene）に空 GameObject を作りアタッチ。
    /// gameSceneName と、NetworkBootstrap 側の titleSceneName を Build Settings 登録名に合わせる。
    public class TitleTestLauncher : MonoBehaviour
    {
        [Tooltip("開始時にロードする対象シーン名（Build Settings に登録が必要）。")]
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("入力の初期値（実行中は画面上で変更できる）")]
        [SerializeField] private bool isHost = true;
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private string port = "7770";
        [SerializeField] private string playerName = "guest";

        // 前回 GameScene から戻ってきたときの状態（-1 なら接続失敗）。
        private bool connectionFailedLastTime;

        private void Start()
        {
            // Title の責務：戻ってきたら GameState を読み、失敗ならエラー表示する。
            // 一度読んだら通常(0)へ戻し、次回以降に持ち越さない。
            int state = PlayerPrefs.GetInt(TitlePrefs.GameStateKey, TitlePrefs.GameStateNormal);
            connectionFailedLastTime = state == TitlePrefs.GameStateConnectionFailed;
            if (connectionFailedLastTime)
            {
                TitlePrefs.SetGameState(TitlePrefs.GameStateNormal);
            }

            // 既に保存済みの値があれば入力欄の初期値として復元する（なければ上の既定を使う）。
            if (!int.TryParse(port, out int defaultPort))
            {
                defaultPort = TitlePrefs.DefaultPort;
            }
            ipAddress = PlayerPrefs.GetString(TitlePrefs.IpAddressKey, ipAddress);
            playerName = PlayerPrefs.GetString(TitlePrefs.NameKey, playerName);
            port = PlayerPrefs.GetInt(TitlePrefs.PortKey, defaultPort).ToString();
            isHost = PlayerPrefs.GetInt(TitlePrefs.IsHostKey, isHost ? 1 : 0) == 1;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 340), GUI.skin.box);
            GUILayout.Label("Title テストランチャ（GameScene 接続検証用）");

            if (connectionFailedLastTime)
            {
                Color prev = GUI.color;
                GUI.color = Color.red;
                GUILayout.Label("接続に失敗しました（GameState = -1）。設定を確認してください。");
                GUI.color = prev;
            }

            GUILayout.Space(6);
            isHost = GUILayout.Toggle(isHost, isHost ? "役割：ホスト" : "役割：ゲスト");

            GUILayout.Space(6);
            GUILayout.Label("プレイヤー名 (Name)");
            playerName = GUILayout.TextField(playerName);

            GUILayout.Label("ポート (Port)");
            port = GUILayout.TextField(port);

            // IP はゲストのときだけ意味を持つ（ホストは自分がサーバー）。
            GUI.enabled = !isHost;
            GUILayout.Label("ホストIP (IP_address) ※ゲストのみ");
            ipAddress = GUILayout.TextField(ipAddress);
            GUI.enabled = true;

            GUILayout.Space(10);
            if (GUILayout.Button(isHost ? "ホストとして開始" : "ゲストとして参加"))
            {
                SaveAndLaunch();
            }

            GUILayout.EndArea();
        }

        /// 入力値を PlayerPrefs へ保存し、GameScene をロードする（本物の Title がやることの最小版）。
        private void SaveAndLaunch()
        {
            if (!int.TryParse(port, out int portValue))
            {
                portValue = TitlePrefs.DefaultPort;
            }

            TitlePrefs.SetIsHost(isHost);
            TitlePrefs.SetPlayerName(playerName);
            TitlePrefs.SetPort(portValue);
            TitlePrefs.SetIpAddress(ipAddress);
            TitlePrefs.SetGameState(TitlePrefs.GameStateNormal); // 新しい試行を通常状態から始める
            TitlePrefs.Save();

            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogError("[TitleTestLauncher] gameSceneName が未設定です。", this);
                return;
            }
            SceneManager.LoadScene(gameSceneName);
        }
    }
}
