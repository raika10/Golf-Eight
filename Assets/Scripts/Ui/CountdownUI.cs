using System.Collections;
using UnityEngine;
using TMPro;

public class CountdownUI : MonoBehaviour
{
    public TMP_Text countdownText;
    public TimerUI timerUI;
    [Tooltip("行動をロックする対象。未設定なら、この端末が操作しているプレイヤーを実行時に探す\n（オンラインではプレイヤーが実行時スポーンなのでInspectorから指定できないため）")]
    public PlayerController playerController;

    // サーバー（GameManager）が ObserversRpc から呼ぶので public。
    // Start() での自動再生はしない：サーバー主導の開始と二重に走ってしまうため。
    public IEnumerator PlayCountdown()
    {
        // カウントダウン中はプレイヤーを動けなくする
        PlayerController target = ResolvePlayerController();
        if (target != null)
            target.SetActionLocked(true);

        countdownText.text = "3"; yield return new WaitForSeconds(1f);
        countdownText.text = "2"; yield return new WaitForSeconds(1f);
        countdownText.text = "1"; yield return new WaitForSeconds(1f);
        countdownText.text = "Start!"; yield return new WaitForSeconds(1f);
        countdownText.text = "";

        // カウントダウン終了後に動けるようにする
        if (target != null)
            target.SetActionLocked(false);

        timerUI.StartTimer();
    }

    /// 行動をロックする対象を決める。Inspector で指定されていればそれを、
    /// 未設定なら「この端末が操作しているプレイヤー」を探す。
    /// オンラインではプレイヤーが実行時にスポーンするため Inspector から事前指定できず、
    /// かつ他人のプレイヤーをロックしてはいけないので、所有しているものだけを対象にする。
    private PlayerController ResolvePlayerController()
    {
        if (playerController != null)
        {
            return playerController;
        }
        foreach (PlayerController pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (pc.IsLocalPlayer)
            {
                return pc;
            }
        }
        return null;
    }
}