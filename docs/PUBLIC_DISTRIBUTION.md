# 公開版に含まれないファイルと検証の扱い

VLat の公開リポジトリは、開発用の非公開アーカイブから作った**履歴を持たないスナップショット**です。
公開版に含まれるファイルは非公開アーカイブの対応するファイルと**同一の内容**で、書き換えていません。
含めなかったファイルは、パス・SHA-256・byte数・理由を
[`docs/PUBLIC_EXPORT_LEDGER.json`](PUBLIC_EXPORT_LEDGER.json)（除外台帳）に記録しています。
（この台帳は公開版を作るときに生成されるため、非公開アーカイブには存在しません。）

## 含めなかったものと理由

| 理由（台帳の `reason`） | 対象 |
|---|---|
| `third-party-model-data` | 商用アバター（しなの / 狛乃 / ゆめか）で測った実験の生データと、モデル別の構造情報を含む報告。権利者の個別許諾を確認できないため。集計結果は [`experiments/REAL_AVATAR_VALIDATION_SUMMARY.md`](experiments/REAL_AVATAR_VALIDATION_SUMMARY.md) |
| `owner-excluded-third-party` | Seed-san を撮影した試験画像とそのrunの出力。所有者の判断で含めない |
| `development-evidence` | 開発時の試験証拠（`docs/product/evidence/`: Unity の test XML・ログ・計測結果・試験画像）。結果は README に要約した |
| `personal-path-raw-evidence` | 実験結果（`experiments/*/results/`）のうち、開発PCの絶対パスを含むファイル。整合性（`SHA256SUMS`）を保つため書き換えず、ファイルごと外した |
| `internal-working-doc` | 開発作業の記録、開発途中の報告・作業台帳、コーディングエージェント用の指示、内部監査 |
| `release-tooling` | 公開版を作るための管理ツール |

第三者素材の扱いは [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md) を参照してください。

## 実験結果とチェックサム

VRChat 向けの検証実験（`experiments/*/results/`）は、合成データの結果を残し、上の理由に当たるファイルだけを外しています。
そのため `SHA256SUMS` には、公開版に存在しないファイルの行が含まれます。

`python3 scripts/run_license_free_checks.py` は除外台帳を読み、次のように扱います。

| 状況 | 結果 |
|---|---|
| 公開版にあるファイルのハッシュが一致 | PASS |
| ファイルがなく、台帳に同じハッシュで記録されている | **NOT RUN（非配布）**。PASS には数えない |
| ファイルがなく台帳にもない、またはハッシュが違う | FAIL |
| checker の入力ディレクトリ全体が非配布 | その checker は NOT RUN |

台帳のハッシュは非公開アーカイブの原本と照合するためのもので、公開版だけで非配布ファイルの内容を確かめることはできません。
元の実験で FAIL だった記録（例: 実アバターの raw surface 20 FAIL）は、要約と各実験報告に残しています。

## 公開版での確認

```bash
python3 scripts/check_public_export.py .                  # 個人パス・認証情報・第三者データ・リンク切れ
python3 scripts/check_public_export.py . --git-history --allow-email <公開している作者メール>   # 全履歴も確認
python3 scripts/run_license_free_checks.py                # 公開版では最初の検査も自動で実行する
```
