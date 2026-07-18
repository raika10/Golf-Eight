using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 破壊表現の確認用テスター（新 Input System 対応）。
/// Play中にクリックした壁を即座に破壊し、砕け方をプレビューできる。
///   左クリック: 狙った壁を破壊（衝撃点＝クリック位置、飛散方向＝視線方向）
/// カメラを持つGameObject（Main Camera など）にアタッチして使う。
/// </summary>
public class WallDestroyTester : MonoBehaviour
{
    [Tooltip("レイキャストに使うカメラ。未指定なら Camera.main を使用")]
    public Camera cam;

    [Tooltip("クリックした壁に与える擬似的な衝撃の強さ")]
    public float impactSpeed = 10f;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            DestroyAtCursor(mouse.position.ReadValue());
    }

    void DestroyAtCursor(Vector2 screenPos)
    {
        if (cam == null)
        {
            Debug.LogWarning("[WallDestroyTester] カメラが設定されていません。");
            return;
        }

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
        {
            var wall = hit.collider.GetComponent<MazeWall>();
            if (wall != null)
            {
                // 視線方向へ破片が飛ぶよう、衝撃速度を渡す
                Vector3 impactVelocity = ray.direction.normalized * impactSpeed;
                wall.TakeDamage(wall.hp, hit.point, impactVelocity);
            }
            else
            {
                Debug.Log($"[WallDestroyTester] MazeWall ではない: {hit.collider.name}");
            }
        }
    }
}
