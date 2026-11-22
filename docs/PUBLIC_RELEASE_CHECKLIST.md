# 公開前チェックリスト（権利・個人情報）

- 実施日: 2026-11-22
- 対象: `main` のコミット `4679729db3f359f902aeaab37c919731b5ab1fc7`（追跡ファイル 690 件、履歴 3 コミット）
- 関連: Issue #5
- 追記（同日）: この監査のあと `main` に入ったのは、コード・テスト・文書・CI設定・`.gitleaks.toml`・Issue/PRテンプレートだけで、
  画像・Unityアセット・実験結果は追加していない。追加後も自動スキャン（現行ツリー・全履歴・Gitleaks）は PASS
- 範囲: 追跡ファイル全体、Git の作者情報と全 ref、GitHub 上の Issue #1〜#8 の本文、最新 CI の成果物

自動スキャナー（[`scripts/check_public_export.py`](../scripts/check_public_export.py)）の PASS は、画像・アセットの権利までは保証しません。
この文書は、スキャナーの外側を人が確認した記録です。秘密情報やメールアドレスの実値は書いていません。
商用アバターのモデル名は、スキャナーの確認済み一覧に入っていないこのファイルには書かず、「商用アバター3体」と書きます。

判定の意味:

- **PASS**: 公開してよい
- **要所有者確認**: 権利・プライバシー上の問題は見つからなかったが、公開するかどうかは所有者が決める
- **除外推奨**: 公開版から外すべき

## 結果の一覧

| 区分 | 対象 | 出所 | 判定 |
|---|---|---|---|
| 実験画像 | PNG 177 件（`experiments/*/results/`） | 実験の Editor コードが合成メッシュを描画した出力（1章） | PASS |
| PNG メタデータ | 同上 | — | PASS（補助チャンクなし） |
| Unity 生成アセット | `.asset` 24、`.mat` 12、`.unity` 2、`.prefab` 2、`.anim` 4、`.controller` 1、`.meta` 187 | 実験の Editor コードが生成（2章） | PASS |
| シェーダー | `.shader` 11、`.cginc` 4 | 自作（3章） | PASS |
| テスト用データ | `Packages/jp.vlat.core/Tests/EditMode/Fixtures/`（`.vlat.json` 11、`.vlat.bin` 1） | 手書きの合成データ | PASS |
| 実験のログ・CSV・JSON | `experiments/*/results/` | 実験の出力 | 要所有者確認（開発環境の情報。4章） |
| 文書とリンク | README、`docs/`、実験の README | 自作 | 要所有者確認（モデル別の FAIL 内訳。5章） |
| 依存ライセンス | [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md) | — | PASS（6章の修正を反映済み） |
| ライセンス表示と貢献の扱い | `LICENSE`、`package.json`、`CONTRIBUTING.md` | — | PASS（7章。2026-11-22 に PolyForm Strict 1.0.0 へ変更） |
| Git の作者情報と ref | 3 コミット、リモートは `main` のみ | — | PASS（8章） |
| GitHub 上の Issue・CI | Issue #1〜#8、CI 3 回分のログと成果物 | — | PASS（8章） |

**除外推奨に当たるファイルはありませんでした。** 第三者の素材そのもの（アバター、テクスチャ、他者の画像、他者のシェーダーやコード）は見つかっていません。
第三者に関係して残っているのは、名称・配布ページへのリンク、VRChat SDK のスクリプトへの GUID 参照、集計した測定値だけです。

## 1. 実験画像（PNG 177 件）

### 確認したこと

- 177 件すべてを目視した。64×64 の 168 件は一覧画像にまとめて、ほかの 9 件は 1 枚ずつ見た。
- 中身が異なるのは 62 種類。`VRCViewContext` と `VRCClientValidation/.../regression-s2` の `images/` は、同じ実験の再実行なので大半が同一バイト。
- アバター、テクスチャ、写真、OS やアプリの画面（ユーザー名などが写り得るもの）は写っていない。

### 出所（生成コード）

| ファイル | 内容 | 生成したコード |
|---|---|---|
| `experiments/VRCViewContext/results/2026-10-17-unity2022.3.22f1/images/*.png`（84） | 黒背景に単色のひし形 1 つ（2 色） | [`ViewContextSweep.cs`](../experiments/VRCViewContext/Overlay/Editor/ViewContextSweep.cs) の `SaveImage`（242〜246 行、298 行〜）。描画対象は [`HeadFrameFixture.cs`](../experiments/VRCHeadFrame/Assets/HeadFrame/Editor/HeadFrameFixture.cs) の `CreateMesh`（144 行〜）が作る 6 頂点の八面体 |
| `experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/regression-s2/images/*.png`（84） | 同上 | 同上（再実行） |
| `.../avatar-preview.png` | 箱を組み合わせた人型と診断カード | [`ClientValidationSetup.cs`](../experiments/VRCClientValidation/Overlay/Editor/ClientValidationSetup.cs) 71 行。人型は [`ClientAvatarAssets.cs`](../experiments/VRCClientValidation/Overlay/Editor/ClientAvatarAssets.cs) の `Create`（27 行〜） |
| `.../card0.png`〜`card2.png` | 数値と点パターンの診断カード | `ClientValidationSetup.cs` 80 行 |
| `.../effect-off.png`、`.../source-reference.png` | 頭部の箱と色付きの目印 | `ClientValidationSetup.cs` 84・88 行 |
| `.../initial-off-harness-failure/effect-off.png`、`source-reference.png` | 同上（初回の失敗 run。後者は全面黒） | 同上 |
| `.../generated/Font.png` | 数字・記号と C E P S W Y の 5×7 ドット文字 | `ClientAvatarAssets.cs` の `Font`（207 行〜）。グリフはコード内に 0/1 で手書き |

### メタデータ

- 177 件とも、チャンクは `IHDR` / `IDAT` / `IEND` だけ。`tEXt` / `iTXt` / `zTXt` / `eXIf` / `iCCP` / `tIME` / `pHYs` はなく、`IEND` の後ろにも余分なデータはない。
- ソフトウェア名・作成者・撮影日時・カラープロファイル名などは入っていない（Unity の `EncodeToPNG` の出力）。

## 2. Unity 生成アセット（YAML）

- 出所:
  - `experiments/VRCHeadFrame/Assets/HeadFrame/Generated/`（メッシュ 6 頂点 ×8、マテリアル 7、シーン 1）は [`HeadFrameSweep.cs`](../experiments/VRCHeadFrame/Assets/HeadFrame/Editor/HeadFrameSweep.cs) 311〜327 行が `HeadFrameFixture` から保存したもの。
  - `experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/generated/` は `ClientAvatarAssets.cs` が生成したもの。メッシュは箱（Body 144 頂点 = 18 個、Head 32 頂点 = 4 個）と板 4 頂点 ×3、Humanoid は同じコードで組んだ骨格、メニューとパラメーター（`VLatEffect`、`KeepHead`）、FX アニメーター（124 行〜）。
- GUID 参照: リポジトリ外を指すのは次の 8 個だけ。lilToon・MToon・UniVRM・商用アバターのアセットへの参照はない。
  - Unity 組み込み: `0000000000000000e000000000000000`、`0000000000000000f000000000000000`
  - VRChat SDK のスクリプト（`VRCAvatarDescriptor`、`PipelineManager`、`VRCParentConstraint`、`VRCHeadChop`、メニュー・パラメーター）: `67cc4cb7839cd3741b63733d5adf0442`、`4ecd63eff847044b68db9453ce219299`、`58e2f01a24261a14cb82e6d3399e8b16`。参照だけで、SDK のファイルは含まない。
  - 実験のシェーダー（`experiments/VRCClientValidation/Overlay/` の 3 本。`.meta` を追跡していないため、検証用プロジェクトで振られた GUID）: `e815493c85a66338c80ee5304ee474c2`、`5265e93f5df18c8f536c70566a134855`、`7a880854371cf98d71eb1176ac85f521`
- 作者名、絶対パス、ユーザー名、マシン名、メールアドレスはない。`.meta` の `userData` と `assetBundleName` はすべて空。
- ProjectSettings: `companyName` は `Kakudoll` と `DefaultCompany`、`cloudProjectId` と `organizationId` は空。`productGUID` はランダム値。

## 3. シェーダー

- 15 ファイルすべてが `VLat/Experiments/*` の診断用で、自作。
- 取り込んでいるのは Unity 標準の `UnityCG.cginc` だけで、その中身は複製していない。頂点入出力の `UNITY_*` マクロは Unity のマニュアルにある定型の書き方。
- ライセンス表記・著作権表記はなく、Unity 組み込みシェーダー、lilToon、MToon / UniVRM から写したコードも見当たらない。

## 4. 実験のログ・CSV・JSON

見つからなかったもの:

- VRChat のユーザー ID・アバター ID・ワールド ID・ファイル ID（`usr_` / `avtr_` / `wrld_` / `file_`）、表示名
- メールアドレス、個人の絶対パス、ホスト名、内部 IP、ハードウェアのシリアル、非公開リソースへの URL
- 商用アバターの名前（実験結果のファイル内）

含まれているもの（すべて `SHA256SUMS` で凍結済みなので、書き換えると整合性検査が FAIL になる）:

| 内容 | ファイル |
|---|---|
| GPU 型番、ドライバー版、グラフィックス API | `environment.txt`、`preflight.json`、`environment-audit.json`、`log-excerpt.txt` |
| VRChat クライアントが入っているかどうかとその版、HMD 検出結果（空） | `experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/environment-audit.json` |
| 実行時刻（UTC）、Unity のプロセス ID、Unity のライセンス確認ログ（ライセンスの中身はない） | 同上、`log-excerpt.txt` |
| 非公開リポジトリのブランチ名と、その基点コミット SHA | `experiments/VRCClientValidation/results/2026-10-17-unity2022.3.22f1/inputs.json` |
| VRChat SDK の一部ファイルの SHA-256（中身はない） | `sdk-inputs.json` |

個人を特定する情報ではなく、再現のための記録として残す価値があるため、そのまま公開してよいと判断した（9章で所有者が確認）。
外す場合は台帳に記録して非配布にする。そのファイルを使う検査は NOT RUN になる。

今後の注意: `experiments/VRCViewContext/client-evidence-template.csv` の `world_instance` 列に実機の値を入れると、インスタンス文字列に VRChat のユーザー ID が入ることがある。現在のスキャナーはこの形式を検出しない。

## 5. 文書とリンク

- 外部 URL は 38 種類。すべて公開ページ（Unity / VRChat / Microsoft の公式資料、UniVRM と VRM 仕様のリポジトリ、lilToon と NDMF の公式資料、GameCI、商用アバター 3 体の BOOTH 配布ページ、`example.com` のテスト値）。非公開リソースや個人のページへのリンクはない。
- 開発環境として OS のビルド番号、GPU、WSL から Windows の Unity を起動したこと、別の VRChat 用プロジェクトに lilToon があったことが書かれている。個人を特定する情報ではない。
- Windows のパスは `C:\path\to\...`、`C:\Temp\...`、`%TEMP%`、`<user>` などの例示だけ。
- 商用アバターの名前は、スキャナーで確認済みのファイルにだけ出てくる。
- **要所有者確認**: [実アバター検証の要約](experiments/REAL_AVATAR_VALIDATION_SUMMARY.md)では FAIL 20 件のモデル別内訳を「2 体に 16 件 / 4 件、1 体は 0 件」と匿名化しているが、[S1-R1 報告](experiments/VRC_P0_S1_IMAGE_DIFFERENCE_INVESTIGATION.md)の冒頭の表と 105 行付近、[S2 View Context 報告](experiments/VRC_P0_S2_VIEW_CONTEXT.md)の 218〜226 行ではモデル名ごとに条件と差の値を載せている。原因は D3D11 の丸めでモデルの欠陥ではないと本文に書かれているものの、方針をそろえるかどうかは所有者が決める。この 2 文書は凍結対象外なので、「モデル A / B / C」へ置き換えても整合性検査には影響しない。

## 6. 依存ライセンスと THIRD_PARTY_NOTICES

2026-11-22 に配布元で確認した結果です。

| 依存 | 版 | 確認したライセンス | 確認した場所 | 記載 |
|---|---|---|---|---|
| UniVRM（`com.vrmc.gltf`、`com.vrmc.univrm`、`com.vrmc.vrm`） | v0.128.3（commit `b045022e`） | MIT（VRM Consortium。MToon は Masataka SUMI、UniGLTF は ousttrue） | タグの `LICENSE.txt`、`Assets/UniGLTF/LICENSE.md`、`Assets/VRM/LICENSE.md` | 正しい。3 パッケージ名を書くとよい |
| `com.unity.nuget.newtonsoft-json` | 3.2.1（Newtonsoft.Json 13.0.2 を同梱） | パッケージは Unity Companion License、同梱の Newtonsoft.Json は MIT | パッケージ内の `LICENSE.md` と `Third Party Notices.md` | おおむね正しい。書き分けるとよい |
| Newtonsoft.Json（NuGet） | 13.0.2 | MIT | nuspec | 正しい |
| `com.unity.test-framework` ほか（performance、codecoverage、timeline、settings-manager） | lock のとおり | Unity Companion License | 各パッケージの `LICENSE.md` | 正しい |
| `com.unity.ext.nunit`（Test Framework の依存） | 2.0.3 | **Unity Package Distribution License**（中の NUnit 3.5 は MIT） | パッケージ内の `LICENSE.md` と `Third Party Notices.md` | **記載なし。「`com.unity.*` は Unity Companion License」とまとめているのは不正確** |
| `com.unity.modules.*` | 1.0.0（組み込み） | Unity エディター本体の一部で、Unity の利用規約に従う | — | 記載なし（必要なら追記） |
| NUnit | 3.13.3 | MIT（パッケージ同梱の `LICENSE.txt`） | nuspec と nupkg | 正しい |
| NUnit3TestAdapter | 4.6.0 | MIT | nuspec | 正しい |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | nuspec | 正しい |
| VRChat SDK（base / avatars） | 3.10.5 | **VRChat Materials License Agreement**（<https://hello.vrchat.com/legal/sdk>）。SDK の再配布、改変物の作成、オープンソースライセンスの適用を禁じている | 同ページ | 「VRChat の SDK 利用規約」とだけ書いている。正式名とリンクを書くとよい。SDK のファイルは含んでいない |
| Seed-san | commit `837f156` | VRM Public License 1.0、VirtualCast, Inc. | 該当 commit の `samples/Seed-san/README.md` | 正しい（含めていない） |
| 商用アバター 3 体 | — | 各モデルの利用規約 | 各 BOOTH 配布ページ（商品名と作者・ショップ名が一致） | 正しい（含めていない） |

THIRD_PARTY_NOTICES に載っていない、開発・CI だけで使うもの（同梱しないので必須ではない。載せるなら「開発用ツール」として別の表にする）:

- Python `jsonschema`（MIT）と、その依存の `attrs`、`referencing`、`rpds-py`、`jsonschema-specifications`（いずれも MIT）
- GitHub Actions: `actions/checkout`、`actions/setup-python`、`actions/setup-dotnet`、`actions/upload-artifact`、`actions/cache`（いずれも MIT）、`game-ci/unity-test-runner`（MIT。固定しているコミットは v4.3.2 のタグと一致）
- .NET SDK 10（MIT）

修正の提案（1〜5 は 2026-11-22 に THIRD_PARTY_NOTICES へ反映済み）:

1. `com.unity.*` の行に「`com.unity.ext.nunit` は Unity Package Distribution License（同梱の NUnit は MIT）」を加える。
2. Newtonsoft の行を「Unity のパッケージ: Unity Companion License / 同梱の Newtonsoft.Json 13.0.2: MIT」と書き分ける。
3. UniVRM の行に `com.vrmc.gltf` / `com.vrmc.univrm` / `com.vrmc.vrm` を書く。
4. VRChat SDK の行を「VRChat Materials License Agreement（リンク）。SDK のファイルは含めず、MIT License は SDK に及ばない」とする。
5. 任意: 開発・CI 用ツールの表を足す。

## 7. ライセンスの表示主体と貢献の扱い

**2026-11-22 追記:** 所有者の判断で、開発中のライセンスを MIT から PolyForm Strict 1.0.0 に変えた（`LICENSE` と
パッケージ内の `LICENSE.md`。条文は公式の 1.0.0 と同一）。表示主体は `marc2825`、`package.json` の `author` もそろえた。
Pull Request のための変更は LICENSE の Additional Permission で認め、貢献は MIT で所有者に提供されるものとした（後で MIT へ戻すため）。
それまで MIT で公開していた版は、その版については MIT のまま。以下は変更前の監査時の記録。


- `LICENSE` の `Copyright (c) 2026 Kakudoll Contributors` と、`package.json` の `author.name`（`Kakudoll Contributors`）は一致している。
- `CONTRIBUTING.md` の「貢献は同じ MIT License で提供される」（inbound = outbound）とも矛盾しない。CLA や DCO はない。
- UPM で `Packages/jp.vlat.core` だけを導入しても MIT の表示が届くよう、パッケージ内にも [`LICENSE.md`](../Packages/jp.vlat.core/LICENSE.md) を置いた（Issue #7）。
- 表示主体を「Kakudoll Contributors」のままにするか、所有者名を併記するかは所有者が決める（9章）。

## 8. Git の作者情報と GitHub 上の情報

- コミット 3 件。作者とコミッターは所有者の GitHub アカウント 1 つで、メールは所有者が公開を許可したアドレス 1 件。書き換えない。
- 3 件中 2 件に `Co-Authored-By` 行があり、Anthropic の noreply アドレス（機械用アドレス。スキャナーでも許可済み）が入っている。
- 署名付きコミットはない。タイムゾーンは +0900。
- リモートの ref は `HEAD` と `main` だけで、タグはない。ローカルには作業用ブランチが 1 つあるが、リモートにはない。公開するときは `main` だけを push する。
- 公開リポジトリにすると見えるようになるもの:
  - Issue #1〜#8 の本文: 個人のパス、メールアドレス、商用アバターの名前、外部 URL はない（リポジトリ自身へのリンクだけ）。
  - CI 3 回分のログと成果物（`license-free-results`）: 個人の情報はなく、マシン名は GitHub ホストのランナーのものだけ。
  - Wiki・Discussions・Projects は無効。リリースはない。

## 9. 所有者だけが確認できる項目

- [ ] 1章の PNG 177 件と `Font.png` は、表に挙げたコードで生成したもので、後から別の画像に差し替えていない。
- [ ] 2章の生成アセットは、表に挙げたコードの出力そのままである（手で配置した第三者の素材を含まない）。
- [ ] 4章の開発環境の情報（GPU 型番・ドライバー版、OS のビルド、VRChat クライアントの導入有無と版、実行時刻、非公開側のブランチ名とコミット SHA）を公開してよい。
- [ ] 5章のモデル名ごとの FAIL 内訳を、2 つの報告でそのまま公開してよい。または要約と同じように匿名化する。
- [ ] 商用アバター 3 体について、モデル名・作者名・配布ページと集計値を載せることが、各モデルの利用規約に反しないと判断した。
- [ ] Seed-san の画像と出力を公開版に含めない判断（2026-11-21）を維持する。
- [ ] 作者メール（所有者が公開を許可したアドレス 1 件）をそのまま公開する。
- [ ] 公開するときに push するのは `main` だけで、ローカルの作業用ブランチは含めない。
- [ ] Issue #1〜#8 の本文が公開されてよい。

## 10. 公開するときの手順

リポジトリを Public にする前後で、所有者が GitHub の設定を変えます（Private のままでは使えない設定を含みます）。

- [ ] Actions の Secret（`UNITY_EMAIL` / `UNITY_PASSWORD` / `UNITY_LICENSE` など）を登録する（[CI_SETUP.md](CI_SETUP.md)）
- [ ] リポジトリ変数 `VLAT_PUBLIC_AUTHOR_EMAILS` に、公開してよい作者メールを登録する
- [ ] Actions → CI → Run workflow（`release_check` にチェック）で `main` を実行し、`Release readiness` が READY になる
- [ ] 9章の所有者確認がすべて済んでいる
- [ ] Public に変更する（push するのは `main` とリリースのタグだけ）
- [ ] Settings → Security で **Private vulnerability reporting** を有効にする（[SECURITY.md](../SECURITY.md) の報告先。Public のリポジトリでだけ使える）
- [ ] Settings → General で **Discussions** を有効にする（Issue テンプレートの質問先）
- [ ] 公開後、README の Git URL で新しいプロジェクトに導入できることを確かめる（[RELEASING.md](product/RELEASING.md) 手順 7）

## 11. 再実行の手順

リポジトリのルートで実行します。

```bash
git rev-parse HEAD                      # 監査したコミットと同じか
git ls-remote origin                    # 公開する ref の一覧
python3 scripts/check_public_export.py .
python3 scripts/check_public_export.py . --git-history --allow-email "$(git log -1 --format=%ae)"
python3 scripts/run_license_free_checks.py --skip-dotnet   # SHA256SUMS は配布分 PASS、非配布分 NOT RUN になること
```

VRChat の ID、メールアドレス、Windows のホスト名、外部 URL を探す（このファイルと台帳は除く）:

```bash
git grep -nIE '(usr|avtr|wrld|file)_[0-9a-f]{8}-[0-9a-f]{4}' -- ':!docs/PUBLIC_RELEASE_CHECKLIST.md'
git grep -nIE '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}' -- ':!docs/PUBLIC_EXPORT_LEDGER.json' ':!docs/PUBLIC_RELEASE_CHECKLIST.md'
git grep -nIE '(DESKTOP|LAPTOP)-[A-Z0-9]{6,}'
git grep -hoIE 'https?://[^ )>"`<]+' | sort -u
```

PNG のチャンクを調べる（標準ライブラリだけ。結果が「0 with extra chunks」なら PASS）:

```bash
python3 - <<'EOF'
import struct, subprocess
files = subprocess.run(["git", "ls-files", "*.png"], capture_output=True, text=True, check=True).stdout.split()
extra = 0
for f in files:
    data = open(f, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", f
    pos, kinds = 8, []
    while pos < len(data):
        n, = struct.unpack(">I", data[pos:pos + 4])
        kind = data[pos + 4:pos + 8].decode("latin-1")
        kinds.append(kind)
        pos += 12 + n
        if kind == "IEND":
            break
    other = sorted(set(kinds) - {"IHDR", "PLTE", "IDAT", "IEND"})
    if other or pos != len(data):
        extra += 1
        print(f, other, "trailing bytes:", len(data) - pos)
print(len(files), "PNG files,", extra, "with extra chunks or trailing data")
EOF
```

リポジトリ外を指す Unity の GUID 参照を列挙する（2章の 8 個だけなら PASS）:

```bash
python3 - <<'EOF'
import re, subprocess
files = subprocess.run(["git", "ls-files"], capture_output=True, text=True, check=True).stdout.split()
yaml = [f for f in files if f.endswith((".asset", ".mat", ".unity", ".prefab", ".anim", ".controller", ".meta"))]
text = {f: open(f, encoding="utf-8").read() for f in yaml}
defined = {m.group(1) for f, t in text.items() if f.endswith(".meta") for m in [re.search(r"^guid: ([0-9a-f]{32})", t, re.M)] if m}
missing = {}
for f, t in text.items():
    for g in re.findall(r"guid: ([0-9a-f]{32}), type", t):
        if g not in defined:
            missing.setdefault(g, set()).add(f.rsplit("/", 1)[-1])
for g, used in sorted(missing.items()):
    print(g, sorted(used))
EOF
```

画像は、PNG を 1 枚ずつか一覧画像にして目視します。
依存ライセンスは、6章の「確認した場所」を、lock ファイルにある版でもう一度開いて確かめます。
