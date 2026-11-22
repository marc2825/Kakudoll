# 公開リポジトリに含めていないもの

このリポジトリは、非公開の開発用リポジトリから作ったスナップショットから始まっています。公開したファイルは非公開側と同じ内容で、書き換えていません。
含めなかったファイルは、パス・SHA-256・サイズ・理由を [除外台帳](PUBLIC_EXPORT_LEDGER.json)（`docs/PUBLIC_EXPORT_LEDGER.json`）に記録しています。

## 含めなかったもの

| 台帳の `reason` | 内容 |
|---|---|
| `third-party-model-data` | 商用アバター（しなの / 狛乃 / ゆめか）で測った実験の生データと、モデル別の構造を含む報告。権利者の許諾を確かめられないため。集計は [REAL_AVATAR_VALIDATION_SUMMARY.md](experiments/REAL_AVATAR_VALIDATION_SUMMARY.md) |
| `owner-excluded-third-party` | Seed-san を撮影した試験画像と、その試験の出力 |
| `development-evidence` | 開発中のテストの記録（`docs/product/evidence/` の test XML、ログ、計測結果、画像） |
| `personal-path-raw-evidence` | 実験結果のうち、開発 PC のパスを含むファイル。`SHA256SUMS` と合わなくなるので書き換えず、ファイルごと外した |
| `internal-working-doc` | 作業記録、途中の報告、コーディングエージェント向けの指示、内部の監査 |
| `release-tooling` | 公開版を作るための管理ツール |

第三者の素材については [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) を見てください。

## 実験結果のチェックサム

`experiments/*/results/` の `SHA256SUMS` には、公開していないファイルの行も残っています。
`scripts/run_license_free_checks.py` は台帳を読み、次のように判定します。

| 状況 | 判定 |
|---|---|
| ファイルがあり、ハッシュが一致する | PASS |
| ファイルがなく、台帳に同じハッシュで載っている | NOT RUN（公開していないため確かめられない） |
| ファイルがなく台帳にもない、またはハッシュが違う | FAIL |

台帳のハッシュは非公開側の原本と照合するためのもので、公開版だけでは内容を確かめられません。
元の実験で FAIL だった結果（実アバターの画像比較の 20 件など）は、要約と各報告にそのまま残しています。

## 公開してよいかの確認

```bash
python3 scripts/check_public_export.py .                    # 個人のパス、認証情報、第三者のデータ、リンク切れ
python3 scripts/check_public_export.py . --git-history \
  --allow-email <公開してよい作者メール>                    # 全履歴も
gitleaks git --redact .                                     # 別の方法で秘密情報を探す（設定は .gitleaks.toml）
```

どちらも、見つかったファイルと種類と件数だけを出し、トークンやメールアドレスの値は表示しません。
この確認が通っても、権利の確認が済んだことにはなりません。第三者の素材かどうかは人が確かめます（[公開前チェックリスト](PUBLIC_RELEASE_CHECKLIST.md)）。

### 履歴の確認で見ているもの

`--git-history` は、現在のファイルと同じ基準で、過去のすべてのコミットを調べます。

- 見るもの: 公開しない種類のファイル（`.vrm`、`.fbx`、`.psd`、鍵など）、`.env*`、Python のキャッシュ、商用アバターのデータの置き場所、
  除外台帳のパス、Seed-san の試験画像、確認していない文書に出てくる商用アバターの名前、ファイルの中身（個人のパス、認証情報、個人のメール、VRChat の ID など）。
- あとで消したりリネームしたりしたファイルも、どこかのコミットにあれば見つけます。同じ内容を別のパスへコピーした場合も、パスごとに判定します。
- Git LFS のポインタ、submodule、symlink は中身を確かめられないので FAIL にします。shallow clone や partial clone も、全部を調べられないので FAIL です。
- branch だけでなく tag、notes、stash、replace の ref も調べます。どの ref からもたどれないオブジェクトは push されないので対象外です。
- コミットの作者・コミッターのメールは、`--allow-email`（カンマ区切り可）で許可したものだけを通します。CI ではリポジトリ変数 `VLAT_PUBLIC_AUTHOR_EMAILS` から渡します。
  許可はコミットの情報にだけ効き、ファイルの中のメールアドレスは許可しません。

注意:

- `git clone` や CI の checkout は branch と tag しか取ってこないので、notes や replace も確かめるときは先に
  `git fetch origin '+refs/notes/*:refs/notes/*' '+refs/replace/*:refs/replace/*'` を実行します。
- 公開したあとで履歴を書き換えても、すでに公開した内容は取り消せません（GitHub では消したコミットを SHA で読めることがあります）。
- Gitleaks は merge コミットの差分を見ないので、merge で入った内容は `--git-history` の方で確かめます。
  `.gitleaks.toml` では、確認済みの誤検知 1 件（画像比較の実験の名前）だけを除外しています。
