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

    [Tooltip("破片1個あたりの目安サイズ（Unity単位）。小さいほど細かく＝重くなる。\n壁の実寸から分割数を自動計算するので、縦壁・横壁どちらも均一に割れる")]
    public float fragmentSize = 0.5f;

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

        // 壁の「ローカル座標系」で分割する（回転していてもワールド軸に引きずられない）。
        // メッシュのローカル境界を基準にすることで、任意サイズ・任意回転の壁に追従する。
        var mf = GetComponent<MeshFilter>();
        Bounds localBounds = mf != null && mf.sharedMesh != null
            ? mf.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one); // フォールバック: 単位キューブ

        // 壁のワールド実寸から、破片が目安サイズに近づくよう分割数を自動決定
        Vector3 worldSize = Vector3.Scale(localBounds.size, transform.lossyScale);
        float s = Mathf.Max(0.05f, fragmentSize);
        int nx = Mathf.Max(1, Mathf.RoundToInt(worldSize.x / s));
        int ny = Mathf.Max(1, Mathf.RoundToInt(worldSize.y / s));
        int nz = Mathf.Max(1, Mathf.RoundToInt(worldSize.z / s));

        // ローカル空間での1破片のサイズと開始点
        Vector3 localPiece = new Vector3(
            localBounds.size.x / nx,
            localBounds.size.y / ny,
            localBounds.size.z / nz);
        Vector3 localOrigin = localBounds.min;

        // 破片の見た目サイズ = ローカル片サイズ × 壁のワールドスケール
        Vector3 fragScale = Vector3.Scale(localPiece, transform.lossyScale);

        // 破片をまとめる親（元の壁とは独立して生存させる）
        var group = new GameObject($"{name}_Fragments").transform;
        group.position = transform.position;

        for (int ix = 0; ix < nx; ix++)
        for (int iy = 0; iy < ny; iy++)
        for (int iz = 0; iz < nz; iz++)
        {
            // ローカル中心 → ワールド座標へ変換（壁の回転・スケールを反映）
            Vector3 localCenter = localOrigin + new Vector3(
                (ix + 0.5f) * localPiece.x,
                (iy + 0.5f) * localPiece.y,
                (iz + 0.5f) * localPiece.z);
            Vector3 worldPos = transform.TransformPoint(localCenter);

            var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frag.transform.SetParent(group, worldPositionStays: true);
            frag.transform.position = worldPos;
            frag.transform.rotation = transform.rotation; // 壁と同じ向き
            frag.transform.localScale = fragScale;

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
