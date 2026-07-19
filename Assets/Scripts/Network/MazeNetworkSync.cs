using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace GolfEight.Network
{
    /// 迷路をサーバー権威のシードで同期生成する薄いアダプタ。
    /// MazeGenerator自体は変更せず、シーン側でMazeGeneratorを無効化（Start自動生成を止める）した上で、
    /// このアダプタがOnStartServer/OnStartClientでシードを確定・共有し、GenerateMaze()を呼び出す。
    [RequireComponent(typeof(MazeGenerator))]
    public class MazeNetworkSync : NetworkBehaviour
    {
        private MazeGenerator mazeGenerator;

        private readonly SyncVar<int> seed = new SyncVar<int>();

        private void Awake()
        {
            mazeGenerator = GetComponent<MazeGenerator>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            seed.Value = Random.Range(0, int.MaxValue);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            mazeGenerator.useRandomSeed = false;
            mazeGenerator.seed = seed.Value;
            mazeGenerator.GenerateMaze();
            BuildWallCache();
        }

        /// 生成した壁を名前で引けるように辞書化する。壁は数百個あり、破壊のたびに
        /// シーン全走査（FindObjectsByType）していると無駄が大きいので、生成直後に一度だけ作る。
        private void BuildWallCache()
        {
            wallsByName.Clear();
            foreach (MazeWall wall in GetComponentsInChildren<MazeWall>(includeInactive: true))
            {
                wallsByName[wall.gameObject.name] = wall;
            }
        }

        // 名前 → 壁。迷路は各クライアントが同じシードから独立生成するので、壁の同期は名前をキーにする。
        private static readonly Dictionary<string, MazeWall> wallsByName = new Dictionary<string, MazeWall>();

        /// 名前（MazeGenerator が付ける "HWall_x_y" / "VWall_x_y"）で壁を探す。
        /// 迷路はNetworkObjectではなく各クライアントが同じシードから独立生成しているため、
        /// 壁の破壊を同期する際はインスタンス参照ではなく名前をキーにする。
        public static MazeWall FindWallByName(string wallName)
        {
            if (string.IsNullOrEmpty(wallName))
            {
                return null;
            }

            if (wallsByName.TryGetValue(wallName, out MazeWall cached))
            {
                if (cached != null)
                {
                    return cached;
                }
                // 完全に壊れて Destroy 済み。エントリを片付けて「無い」を返す
                wallsByName.Remove(wallName);
                return null;
            }
            return null;
        }
    }
}
