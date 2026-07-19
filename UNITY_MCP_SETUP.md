# Unity MCP セットアップ

## 前提条件

- [ ] Claude Code CLI インストール済み
- [ ] 別の PC での初回セットアップ想定
- [ ] Unity Editor 起動済み

## セットアップ手順

### 1. Unity Editor 内で MCP サーバー有効化

1. `Project Settings > AI > Unity MCP` を開く
2. トグルを **ON** に（ここで MCP サーバーが起動）
3. ポート番号確認（デフォルト: 6000 前後）

### 2. Claude Code 側で接続

Claude Code CLI で unity-mcp コマンドが使えるか確認します。（内部的に MCP サーバーへ接続を試みる）

### 3. 初回接続時の承認

1. Claude Code が接続を試みる
2. Unity Editor に「Claude Code からの接続許可」ダイアログが出る
3. **「Always Allow」または「Allow」を選ぶ**（推奨: Always Allow）
4. これで approval list に登録される

## トラブルシューティング

### 症状: 「Connection revoked」エラーが出る

Claude Code から `Unity_GetConsoleLogs` などの MCP コマンドを実行しても
```
Connection revoked. Go to Project Settings > AI > Unity MCP to change approval.
```

### 原因と対処

#### 1. 承認ポリシーが「Deny」に設定されている

1. `Project Settings > AI > Unity MCP` を再度開く
2. Policy dropdown で「Auto Approve」または「Always Allow」を選択
3. Apply

#### 2. approval list に「Claude」が「Revoked」状態で残っている

1. approval list を確認
2. 「Claude」 or 「Claude Code」エントリに「Revoked」マークがあれば削除
3. 削除後、Claude Code から再接続試行

#### 3. MCP サーバーが起動していない

1. Unity Editor の `Project Settings > AI > Unity MCP` の toggle が **ON** か確認
2. OFF なら ON に切り替える（自動で MCP サーバー起動）
3. コンソールに `[MCP] Server listening on port XXXX` が出ているか確認

#### 4. それでも駄目な場合（根治策）

1. Unity Editor を **完全に閉じる**
2. 3秒待つ
3. Unity Editor を再度開く
4. `Project Settings > AI > Unity MCP` で toggle ON
5. Claude Code で再接続試行

## 接続確認

成功すると以下のコマンドが Claude Code から使えます：

```bash
# Unity Console ログを取得
Unity_GetConsoleLogs

# Unity Editor コマンド実行
Unity_RunCommand

# シーン画面をスクリーンショット
Unity_SceneView_Capture2DScene
```

## よくある間違い

| 間違い | 対処 |
|------|------|
| Unity Editor を閉じた状態で Claude Code から接続試行 | ✅ Unity Editor は常に起動しておく |
| approve dialog を「Deny」したまま放置 | ✅ Policy を「Always Allow」に変更、または dialog で「Allow」を選択 |
| approval list に複数の「Claude」エントリがあって混在 | ✅ 古い「Revoked」エントリを削除してスッキリさせる |

## デフォルト設定

| 項目 | 値 |
|------|-----|
| MCP Server Port | 6000（Unity Editor が listen） |
| Approval Policy | Auto Approve or Manual（dialog 毎に許可） |
| Network | localhost のみ（LAN 越しの接続は不可） |
