using UnityEngine;
using UnityEngine.SceneManagement;

/// TestRoomScene 内に置くUIスクリプト。
/// 自分がどの部屋（Sceneインスタンス）にいるかを確認する用途。
public class TestRoomSceneUI : MonoBehaviour
{
    private void OnGUI()
    {
        Scene s = gameObject.scene;
        GUILayout.BeginArea(new Rect(10, 220, 300, 100));
        GUILayout.BeginVertical("box");
        GUILayout.Label("=== RoomScene 内 ===");
        GUILayout.Label("Scene 名: " + s.name);
        GUILayout.Label("Scene handle: " + s.handle);
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
