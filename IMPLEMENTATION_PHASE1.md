# Phase 1: net-bootstrap（FishNet 基盤セットアップ）

## 実装開始前に確認

- [ ] Unity 2023 LTS 以上
- [ ] FishNet 導入済み（ProjectSettings/Packages/ に設定あり）
- [ ] Assets/Scripts/ に自分たちのスクリプトだけ（FishNet 公式スクリプトフォルダは NO）

## 新しく作成するファイル

### 1. Assets/Scripts/Network/GameManager.cs

```
• NetworkBehaviour 継承
• ゲーム状態機械（Lobby→Countdown→Playing→Finished）
• [SerializeField] public float gameTimeLimit = 300f; で制限時間
• ServerRpc で状態遷移、ObserversRpc で全クライアント同期
• TimerUI.StartTimer() / StopTimer() を駆動
• GolfHole.onBallHoled に接続して勝者判定
• 実装後：LobbyManager → GameManager に変更
```

### 2. Assets/Scripts/Network/NetworkSpawner.cs

```
• ServerManager を取得して SpawnObject() を呼び出し
• ボール4個を Instantiate → Spawn() 管理
• プレイヤー4個を SpawnWithClientData() で異なる ID 付きスポーン
• 既存の Ball prefab / PlayerController prefab をそのまま NetworkObject 化
```

### 3. Assets/Scripts/Network/BallNetworkSync.cs

```
• GolfBall に NetworkBehaviour コンポーネント追加
• NetworkTransform で球の位置・回転を同期（サーバー権威）
• physics ownership = Server
```

### 4. Assets/Scripts/Network/PlayerNetworkSync.cs

```
• PlayerController に NetworkBehaviour コンポーネント追加
• InputData struct（方向キー入力）を [SerializeField] で追跡
• OwnershipCheck(): IsOwner なら操作可、さもなくば受け取った InputData から位置更新
• MoveCharacter(InputData) で実際の CharacterController 操作
```

## 既存ファイルの変更

| ファイル | 変更内容 |
|---------|--------|
| LobbyManager.cs | GUI から NetworkSpawner を呼び出す |
| TimerUI.cs | ローカル Update ではなく GameManager から駆動（StartTimer()/StopTimer() は既にあるので概ねOK） |
| ScreenSwitcher.cs | ShowLobby() / ShowCountdown() / ShowPlaying() 内で GameManager 状態を参照 |

## 確認ポイント（実装後のテスト）

### 1. シーン構成

- [ ] ロビーシーン → ゲームシーン遷移で NetworkManager が破壊されない
- [ ] `DontDestroyOnLoad(NetworkManager)` が必須

### 2. 2窓 LAN テスト

- [ ] PC1（ホスト）でゲーム起動 → Tugboat listen
- [ ] PC2（クライアント）でホストIP:Port 入力 → 接続
- [ ] 両方に 4個のボール + 4プレイヤーが見える
- [ ] ホスト側で時間が進む → クライアント側にも反映（同じ秒数）

### 3. 入力同期

- [ ] クライアント側で移動キー → ホスト側でプレイヤーが動く（遅延OK、数フレーム程度）

## 依存関係

- [ONLINE_DESIGN.md](ONLINE_DESIGN.md) — ゲーム仕様・権限モデル
- [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md) — FishNet パターン・既存コード活用法
- FishNet ドキュメント — NetworkManager セットアップ、ServerRpc/ObserversRpc パターン

## 次のフェーズへ向けて

Phase 1 完了後に **net-player-spawn** へ進む。  
その時点で各プレイヤーが異なるスタート位置から始まる設定を実装する。
