using FishNet.Object;
using UnityEngine;

/// スポーンされたテスト用プレイヤー。オーナーのみ WASD で移動できる。
/// 位置同期は NetworkTransform が担う。
public class TestNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 4f;

    private Renderer _renderer;

    public override void OnStartClient()
    {
        base.OnStartClient();
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
            _renderer.material.color = IsOwner ? Color.green : Color.red;
    }

    private void Update()
    {
        if (!IsOwner) return;
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        transform.Translate(new Vector3(h, 0f, v) * moveSpeed * Time.deltaTime, Space.World);
    }
}
