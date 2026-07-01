using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 迷路の壁ブロック1つに付くコンポーネント。
/// TakeDamage() で耐久を削り、0以下で破壊表現（破片化）を再生する。
/// </summary>
[RequireComponent(typeof(Collider))]
public class MazeWall : MonoBehaviour
{
    [Header("耐久")]
    [Tooltip("壁の耐久値。0以下で破壊される")]
    public int hp = 1;

    [Header("破片化の設定")]
    [Tooltip("破壊時に壁を破片に分割する")]
    public bool shatterOnDestroy = true;

    [Tooltip("各軸方向の分割数（大きいほど細かく＝重くなる）")]
    public Vector3Int fragmentCounts = new Vector3Int(3, 2, 1);

    [Tooltip("破片を吹き飛ばす力")]
    public float explosionForce = 4f;

    [Tooltip("爆風の半径")]
    public float explosionRadius = 3f;

    [Tooltip("破片が消えるまでの秒数")]
    public float fragmentLifetime = 3f;

    [Tooltip("破片に使うマテリアル（未指定なら元の壁のマテリアルを流用）")]
    public Material fragmentMaterial;

    [Header("破壊時イベント")]
    [Tooltip("SEやパーティクル再生などをここに接続")]
    public UnityEvent onDestroyed;

    /// <summary>ダメージを与える（衝撃情報なし）。</summary>
    public void TakeDamage(int amount)
    {
        TakeDamage(amount, transform.position, Vector3.zero);
    }

    /// <summary>ダメージを与える（衝撃点・速度つき）。ボールの当たった位置と速度を渡すと自然に飛散する。</summary>
    public void TakeDamage(int amount, Vector3 impactPoint, Vector3 impactVelocity)
    {
        hp -= amount;
        if (hp <= 0)
            DestroyWall(impactPoint, impactVelocity);
    }

    public void DestroyWall()
    {
        DestroyWall(transform.position, Vector3.zero);
    }

    public void DestroyWall(Vector3 impactPoint, Vector3 impactVelocity)
    {
        onDestroyed?.Invoke();

        if (shatterOnDestroy)
            Shatter(impactPoint, impactVelocity);

        Destroy(gameObject);
    }

    // ── 破片生成 ────────────────────────────────────────────────

    void Shatter(Vector3 impactPoint, Vector3 impactVelocity)
    {
        var rend = GetComponent<Renderer>();
        var mat = fragmentMaterial != null ? fragmentMaterial
                : rend != null ? rend.sharedMaterial : null;

        // ワールド空間での壁のサイズ（この壁は非回転のBoxを前提）
        Vector3 wallSize = rend != null ? rend.bounds.size : transform.lossyScale;
        Vector3 center = rend != null ? rend.bounds.center : transform.position;

        int nx = Mathf.Max(1, fragmentCounts.x);
        int ny = Mathf.Max(1, fragmentCounts.y);
        int nz = Mathf.Max(1, fragmentCounts.z);
        Vector3 piece = new Vector3(wallSize.x / nx, wallSize.y / ny, wallSize.z / nz);
        Vector3 origin = center - wallSize * 0.5f;

        // 破片をまとめる親（元の壁とは独立して生存させる）
        var group = new GameObject($"{name}_Fragments").transform;
        group.position = center;

        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frag.transform.SetParent(group, worldPositionStays: true);
            frag.transform.rotation = transform.rotation;
            frag.transform.localScale = piece;
            frag.transform.position = origin + new Vector3(
                (ix + 0.5f) * piece.x,
                (iy + 0.5f) * piece.y,
                (iz + 0.5f) * piece.z);

            if (mat != null)
                frag.GetComponent<Renderer>().sharedMaterial = mat;

            var rb = frag.AddComponent<Rigidbody>();
            rb.linearVelocity = impactVelocity * 0.5f;
            rb.AddExplosionForce(explosionForce, impactPoint, explosionRadius, 0.3f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * explosionForce, ForceMode.Impulse);
        }

        var cleanup = group.gameObject.AddComponent<FragmentCleanup>();
        cleanup.lifetime = fragmentLifetime;
    }
}
