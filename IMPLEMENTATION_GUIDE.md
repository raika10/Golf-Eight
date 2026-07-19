# 実装ガイド：FishNet パターンと既存コード活用法

## FishNet 基本パターン

### 1. NetworkBehaviour 継承

```csharp
using FishNet.Component.Authoring;

public class MyNetworkScript : NetworkBehaviour
{
    // NetworkBehaviour を継承したコンポーネントをプレハブに付ける
}
```

- prefab をスポーン時に `ServerManager.Spawn()` で管理対象にする

### 2. ServerRpc と ObserversRpc

```csharp
// サーバー側でのみ実行
[ServerRpc]
private void ChangeGameState_ServerRpc(GameState newState)
{
    // サーバーだけが実行
    ChangeGameState_ObserversRpc(newState);
}

// 全クライアント側で実行
[ObserversRpc]
private void ChangeGameState_ObserversRpc(GameState newState)
{
    // 全クライアント（サーバー含む）が実行
}

// 呼び出し時
ChangeGameState_ServerRpc(GameState.Playing);
```

### 3. IsOwner / IsServerOnly

```csharp
if (!IsOwner) return;  // 自分の所有権がないなら処理しない
if (IsServerOnly) { /* サーバーのみの処理 */ }
```

## 既存コードの活かし方

### PlayerController.cs

| 現在 | 変更 |
|------|------|
| `IsLocalPlayer` で input checking | `IsOwner` に置き換える |
| LocalPlayer フラグ | ネットワーク無関係なので削除可 |
| input の同期 | `PlayerNetworkSync` に任せる |

**変更はほぼ IsOwner への置き換えだけで OK**

### GolfBall.cs と BallHitController.cs

| 現在 | 変更 |
|------|------|
| ローカル単一球 | 4個のボール全てが NetworkBehaviour |
| 各自が Rigidbody を持つ | サーバーのみが Rigidbody を持つ（physics ownership = Server） |
| ローカルで破壊 | クライアントは NetworkTransform で位置・回転を受け取るだけ |
| - | 破壊イベント（MazeWall.CarveSphere() 呼び出し）は ServerRpc で送信 |

### MazeGenerator.cs

| 活用方法 | 用途 |
|--------|------|
| `GenerateMaze(seed)` の決定論性 | クライアント側でも同じ seed で迷路を生成（MazeSyncManager で実装） |
| - | **注意**: 破壊状態は別途 SyncVar で配り直す（seed だけでは足りない） |

### TimerUI.cs / CountdownUI.cs

| 現在 | 変更 |
|------|------|
| `StartCoroutine()` でローカル進行 | GameManager が ServerRpc で状態遷移 → ObserversRpc で全員の UI 駆動 |

**実装パターン**:

```csharp
// GameManager (server-side)
[ServerRpc]
void StartCountdown_ServerRpc()
{
    // サーバー: Countdown 状態へ遷移
    StartCountdown_ObserversRpc();
}

[ObserversRpc]
void StartCountdown_ObserversRpc()
{
    // 全クライアント: UI を開始
    countdownUI.StartCoroutine(countdownUI.PlayCountdown());
}
```

### GolfHole.cs

| 活用方法 | 用途 |
|--------|------|
| `onBallHoled` イベント | GameManager が subscribe して勝者判定 |

**接続パターン**:

```csharp
// GameManager.cs
void OnEnable()
{
    golfHole.onBallHoled.AddListener(OnBallHoled);
}

void OnBallHoled(GolfBall ball)
{
    // サーバー側: 勝者判定 → ObserversRpc で宣言
    DeclareWinner_ObserversRpc(ball.ownerClientId);
}
```

## 壁破壊との同期

### 現在の実装

`BallHitController.BreakWallsAfterSwing()`

### FishNet 対応パターン

```csharp
// BallHitController.cs

void OnSwing()
{
    if (IsOwner)
    {
        // クライアント: サーバーに破壊を要求
        RequestWallBreak_ServerRpc(hitPos, hitRadius);
    }
}

[ServerRpc]
void RequestWallBreak_ServerRpc(Vector3 pos, float radius)
{
    // サーバー: 実際に破壊処理
    MazeWall wall = Physics.OverlapSphere(pos, radius)[0];
    wall.CarveSphere(pos, radius);
    
    // 全クライアントに通知（破壊状態を同期）
    NotifyWallBroken_ObserversRpc(wallId, pos, radius);
}

[ObserversRpc]
void NotifyWallBroken_ObserversRpc(int wallId, Vector3 pos, float radius)
{
    // 全クライアント: 見た目を同期
    mazeWalls[wallId].VisualizeBrokenArea(pos, radius);
}
```

## LAN 接続（Tugboat）

### NetworkManager 設定

| 項目 | 値 |
|------|-----|
| Transport | Tugboat を選択 |
| Listen on port | 7770（デフォルト） |
| External IP | ホスト側の LAN IP（例: 192.168.1.100） |

### 接続フロー

1. **ホスト側**: `ServerManager.StartConnection()` で listen 開始
2. **クライアント側**: `ClientManager.StartConnection(hostIp, 7770)` で接続
3. **既存の RoomJoinValidator**: 廃止（IP直接入力へ）

## デバッグのコツ

- **MCP が繋がったら**: `Unity_GetConsoleLogs` で Editor console を見ながら実装
- **シーン遷移**: `DontDestroyOnLoad(NetworkManager)` を忘れずに
- **ローカルテスト**: 2つの Unity Editor を同時起動して 2窓テスト
- **Spawn確認**: サーバー側で `Spawn()` の戻り値 != null か確認

## 承認されたアプローチ

✅ **単一ブランチ運用**: Feature ブランチは最小単位（Phase 毎に分ける）  
✅ **LAN-only 開発**: サーバー/クライアント分岐テストは 2窓で十分  
✅ **seed ベース迷路同期**: 全員が同じ seed → 同じ迷路 → 破壊状態のみ同期

---

参照：
- [ONLINE_DESIGN.md](ONLINE_DESIGN.md) — 全体設計
- [IMPLEMENTATION_PHASE1.md](IMPLEMENTATION_PHASE1.md) — Phase 1 詳細
- [UNITY_MCP_SETUP.md](UNITY_MCP_SETUP.md) — MCP セットアップ
