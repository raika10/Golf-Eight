using UnityEngine;
using TMPro;

public class LobbyManager : MonoBehaviour
{
    public TMP_Text roomIDText;

    void Start()
    {
        // 初期表示
        roomIDText.text = "Room ID: 待機中...";
    }

    // 通信担当がサーバからIDを受け取ったらこのメソッドを呼ぶ
    public void SetRoomID(string roomID)
    {
        roomIDText.text = "Room ID: " + roomID;
    }
}
