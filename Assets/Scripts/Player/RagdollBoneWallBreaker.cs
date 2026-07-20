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

    private RagdollController ragdoll; // 壁破壊の窓口（権威判定とネットワーク配信を担う）

    private void Awake()
    {
        ragdoll = GetComponentInParent<RagdollController>();
    }

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
        // 破片は進行方向へ飛ぶ。壁を壊すのは権威を持つ端末だけなので、必ず RagdollController の窓口を通す
        //（直接 TakeDamage を呼ぶと各端末が独立に壊して迷路の形が食い違う）。
        if (ragdoll != null)
        {
            ragdoll.ReportWallDamage(wall, damage, c.point, -collision.relativeVelocity);
        }
        else
        {
            wall.TakeDamage(damage, c.point, -collision.relativeVelocity); // 単体テスト等のフォールバック
        }
    }
}
