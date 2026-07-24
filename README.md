# Golf-Eight

迷路を壊しながらゴールを目指す、最大4人のマルチプレイ対戦ゴルフゲーム（Unity製）。

自キャラを操作してゴルフボールを打ち、迷路の壁をスイングで破壊しながら最短ルートを切り開き、
誰よりも早くホールインを目指します。

## ゲーム概要

- **プレイ人数**: 最大4人（LAN内対戦）
- **勝利条件**: 誰かがホールイン → 勝ち / 制限時間内に決着しなければ引き分け
- **迷路**: 毎回シード値から自動生成。壁はスイングで破壊可能（ブロック単位で吹き飛ぶ）
- **ネットワーク**: [FishNet](https://fish-networking.gitbook.io/docs/) による LAN 内直結対戦（ホストのIP:Portを直接入力して参加）

## 操作方法

| 操作 | キー |
|---|---|
| 移動 | W / A / S / D |
| 視点 | マウス |
| ジャンプ | Space |
| スイング（左クリック長押しでチャージ→離すと打つ） | マウス左クリック |

## 動作環境

- Unity **6000.3.17f1**（Unity 6）
- Render Pipeline: URP
- Input System: 新Input System（`Keyboard.current` / `Mouse.current`）

主なパッケージ：
- FishNet（ネットワーク同期）
- Unity AI Navigation
- Unity Multiplayer Center

## セットアップ

1. Unity Hub からこのプロジェクトを Unity **6000.3.17f1** で開く
2. `Assets/Scenes/TitleScene.unity` から起動
   - ホスト側: ルームを作成してLAN IPを表示
   - クライアント側: ホストのLAN IP:Portを入力して参加
3. 全員揃ったら `Assets/Scenes/GameScene.unity` でゲーム開始

LAN内2台での対戦テストが基本ですが、[ParrelSync](https://github.com/VeriorPies/ParrelSync) を使えば
1台のPCでもエディタを複製してホスト/クライアントの2窓テストが可能です。

## プロジェクト構成

```
Assets/
├─ Scripts/
│  ├─ Player/     … 移動・カメラ・スイング・ノックバックなど自キャラ制御
│  ├─ Ball/       … ゴルフボールの物理・打撃・ホール判定
│  ├─ Maze/       … 迷路の自動生成・壁の破壊表現（詳細: Assets/Scripts/Maze/README.md）
│  ├─ Network/    … FishNetによる状態同期・ロビー・スポーン管理
│  ├─ Ui/         … タイマー・ミニマップ・画面遷移などUI
│  └─ Audio/      … BGM・ボイス再生
├─ Prefabs/       … Ball / Player / Hole / Maze の各プレハブ
└─ Scenes/        … TitleScene（タイトル・ロビー）/ GameScene（本編）ほかテスト用シーン
```

## ドキュメント

設計・実装の詳細は以下を参照してください。

- [ONLINE_DESIGN.md](ONLINE_DESIGN.md) — オンライン対戦化の全体設計（FishNet）
- [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md) — FishNetの実装パターンと既存コードの活かし方
- [IMPLEMENTATION_PHASE1.md](IMPLEMENTATION_PHASE1.md) — Phase 1（net-bootstrap）の実装手順
- [UNITY_MCP_SETUP.md](UNITY_MCP_SETUP.md) — Unity MCPのセットアップ
- [Assets/Scripts/Maze/README.md](Assets/Scripts/Maze/README.md) — 迷路生成・壁破壊システムの仕様
