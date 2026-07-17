using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RoomJoinValidator : MonoBehaviour
{
    public TMP_InputField roomIDInputField;
    public Button joinButton;
    public TMP_Text errorText;

    // 有効なルームIDのリスト（通信担当が後で実装）
    // 今はテスト用に仮のIDを設定
    private string[] validRoomIDs = { "AB12", "CD34", "EF56" };

    void Start()
    {
        errorText.text = "";
        joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    void OnJoinButtonClicked()
    {
        string inputID = roomIDInputField.text.Trim();

        // 空文字チェック
        if (string.IsNullOrEmpty(inputID))
        {
            errorText.text = "ルームIDを入力してください";
            return;
        }

        // 文字列の形式チェック（英数字のみ、4文字）
        if (inputID.Length != 4 || !IsAlphanumeric(inputID))
        {
            errorText.text = "ルームIDは英数字4文字で入力してください";
            return;
        }

        // 存在チェック（通信担当が後でサーバーチェックに変更）
        if (!IsValidRoomID(inputID))
        {
            errorText.text = "そのルームIDは存在しません";
            return;
        }

        // 全てのチェックを通過したらロビーへ
        errorText.text = "";
        FindObjectOfType<ScreenSwitcher>().ShowLobby();
    }

    bool IsAlphanumeric(string str)
    {
        foreach (char c in str)
        {
            if (!char.IsLetterOrDigit(c))
                return false;
        }
        return true;
    }

    bool IsValidRoomID(string roomID)
    {
        foreach (string validID in validRoomIDs)
        {
            if (validID == roomID.ToUpper())
                return true;
        }
        return false;
    }
}