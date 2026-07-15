using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 部屋入場時にその部屋のSceneへプレイヤーをスポーンする。
/// TestRoomManager から SpawnInRoom() / DespawnForConnection() を呼んで使う。
public class TestPlayerSpawner : NetworkBehaviour
{
    [SerializeField] private NetworkObject playerPrefab;

    [Server]
    public void SpawnInRoom(NetworkConnection conn, Scene roomScene)
    {
        if (playerPrefab == null)
        {
            Debug.LogWarning("[TestPlayerSpawner] playerPrefab が未設定です");
            return;
        }
        Vector3 pos = new Vector3(Random.Range(-3f, 3f), 1f, Random.Range(-3f, 3f));
        NetworkObject obj = Instantiate(playerPrefab, pos, Quaternion.identity);

        // Spawn の第3引数に roomScene を渡すことで、
        // そのSceneインスタンスに属するConnectionにだけ同期される
        InstanceFinder.ServerManager.Spawn(obj, conn, roomScene);

        Debug.Log("[Server] スポーン: room=" + roomScene.name
                  + " handle=" + roomScene.handle
                  + " conn=" + conn.ClientId);
    }

    [Server]
    public void DespawnForConnection(NetworkConnection conn)
    {
        foreach (NetworkObject nob in conn.Objects)
        {
            if (nob == null) continue;
            InstanceFinder.ServerManager.Despawn(nob);
            break;
        }
    }
}
