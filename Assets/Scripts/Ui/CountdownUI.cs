using System.Collections;
using UnityEngine;
using TMPro;

public class CountdownUI : MonoBehaviour
{
    public TMP_Text countdownText;
    public TimerUI timerUI;

    public IEnumerator PlayCountdown()
    {
        countdownText.text = "3";
        yield return new WaitForSeconds(1f);

        countdownText.text = "2";
        yield return new WaitForSeconds(1f);

        countdownText.text = "1";
        yield return new WaitForSeconds(1f);

        countdownText.text = "Start!";
        yield return new WaitForSeconds(1f);

        countdownText.text = "";

        // カウントダウンが終わったらタイマーを開始
        timerUI.StartTimer();
    }
}