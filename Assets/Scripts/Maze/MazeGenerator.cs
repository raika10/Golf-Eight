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

    [Header("乱数シード")]
    public bool useRandomSeed = true;
    public int seed;

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
        SpawnBlocks();

        Debug.Log($"[MazeGenerator] 迷路生成完了 (seed={seed}, {width}x{height})");
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

    // ── ブロック配置 ────────────────────────────────────────────────

    void SpawnBlocks()
    {
        float half = cellSize * 0.5f;
        float halfH = wallHeight * 0.5f;

        // 床タイル
        if (floorPrefab != null)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    SpawnBlock(
                        floorPrefab,
                        new Vector3(x * cellSize + half, 0f, y * cellSize + half),
                        new Vector3(cellSize, 0.1f, cellSize),
                        $"Floor_{x}_{y}",
                        isWall: false);
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
                    SpawnBlock(
                        pillarPrefab,
                        new Vector3(x * cellSize, halfH, y * cellSize),
                        new Vector3(wallThickness, wallHeight, wallThickness),
                        $"Pillar_{x}_{y}",
                        isWall: false);
        }
    }

    void SpawnBlock(GameObject prefab, Vector3 localPos, Vector3 scale, string blockName, bool isWall)
    {
        if (prefab == null) return;

        var worldPos = transform.TransformPoint(localPos);
        var go = Instantiate(prefab, worldPos, Quaternion.identity, container);
        go.transform.localScale = scale;
        go.name = blockName;

        if (isWall && go.GetComponent<MazeWall>() == null)
            go.AddComponent<MazeWall>();
    }
}
