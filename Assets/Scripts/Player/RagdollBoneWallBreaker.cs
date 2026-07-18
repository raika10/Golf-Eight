using UnityEngine;

/// ragdoll の骨に付けて、着地後に地面を滑っている時などに壁（MazeWall）へ速く当たったら壊す。
/// 飛行中は骨のコライダーがOFFなので衝突は起きず、着地して当たり判定が戻った状態でだけ働く
///（＝ボールと同じ「速い当たりだけ壊す」挙動を、崩れて滑る体でも再現する）。
/// RagdollController が各骨へ自動で付ける。
public class RagdollBoneWallBreaker : MonoBehaviour
{
    [HideInInspector] public bool breakEnabled = true; // 壁破壊を有効にするか
    [HideInInspector] public float minSpeed = 3f;      // この相対速度以上で当たった時だけ壊す (m/s)
    [HideInInspector] public int damage = 1;           // 1回の衝突で与えるダメージ

    private void OnCollisionEnter(Collision collision)
    {
        if (!breakEnabled)
        {
            return;
        }
        MazeWall wall = collision.collider.GetComponentInParent<MazeWall>();
        if (wall == null || collision.relativeVelocity.magnitude < minSpeed)
        {
            return;
        }
        ContactPoint c = collision.GetContact(0);
        wall.TakeDamage(damage, c.point, -collision.relativeVelocity); // 破片は進行方向へ飛ぶ
    }
}
