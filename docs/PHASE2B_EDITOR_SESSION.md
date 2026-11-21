# Phase 2B Editor Session Architecture

## 1. Scope

Phase 2BはVLatを文書としてNew/Open/Edit/Undo/Redo/Save/Save As/Closeできる
UI Toolkit shellである。編集対象はmetadata、Angle Band、Layer、既存5種のModifierの
schema propertyとし、mesh bindingは参照表示に留める。

Preview Scene、VRM表示、vertex picking、gizmo、2D warp、rebind UIはこのPhaseに含めない。
vertexDeltaのtyped commandはPhase 2Aから維持するが、大量のdeltaを直接編集するUIも設けない。

## 2. Ownership and dependency flow

```text
VlatEditorWindow (UXML / USS, input and rendering adapter)
    ↓
VlatEditorDocumentController (session owner)
    ├─ VlatEditorSelection
    ├─ VlatRecoveryStore
    └─ temporary VlatAuthoringDocument (HideAndDontSave)
           ↓
       VlatAuthoringCommands
           ↓
       VlatAuthoringPersistence
           ├─ editing/save validation
           ├─ canonical binary pack
           └─ atomic VlatSerializer.Save
                  ↓
              immutable VLat.Core model
```

- Windowはfile dialog、dirty確認、UI eventのcommit boundary、表示更新だけを担当する。
- Controllerはdocument lifetime、source path、saved revision、selection、diagnostics、
  recovery lifecycleを所有する。
- collection変更とproperty変更はすべて`VlatAuthoringCommands`を通し、Windowから
  mutable collectionを直接変更しない。
- SaveはPhase 2Aの`VlatAuthoringPersistence.Save`だけを使い、別のserialize/write経路を
  作らない。

## 3. Document lifecycle

| Operation | Behaviour |
| --- | --- |
| New | dirtyをSave/Discard/Cancelで解決後、新しいtemporary Authoring Modelへ置換する |
| Open | candidateをPhase 2A経路で先にloadし、成功かつdirty解決成功時だけcurrent documentを置換する |
| Save | current sourceへsave validation、canonical pack、atomic pair commitを行う |
| Save As | 新pathへのcommit成功後だけcurrent source pathとsave pointを切り替える |
| Close Save | source未指定ならSave Asを要求し、成功時だけsessionを閉じる |
| Close Discard | recoveryを削除してtemporary documentを破棄する |
| Close Cancel | current documentとrecoveryを維持する |
| Domain reload | recoveryを確定後にtemporary documentを解放し、reload後にsnapshotから復元する |

Unity 2022.3の`hasUnsavedChanges`、`saveChangesMessage`、`SaveChanges()`、
`DiscardChanges()`をwindow closeへ使用する。New/Openは同じController policyを
`DisplayDialogComplex`から呼ぶ。

## 4. Dirty and Undo semantics

`VlatAuthoringDocument`はcompleted commandごとに単調な`revision`をserializeする。
Controllerは最後に成功したSaveの`savedRevision`をUndo対象外のsession stateとして持つ。

```text
IsDirty := document.Revision != savedRevision
```

このため次を区別できる。

- load/new直後、successful save直後: clean
- property edit、add/remove/reorder、vertexDelta command後: dirty
- Undoで保存時revisionへ戻る: clean
- 保存点より前へUndoする: dirty
- Redo: 復元されたrevisionに従って再判定
- validation/write失敗: saved revisionを更新せずdirtyのまま

各commandは独立したUnity Undo groupを開始する。Text/number fieldはdelayed commitを使い、
1文字ごとのUndo recordを作らない。Undo/Redo callback後にhierarchy、inspector、selection、
editing validation、dirty status、recoveryをまとめてrefreshする。

## 5. Selection

Band、Layer、ModifierはEditor-only GUID identityをtemporary Authoring Model内に持つ。
このidentityとdocument revisionはAuthoringからimmutable Coreへ変換しないため、
`.vlat.json`のformat fieldにはならない。

Selectionはkind、identity、Modifierの場合はparent Layer identityを保持する。property edit、
reorder、Undo/Redo、無関係なadd/removeでは同じidentityを再解決する。選択対象を削除した場合は
隣接項目、Modifierなら親Layer、対象がなければDocumentへfallbackする。validation pathの
`angleBands[i]`、`layers[i]`、`modifiers[i]`も現在のidentityへ変換して選択できる。

## 6. Recovery safety

既定保存先はprojectの`Library/VLat`で、Project assetでもGit管理対象でもない。
`SessionState`へdocument全体を格納しない。

1. uniqueな世代snapshotへ`EditorJsonUtility`でAuthoring Modelを書き、flushする。
2. source path、saved revision、selection、snapshot filenameをmanifest temporary fileへ書き、
   flushする。
3. manifestをreplace/moveでcommitする。
4. commit後に古い世代だけを削除する。

snapshotはtyped `SerializeReference` ModifierとEditor identityを含むため、保存済みdocumentの
未保存差分と完全な新規未保存documentの両方を復元できる。破損manifest/snapshotは例外を外へ
出さず削除する。recoveryはsource JSON/binを読取り・上書きせず、normal Discard/close時に消す。

## 7. Validation and file safety

commitごとにPhase 2A Editing validationを表示し、編集中のerror相当はwarningとして保持できる。
Save時はPhase 2A persistenceがSave validationを再実行し、errorがあれば書込み前に拒否する。
Save failure diagnosticsはseverity/path/messageを保持し、関連Band/Layer/Modifierを選択可能にする。

Phase 2Aのfile transactionはJSON/binをすべてtemporary fileへwrite/flushしてからpairをcommitし、
途中失敗時はrollbackする。このtransactionをPhase 2Bでもそのまま使用する。

## 8. Acceptance coverage

EditMode testsはdocument lifecycle、save pointを跨ぐUndo/Redo、close policy、保存済み/新規の
recovery、corruption/source非変更、全CRUD/property/reorder、stable selection、diagnostic
selection、save/reload semantic integration、UXML assetと実`VlatEditorWindow.CreateGUI`構築を
対象にする。

対話的なEditor画面で行うmanual smokeの実施/未実施結果は`PROGRESS.md`のPhase 2B記録に残す。
