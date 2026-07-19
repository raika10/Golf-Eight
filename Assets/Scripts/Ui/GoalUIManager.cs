using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class GoalUIManager : MonoBehaviour
{
    public GameObject goalPanel;
    public TMP_Text goalText;

    void Start()
    {
        goalPanel.SetActive(false);
    }

    // GolfHoleのonBallHoledから呼ばれる
    public void OnBallHoled(GolfBall ball)
    {
        goalPanel.SetActive(true);
        goalText.text = "ゴール！！";
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