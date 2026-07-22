using UnityEngine;
using TMPro;

public class TimerUI : MonoBehaviour
{
    public TMP_Text timerText;
    public float timeLimit = 180f;
    public Color normalColor = Color.white;
    public Color warningColor = Color.red;
    public float warningTime = 30f;
    public PlayerController playerController;

    private float remainingTime;
    private bool isRunning = false;

    void Start()
    {
        timerText.text = "";
        remainingTime = timeLimit;
    }

    public void StartTimer()
    {
        remainingTime = timeLimit;
        isRunning = true;
    }

    public void StopTimer()
    {
        isRunning = false;
    }

    void Update()
    {
        if (!isRunning) return;

        remainingTime -= Time.deltaTime;

        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            isRunning = false;
            timerText.text = "Time Up!";
            timerText.color = warningColor;

            // タイムアップ後にプレイヤーを動けなくする
            if (playerController != null)
                playerController.SetActionLocked(true);

            return;
        }

        if (remainingTime <= warningTime)
        {
            timerText.color = warningColor;
        }
        else
        {
            timerText.color = normalColor;
        }

        int minutes = (int)(remainingTime / 60);
        int seconds = (int)(remainingTime % 60);
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}