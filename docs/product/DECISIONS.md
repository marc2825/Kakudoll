# 実装判断と未決定事項

2026-10-20。今回のユーザー指示でPRD/ロードマップを基本方針とし、
Keyform v0.2の定義・段階実装を開始することが明示された。
旧文書の「GPU本格採用を進めない」は維持し、Authoring/Coreの限定スライスを進める。
旧 `docs/spec` のv0.1意味論、asmdef方向、Unity baselineは変更しない。

| ID | 判断 | 理由・実装範囲 |
|---|---|---|
| K-001 | v0.1を残し、v0.2の単独Keyformプロファイルを併設 | 既存reader/I/O/Validatorを再利用。旧ファイルの自動書換なし |
| K-002 | 最初は1 group、Yaw、位置のみ | 小さな完結した試験単位にする。複数Layerとの合成を推測しない |
| K-003 | 展開済みYaw支持区間＋smoothstep凸補間 | キー厳密再現、wrap、非負重み、中立fadeを単純に定義できる |
| K-004 | null deltaを中立キーとして明記 | 旧bufferViewの正のbyteLength制約を破らず、ゼロ形状を保存できる |
| K-005 | 同一Mesh内のsingle-frame100%を位置取込 | Shape Keyとの一致が説明可能。外部転写・Pose・frame補間の推測を避ける |
| K-006 | 名前/count/positionHashにTopology hashを併用 | 同頂点数でも頂点順・triangle変更を見逃さない |
| K-007 | 未対応backendは明示的に拒否 | 新しい文書を旧Editorへ渡して保存するとキーを失うため |
| K-008 | UIやGPU採用判定をCore試験と分離 | 自動テスト完了だけで制作E2E/実機対応を宣言しない |

以下は未決定または未実装で、今回の形式に暗黙で組み込まない。

1. Keyformと旧Layer/Expressionの合成順序、複数group同一頂点の競合。
2. Authoring ScriptableObjectでのkey identity、revision、Undo、recoveryの拡張。
3. Preview cloneのneutral meshとskin-spaceからbind-mesh-localへの変換。
   単純な逆Transformだけでは任意の混合Skinning差分を保存できない。
4. 直接編集でどの既存keyを編集するか、既存差分に対するoverwrite/addの操作仕様。
5. 支持区間・補間方式を増やす際のmigrationと法線/接線の方針。
6. GPU data packing、Head Frame、Stereo fallback、Bounds、全Shader pass。
7. NDMF/Avatar Optimizerの版・ビルド順序・生成Asset所有権。
8. v0.2 targetAvatarのVRM以外のmetadata。
   今回は既存TargetAvatar構造を保持するため、汎用VRChat Avatar入力全般を対応済みとしない。

実装が既存仕様と衝突する段階では `DECISIONS_NEEDED.md` に論点を記録し、
該当する実装を止める。今回の単独groupは既存Layerと併用できない制約によって
未決定の合成意味論に踏み込まない。

## 2026-10-21 — 1A Editor接続の決定

- K-009: 既存Authoring Documentへgroup/keyを直列化して保持する。位置は既存typed delta、
  Saveは既存canonical plan、Undoは既存Record/Finish、Recoveryは既存snapshotを再利用する。
- K-010: group/keyのEditor-only identityを導入し、Yaw変更時の明示的なソートで選択を維持する。
  v0.2 on-diskのキーにID等は追加しない。
- K-011: 空Documentから中立0°キーを持つ未binding draftを作れる。対象設定までSave不可。
  入力不正は変更前に拒否。最後のキーはgroupごと削除し、空Documentの版を0.1.0へ戻す。
- K-012: 位置差分のあるgroupは異なるMeshへrebindしない。path/name/count/position/topologyを
  取込時に厳密照合する。任意Pose変換・自動転写は未対応。
- K-013: 基本Undo/Recoveryは小さな既存経路拡張で1Aに含める。操作網羅は1B、
  Keyform Previewと直接編集は後続段階。Editor接続のみでGate 1全体の完了とはしない。

上の未決定事項2のidentity/revision/基本Undo/Recoveryは1Aで具体化した。
合成順序・Skinning変換・GPU等の未決定事項は引き続き未決定。

## 2026-10-21 — 1B保存点・Recovery境界

- K-014: Unity Undoだけを履歴の所有者とし、command完了時にdiffをflushする。
- K-015: 保存先と保存基準はcontrollerが所有。Undo後はDocumentのsourcePathを同期する。
  canonical内容の指紋でDirtyを比較し、同値編集/再取込・選択は履歴を作らない。
- K-016: Recovery manifestのみv2へ拡張。旧v1は読めるが、source指紋がなければ
  保存元への上書きを保留し、Open/別名保存を促す。vlat v0.2形式は変更しない。
- K-017: 保存元JSON/binの変更を復元/Save境界で検出。未知/破損snapshotは隔離して診断する。
- K-018: WindowのOnDisableとbeforeAssemblyReloadでcheckpoint/解放し、復元する。
  Unity Undo stackはDocument破棄境界で終了し、独自履歴の永続化は行わない。
- K-019: 実Domain Reload、Window再生成、別プロセス再起動、controller checkpoint再生成を別集計する。
  実行証拠がない人手dialog/異常終了はNOT RUNを維持する。

## 2026-11-19 — 監査・大規模応答性・CI・UI

- K-020: Editorの処理区間は `VlatEditorProfiling`（Unity `ProfilerMarker` "VLat.Editor.*"）で計測する。
  試験専用observerは既定null、値型scopeで割当なし。性能値の最終runではobserverを無効にする。
- K-021: Recovery snapshotはbytes列・hash・manifest v2・WriteThrough/Flush(true)・atomic publishを維持し、
  encode回数と同期I/O回数だけを減らす。Recovery表現の変更はD-011の判断後に行う。
- K-022: 編集中のin-memory pack結果は「同じDocument instanceかつ同じRevision」に限って再利用する。
  RevisionはFinishとUndo/Redoの復元で必ず変わるため、内容と1対1。commit直前の照合と編集検証は常に新規pack、保存は対象外。
  in-memory snapshotのbuffer URIは編集検証と同じ既定値に統一した（fileは作らない）。
- K-023: Openの2回目のRecovery書込は同一状態の重複なので削除（diagnostics/LastMessageはRecovery対象外）。
- K-024: CIはライセンス不要の検証を常時実行し、Unityジョブはpreflightで実行可否を判定する。
  Secretなしはskipped＋NOT RUN annotationで、PASSとは扱わない。`VLAT_REQUIRE_UNITY_TESTS=true` で失敗に切替可能。
- K-025: Editor UIは要素name・型・既存の操作経路を契約として維持し、レイアウト・文言・配色だけを整理する。
  v0.1 Layer編集は削除せず折りたたみへ移す。色はUnity Editorテーマ変数を使う。
- K-026: 凍結済み実験のprepare scriptはSHA256SUMSの対象なので変更しない。SDK 3.10.6の互換試験は別の新規入口で行う。

## 2026-11-20 — D-010〜D-012の決定（ユーザー承認: 全て推奨案）

- K-027（D-010）: 旧spec 01/00は本文を変更せず、冒頭注記で「VRChat関連の範囲・優先順位はPRDとGate判定が優先、
  v0.1意味論・asmdef方向・baselineは旧specが正」と明記する。
- K-028（D-011）: Authoring keyformの位置差分は `ISerializationCallbackReceiver` で
  `VlatAuthoringDeltaPacking`（format 1、little-endian、index int32＋position float32×3、法線は非ゼロ時のみ）の
  base64へ直列化する。値はbinary32のbit列をそのまま保持する。旧 `_deltas` は読込専用で、直列化時は常に空。
  Recovery manifestはv3。v1/v2 snapshot（要素ごとの旧表現）は読込可能で、次の書込からv3になる。
  旧実装がv3を読むと未知versionとして隔離される（黙った差分消失なし）。pack済み差分の破損・旧新表現の混在は
  `SerializationError` として保存検証Error・Recovery隔離にする。v0.1 vertexDelta Modifierの表現は今回変更しない。
- K-029（D-012）: Coreの診断（`$.vlat.version` のnewer-minor警告）は変更しない。Editor Windowは表示用項目へ変換し、
  Keyform v0.2文書では情報1件「Keyform v0.2文書です…」として表示、同一内容は1件に統合する。
  項目は元の診断indexを保持し、クリック時の選択対象はずれない。警告数バッジは表示と同じ集計にする。

## 2026-11-20 — 大規模応答性の追加改善（計測に基づく）

- K-030: 編集検証のvertexDelta要素ごとの診断pathは、診断を出す時だけ組み立てる。範囲内indexの重複検出は、
  頂点数が既知（4 Mi以下）なら配列、未知ならHashSet。診断のpath・message・順序は不変。
  canonical planは差分が既に昇順（重複なし）なら並べ替えを省く（重複はstructure検証で先に拒否される）。
- K-031: `VlatKeyformBindingUtility.Capture` のImporter probeは、同じ呼出で同じMeshから計算済みの位置/Topology指紋の
  再計算だけを省く（`ImportPositions` のinternal overload、publicの挙動は不変）。有限値・名前・頂点数・document検証は維持。
  Topology指紋は入力byte列を変えずに、index配列をまとめてSHA-256へ流す。
- K-032: `scripts/run_editor_1e.py` はstageの `Packages/` `schema/` をsourceと完全一致させ（残存fileを削除し `.stage.json` に記録）、
  `--source` で同日のmain基準を同じ手順で測れるようにした。2026-11-20の残存file混在によるcompile errorの再発防止。
- K-033: gesture開始のbind→world写像の逆行列は `[A 0; 0 1]` 専用の3x3逆行列（double余因子展開）で計算する。
  非有限値・条件数1e6・積の単位行列差1e-3の判定、閾値、messageは汎用4x4版と同じ。汎用版はCamera/Root/Head等に引き続き使う。
  pass 1〜3の列は影響頂点だけ設定し、全頂点の有限性検査は維持。`Camera.nearClipPlane` はloop外で1回読む。
- K-034: keyformのcanonical planはAuthoring deltaを複製せず参照する。planは1回のPack呼出内（同期処理）だけで使い、
  pack結果はimmutable documentとbyte配列で、Authoringへの参照を残さない（試験で独立性を確認）。v0.1 Modifierの経路は不変。

## 2026-11-21 — 試験証拠の保存方針（Issue #3）

- K-035: 2026-11-19以降の証拠ディレクトリは要約・最終test XML・報告に使った結果・FAIL/NOT RUN記録・`inputs.json`・`SHA256SUMS`
  だけを作業treeに置く。反復的な生出力は `ARCHIVED.sha256` に記録してgit履歴（commit指定）から取得可能にし、外部公開は行わない。
  2026-10のGate 1A〜1E証拠は凍結。上限60 files / 2 MiBと合格XML 2件以下をCIのlicense-free jobで検査する。

## 2026-11-22 — 公開名 Kakudoll（所有者の決定）

- K-036: 公開するツールの名前を **Kakudoll** にする（公開リポジトリ `marc2825/Kakudoll`）。README、メニュー（Kakudoll > Editor）、
  ウィンドウ名、パッケージの表示名、画面のメッセージ・Undo名、生成するオブジェクト名、LICENSEの著作権者名を変更した。
- ファイル形式の名前は **VLat** のまま（`.vlat.json` / `.vlat.bin`、`vlat.version`、Schema、Core の診断メッセージ）。
  変えると v0.1 / v0.2 の既存ファイルとの互換性と仕様が変わるため。
- パッケージID `jp.vlat.core`、名前空間 `VLat.*`、asmdef名、クラス名 `Vlat*`、フォルダ `VLatProject`、Recovery の保存先
  `Library/VLat`、Profiler marker、CI変数も維持する。asmdef名は 03_ARCHITECTURE で定めており、変更は利用者のプロジェクト参照・
  保存済みのRecoveryにも影響するため、必要になったら別の判断として扱う。凍結済みの実験ファイルは変更しない。
