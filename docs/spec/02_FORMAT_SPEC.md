# 02. VLat フォーマット仕様 v0.1

拡張子: `.vlat.json`(本体、UTF-8 JSON)+ `.vlat.bin`(任意、バイナリバッファ)。
1つの VLatドキュメントは1体のVRMアバターに対する視点依存差分レイヤを記述する。

## 1. トップレベル構造

```jsonc
{
  "vlat": {
    "version": "0.1.0",              // SemVer 2.0.0。互換性規則は§6
    "generator": "VLat Editor 0.1",
    "targetAvatar": {                // §4 バインディング
      "name": "SampleAvatar",
      "vrmVersion": "1.0",
      "meshBindings": [ /* MeshBinding[] */ ]
    }
  },
  "buffers": [ { "uri": "sample.vlat.bin", "byteLength": 123456 } ],
  "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 4096 } ],
  "textures": [ /* Texture[]; 省略可 */ ], // §3.2
  "angleBands": [ /* AngleBand[] */ ],   // §2
  "layers":     [ /* Layer[] */ ]        // §3
}
```

設計方針: buffers/bufferViews の参照とバイト範囲は glTF 2.0 と同じ意味論に
揃える(将来 glTF extension 化する際の親和性のため)。v0.1 の `buffers[].uri` は
JSONファイルを基準とする外部ファイルの相対URIに限定し、絶対URIと data URI は
許可しない。`../` を含む親相対URIは許可する。

数値の実装範囲: インデックス、`vertexCount`、`priority`、`renderQueue.offset`
は符号付き32 bit整数の範囲とする（インデックスと `vertexCount` は0以上）。
buffer / bufferView の `byteOffset` と `byteLength` は符号付き64 bit整数の
非負範囲とし、`byteLength` は1以上とする。
その他の数値は有限な IEEE 754 binary64 の範囲とし、NaN と正負の無限大、および
binary64 の最大有限値を超える数値は許可しない。

## 2. AngleBand — 角度帯

評価角度は **頭部ボーン(VRM Humanoid `head`)ローカル座標系におけるカメラ方向** から算出する。

- 頭部ローカル前方 +Z、上 +Y、右 +X(VRM 1.0 規約に従う)。
- `dir = normalize(headWorldToLocal * (cameraPos - headPos))`
- `yaw = atan2(dir.x, dir.z)` ∈ (−180°, 180°]、正面=0°、キャラクターの左手側から見る=正。
- `pitch = asin(dir.y)` ∈ [−90°, 90°]、見上げ=負、見下ろし=正。
- roll は v0.1 では未使用。

```jsonc
{
  "id": "band_profileL",
  "name": "左横顔",
  "yaw":   { "min": 60, "max": 120 },     // 度。min>max で ±180 跨ぎを表現
  "pitch": { "min": -30, "max": 30 },
  "feather": { "yaw": 15, "pitch": 10 },  // 境界外側への遷移幅(度)
  "hysteresis": 0                          // 度。visibility系のみ有効。省略時0
}
```

**重み関数**: 帯内部で 1、feather 域では境界からの角距離に対する smootherstep(6t⁵−15t⁴+10t³)で 0→1。yaw・pitch 重みの積を最終重み w ∈ [0,1] とする。

`hysteresis` は AngleBand の連続重みを変更しない。visibility Modifier
ごとに状態を持ち、OFF→ON は通常の `w ≥ 0.5`、ON→OFF は yaw/pitch
各軸の `w=0.5` 等価境界を `hysteresis` 度だけ外側へ広げた領域の
いずれか一軸を出たときとする。

## 3. Layer と Modifier

### 3.1 Layer
```jsonc
{
  "id": "layer_bangs",
  "name": "前髪補正",
  "tag": "bangs",            // 意味タグ: bangs|sideHair|hairHighlight|eyeHighlight|faceLine|other
  "enabled": true,
  "priority": 0,             // 大きいほど優先。§3.3
  "bandId": "band_profileL",
  "modifiers": [ /* Modifier[] */ ]
}
```

1ドキュメントの `layers` は最大64件とする。これはランタイムで事前確保する
評価バッファと、動的に追加するブレンドシェイプ数を制限するためのv0.1上限である。

### 3.2 Modifier(判別共用体: `type` で分岐)

**vertexDelta** — 形状補正。バインドポーズ・メッシュローカル空間の頂点変位。
```jsonc
{
  "type": "vertexDelta",
  "meshBinding": 0,                    // meshBindings のインデックス
  "indices":   { "bufferView": 0, "componentType": "UINT32" },  // スパース: 対象頂点index列
  "positions": { "bufferView": 1, "componentType": "FLOAT32" }, // 同数の vec3
  "normals":   { "bufferView": 2, "componentType": "FLOAT32" }  // 任意。省略時は再計算しない
}
```
- 適用: 重み w を係数として加算(ランタイムでは動的ブレンドシェイプの weight = w*100)。
- 格納は必ずスパース(NFR-3)。
- `indices` はlittle-endian UINT32の連続列（4 byte/要素）、`positions`と`normals`は
  little-endian FLOAT32 vec3の連続列（12 byte/要素）とし、strideは持たない。
  3列の要素数は一致しなければならない。indexは`vertexCount`未満かつ列内で一意、
  FLOAT32成分は有限値でなければならない。

**visibility** — 表示/非表示。
```jsonc
{ "type": "visibility", "meshBinding": 1, "subMesh": 0, "visibleInBand": false }
```
- w ≥ 0.5 で状態切替(+バンドの hysteresis)。サブメッシュ単位の非表示はレンダラ分割不可の場合シェーダマスク(MToonのalpha)にフォールバックし、その旨をランタイムが警告する。

**opacity** — 透明度。`meshBinding` と `materialSlot` で対象を一意に指定する。
```jsonc
{ "type": "opacity", "meshBinding": 0, "materialSlot": 0, "value": 0.35 }
```
- 実効値 = lerp(1, value, w)。MToon の透明モードが Opaque の場合、ロード時に TransparentWithZWrite のマテリアルインスタンスへ複製して差し替える(非破壊: 元マテリアルアセットは不変)。

**materialProperty** — 任意マテリアルプロパティ。
```jsonc
{
  "type": "materialProperty",
  "meshBinding": 0,
  "materialSlot": 0,
  "property": "_ShadeColor",
  "valueType": "color",              // float|color|vector|textureIndex
  "bandValue": [0.9, 0.85, 1.0, 1],  // w=1 での値
  "interpolate": true                 // false なら w≥0.5 でスイッチ
}
```
- textureIndex の場合 `textures` 配列を参照。テクスチャは補間せずスイッチのみ。

Texture 要素はJSONファイルを基準とする外部ファイルの相対 URI または bufferView の
ちょうど一方を持つ。
bufferView 参照時は `mimeType` を必須とする。

```jsonc
{ "name": "Highlight", "uri": "highlight.png" }
{ "name": "Mask", "bufferView": 3, "mimeType": "image/png" }
```

**renderQueue** — 描画順オフセット。
```jsonc
{ "type": "renderQueue", "meshBinding": 0, "materialSlot": 0, "offset": 10 }
```
- w ≥ 0.5 でスイッチ(補間しない)。

### 3.3 競合解決
同一ターゲット(mesh/subMesh/material×プロパティ)に複数 Layer が作用する場合:
1. 重み `w > 0` の Layer のうち `priority` 最大の Layer 群のみが有効。
2. 同 priority 群の生重み合計 `S` が1以下なら各重みを保ち、ベース状態を
   `1-S` で混合する。`S > 1` のときだけ各重みを `S` で割って正規化する。
   スイッチ型プロパティは生重み最大の Layer が勝ち、その `w ≥ 0.5` のときに切り替える。
3. vertexDelta は例外的に全 priority を加算合成する(形状は加算が直感に合うため)。

## 4. MeshBinding — アバターへの参照

```jsonc
{
  "hierarchyPath": "Face/Hair_Front",         // ルートからの Transform パス
  "meshName": "Hair_Front",
  "vertexCount": 4820,
  "positionHash": "xxh64:9f2ab01234567890",   // バインドポーズ頂点座標のハッシュ
  "materials": [ { "slot": 0, "materialName": "MAT_Hair", "shader": "VRM10/MToon10" } ]
}
```

**再バインド規則**(F-1.5): ロード時に (1) hierarchyPath 完全一致 → (2) meshName + vertexCount 一致 → (3) vertexCount + positionHash 一致、の順で解決。解決不能な binding は Layer ごと無効化し、エディタは再バインドUIを提示する。vertexCount が不一致の vertexDelta は適用禁止(頂点index破綻防止)。

`positionHash` は、Meshの頂点index順に各positionの `x`, `y`, `z` を
IEEE 754 binary32のlittle-endian（1頂点12 byte）で連結し、seed 0のxxHash64を
計算する。頂点数や区切りは入力へ追加しない。文字列表現は
`xxh64:` + 16桁の小文字16進数とする。読み込み時は互換性のため大文字16進数も
受理してよい。

## 5. バリデーション
- JSON Schema (`vlat.schema.json`) を仕様の一部として同梱。CI でサンプルファイル全件を検証。
- 追加の意味的検証(C#側): bufferView範囲、indexの範囲・一意性、vertexDeltaの有限値と
  要素数、yaw/pitch範囲、id一意性、参照整合。

Phase 2A Editorのcanonical出力規則は`docs/PHASE2A_FORMAT_AUDIT.md`に記録する。
これはreaderが受理するv0.1ファイルを狭める新format要件ではなく、同じAuthoring Modelから
決定的なJSON/binaryを生成するためのwriter policyである。

## 6. バージョニング方針
- `version` は SemVer 2.0.0 に適合し、prerelease の数値識別子に先頭0を許可しない。
- `0.x` 間は破壊的変更を許容するが、マイグレーションノートを必ず残す。
- 読み込み側より新しい、同一major内のminorは警告を返したうえで現行Schemaとして
  検証する。現行Schemaの既知フィールドだけで構成されるドキュメントは読み込めるが、
  `additionalProperties: false` に反する未知フィールドは誤解釈防止のためエラーとする。
- 将来的な VRM/glTF extension 化(例: `VLAT_view_dependent`)を見据え、buffers 意味論と id 参照方式を glTF に揃えておく。

## 7. 既知の制約(仕様として明記)
- 評価カメラは単一。VR両眼は両眼中点で評価。
- 複数観察者の同時整合は非対応(カメラごとにアバター複製で運用)。
- ボーン・リグ構造の差分は表現しない。
- 頂点数が異なる改変アバターへの vertexDelta 転写は非対応(将来課題: 対応点探索による転写)。
