using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ブロックを組み合わせて迷路を自動生成する。
/// DFS（深さ優先探索）でパスを掘り進め、壁・床・柱を個別のGameObjectとして配置する。
/// Inspector上の [Generate Maze] からエディタ実行も可能。
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [Header("迷路サイズ")]
    public int width = 10;
    public int height = 10;
    public float cellSize = 3f;

    [Header("壁の形状")]
    public float wallHeight = 2f;
    public float wallThickness = 0.3f;

    [Header("プレハブ（必須: wallPrefab）")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;
    public GameObject pillarPrefab;

    [Header("スタート/ゴール")]
    [Tooltip("スタート地点に置くマーカー（任意）")]
    public GameObject startPrefab;
    [Tooltip("ゴール地点に置くマーカー。ポール（旗竿）を想定")]
    public GameObject goalPrefab;

    [Header("スタート地点の広場")]
    [Tooltip("スタートのマスを中心に、この半径（マス）ぶんだけ内部の壁と柱を取り除いて広場にする。\nプレイヤーが動き回れる・ボールを構えられるようにするため。0で無効")]
    public int startClearRadius = 1;
    [Tooltip("広場の床タイルに差し替えるマテリアル（芝生）。未設定なら床はそのまま")]
    public Material startFloorMaterial;

    [Header("乱数シード")]
    public bool useRandomSeed = true;
    public int seed;

    /// <summary>スタートのマス座標（掘り始めのマス）。生成後に確定。</summary>
    public Vector2Int StartCell { get; private set; }
    /// <summary>ゴールのマス座標（スタートから最も遠いマス）。生成後に確定。</summary>
    public Vector2Int GoalCell { get; private set; }

    // true = 壁あり
    // hWalls[y, x] : Y方向の境界（行yと行y-1の間）、X位置x の水平壁
    // vWalls[y, x] : X方向の境界（列xと列x-1の間）、Z位置y の垂直壁
    private bool[,] hWalls;
    private bool[,] vWalls;
    private bool[,] visited;

    private Transform container;

    void Start() => GenerateMaze();

    [ContextMenu("Generate Maze")]
    public void GenerateMaze()
    {
        if (wallPrefab == null)
        {
            Debug.LogError("[MazeGenerator] wallPrefab が未設定です。");
            return;
        }

        // 既存の迷路を削除
        if (container != null)
            DestroyImmediate(container.gameObject);

        container = new GameObject("MazeBlocks").transform;
        container.SetParent(transform);
        container.localPosition = Vector3.zero;

        if (useRandomSeed)
            seed = Random.Range(0, int.MaxValue);
        Random.InitState(seed);

        InitWalls();
        CarvePassagesDFS();
        DetermineStartAndGoal();
        ClearStartArea();
        SpawnBlocks();

        Debug.Log($"[MazeGenerator] 迷路生成完了 (seed={seed}, {width}x{height}, " +
                  $"start={StartCell}, goal={GoalCell})");
    }

    // ── アルゴリズム ────────────────────────────────────────────────

    void InitWalls()
    {
        // 全境界に壁を立てた状態で開始
        hWalls = new bool[height + 1, width];
        vWalls = new bool[height, width + 1];
        visited = new bool[height, width];

        for (int y = 0; y <= height; y++)
            for (int x = 0; x < width; x++)
                hWalls[y, x] = true;

        for (int y = 0; y < height; y++)
            for (int x = 0; x <= width; x++)
                vWalls[y, x] = true;
    }

    void CarvePassagesDFS()
    {
        // スタックを使った反復DFSで、大きな迷路でもスタックオーバーフローしない
        var stack = new Stack<Vector2Int>();
        var start = new Vector2Int(0, 0);
        visited[0, 0] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            var neighbors = GetUnvisitedNeighbors(current);

            if (neighbors.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var next = neighbors[Random.Range(0, neighbors.Count)];
            RemoveWallBetween(current, next);
            visited[next.y, next.x] = true;
            stack.Push(next);
        }
    }

    List<Vector2Int> GetUnvisitedNeighbors(Vector2Int cell)
    {
        var result = new List<Vector2Int>(4);
        Vector2Int[] dirs = {
            new Vector2Int( 0,  1),  // 北
            new Vector2Int( 0, -1),  // 南
            new Vector2Int( 1,  0),  // 東
            new Vector2Int(-1,  0),  // 西
        };
        foreach (var d in dirs)
        {
            var n = cell + d;
            if (n.x >= 0 && n.x < width && n.y >= 0 && n.y < height && !visited[n.y, n.x])
                result.Add(n);
        }
        return result;
    }

    void RemoveWallBetween(Vector2Int a, Vector2Int b)
    {
        var d = b - a;
        if      (d.y ==  1) hWalls[a.y + 1, a.x] = false; // 北へ移動 → a の上の水平壁を削除
        else if (d.y == -1) hWalls[a.y,     a.x] = false; // 南へ移動 → a の下の水平壁を削除
        else if (d.x ==  1) vWalls[a.y, a.x + 1] = false; // 東へ移動 → a の右の垂直壁を削除
        else if (d.x == -1) vWalls[a.y, a.x    ] = false; // 西へ移動 → a の左の垂直壁を削除
    }

    // ── スタート/ゴール決定 ────────────────────────────────────────

    void DetermineStartAndGoal()
    {
        StartCell = new Vector2Int(0, 0);        // DFSの起点をスタートにする
        GoalCell = FindFarthestCell(StartCell);  // そこから最も遠いマスをゴールに
    }

    /// <summary>広場のマス範囲（minX..maxX, minY..maxY はいずれも両端を含む）。</summary>
    struct CellRange
    {
        public int minX, maxX, minY, maxY;
        public bool Contains(int x, int y) => x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>広場に含まれるマスの範囲。startClearRadius が0ならスタートのマスだけになる。</summary>
    CellRange StartAreaCells()
    {
        int r = Mathf.Max(0, startClearRadius);
        return new CellRange
        {
            minX = Mathf.Max(0, StartCell.x - r),
            maxX = Mathf.Min(width - 1, StartCell.x + r),
            minY = Mathf.Max(0, StartCell.y - r),
            maxY = Mathf.Min(height - 1, StartCell.y + r),
        };
    }

    /// <summary>
    /// スタートのマス周辺の「内部の壁」を取り除いて、プレイヤーが動ける広場にする。
    /// 迷路の一番外側（外周）の壁は残すので、広場から場外へ抜けてしまうことはない。
    /// ゴール決定（FindFarthestCell）の後に呼ぶこと。広場化で通路にループができても
    /// ゴールの位置は変わらない（先に確定させておく）。
    /// </summary>
    void ClearStartArea()
    {
        if (startClearRadius <= 0) return;

        var area = StartAreaCells();

        // 水平壁 hWalls[y, x] は 行 y-1 と 行 y の境界。
        // 両側のマスが広場内に収まる内部境界（y = minY+1 .. maxY）だけを消す＝外周は残る。
        for (int y = area.minY + 1; y <= area.maxY; y++)
            for (int x = area.minX; x <= area.maxX; x++)
                hWalls[y, x] = false;

        // 垂直壁 vWalls[y, x] は 列 x-1 と 列 x の境界。
        // 同様に内部境界（x = minX+1 .. maxX）だけを消す。
        for (int y = area.minY; y <= area.maxY; y++)
            for (int x = area.minX + 1; x <= area.maxX; x++)
                vWalls[y, x] = false;
    }

    // 通路をたどってBFSし、fromから最も遠いマスを返す
    Vector2Int FindFarthestCell(Vector2Int from)
    {
        var dist = new int[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                dist[y, x] = -1;

        var queue = new Queue<Vector2Int>();
        dist[from.y, from.x] = 0;
        queue.Enqueue(from);

        Vector2Int farthest = from;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (dist[c.y, c.x] > dist[farthest.y, farthest.x])
                farthest = c;

            foreach (var n in GetPassableNeighbors(c))
                if (dist[n.y, n.x] < 0)
                {
                    dist[n.y, n.x] = dist[c.y, c.x] + 1;
                    queue.Enqueue(n);
                }
        }
        return farthest;
    }

    // 壁で隔てられていない（＝通れる）隣マスを返す
    List<Vector2Int> GetPassableNeighbors(Vector2Int c)
    {
        var result = new List<Vector2Int>(4);
        if (c.y + 1 < height && !hWalls[c.y + 1, c.x]) result.Add(new Vector2Int(c.x, c.y + 1)); // 北
        if (c.y - 1 >= 0     && !hWalls[c.y,     c.x]) result.Add(new Vector2Int(c.x, c.y - 1)); // 南
        if (c.x + 1 < width  && !vWalls[c.y, c.x + 1]) result.Add(new Vector2Int(c.x + 1, c.y)); // 東
        if (c.x - 1 >= 0     && !vWalls[c.y, c.x    ]) result.Add(new Vector2Int(c.x - 1, c.y)); // 西
        return result;
    }

    // ── ブロック配置 ────────────────────────────────────────────────

    void SpawnBlocks()
    {
        float half = cellSize * 0.5f;
        float halfH = wallHeight * 0.5f;
        var startArea = StartAreaCells();

        // 床タイル（広場のぶんは芝生マテリアルに差し替える）
        if (floorPrefab != null)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var go = SpawnBlock(
                        floorPrefab,
                        new Vector3(x * cellSize + half, 0f, y * cellSize + half),
                        new Vector3(cellSize, 0.1f, cellSize),
                        $"Floor_{x}_{y}",
                        isWall: false);

                    if (startFloorMaterial != null && startArea.Contains(x, y))
                        ApplyMaterial(go, startFloorMaterial);
                }
        }

        // 水平壁（東西方向に伸びる、Z境界に配置）
        for (int y = 0; y <= height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!hWalls[y, x]) continue;
                SpawnBlock(
                    wallPrefab,
                    new Vector3(x * cellSize + half, halfH, y * cellSize),
                    new Vector3(cellSize - wallThickness, wallHeight, wallThickness),
                    $"HWall_{x}_{y}",
                    isWall: true);
            }
        }

        // 垂直壁（南北方向に伸びる、X境界に配置）
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x <= width; x++)
            {
                if (!vWalls[y, x]) continue;
                SpawnBlock(
                    wallPrefab,
                    new Vector3(x * cellSize, halfH, y * cellSize + half),
                    new Vector3(wallThickness, wallHeight, cellSize - wallThickness),
                    $"VWall_{x}_{y}",
                    isWall: true);
            }
        }

        // 角の柱（壁の継ぎ目を埋める）
        if (pillarPrefab != null)
        {
            for (int y = 0; y <= height; y++)
                for (int x = 0; x <= width; x++)
                {
                    // 柱(x, y)は4マスの交点。広場の内部の交点は、壁と同じく柱も立てない
                    // （壁だけ消しても柱が残ると広場に障害物が点在してしまうため）。
                    bool insideStartArea = x > startArea.minX && x <= startArea.maxX
                                        && y > startArea.minY && y <= startArea.maxY;
                    if (insideStartArea) continue;

                    SpawnBlock(
                        pillarPrefab,
                        new Vector3(x * cellSize, halfH, y * cellSize),
                        new Vector3(wallThickness, wallHeight, wallThickness),
                        $"Pillar_{x}_{y}",
                        isWall: false);
                }
        }

        // スタート/ゴールのマーカー（ゴールにはポールを立てる想定）
        SpawnMarker(startPrefab, StartCell, "Start");
        SpawnMarker(goalPrefab, GoalCell, "Goal");
    }

    /// <summary>マーカーPrefabをマス中央・床の上に直立させて配置する（スケールはPrefabのまま）。</summary>
    void SpawnMarker(GameObject prefab, Vector2Int cell, string label)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, CellToWorld(cell), Quaternion.identity, container);
        go.name = label;
    }

    /// <summary>マス座標を、そのマス中央・床面のワールド座標へ変換する。ボールやホールの配置に使う。</summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        float half = cellSize * 0.5f;
        // floorPrefab の厚み(0.1)の上面に合わせて少し持ち上げる
        return transform.TransformPoint(
            new Vector3(cell.x * cellSize + half, 0.05f, cell.y * cellSize + half));
    }

    GameObject SpawnBlock(GameObject prefab, Vector3 localPos, Vector3 scale, string blockName, bool isWall)
    {
        if (prefab == null) return null;

        var worldPos = transform.TransformPoint(localPos);
        var go = Instantiate(prefab, worldPos, Quaternion.identity, container);
        go.transform.localScale = scale;
        go.name = blockName;

        if (isWall && go.GetComponent<MazeWall>() == null)
            go.AddComponent<MazeWall>();

        return go;
    }

    /// <summary>生成したブロックのマテリアルを差し替える（Prefabのマテリアル自体は変更しない）。</summary>
    static void ApplyMaterial(GameObject go, Material material)
    {
        if (go == null) return;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            renderer.sharedMaterial = material;
    }
}
