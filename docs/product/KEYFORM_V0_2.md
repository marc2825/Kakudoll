# KeyformGroup v0.2 — 最初の実装契約

2026-10-20。今回のユーザー指示とPRD FR-010/020/030に基づく追加契約。
既存 `docs/spec/02_FORMAT_SPEC.md` のv0.1を置換しない。
本書は単独グループの位置差分・CPU参照評価・I/Oのデータ契約。
2026-10-21のEditor接続は `EDITOR_KEYFORM_1A.md`、Previewは `EDITOR_KEYFORM_1C.md`。
2026-10-25の直接編集は `EDITOR_KEYFORM_1D.md`。
CPU Runtimeへの自動適用とGPU製品再生は別の能力である。

## 1. 互換性と配置

- `VLatDocument` に省略可能な `keyformGroups` を追加する。
- このフィールドを持つJSONのversionは厳密に `0.2.0`。v0.1では空配列も拒否する。
- キーを持たない従来の出力は、このフィールドを出力せず従来のまま保存する。
  旧readerはv0.2拡張を未知フィールドとして拒否するため、黙って形状を落とさない。
- v0.1は既存reader/writerで引き続き読み書きする。自動変換・原本上書きはしない。
- フィールドを省略した将来minorの旧形式文書については、既存の「警告して既知形式を検証」
  という規則を維持する。新しいKeyformプロファイルのSchemaとは区別する。
- v0.1 Schemaは変更せず、`schema/vlat.keyform-v0.2.schema.json` を追加した。
  旧Schemaのbinding/buffer/accessor/vertexDelta定義を相対参照で再利用する。
- `VlatValidator.SupportedVersion` は旧バックエンド用の0.1.0を維持する。
  `KeyformVersion` は0.2.0。従来のnewer-minor警告は残す。

## 2. データ

```json
{
  "id": "shape-keys",
  "meshBinding": 0,
  "topologyHash": "sha256:<64 lowercase hex digits>",
  "coordinateSpace": "bind-mesh-local",
  "interpolation": "yaw-smoothstep",
  "supportStart": -90,
  "supportEnd": 120,
  "keys": [
    {"yaw": -45, "delta": {"type": "vertexDelta", "meshBinding": 0,
      "indices": {"bufferView": 0, "componentType": "UINT32"},
      "positions": {"bufferView": 1, "componentType": "FLOAT32"}}},
    {"yaw": 0, "delta": null},
    {"yaw": 60, "delta": {"type": "vertexDelta", "meshBinding": 0,
      "indices": {"bufferView": 2, "componentType": "UINT32"},
      "positions": {"bufferView": 3, "componentType": "FLOAT32"}}}
  ]
}
```

実際にCoreとSchemaで読み込む合成fixture:
`Packages/jp.vlat.core/Tests/EditMode/Fixtures/Keyform/three-keys.vlat.json` と32 byteのbin。
fixtureのハッシュ文字列は構造検証用のプレースホルダーであり、実アバター用bindingではない。

- 初回は1 document = 1 group、1〜16 keys、対象Meshは1〜200,000 vertices。
- `layers`、`angleBands`、`textures` は空。既存Layerとの合成は、意味を推測せず拒否する。
- 全キーは同一 `meshBinding` を参照。同じ基準メッシュの元vertex indexを使う。
- 位置差分はbind-pose mesh-local。Head-localなのは**観測方向**であり、差分座標系ではない。
- Head frameの符号は既存AngleEvaluatorの規則を保持する。Yawの方向ベクトルは
  `(sin(yaw), 0, cos(yaw))`。Pitchはこのプロファイルに含めない。
- `delta: null` は必須フィールドとして明示した中立キー。暗黙のゼロキー挿入はしない。
- deltaは既存vertexDeltaのUINT32/FLOAT32 little-endian bufferView形式を再利用する。
  法線・接線差分は初回プロファイルで未対応。normalsがあれば拒否する。
- 支持対象はdeltaのスパースindex。未収録頂点の差分は0。
  独立mask属性は未導入。1DのSoft Selectionは編集時に差分へ乗算し、同じindexで確定する。
- bin合計16 MiB以下。Builderは全キーを検査・サイジングしてから確保する。
  readerはKeyform宣言を検証し、実ファイル長と宣言長を確認してからbinを読む。
  この上限はbinの上限であり、JSON DOM・Editor作業メモリの総量保証ではない。

## 3. Topologyの指紋

`MeshBinding.positionHash` は既存xxHash64を再利用する。
追加の `topologyHash` はSHA-256で、以下をsigned INT32 little-endianで連結する。

1. vertexCount
2. subMeshCount
3. 各subMeshの `MeshTopology` 整数、indexCount、index列

三角形のみ。`GetIndices(subMesh, applyBaseVertex: true)` で元vertex indexへ解決する。
subMesh・triangle・indexの順序を保持する。指紋生成時のindex数上限は2,000,000。
取込時に名前・頂点数・位置hash・Topology hashが一致しなければ拒否する。
外部Mesh間の転写、頂点順変更の自動補正、Pose差分の自動逆変換はしない。
これはSkinning・bindpose・Bone配列の互換性保証ではなく、将来のCompilerで別途検証する。

## 4. Yaw補間

- supportStartは `[-180,180)`。`0 < supportEnd - supportStart < 360`。
- keysはsupport両端より厳密に内側に配置し、展開済みYawの昇順・重複なし。
- ±180跨ぎは、例えばsupport `[120,240]`、keys `[150,180,210]` として保存する。
  問合せYaw `-150` は `210` と同一方向。保存時のキー順は推測して並べ替えない。
- 問合せYawは任意の有限値を受理し、supportStartを基準に360度周期で展開する。
  非有限値は例外で拒否し、出力配列をゼロにする。
- support外と両端では全差分重み0。最初のキーまでと最後のキー以降は基準形状へfade。
- 隣接キーの間では `t=(yaw-yawL)/(yawR-yawL)`、
  `s=t*t*(3-2*t)`、`wL=1-s, wR=s`、他のキーは0。
- support端はゼロ差分の仮想キーとして同じ補間を用いる。
  全重みと基準残余重みの合計は1、非負、アクティブなキーは最大2。
- `p[j] = p0[j] + Σ w[k] d[k,j]`。キー位置で当該キー差分を厳密に再現する。
  参照評価器の出力は `Σ w*d` であり、絶対頂点位置ではない。
- 現在はLayer合成を禁止するため、別の表情・Modifier適用後の最終形状の厳密性は保証しない。

テストの基準:
整数キーでweightはexact equality、既知の中点・1/4点も独立期待値で照合。
wrap sweepの合計誤差 `1e-14`、境界の±`1e-6°`でweight差 `<1e-10`。
geometryは格納FLOAT32の値をdouble演算で補間する。FLOAT32丸め前の任意double値との一致は保証しない。

## 5. 実装と保存

| 実装 | 役割 |
|---|---|
| `KeyformModels` | immutable group/key、既存MeshBinding/accessorを再利用 |
| `YawKeyformEvaluator` | 呼出し側重みbufferへの上書き、割当なし |
| `KeyformReferenceEvaluator` | 初期化時decode、外部buffer変更から独立、差分配列を再使用 |
| `KeyformDocumentBuilder` | FLOAT32入力検証、index昇順のスパースpack、完全ゼロキーはnull |
| `VlatSerializer` | 既存の厳格JSON、診断、トランザクション保存・再読込を共用 |
| `VlatBlendShapeKeyformImporter.ImportPositions` | Unity Meshの単一100%フレームを読む入口 |

Builderは入力配列を変更しない。全く同じ入力とURIならJSON/binは決定的。
bufferViewはindex列の後に位置列を格納し、全て4 byte alignment。
入力が中立だけならbin・bufferViewは生成しない。

Importerのshape index `-1` は中立。0以上は同一MeshのBlendShapeを選択し、
複数フレームや100以外のframe weightを拒否する。
元Mesh、Renderer weight、Material、Expression設定は変更しない。
`GetBlendShapeFrameVertices` の位置配列のみを読み、法線・接線・Pose変換の非対応を警告する。
API利用者は成功結果を確認して `VlatSerializer.Save` へ渡す。
1AのEditorでは同じImporterと既存Authoring pack/saveを使うImportボタンを追加した。

## 6. Backend capability

| 経路 | 今回の能力 |
|---|---|
| Core v0.2 reader/writer | 単独Keyformプロファイルを検証・保存 |
| Core参照評価器 | 上記Yaw位置差分のみ |
| Editor Document / I/O | 1Aの編集/I/Oと1BのUndo/Dirty/Recovery/実Reload・再起動を検証済み（Windows） |
| 既存3視点Preview | 1Cで単独groupのYaw位置差分を接続。clone上の追加BlendShape＋Core重み、各Camera独立。Windows自動検証GO／実アバターNOT RUN |
| Editor 2Dドラッグ | 1Dで既存選択・Soft weightと固定深度逆投影を接続。Windows自動検証GO／実アバターNOT RUN |
| 既存CPU `VlatApplier` | Keyform文書を明示的に拒否。旧v0.1再生は継続 |
| GPU Probe | Keyform接続未実装。S1/S2の診断のみ |
| GPU製品 / lilToon / NDMF | NO-GO。宣言を示すデータフィールドを追加しても対応扱いにしない |

既存asmdefの依存方向を維持する。CoreはUnity非依存、ImporterはEditor→Core/Runtime。
CPU参照評価器は検証・編集時の参照用で、毎フレームMeshを書き換える新Runtimeではない。

## 7. 今回の外側

人手dialog・異常終了耐性・実アバターPreview/2Dドラッグ品質の確認、
Layer合成、Pitch、複数グループ、法線・接線、GPU符号化、NDMF/lilToon統合。
これらは `NEXT_TASKS.md` の順で進める。未実装をGate 1完了に含めない。
