using UnityEngine;

/// 水場・場外などに置くリスポーン用トリガー。
/// 入ってきたプレイヤー（KnockbackReceiver持ち）をリスポーンさせる。
/// 使い方：空オブジェクトに Collider（Box等）を付けて Is Trigger にし、これを足して範囲を覆う。
[RequireComponent(typeof(Collider))]
public class RespawnZone : MonoBehaviour
{
    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        KnockbackReceiver receiver = other.GetComponentInParent<KnockbackReceiver>();
        if (receiver != null)
        {
            receiver.TriggerRespawn();
        }
    }
}
