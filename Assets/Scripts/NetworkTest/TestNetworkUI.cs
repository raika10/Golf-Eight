using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

/// FishNet 接続テスト用 OnGUI。NetworkManager と同じ GameObject にアタッチする。
public class TestNetworkUI : MonoBehaviour
{
    [SerializeField] private string serverAddress = "localhost";

    private NetworkManager _nm;
    private LocalConnectionState _serverState = LocalConnectionState.Stopped;
    private LocalConnectionState _clientState = LocalConnectionState.Stopped;

    private void Start()
    {
        _nm = GetComponent<NetworkManager>();
        if (_nm == null)
            _nm = FindObjectOfType<NetworkManager>();

        _nm.ServerManager.OnServerConnectionState += args => _serverState = args.ConnectionState;
        _nm.ClientManager.OnClientConnectionState += args => _clientState = args.ConnectionState;
    }

    private void OnGUI()
    {
        if (_nm == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 220, 200));
        GUILayout.BeginVertical("box");
        GUILayout.Label("=== FishNet 接続テスト ===");
        GUILayout.Label("Server: " + _serverState);
        GUILayout.Label("Client: " + _clientState);
        GUILayout.Space(6);

        bool serverOn = _serverState == LocalConnectionState.Started;
        bool clientOn = _clientState == LocalConnectionState.Started;

        if (!serverOn && !clientOn)
        {
            if (GUILayout.Button("Host 起動 (Server+Client)"))
            {
                _nm.ServerManager.StartConnection();
                _nm.ClientManager.StartConnection();
            }
            if (GUILayout.Button("Server のみ起動"))
                _nm.ServerManager.StartConnection();
            if (GUILayout.Button("Client として接続"))
                _nm.ClientManager.StartConnection(serverAddress);
        }
        else
        {
            if (serverOn && GUILayout.Button("Server 停止"))
                _nm.ServerManager.StopConnection(true);
            if (clientOn && GUILayout.Button("Client 切断"))
                _nm.ClientManager.StopConnection();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
