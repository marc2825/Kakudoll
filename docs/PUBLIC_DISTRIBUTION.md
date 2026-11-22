# 公開版に含まれないファイルと検証の扱い

Kakudoll の公開リポジトリは、開発用の非公開リポジトリから作った、**履歴を持たないスナップショット**です。
公開しているファイルは非公開側と同じ内容で、書き換えていません。
含めなかったファイルは、パス・SHA-256・サイズ・理由を [`docs/PUBLIC_EXPORT_LEDGER.json`](PUBLIC_EXPORT_LEDGER.json)（除外台帳）に記録しています。

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
gitleaks git --redact .                                   # 独立した秘密情報検査（設定: .gitleaks.toml）
```

`check_public_export.py` が出力するのは file・種類・件数だけで、トークンやメールアドレスの値は出しません。
Gitleaks は `--redact` で値を伏せます（`-v` を付けると commit の作者メールも表示するため、CI では付けません）。
検査が PASS しても、権利の確認が済んだことにはなりません。第三者素材の扱いは人が確認します。

## Git履歴の検査（`--git-history`）

現行treeと履歴は同じ判定（`scripts/check_public_export.py` の `file_findings()`）を使います。
判定するのは、拡張子（`.vrm`・`.fbx`・`.psd`・鍵など）、`.env*`、Python bytecode、商用アバター由来データの置き場、
除外台帳のpath、Seed-san の試験画像、確認済みでない文書での商用アバター名、内容（個人パス・認証情報・個人メール等）です。
Markdown のリンク切れは現行treeだけで確認します。

| 対象 | 扱い |
|---|---|
| 削除・リネームしたfile | どのrefからでも到達できる全commitの tree にある (path, blob) を、pathごとに検査する。後のcommitで消しても検出する |
| 同じblobが複数のpathにある | pathごとに判定する（許可されたpathにある内容を、禁止pathへコピーした場合も検出する） |
| Git LFS ポインタ | FAIL。実体は LFS storage にあり、この検査では内容を確認できない |
| submodule（gitlink） | FAIL。参照先は別リポジトリで、内容を確認できない |
| symlink | FAIL。公開版では使わない。リンク先の文字列は内容として検査する |
| tag・notes・stash・replace などの ref | `refs/` 以下の全 ref と HEAD を対象にする。annotated tag はタガーとメッセージも検査する。notes の本文は文章として検査する。`refs/replace` の置き換えは無効にして元の object を読む |
| どの ref からも到達できない object（reflog だけにある commit、dangling object） | push されないため対象外 |
| shallow clone・partial clone | FAIL。全履歴と全blobがないと検査できない |

- 検査するのは、手元のリポジトリにある ref だけです。`git clone` や `actions/checkout` は branch と tag しか取得しないため、
  公開リポジトリの notes・replace も確かめるときは `git fetch origin '+refs/notes/*:refs/notes/*' '+refs/replace/*:refs/replace/*'` を先に実行します。
  GitHub の `refs/pull/*` は PR の作成者が作る ref で、この検査の対象外です。
- force push で消した commit は、GitHub 上で SHA を指定すると読める場合があります。公開した後で履歴を書き換えても、漏えいは取り消せません。
- 除外台帳は現行treeのものを使い、pathで照合します。内容のハッシュでは照合しません（配布する `packages-lock.json` などに、除外したfileと同じ内容のものがあるため）。
- 商用アバター名の確認済みリスト（`MODEL_NAME_REVIEWED`）は path 単位です。確認済みのpathにある過去の版も確認済みとして扱います。

### 作者メール

commit の作者・コミッター、tag のタガー、commit/tag のメッセージにある個人メールは、
所有者が公開を認めたものだけを `--allow-email`（複数指定・カンマ区切り可）で許可します。
CI で実行するときは、リポジトリ変数 `VLAT_PUBLIC_AUTHOR_EMAILS`（カンマ区切り）から渡します。
許可リストは作者情報とメッセージにだけ効き、file内容の個人メールは許可しません。
許可済みのメールを、履歴の書き換えで機械的に消すことはしません。

### Gitleaks

Gitleaks（`gitleaks git --redact`）は、この検査とは別の規則で全refの差分（`git log -p --all`）を調べます。
merge commit の差分は見ないため、merge で入った内容は上の検査（全commitの tree）で確認します。
`.gitleaks.toml` は既定ルールをすべて使い、確認済みの誤検出1件（画像差分検証の variant 名）だけを、ルール・file・値をすべて限定して除外しています。
