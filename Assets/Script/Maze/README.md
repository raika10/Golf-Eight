# 迷路システム 説明書

ブロックを組み合わせて迷路を自動生成し、壁を個別に破壊できるシステムです。
メッシュ変形ではなく **1枚1枚が独立したGameObject（壁ブロック）** なので、
ゲーム中に壁を壊す演出を簡単に組み込めます。

---

## 1. ファイル構成

| ファイル | 役割 | 手動アタッチ |
|---|---|---|
| `MazeGenerator.cs` | 迷路を自動生成する司令塔 | ✅ 空GameObjectに付ける |
| `MazeWall.cs` | 壁1枚の耐久・破壊表現 | ❌ 生成時に自動で付く |
| `FragmentCleanup.cs` | 破片の後始末（自動で縮小・消去） | ❌ 破壊時に自動で付く |
| `WallDestroyTester.cs` | 破壊のテスト用（クリックで壊す） | ✅ Main Cameraに付ける |

**手で付けるのは `MazeGenerator` と `WallDestroyTester` の2つだけ** です。

---

## 2. セットアップ手順

### ① 壁などのPrefabを用意
最低限 **Wall Prefab（壁）** が必要です。床・柱は任意。
- `Cube`（Hierarchy → 3D Object → Cube）を Project ウィンドウにドラッグしてPrefab化すればOK。
- Cubeなら Collider が標準で付くので、そのまま使えます。

### ② 迷路の生成元を作る
1. Hierarchy で右クリック →「Create Empty」→ 名前を「MazeManager」などに。
2. `MazeGenerator` をアタッチ。
3. Inspector で **Wall Prefab を設定（必須）**。Floor / Pillar Prefab は任意。

### ③ 破壊テスターを付ける（動作確認用）
1. Main Camera を選択。
2. `WallDestroyTester` をアタッチ（`cam` 欄は空でOK、自動で自分のCameraを使用）。

### ④ 再生
- Play すると迷路が自動生成されます。
- 壁を **左クリック** で破壊、**R キー** で全破壊できます。

---

## 3. 迷路の生成アルゴリズム（MazeGenerator）

**DFS（深さ優先探索）による穴掘り法** を使っています。

1. すべてのマスの境界に壁を立てた状態から開始。
2. スタート地点(0,0)から、まだ訪れていない隣のマスへランダムに進む。
3. 進んだ方向の壁を1枚消して「通路」を作る。
4. 行き止まりになったら1つ戻り(バックトラック)、別の道を探す。
5. 全マスを訪れたら完成。

これにより、**すべてのマスが必ず1本の道でつながった迷路**（ループなし）ができます。

### 壁データの持ち方
- `hWalls[y, x]` … 水平壁（東西に伸びる壁、Z方向の境界）
- `vWalls[y, x]` … 垂直壁（南北に伸びる壁、X方向の境界）
- `true` = 壁あり、`false` = 通路

生成後、`SpawnBlocks()` が壁データを見て、壁のある場所にだけPrefabをInstantiateします。
壁ブロックには自動で `MazeWall` コンポーネントが付きます。

### 生成後のオブジェクト構造
```
MazeManager
└─ MazeBlocks
   ├─ Floor_0_0, Floor_1_0, ...   ← 床タイル
   ├─ HWall_0_0, ...              ← 水平壁（MazeWall付き）
   ├─ VWall_0_0, ...              ← 垂直壁（MazeWall付き）
   └─ Pillar_0_0, ...             ← 角の柱
```

### MazeGenerator の主なパラメータ
| 項目 | 説明 |
|---|---|
| `width` / `height` | 迷路のマス数 |
| `cellSize` | 1マスの大きさ（Unity単位） |
| `wallHeight` / `wallThickness` | 壁の高さ・厚み |
| `useRandomSeed` | ON=毎回ランダム / OFF=固定シード |
| `seed` | 固定シード値（同じ値なら同じ迷路を再現） |

> 💡 Inspector 右上の「⋮」メニュー →「Generate Maze」で、Play せずにエディタ上でも生成できます。

---

## 4. 壁の破壊表現（MazeWall）

壁が壊れるとき、**壁と同じサイズ・向きの小さなブロック群に分割** して物理で飛散させます。
（メッシュのランタイム破砕ではなく、ブロックの再配置なので軽量かつ調整しやすい）

### 処理の流れ
```
TakeDamage() でHPが0以下になる
  → Shatter() が壁を小ブロックに分割
  → 各ブロックに Rigidbody を付け、衝撃点から爆発力で飛散
  → FragmentCleanup が数秒後に縮小して自動消去
```

### 割れ方が均一になる仕組み
分割数を固定せず、**壁の実寸を「破片1個の目安サイズ」で割って自動計算** します。
そのため、横長の壁も縦長の壁も同じ大きさのブロックに割れます。
破片は壁のローカル座標系で並べるので、壁が回転していても割れ目が壁の面に沿います。

### MazeWall の主なパラメータ
| 項目 | 説明 |
|---|---|
| `hp` | 壁の耐久値。0以下で破壊 |
| `shatterOnDestroy` | OFFにすると破片を出さず即消去 |
| `fragmentSize` | 破片1個の目安サイズ。小さいほど細かく割れる（重くなる） |
| `explosionForce` | 破片を吹き飛ばす力 |
| `explosionRadius` | 爆風の半径 |
| `fragmentLifetime` | 破片が消えるまでの秒数 |
| `fragmentMaterial` | 破片のマテリアル（未指定なら壁のものを流用） |
| `onDestroyed` | 破壊時に発火するイベント（SEやパーティクルを接続） |

---

## 5. ゲームから壁を壊す方法

### ボールなどの衝突で壊す例
壊したいオブジェクト（ゴルフボールなど）側にこう書きます。

```csharp
void OnCollisionEnter(Collision col)
{
    var wall = col.gameObject.GetComponent<MazeWall>();
    if (wall != null)
    {
        Vector3 point = col.GetContact(0).point;   // 当たった位置
        Vector3 vel   = col.relativeVelocity;       // 衝突の速度
        wall.TakeDamage(1, point, -vel);            // 進行方向に破片が飛ぶ
    }
}
```

### 呼び出せるメソッド
| メソッド | 用途 |
|---|---|
| `TakeDamage(int amount)` | ダメージのみ（衝撃情報なし） |
| `TakeDamage(int amount, Vector3 impactPoint, Vector3 impactVelocity)` | 衝撃点・速度つき（自然な飛散） |
| `DestroyWall()` | 即破壊 |

---

## 6. 破壊テスター（WallDestroyTester）

Play中に破壊挙動を確認するための道具です。**Main Camera** に付けて使います。

| 操作 | 動作 |
|---|---|
| 左クリック | 狙った壁を即破壊（クリック地点が衝撃点、視線方向に破片が飛ぶ） |
| R キー | シーン内の全壁を一斉破壊（負荷テストにも使える） |

> ⚠️ このプロジェクトは新 Input System を使用しているため、
> テスターも `Mouse.current` / `Keyboard.current` で入力を取得しています。
> 完成後はこのコンポーネントを外して構いません。

---

## 7. パフォーマンスの注意

- 壁は数百枚になり得ますが、**壊れた壁だけ**が破片化するので通常は問題ありません。
- 短時間に大量破壊するなら、`GameObject.CreatePrimitive` のコストが効いてきます。
  その場合は **破片のオブジェクトプール化** が次の改善点です（必要になってから対応で十分）。
- 破片を細かくしすぎる（`fragmentSize` を小さくしすぎる）と、
  1回の破壊で大量のRigidbodyが生成され重くなります。

---

## 8. 今後の拡張アイデア
- `onDestroyed` に土煙パーティクルや破壊SEを接続して迫力を出す。
- 壁の `hp` を上げて「複数回当てないと壊れない壁」を作る。
- ゴール地点・スタート地点のマーカーを `SpawnBlocks()` に追加する。
