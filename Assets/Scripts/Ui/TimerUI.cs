using UnityEngine;
using TMPro;

public class TimerUI : MonoBehaviour
{
    public TMP_Text timerText;
    public float timeLimit = 300f;    // 制限時間（秒）デフォルト5分
    public Color normalColor = Color.white;   // 通常時の色
    public Color warningColor = Color.red;    // 残り30秒の色
    public float warningTime = 30f;           // 警告を出す残り時間

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

        // 残り時間が0になったらタイムアップ
        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            isRunning = false;
            timerText.text = "Time Up!";
            timerText.color = warningColor;
            return;
        }

        // 残り30秒以下で赤くなる
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