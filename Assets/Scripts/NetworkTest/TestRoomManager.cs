using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Scene スタッキングを使った部屋テスト。
/// 部屋ごとに TestRoomScene の別インスタンスをロードし、
/// プレイヤーをその Scene に移動することで完全な分離を実現する。
/// NetworkObject がアタッチされた GameObject にこのスクリプトを追加する。
public class TestRoomManager : NetworkBehaviour
{
    [SerializeField] private TestPlayerSpawner spawner;

    // ---- サーバー専有データ ----
    // 部屋名 → Sceneインスタンス（スタッキングで複数存在できる）
    private readonly Dictionary<string, Scene> _roomScenes = new Dictionary<string, Scene>();
    // 部屋名 → 最大人数
    private readonly Dictionary<string, int> _maxPlayers = new Dictionary<string, int>();
    // 部屋名 → 現在人数
    private readonly Dictionary<string, int> _playerCounts = new Dictionary<string, int>();
    // 部屋作成中のリクエスト（OnLoadEnd で部屋名を特定するために使う）
    private readonly Dictionary<string, NetworkConnection> _pendingCreators = new Dictionary<string, NetworkConnection>();

    // ---- クライアント専有データ ----
    private readonly List<string> _roomListDisplay = new List<string>();
    private string _localRoomName = "";
    private string _newRoomInput = "Room1";
    private string _joinRoomInput = "";
    private string _statusText = "";
    private string _currentSceneName = "";

    // ---- ライフサイクル ----

    public override void OnStartServer()
    {
        base.OnStartServer();
        InstanceFinder.NetworkManager.SceneManager.OnLoadEnd += OnSceneLoadEnd;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (InstanceFinder.NetworkManager != null)
            InstanceFinder.NetworkManager.SceneManager.OnLoadEnd -= OnSceneLoadEnd;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        InstanceFinder.NetworkManager.SceneManager.OnLoadEnd += OnClientSceneLoaded;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (InstanceFinder.NetworkManager != null)
            InstanceFinder.NetworkManager.SceneManager.OnLoadEnd -= OnClientSceneLoaded;
    }

    // ---- OnGUI ----

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(240, 10, 260, 420));
        GUILayout.BeginVertical("box");
        GUILayout.Label("=== 部屋テスト (Scene分離) ===");

        if (IsServerStarted)
            GUILayout.Label("[SERVER] 部屋数: " + _roomScenes.Count);

        if (IsClientStarted)
        {
            GUILayout.Label("現在のScene: " + _currentSceneName);
            GUILayout.Label("現在の部屋: " + (_localRoomName == "" ? "ロビー" : _localRoomName));
            GUILayout.Space(4);

            if (_localRoomName == "")
            {
                GUILayout.Label("部屋名 (作成):");
                _newRoomInput = GUILayout.TextField(_newRoomInput);
                if (GUILayout.Button("部屋を作成して入場"))
                    CreateRoomServerRpc(_newRoomInput, 4);

                GUILayout.Space(4);
                GUILayout.Label("部屋名 (入場):");
                _joinRoomInput = GUILayout.TextField(_joinRoomInput);
                if (GUILayout.Button("入場"))
                    JoinRoomServerRpc(_joinRoomInput);
            }
            else
            {
                if (GUILayout.Button("退室してロビーへ"))
                    LeaveRoomServerRpc(_localRoomName);
            }

            if (GUILayout.Button("部屋一覧を更新"))
                RequestRoomListServerRpc();

            GUILayout.Space(4);
            GUILayout.Label("--- 部屋一覧 ---");
            foreach (string entry in _roomListDisplay)
                GUILayout.Label("  " + entry);

            if (_statusText != "")
            {
                GUILayout.Space(4);
                GUILayout.Label(_statusText);
            }
        }
        else
        {
            GUILayout.Label("(未接続)");
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    // ---- ServerRpc: クライアント → サーバー ----

    [ServerRpc(RequireOwnership = false)]
    private void CreateRoomServerRpc(string roomName, int max, NetworkConnection sender = null)
    {
        if (_roomScenes.ContainsKey(roomName))
        {
            SendStatusTargetRpc(sender, roomName + " はすでに存在します");
            return;
        }
        if (_pendingCreators.ContainsKey(roomName))
        {
            SendStatusTargetRpc(sender, roomName + " は作成中です");
            return;
        }

        _pendingCreators[roomName] = sender;
        _maxPlayers[roomName] = max;
        _playerCounts[roomName] = 0;

        // 同じSceneを別インスタンスとしてロード（Scene スタッキング）
        // ServerParams[0] に部屋名を埋め込み、OnLoadEnd で識別する
        SceneLoadData sld = new SceneLoadData("TestRoomScene");
        sld.Options.AllowStacking = true;
        sld.ReplaceScenes = ReplaceOption.None;
        sld.Params.ServerParams = new object[] { roomName };

        InstanceFinder.NetworkManager.SceneManager.LoadConnectionScenes(sender, sld);
        Debug.Log("[Server] Scene ロード開始: " + roomName);
    }

    [ServerRpc(RequireOwnership = false)]
    private void JoinRoomServerRpc(string roomName, NetworkConnection sender = null)
    {
        if (!_roomScenes.TryGetValue(roomName, out Scene scene))
        {
            SendStatusTargetRpc(sender, roomName + " が見つかりません");
            return;
        }
        if (_playerCounts[roomName] >= _maxPlayers[roomName])
        {
            SendStatusTargetRpc(sender, roomName + " は満員です");
            return;
        }

        // 既存のSceneインスタンスへ接続者を移動（handle指定でスタックの中から特定の部屋を選ぶ）
        SceneLoadData sld = new SceneLoadData(scene);
        sld.ReplaceScenes = ReplaceOption.None;

        InstanceFinder.NetworkManager.SceneManager.LoadConnectionScenes(sender, sld);

        _playerCounts[roomName]++;

        // 部屋のSceneインスタンスにプレイヤーをスポーン（Room隔離の核心）
        if (spawner != null)
            spawner.SpawnInRoom(sender, scene);

        SendJoinedTargetRpc(sender, roomName);
        PushRoomListObserversRpc(BuildRoomListCsv());
        Debug.Log("[Server] 入場: " + roomName + " conn=" + (sender != null ? sender.ClientId.ToString() : "?"));
    }

    [ServerRpc(RequireOwnership = false)]
    private void LeaveRoomServerRpc(string roomName, NetworkConnection sender = null)
    {
        if (_roomScenes.ContainsKey(roomName) && _playerCounts[roomName] > 0)
            _playerCounts[roomName]--;

        // 退室するプレイヤーのオブジェクトを削除
        if (spawner != null && sender != null)
            spawner.DespawnForConnection(sender);

        // 部屋シーンから接続を外す。
        // これをしないと退室者は部屋シーンのObserverのままで、他プレイヤーが見え続ける。
        if (_roomScenes.TryGetValue(roomName, out Scene roomScene))
        {
            SceneUnloadData sud = new SceneUnloadData(roomScene);
            InstanceFinder.NetworkManager.SceneManager.UnloadConnectionScenes(sender, sud);
        }

        // ロビーSceneへ戻す（TestNetworkScene がロビー）
        SceneLoadData sld = new SceneLoadData("TestNetworkScene");
        sld.ReplaceScenes = ReplaceOption.None;
        InstanceFinder.NetworkManager.SceneManager.LoadConnectionScenes(sender, sld);

        SendLeftTargetRpc(sender);
        PushRoomListObserversRpc(BuildRoomListCsv());
        Debug.Log("[Server] 退室: " + roomName);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestRoomListServerRpc(NetworkConnection sender = null)
    {
        SendRoomListTargetRpc(sender, BuildRoomListCsv());
    }

    // ---- ObserversRpc / TargetRpc: サーバー → クライアント ----

    [ObserversRpc]
    private void PushRoomListObserversRpc(string csv)
    {
        ApplyRoomList(csv);
    }

    [TargetRpc]
    private void SendRoomListTargetRpc(NetworkConnection conn, string csv)
    {
        ApplyRoomList(csv);
    }

    [TargetRpc]
    private void SendStatusTargetRpc(NetworkConnection conn, string message)
    {
        _statusText = message;
        Debug.Log("[Client] " + message);
    }

    [TargetRpc]
    private void SendJoinedTargetRpc(NetworkConnection conn, string roomName)
    {
        _localRoomName = roomName;
        _statusText = roomName + " に入場しました";
        Debug.Log("[Client] " + _statusText);
    }

    [TargetRpc]
    private void SendCreatedTargetRpc(NetworkConnection conn, string roomName)
    {
        _localRoomName = roomName;
        _statusText = roomName + " を作成しました";
        Debug.Log("[Client] " + _statusText);
    }

    [TargetRpc]
    private void SendLeftTargetRpc(NetworkConnection conn)
    {
        _localRoomName = "";
        _statusText = "ロビーに戻りました";
        Debug.Log("[Client] " + _statusText);
    }

    // ---- サーバー側コールバック ----

    // SceneロードがサーバーとクライアントどちらでもOnLoadEndが来るため AsServer で分岐
    private void OnSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (!args.QueueData.AsServer) return;
        if (args.LoadedScenes.Length == 0) return;

        object[] serverParams = args.QueueData.SceneLoadData.Params.ServerParams;
        if (serverParams == null || serverParams.Length == 0) return;

        string roomName = serverParams[0] as string;
        if (string.IsNullOrEmpty(roomName)) return;
        if (!_pendingCreators.TryGetValue(roomName, out NetworkConnection creator)) return;

        _pendingCreators.Remove(roomName);

        Scene roomScene = args.LoadedScenes[0];
        _roomScenes[roomName] = roomScene;
        _playerCounts[roomName] = 1;

        Debug.Log("[Server] 部屋作成完了: " + roomName + " handle=" + roomScene.handle);

        // 作成者を部屋Sceneにスポーン
        if (spawner != null)
            spawner.SpawnInRoom(creator, roomScene);

        SendCreatedTargetRpc(creator, roomName);
        PushRoomListObserversRpc(BuildRoomListCsv());
    }

    // ---- クライアント側コールバック ----

    private void OnClientSceneLoaded(SceneLoadEndEventArgs args)
    {
        if (args.QueueData.AsServer) return;
        if (args.LoadedScenes.Length == 0) return;
        _currentSceneName = args.LoadedScenes[0].name;
    }

    // ---- ユーティリティ ----

    private string BuildRoomListCsv()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, Scene> kv in _roomScenes)
        {
            if (sb.Length > 0) sb.Append(',');
            string name = kv.Key;
            sb.Append(name).Append(':')
              .Append(_playerCounts[name]).Append(':')
              .Append(_maxPlayers[name]);
        }
        return sb.ToString();
    }

    private void ApplyRoomList(string csv)
    {
        _roomListDisplay.Clear();
        if (string.IsNullOrEmpty(csv)) return;
        foreach (string entry in csv.Split(','))
        {
            string[] parts = entry.Split(':');
            if (parts.Length == 3)
                _roomListDisplay.Add(parts[0] + " (" + parts[1] + "/" + parts[2] + ")");
        }
    }
}
