using UnityEngine;

public class ScreenSwitcher : MonoBehaviour
{
    public GameObject titlePanel;
    public GameObject roomPanel;
    public GameObject lobbyPanel;
    public GameObject joinPanel;

    void Start()
    {
        ShowTitle();
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

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}