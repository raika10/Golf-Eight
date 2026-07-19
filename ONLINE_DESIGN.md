# Golf-Eight オンライン化設計（FishNet）

Golf-Eight を FishNet でオンライン対戦化する計画（2026-07-19 に設計）。実装は未着手。

## ゲーム仕様

- **プレイ人数**: 4人以下
- **スタート**: 全員同じ位置からスタート
- **ゲーム時間**: N分で1ゲーム
- **勝利条件**: 誰かがホールインで勝ち / 時間切れで引き分け
- **ルーム作成**: ランダム4文字コードを生成・表示（接続には使わず）

## 確定した設計判断

### ネットワーク

- **LAN内限定**。リレー不要、FishNet の Tugboat（直結UDP）一本
- **接続キー＝ホストのLAN IP:Port を直接入力**して参加
  - ランダム4文字コードは接続には使わず表示だけ
  - 現 `RoomJoinValidator` の仮リスト検証は廃止予定
- ホスト型（1人がサーバー兼クライアント、最大4人）

### 物理の権限モデル

| 対象 | 権限 | 同期方式 |
|------|------|--------|
| ボール (×4) | サーバー権威 | サーバーが Rigidbody 所有、NetworkTransform で補間 |
| プレイヤー | 所有者権威 | 各自が CharacterController を操作、位置同期 |
| 迷路 | サーバー権威 | NetworkObject にしない、seed ベース生成 + 破壊状態同期 |

### ゲーム状態

- **サーバー側**: GameManager が状態機械 `Lobby→Countdown→Playing→Finished` を管理
- **全体**: `ServerRpc`/`ObserversRpc` で状態遷移を同期

### 迷路の同期

- **方針**: `MazeSeed` を SyncVar で配り、各クライアントが決定論生成
- **破壊**: サーバー判定→`ObserversRpc` で全員同期
- **破片**: 見た目だけなのでローカル OK

### 打撃・ボール操作

- 打撃/吹っ飛ばしは `ServerRpc`→サーバー物理→同期

## 既存コードの活かせる点

| ファイル | 現在の用途 | 活かし方 |
|---------|----------|--------|
| PlayerController | IsLocalPlayer で分岐 | IsOwner に置き換えればほぼ流用可 |
| BallHitController | 打撃処理 | ServerRpc で送信して同期 |
| MazeGenerator | 決定論生成 | seed 受け取り → 全クライアント同期生成 |
| GolfHole | ホールイン判定 | onBallHoled イベントをサーバーに接続 |
| TimerUI / CountdownUI | UI 表示 | サーバー駆動へ改造 |
| ScreenSwitcher | シーン遷移 | サーバー状態参照 |

## 実装フェーズ（feature ブランチ）

各フェーズを 2窓 LAN テストで検証してからマージ。

1. **net-bootstrap** — NetworkManager, GameManager, スポーン基盤
2. **net-player-spawn** — 各プレイヤーのスタート位置割り当て
3. **net-maze-sync** — Seed ベース迷路生成 + 破壊状態同期
4. **net-ball-hit** — ボール打撃と壁破壊の同期
5. **net-gamestate** — ゲーム状態機械の全体動作
6. **net-winflow** — 勝者判定と勝敗画面

---

詳細は以下を参照：
- [Implementation Phase 1](IMPLEMENTATION_PHASE1.md)
- [Unity MCP Setup](UNITY_MCP_SETUP.md)
- [Implementation Guide](IMPLEMENTATION_GUIDE.md)
