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
        }
    }
}
