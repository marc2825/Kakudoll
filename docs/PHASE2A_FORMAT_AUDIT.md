# Phase 2A — Format / Authoring Persistence Audit

実施日: 2026-08-30。対象formatは`0.1.0`のままであり、新versionや新fieldは追加しない。
この文書は既存の`VlatDocument`、parser/serializer/validator、Runtime binderと、
Phase 2AのEditor保存方針の対応を記録する。

## 1. 既存formatと実装の保証

- `VlatDocument`とその子modelはconstructorでcollectionをcopyし、公開側は
  `IReadOnlyList`として保持する。Runtime/Core表現はimmutableである。
- JSON parserは未知／重複property、型不正、非標準数値を拒否する。serializerは
  invariant cultureと固定property順を使うため、同じimmutable documentからのJSONは
  決定的である。
- `buffers[].uri`はJSON基準の相対外部URIである。`bufferView`は`buffer` index、
  non-negative `byteOffset`、1以上の`byteLength`で範囲を指定する。複数bufferと、
  1 buffer内の複数viewを読み込める。
- `vertexDelta.indices`はlittle-endian `UINT32`の連続列（4 byte/要素）、
  `positions`/`normals`はlittle-endian `FLOAT32` vec3の連続列（12 byte/要素）で、
  strideやview内offsetは持たない。要素数はviewの`byteLength`から導出し、indices、
  positions、存在するnormalsで一致しなければならない。
- vertex indexは対象bindingの`vertexCount`未満かつ列内で一意、position/normal成分は
  有限値でなければならない。Phase 2A監査前はCoreが後二点を検査せずRuntime binderだけが
  拒否していたため、format semanticsを変えずCore validatorをRuntime契約へ揃えた。
- embedded textureのbufferView payloadはMIMEごとのopaque byte列として扱い、再encodeしない。
  外部textureは相対URIを保持する。
- 現formatはbufferViewのalignment、vertex index列の順序、未参照view/byteの保持、
  複数bufferの保存時分割方法を規定しない。readerは範囲が正しければ非4-byte-aligned viewも
  引き続き受理する。

## 2. Phase 2A canonical output policy

on-disk配列順のうちbindings、textures、bands、layers、modifiersは参照index、表示順、
競合時のstable orderを不用意に変えないためAuthoring Modelの順を維持する。
各`vertexDelta`内だけは、semanticに順序非依存であるためvertex index昇順へcopy-sortする。
編集中のlist順自体は変更しない。

Save/Exportでは次の順に単一canonical bufferを作る。

1. embedded texture payloadをtextures配列順に配置する。
2. layers配列順、各modifiers配列順にvertexDeltaのindices、positions、任意normalsを配置する。
3. 各bufferViewの開始offsetを4 byte境界に切り上げ、paddingは0で埋める。
4. buffer末尾も4 byte境界まで0 paddingする。
5. binary payloadがなければ`buffers`/`bufferViews`と`.vlat.bin`出力を持たない。

複数bufferを同じ分割で保存する案は入力byteを保持しやすい一方、編集後のview所有権、
空き領域、共有view、追加payloadの配置が入力履歴に依存する。単一bufferへcanonicalizeする案は
入力の未参照byteとpaddingを保持しないが、typed authoring dataだけから出力が一意になる。
Phase 2Aでは後者を採用した。

したがって、入力が既に同じcanonical policyでない限りLoad→Saveのbinary byte equalityは
保証しない。semantic equalityはmetadata/binding、texture payloadまたはURI、band/layer/modifier
値、vertex indexとposition/normal deltaの集合、およびSave後のCore validation成功で比較する。
canonical save後の再saveはJSON/binaryともbyte-identicalである。

## 3. Authoring single source of truth

編集session中のsingle source of truthはEditor assemblyの`VlatAuthoringDocument`である。
metadata、bindings、textures、bands、layers、polymorphic modifiersをUnity-serializableなmutable
fieldとして持ち、vertexDeltaは`meshBinding`、original vertex index、`Vector3` position/normal
deltaとして保持する。accessor、bufferView、binary offsetはSave planを作るまでAuthoring Modelに
存在しない。`.vlat.bin`をdrag、property edit、Undoのたびに変更しない。

保存経路は`VlatAuthoringPersistence`に集約し、次の段階を分離する。

1. authoring structure validation
2. vertexDelta canonical ordering plan
3. payload sizing
4. 4-byte alignmentとbufferView assignment
5. binary packing
6. immutable `VlatDocument`生成
7. Core validation
8. deterministic JSON serializationと既存atomic file save

編集途中のinvalid stateはmodelに保持でき、Editing validationではwarningとして提示する。
Save validationでは同じ状態をerrorとして拒否し、UI側の入力制約には依存しない。

## 4. Undo、ownership、lifecycle

Undo targetは`HideFlags.HideAndDontSave`のtemporary `ScriptableObject`である。
`SerializeReference`でmodifier派生型を含むtyped stateをsnapshotし、各completed commandが
`Undo.RecordObject`を1回呼ぶ。Undo snapshotにon-disk binary全体は含まれない。

Authoring Modelのownerはdocumentを開いたEditor session（Phase 2BではEditor window/session）で、
close、document replacement、error pathで`DestroyImmediate`する。Project assetとして生成せず、
元VRM/Mesh/Materialやscene objectへの参照を保存・変更しない。domain reloadではtemporary stateを
永続化対象とせず破棄し、最後に保存済みのsource pathから明示的に再loadする。未保存変更の
domain reload越し保持はPhase 2Aの保証外であり、Phase 2Bのreload/close UXで扱う。

## 5. Phase boundary

Phase 2Aはauthoring/persistence/Undo foundationだけを提供する。EditorWindow、UI Toolkit、
Preview Scene、multi-view、vertex picking、gizmo、2D warp、rebinding UIは実装しない。
