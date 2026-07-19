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
        goalPanel.SetActive(false);
    }

    // GolfHoleのonBallHoledから呼ばれる
    public void OnBallHoled(GolfBall ball)
    {
        goalPanel.SetActive(true);
        goalText.text = "ゴール！！";

        // ゴール時にプレイヤーを動けなくする
        if (playerController != null)
            playerController.SetActionLocked(true);

        // タイマーを止める
        if (timerUI != null)
            timerUI.StopTimer();
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