using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Net;

public class RoomJoinValidator : MonoBehaviour
{
    public TMP_InputField ipInputField;
    public Button joinButton;
    public TMP_Text errorText;

    void Start()
    {
        errorText.text = "";
        joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    void OnJoinButtonClicked()
    {
        string inputIP = ipInputField.text.Trim();

        // 空文字チェック
        if (string.IsNullOrEmpty(inputIP))
        {
            errorText.text = "IPアドレスを入力してください";
            return;
        }

        // IPアドレス形式チェック
        if (!IPAddress.TryParse(inputIP, out _))
        {
            errorText.text = "正しいIPアドレスを入力してください";
            return;
        }

        // チェックを通過したらGameSceneへ
        errorText.text = "";
        FindObjectOfType<ScreenSwitcher>().StartJoin(inputIP);
    }
}