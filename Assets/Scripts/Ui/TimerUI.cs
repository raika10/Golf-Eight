using UnityEngine;
using TMPro;

public class TimerUI : MonoBehaviour
{
    public TMP_Text timerText;
    private float elapsedTime = 0f;
    private bool isRunning = false;

    void Start()
    {
        timerText.text = "";  // 最初は非表示
    }

    public void StartTimer()
    {
        elapsedTime = 0f;
        isRunning = true;
    }

    void Update()
    {
        if (!isRunning) return;

        elapsedTime += Time.deltaTime;

        int minutes = (int)(elapsedTime / 60);
        int seconds = (int)(elapsedTime % 60);

        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}