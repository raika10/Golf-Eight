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
        ShowGoal();
    }

    /// ゴール表示を出す。オンラインではカップイン判定がサーバーでしか走らないため、
    /// 他クライアントに対しては GameManager が ObserversRpc からこれを呼ぶ。
    public void ShowGoal()
    {
        ShowResult("ゴール！！");
    }

    /// 制限時間切れの表示を出す。誰もカップインしていないので「ゴール」とは出し分ける。
    public void ShowTimeUp()
    {
        ShowResult("タイムアップ");
    }

    /// 決着時の共通表示。何度呼ばれても同じ結果になる（サーバーは GolfHole 経由と
    /// GameManager の配信の両方から呼ばれうるため）。
    private void ShowResult(string message)
    {
        if (goalPanel != null)
            goalPanel.SetActive(true);
        if (goalText != null)
            goalText.text = message;

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