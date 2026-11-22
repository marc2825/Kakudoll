# Phase 1 ランタイム手動確認手順

## 1. 前提

このリポジトリには、ライセンス確認済みのサンプルVRMアバターをまだ同梱して
いません。確認者が利用権を持つVRM 1.0アバターを用意し、次の場所へ配置して
ください。

```text
VLatProject/Assets/Samples/Avatars/
```

元の `.vrm`、インポートされたMesh、Materialは比較用の原本です。確認中に編集・
上書き保存しないでください。Git管理下のアセットを使う場合は、作業前の
`git status --short` を記録しておくと、非破壊性を確認しやすくなります。

必要な環境:

- Unity 2022.3.62f3
- Built-in Render Pipeline
- UniVRM 0.128.3（プロジェクトの `Packages/manifest.json` で固定）
- VRM 1.0アバターと、そのアバターに対応する `.vlat.json` / `.vlat.bin`

VRM 0.xを確認する場合は、UniVRMの `canLoadVrm0X` 対応ロード経路でVRM 1.0へ
移行された `Vrm10Instance` を使用します。VLat Runtimeは旧VRM Runtimeへ直接依存
せず、以降の適用手順をVRM 1.0と共通化します。

## 2. RuntimeDebugシーンの作成

1. Unity Hubから `VLatProject` をUnity 2022.3.62f3で開き、パッケージの解決と
   スクリプトコンパイルが完了するまで待ちます。
2. `Assets/Samples/Scenes` フォルダーを作り、新しいシーンを
   `Assets/Samples/Scenes/RuntimeDebug.unity` として保存します。
3. `Assets/Samples/Avatars` に配置したVRMをUniVRMでインポートし、生成された
   アバターPrefabをシーン原点へ配置します。Hierarchy上のインスタンスだけを
   操作し、Projectウィンドウ側の原本は編集しません。
4. アバターの頭部が見える位置へMain Cameraを置きます。評価カメラにはこの
   Main Cameraを1台だけ使用します。
5. アバターのルートへ `VlatApplier` を追加し、Inspectorで次を割り当てます。

   - `Vrm Instance`: VRM 1.0のアバターインスタンス
   - `Evaluation Camera`: Main Camera
   - `Document Path`: 対象 `.vlat.json` の絶対パス、または
     `StreamingAssets` からの相対パス
   - `Load On Start`: オン

   外部bufferやtextureを使う場合は、対応する `.vlat.bin` / 画像もJSONからの
   相対位置を保って配置してください。ビルドアプリで確認するファイル一式は
   `Assets/StreamingAssets` 配下へ置きます。

6. 空のGameObjectを `RuntimeDebugController` という名前で作成し、同名の
   コンポーネントを追加します。`Applier` に手順5の `VlatApplier`、
   `Orbit Camera` にMain Camera、`Orbit Target` にアバターのhead Transformを
   割り当てます。
7. Directional Lightを1灯置き、Gameビューで顔、前髪、ハイライト、輪郭が
   判別できる明るさにします。シーンを保存します。

`.vlat` が未作成の場合は、Hierarchyでアバター配下の前髪
`SkinnedMeshRenderer` を選択して `Kakudoll > Dev > Generate Sample Document` を
実行し、保存ダイアログで生成先を `Assets/Samples` 配下にします。メニューが
無効な場合は、`Vrm10Instance` 配下にあるMesh付きのRendererを選択しているか
確認してください。生成後に手順5の `VlatApplier` へ割り当て直します。

## 3. 角度と遷移の確認

1. Play Modeへ入り、Gameビュー左上に次の情報が表示されることを確認します。

   - 現在のyaw / pitch
   - 有効AngleBand数
   - 適用中Modifier数
   - Before / After切替
   - Debug Angle Overrideとyaw / pitchスライダー

2. Gameビュー上で右マウスボタンを押したままドラッグし、カメラがheadを中心に
   軌道移動することを確認します。マウスホイールで距離を変え、カメラがheadを
   通過したり反転したりしないことも確認します。
3. `Debug Angle Override` をオフにし、正面から左右の横顔までゆっくりカメラを
   動かします。表示yawの符号が、キャラクターの左手側から見たとき正、右手側から
   見たとき負になることを確認します。
4. `Debug Angle Override` をオンにし、pitchを0°に保ったままyawスライダーを
   -180°から180°まで往復させます。次にyawを補正対象角度へ固定し、pitchを
   -90°から90°まで往復させます。
5. 各AngleBandの境界とfeather範囲を特にゆっくり通過し、連続Modifierの変化が
   滑らかであることを確認します。visibilityは仕様どおり閾値で切り替わり、
   hysteresis付きの場合は境界付近の小さな往復で点滅しないことを確認します。

## 4. 表情との併用確認

1. `Debug Angle Override` をオンにし、vertexDeltaが十分に効くyaw / pitchへ
   スライダーを合わせます。
2. UniVRMのExpression操作手段（アバター付属のAnimator、確認用AnimationClip、
   またはUniVRMのExpressionデバッグUI）から、`happy`、`angry`、`blink`、
   `aa` など利用可能な表情を1つずつ0から1へ変化させます。
3. 表情を1に保ったままyaw / pitchをAngleBand内外へ往復させます。
4. 表情とVLatの両方のブレンドシェイプが同時に残り、頂点の瞬間移動、表情weight
   のリセット、意図しないRenderer非表示が起きないことを確認します。

## 5. Before / Afterと非破壊性の確認

1. 補正が見やすい角度と表情を固定します。
2. オーバレイの `Before` を選び、VLat由来のvertexDelta、visibility、opacity、
   materialProperty、renderQueueが基準状態へ即時に戻ることを確認します。
3. `After` を選び、同じ角度で補正が即時に復元されることを確認します。この操作を
   10回繰り返し、状態の蓄積、点滅、Material数の継続的な増加がないことを確認します。
4. Play Modeを終了します。Projectウィンドウの元VRM、元Mesh、元Materialに
   未保存変更が付いていないことを確認します。Git管理下の場合は再度
   `git status --short` を実行し、手順開始前と比較します。
5. シーンを開き直して元アバターのRendererを確認し、元Meshと元Materialの参照が
   保持されていることを確認します。VLatが利用する複製はPlay Mode中の
   アバターインスタンスにだけ存在するのが正常です。

## 6. 合格基準

| 確認対象 | 合格条件 |
|---|---|
| カメラ操作 | 右ドラッグでhead中心に安定して軌道移動し、ホイールで安全に拡大縮小できる |
| 角度表示 | 正面がおおむね0°、左手側が正yaw、右手側が負yawとなり、pitchもカメラ位置に追従する |
| AngleBand遷移 | feather内の連続補正に目視できる段差がなく、visibilityが境界付近でチャタリングしない |
| 表情併用 | VRM ExpressionとVLat補正が同時に有効で、片方がもう片方を消去しない |
| Before / After | 1操作で即時に基準状態と補正状態を往復でき、10往復後も結果が同じ |
| バインド失敗時 | Consoleに対象を特定できる診断が出て、該当Layerだけが安全に無効化される |
| 非破壊性 | Play Mode終了後、元VRM・元Mesh・元Materialの内容と参照に変更がない |

いずれかを満たさない場合は、使用したUnity/UniVRMバージョン、アバター名、
`.vlat.json`、再現角度、Consoleログ、Gameビューの画像を添えて記録してください。
