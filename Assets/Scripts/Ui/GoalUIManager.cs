using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GoalUIManager : MonoBehaviour
{
    public GameObject goalPanel;
    public TMP_Text goalText;
    public PlayerController playerController;
    public TimerUI timerUI;


    void Start()
    {
        if (goalPanel != null)
            goalPanel.SetActive(false);
    }

    // GolfHoleのonBallHoledから呼ばれる
    public void OnBallHoled(GolfBall ball)
    {
        // オンラインでは GameManager が勝者を判定して全員へ配るので、ここでは何もしない。
        // 両方走らせると、先に「ゴール！！」が出てから勝者表示で上書きされてちらつく。
        if (FindFirstObjectByType<GolfEight.Network.GameManager>() != null)
        {
            return;
        }
        ShowGoal();
    }

    /// ゴール表示を出す。オンラインではカップイン判定がサーバーでしか走らないため、
    /// 他クライアントに対しては GameManager が ObserversRpc からこれを呼ぶ。
    public void ShowGoal()
    {
        ShowResult("ゴール！！");
    }

    /// 勝者を表示する。勝者はカップインしたボールの持ち主。
    /// winnerIndex が負なら持ち主を特定できなかった場合なので、単に「ゴール！！」に留める。
    public void ShowWinner(int winnerIndex)
    {
        if (winnerIndex < 0)
        {
            ShowGoal();
            return;
        }
        ShowResult(ResolveWinnerLabel(winnerIndex) + " の勝ち！",
                   GolfEight.Network.PlayerColors.Get(winnerIndex));
    }

    /// 勝者の表示名を決める。Title で入力した名前があればそれを、
    /// 無ければ従来どおり色ベースの既定名（「プレイヤー1（赤）」）を使う。
    private string ResolveWinnerLabel(int winnerIndex)
    {
        foreach (var sync in FindObjectsByType<GolfEight.Network.PlayerNetworkSync>(FindObjectsSortMode.None))
        {
            if (sync.PlayerIndex == winnerIndex && !string.IsNullOrEmpty(sync.PlayerName))
            {
                return sync.PlayerName;
            }
        }
        return GolfEight.Network.PlayerColors.GetDisplayName(winnerIndex);
    }

    /// 制限時間切れの表示を出す。誰もカップインしていないので「ゴール」とは出し分ける。
    public void ShowTimeUp()
    {
        ShowResult("タイムアップ");
    }

    /// 決着表示を消してロビー状態に戻す（再戦時に GameManager から呼ぶ）。
    /// 行動ロックはゲーム状態が決めるので、ここでは解除しない。
    public void HideResult()
    {
        if (goalPanel != null)
            goalPanel.SetActive(false);
    }

    /// 決着時の共通表示。何度呼ばれても同じ結果になる（サーバーは GolfHole 経由と
    /// GameManager の配信の両方から呼ばれうるため）。
    /// color を渡すと文字色を勝者の色に合わせる（渡さなければ白）。
    private void ShowResult(string message, Color? color = null)
    {
        if (goalPanel != null)
            goalPanel.SetActive(true);
        if (goalText != null)
        {
            goalText.text = message;
            goalText.color = color ?? Color.white;
        }

        // 決着後はプレイヤーを動けなくする
        PlayerController target = ResolvePlayerController();
        if (target != null)
            target.SetActionLocked(true);

        // タイマーを止める
        if (timerUI != null)
            timerUI.StopTimer();
    }

    /// 行動をロックする対象を決める。Inspector で指定されていればそれを、
    /// 未設定なら「この端末が操作しているプレイヤー」を探す。
    /// オンラインではプレイヤーが実行時にスポーンするため Inspector から事前指定できず、
    /// かつ他人のプレイヤーをロックしてはいけないので、所有しているものだけを対象にする。
    private PlayerController ResolvePlayerController()
    {
        if (playerController != null)
            return playerController;

        foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (pc.IsLocalPlayer)
                return pc;
        }
        return null;
    }

    public void OnNextMatch()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}