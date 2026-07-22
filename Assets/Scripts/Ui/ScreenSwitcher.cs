using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class ScreenSwitcher : MonoBehaviour
{
    public GameObject titlePanel;
    public GameObject roomPanel;
    public GameObject lobbyPanel;
    public GameObject joinPanel;

    public TMP_InputField nameInputField;
    public TMP_InputField portInputField;
    public TMP_InputField ipInputField;
    public TMP_Text errorText;
    public TMP_InputField portInputField_host;

    public string GameSceneName = "GameScene";

    void Start()
    {
        // 前回の入力値をInputFieldに反映
        if (nameInputField != null)
            nameInputField.text = PlayerPrefs.GetString("Name", "");
        if (ipInputField != null)
            ipInputField.text = PlayerPrefs.GetString("IP_address", "");
        if (portInputField != null)
            portInputField.text = PlayerPrefs.GetInt("Port", 7770).ToString();
        if (portInputField_host != null)
            portInputField_host.text = PlayerPrefs.GetInt("Port", 7770).ToString();

        // GameState=-1なら接続失敗→RoomPanelを開いてエラー表示
        if (PlayerPrefs.GetInt("GameState", 0) == -1)
        {
            ShowRoom();
            if (errorText != null)
                errorText.text = "接続に失敗しました";
            PlayerPrefs.SetInt("GameState", 0);
        }
        else
        {
            ShowTitle();
        }
    }

    private string GetName()
    {
        string name = nameInputField != null ? nameInputField.text.Trim() : "";
        return string.IsNullOrEmpty(name) ? "Guest" : name;
    }

    public void ShowTitle()
    {
        titlePanel.SetActive(true);
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
        joinPanel.SetActive(false);
    }

    public void ShowRoom()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(true);
        lobbyPanel.SetActive(false);
        joinPanel.SetActive(false);
    }

    public void ShowLobby()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
        joinPanel.SetActive(false);
    }

    public void ShowJoin()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(false);
        joinPanel.SetActive(true);
        lobbyPanel.SetActive(false);
    }

    public void ShowRoomFromJoin()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(true);
        joinPanel.SetActive(false);
        lobbyPanel.SetActive(false);
    }

    public void ShowRoomFromLobby()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(true);
        lobbyPanel.SetActive(false);
        joinPanel.SetActive(false);
    }

    // ホストになる
    public void StartHost()
    {
        PlayerPrefs.SetString("Name", GetName());
        PlayerPrefs.SetInt("IsHost", 1);
        PlayerPrefs.SetInt("GameState", 0);
        // ポート番号を保存（入力が無い場合はデフォルト値7770）
        PlayerPrefs.SetInt("Port", portInputField_host != null ? int.TryParse(portInputField_host.text.Trim(), out int p) ? p : 7770 : 7770);
        PlayerPrefs.Save();
        SceneManager.LoadScene(GameSceneName);
    }

    // ゲストとして参加する
    public void StartJoin(string ipAddress)
    {
        string portStr = portInputField != null ? portInputField.text.Trim() : "7770";
        int port = int.TryParse(portStr, out int p) ? p : 7770;

        PlayerPrefs.SetString("Name", GetName());
        PlayerPrefs.SetString("IP_address", ipAddress);
        PlayerPrefs.SetInt("Port", port);
        PlayerPrefs.SetInt("IsHost", 0);
        PlayerPrefs.SetInt("GameState", 0);
        PlayerPrefs.Save();
        Debug.Log($"Joining game at {ipAddress}:{port} as {GetName()}");
        SceneManager.LoadScene(GameSceneName);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}