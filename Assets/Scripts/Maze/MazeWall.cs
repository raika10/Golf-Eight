using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 迷路の壁ブロック1つに付くコンポーネント。
/// TakeDamage() で破壊する。localizedDestruction が有効なら「当たった箇所だけ」を削り、
/// 残りの壁は立ったまま残す（無傷の壁は従来どおり軽いまま。当たった壁だけボクセル化する）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class MazeWall : MonoBehaviour
{
    [Header("耐久")]
    [Tooltip("壁の耐久値。0以下で破壊される（部分破壊OFF時のみ使用）")]
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

    [Header("部分破壊（当たった箇所だけ壊す）")]
    [Tooltip("ONにすると、壁全体ではなく当たった箇所の周辺だけが崩れ、残りは立ったまま残る")]
    public bool localizedDestruction = true;

    [Tooltip("破壊の解像度＝1セルのワールドサイズ目安 (m)。小さいほど細かく削れる（＝重い）")]
    public float destroyCellSize = 0.4f;

    [Tooltip("1回の衝突で削れる半径 (m)。ボールの半径くらいにすると自然に穴が抜ける")]
    public float destroyRadius = 0.7f;

    [Tooltip("衝突が速いほど穴を大きくする量（半径 = destroyRadius + 速さ × これ）")]
    public float destroyRadiusPerSpeed = 0.02f;

    [Header("破壊時イベント")]
    [Tooltip("SEやパーティクル再生などをここに接続")]
    public UnityEvent onDestroyed;

    // ── 部分破壊用の内部状態（ボクセル化してから使う） ──────────────
    bool voxelized;                 // 一度でも当たってボクセル化したか
    bool[,,] alive;                 // 各セルが残っているか
    int nx, ny, nz;                 // 格子の分割数
    int aliveCount;                 // 残っているセル数
    Vector3 localMin;               // 格子の原点（メッシュのローカル最小）
    Vector3 cellLocalSize;          // 1セルのローカルサイズ
    Material wallMat;               // 破片・壁に使うマテリアル
    Mesh genMesh;                   // 組み直し用の生成メッシュ（このインスタンスだけ書き換える）
    MeshFilter meshFilter;
    Collider originalCollider;      // ボクセル化時に無効化する元のコライダー
    readonly List<BoxCollider> colliderPool = new List<BoxCollider>();

    // メッシュ生成の一時バッファ（使い回してGCを抑える）
    static readonly List<Vector3> vBuf = new List<Vector3>();
    static readonly List<Vector3> nBuf = new List<Vector3>();
    static readonly List<Vector2> uvBuf = new List<Vector2>();
    static readonly List<int> tBuf = new List<int>();

    /// <summary>ダメージを与える（衝撃情報なし）。</summary>
    public void TakeDamage(int amount)
    {
        TakeDamage(amount, transform.position, Vector3.zero);
    }

    /// <summary>ダメージを与える（衝撃点・速度つき）。ボールの当たった位置と速度を渡すと自然に飛散する。</summary>
    public void TakeDamage(int amount, Vector3 impactPoint, Vector3 impactVelocity)
    {
        if (localizedDestruction)
        {
            EnsureVoxelized();
            CarveSphere(impactPoint, impactVelocity);
            return;
        }

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

    void OnDestroy()
    {
        // 実行時に生成したメッシュは自動で解放されないので明示的に破棄する
        if (genMesh != null)
            Destroy(genMesh);
    }

    // ── 部分破壊 ────────────────────────────────────────────────

    /// 初めて当たったときだけ、壁をボクセル格子として扱えるよう準備する（遅延ボクセル化）。
    void EnsureVoxelized()
    {
        if (voxelized)
            return;
        voxelized = true;

        var rend = GetComponent<Renderer>();
        wallMat = fragmentMaterial != null ? fragmentMaterial
                : rend != null ? rend.sharedMaterial : null;

        meshFilter = GetComponent<MeshFilter>();
        Bounds localBounds = meshFilter != null && meshFilter.sharedMesh != null
            ? meshFilter.sharedMesh.bounds
            : new Bounds(Vector3.zero, Vector3.one);

        // 壁のワールド実寸から分割数を決める（回転なし・単位キューブをスケールした壁を前提）
        Vector3 worldSize = Vector3.Scale(localBounds.size, transform.lossyScale);
        float s = Mathf.Max(0.05f, destroyCellSize);
        nx = Mathf.Clamp(Mathf.RoundToInt(worldSize.x / s), 1, 40);
        ny = Mathf.Clamp(Mathf.RoundToInt(worldSize.y / s), 1, 40);
        nz = Mathf.Clamp(Mathf.RoundToInt(worldSize.z / s), 1, 40);

        localMin = localBounds.min;
        cellLocalSize = new Vector3(localBounds.size.x / nx, localBounds.size.y / ny, localBounds.size.z / nz);

        alive = new bool[nx, ny, nz];
        for (int x = 0; x < nx; x++)
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
            alive[x, y, z] = true;
        aliveCount = nx * ny * nz;

        // 元のコライダーは無効化し、以降はセルから組んだ BoxCollider 群を使う
        originalCollider = GetComponent<Collider>();
        if (originalCollider != null)
            originalCollider.enabled = false;

        // 生成メッシュを用意（元の共有Cubeメッシュは汚さず、自前インスタンスに差し替える）
        genMesh = new Mesh { name = $"{name}_Voxel" };
        if (meshFilter != null)
            meshFilter.sharedMesh = genMesh;
    }

    /// 衝撃点の周辺（半径内）のセルを削り、その分を破片として飛ばす。
    void CarveSphere(Vector3 impactPoint, Vector3 impactVelocity)
    {
        float radius = destroyRadius + impactVelocity.magnitude * destroyRadiusPerSpeed;
        float r2 = radius * radius;

        Transform group = null;   // 破片の親（削れたセルがある時だけ作る）
        int removed = 0;

        // 半径内のセルを削る。同時に、最も近い生きたセルも覚えておく（何も削れなかった時の保険）
        int nearX = -1, nearY = -1, nearZ = -1;
        float nearSqr = float.MaxValue;

        for (int x = 0; x < nx; x++)
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        {
            if (!alive[x, y, z])
                continue;

            Vector3 worldCenter = transform.TransformPoint(CellLocalCenter(x, y, z));
            float sqr = (worldCenter - impactPoint).sqrMagnitude;
            if (sqr < nearSqr)
            {
                nearSqr = sqr;
                nearX = x; nearY = y; nearZ = z;
            }
            if (sqr <= r2)
            {
                KillCell(x, y, z, ref group, impactPoint, impactVelocity);
                removed++;
            }
        }

        // 半径がセルより小さいなどで1個も削れなかったら、最も近いセルを1つだけ削る（必ず反応する）
        if (removed == 0 && nearX >= 0)
        {
            KillCell(nearX, nearY, nearZ, ref group, impactPoint, impactVelocity);
            removed++;
        }

        aliveCount -= removed;

        if (group != null)
        {
            var cleanup = group.gameObject.AddComponent<FragmentCleanup>();
            cleanup.lifetime = fragmentLifetime;
        }

        // 壁が全部無くなったら丸ごと破棄。残っていればメッシュ・コライダーを組み直す
        if (aliveCount <= 0)
        {
            onDestroyed?.Invoke();
            Destroy(gameObject);
            return;
        }

        RebuildMesh();
        RebuildColliders();
    }

    /// 1セルを削って破片1個に変える。
    void KillCell(int x, int y, int z, ref Transform group, Vector3 impactPoint, Vector3 impactVelocity)
    {
        alive[x, y, z] = false;

        if (group == null)
        {
            group = new GameObject($"{name}_Fragments").transform;
            group.position = transform.position;
        }

        Vector3 worldPos = transform.TransformPoint(CellLocalCenter(x, y, z));
        Vector3 fragScale = Vector3.Scale(cellLocalSize, transform.lossyScale);

        var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frag.transform.SetParent(group, worldPositionStays: true);
        frag.transform.position = worldPos;
        frag.transform.rotation = transform.rotation;
        frag.transform.localScale = fragScale;

        if (wallMat != null)
            frag.GetComponent<Renderer>().sharedMaterial = wallMat;

        var rb = frag.AddComponent<Rigidbody>();
        rb.linearVelocity = impactVelocity * 0.5f;
        rb.AddExplosionForce(explosionForce, impactPoint, explosionRadius, 0.3f, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * explosionForce, ForceMode.Impulse);
    }

    /// セル(x,y,z)のローカル中心。
    Vector3 CellLocalCenter(int x, int y, int z)
    {
        return localMin + new Vector3(
            (x + 0.5f) * cellLocalSize.x,
            (y + 0.5f) * cellLocalSize.y,
            (z + 0.5f) * cellLocalSize.z);
    }

    bool Alive(int x, int y, int z)
    {
        return x >= 0 && x < nx && y >= 0 && y < ny && z >= 0 && z < nz && alive[x, y, z];
    }

    /// 残ったセルから、外に面した部分だけのメッシュを組み直す（隣が生きている面はカリング）。
    void RebuildMesh()
    {
        vBuf.Clear(); nBuf.Clear(); uvBuf.Clear(); tBuf.Clear();

        for (int x = 0; x < nx; x++)
        for (int y = 0; y < ny; y++)
        for (int z = 0; z < nz; z++)
        {
            if (!alive[x, y, z])
                continue;

            float x0 = localMin.x + x * cellLocalSize.x, x1 = x0 + cellLocalSize.x;
            float y0 = localMin.y + y * cellLocalSize.y, y1 = y0 + cellLocalSize.y;
            float z0 = localMin.z + z * cellLocalSize.z, z1 = z0 + cellLocalSize.z;

            var p000 = new Vector3(x0, y0, z0);
            var p100 = new Vector3(x1, y0, z0);
            var p010 = new Vector3(x0, y1, z0);
            var p110 = new Vector3(x1, y1, z0);
            var p001 = new Vector3(x0, y0, z1);
            var p101 = new Vector3(x1, y0, z1);
            var p011 = new Vector3(x0, y1, z1);
            var p111 = new Vector3(x1, y1, z1);

            if (!Alive(x - 1, y, z)) EmitQuad(p000, p001, p011, p010, Vector3.left);
            if (!Alive(x + 1, y, z)) EmitQuad(p100, p110, p111, p101, Vector3.right);
            if (!Alive(x, y - 1, z)) EmitQuad(p000, p100, p101, p001, Vector3.down);
            if (!Alive(x, y + 1, z)) EmitQuad(p010, p011, p111, p110, Vector3.up);
            if (!Alive(x, y, z - 1)) EmitQuad(p000, p010, p110, p100, Vector3.back);
            if (!Alive(x, y, z + 1)) EmitQuad(p001, p101, p111, p011, Vector3.forward);
        }

        genMesh.Clear();
        genMesh.indexFormat = vBuf.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        genMesh.SetVertices(vBuf);
        genMesh.SetNormals(nBuf);
        genMesh.SetUVs(0, uvBuf);
        genMesh.SetTriangles(tBuf, 0);
        genMesh.RecalculateBounds();
    }

    /// 四角形を1枚追加する。法線 n が外を向くよう三角形の巻き順を自動で合わせる。
    static void EmitQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        // a,b,c,d の並びが外向き（右手系の面法線が n と同じ側）でなければ逆順にする
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f)
        {
            Vector3 tmp = b; b = d; d = tmp;
        }

        int i = vBuf.Count;
        vBuf.Add(a); vBuf.Add(b); vBuf.Add(c); vBuf.Add(d);
        nBuf.Add(n); nBuf.Add(n); nBuf.Add(n); nBuf.Add(n);
        uvBuf.Add(new Vector2(0, 0)); uvBuf.Add(new Vector2(1, 0));
        uvBuf.Add(new Vector2(1, 1)); uvBuf.Add(new Vector2(0, 1));
        tBuf.Add(i); tBuf.Add(i + 1); tBuf.Add(i + 2);
        tBuf.Add(i); tBuf.Add(i + 2); tBuf.Add(i + 3);
    }

    /// 残ったセルを、なるべく少ない BoxCollider にまとめて組み直す（貪欲マージ）。
    void RebuildColliders()
    {
        var used = new bool[nx, ny, nz];
        int boxIndex = 0;

        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            if (!alive[x, y, z] || used[x, y, z])
                continue;

            // X方向へ伸ばす
            int x2 = x;
            while (x2 + 1 < nx && alive[x2 + 1, y, z] && !used[x2 + 1, y, z])
                x2++;

            // Y方向へ伸ばす（X区間が全部生きている限り）
            int y2 = y;
            while (y2 + 1 < ny && RowFree(used, x, x2, y2 + 1, z))
                y2++;

            // Z方向へ伸ばす（X×Y面が全部生きている限り）
            int z2 = z;
            while (z2 + 1 < nz && SliceFree(used, x, x2, y, y2, z2 + 1))
                z2++;

            for (int zz = z; zz <= z2; zz++)
            for (int yy = y; yy <= y2; yy++)
            for (int xx = x; xx <= x2; xx++)
                used[xx, yy, zz] = true;

            Vector3 size = new Vector3(
                (x2 - x + 1) * cellLocalSize.x,
                (y2 - y + 1) * cellLocalSize.y,
                (z2 - z + 1) * cellLocalSize.z);
            Vector3 center = localMin + new Vector3(x * cellLocalSize.x, y * cellLocalSize.y, z * cellLocalSize.z) + size * 0.5f;

            BoxCollider box = GetPooledCollider(boxIndex++);
            box.center = center;
            box.size = size;
            box.enabled = true;
        }

        // 余ったコライダーは無効化しておく
        for (int i = boxIndex; i < colliderPool.Count; i++)
            colliderPool[i].enabled = false;
    }

    bool RowFree(bool[,,] used, int x, int x2, int y, int z)
    {
        for (int xi = x; xi <= x2; xi++)
            if (!alive[xi, y, z] || used[xi, y, z])
                return false;
        return true;
    }

    bool SliceFree(bool[,,] used, int x, int x2, int y, int y2, int z)
    {
        for (int yi = y; yi <= y2; yi++)
            if (!RowFree(used, x, x2, yi, z))
                return false;
        return true;
    }

    BoxCollider GetPooledCollider(int index)
    {
        while (colliderPool.Count <= index)
            colliderPool.Add(gameObject.AddComponent<BoxCollider>());
        return colliderPool[index];
    }

    // ── 破片生成（部分破壊OFF時の従来どおりの全体破壊） ─────────────

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
        int fnx = Mathf.Max(1, Mathf.RoundToInt(worldSize.x / s));
        int fny = Mathf.Max(1, Mathf.RoundToInt(worldSize.y / s));
        int fnz = Mathf.Max(1, Mathf.RoundToInt(worldSize.z / s));

        // ローカル空間での1破片のサイズと開始点
        Vector3 localPiece = new Vector3(
            localBounds.size.x / fnx,
            localBounds.size.y / fny,
            localBounds.size.z / fnz);
        Vector3 localOrigin = localBounds.min;

        // 破片の見た目サイズ = ローカル片サイズ × 壁のワールドスケール
        Vector3 fragScale = Vector3.Scale(localPiece, transform.lossyScale);

        // 破片をまとめる親（元の壁とは独立して生存させる）
        var group = new GameObject($"{name}_Fragments").transform;
        group.position = transform.position;

        for (int ix = 0; ix < fnx; ix++)
        for (int iy = 0; iy < fny; iy++)
        for (int iz = 0; iz < fnz; iz++)
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
