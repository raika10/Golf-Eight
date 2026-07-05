using UnityEngine;

public class ScreenSwitcher : MonoBehaviour
{
    public GameObject titlePanel;
    public GameObject roomPanel;
    public GameObject lobbyPanel;

    void Start()
    {
        // 最初はタイトル画面だけ表示
        ShowTitle();
    }

    public void ShowTitle()
    {
        titlePanel.SetActive(true);
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(false);
    }

    public void ShowRoom()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(true);
        lobbyPanel.SetActive(false);
    }

    public void ShowLobby()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(false);
        lobbyPanel.SetActive(true);
    }

    public void ShowRoomFromLobby()
    {
        titlePanel.SetActive(false);
        roomPanel.SetActive(true);
        lobbyPanel.SetActive(false);
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