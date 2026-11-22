# Kakudoll

見る角度によって、アバターの形を描き分けるための Unity ツールです。

アニメの作画では、正面と横顔で前髪の流れや顔の輪郭をわざと描き変えます。
Kakudoll は、その「角度ごとの見え方」を 3D アバターでも作れるようにすることを目指しています。
元の VRM・メッシュ・マテリアルは書き換えません。

> **開発中（ベータ前）です。** VRChat 上ではまだ使えません。

## できること

- 左右の向き（Yaw）ごとにキーを作り、その角度での形を編集する
- 頂点を選んで、Unity の画面上でドラッグして形を変える（ソフト選択つき）
- 3 つのプレビューで、キーとキーのあいだの変化を確かめる
- 既存の BlendShape から形を取り込む
- 元に戻す・やり直し、保存、異常終了したときの復元

## まだできないこと

- VRChat 上での再生（実機での確認もまだです）
- 編集した形をゲーム実行中に再生すること（古い v0.1 形式だけは `VlatApplier` で再生できます）
- 複数メッシュの同時編集、法線の再計算、上下の向き（Pitch）での切り替え
- 大きなメッシュ（13 万頂点ほど）では、確定や元に戻すのに 0.2〜0.3 秒かかります

## 動作環境

- Unity 2022.3.62f3（Built-in Render Pipeline）
- UniVRM v0.128.3（VRM 1.0 対応。VRM 0.x は読み込みのみ）
- Windows で動作を確認しています

## はじめかた

1. Unity Hub で Unity 2022.3.62f3 をインストールする
2. このリポジトリを clone し、`VLatProject` フォルダを Unity Hub で開く
   （初回は UniVRM の取得に Git とネット接続が必要です）
3. メニューの **Kakudoll > Editor** を開く

自分のプロジェクトで使う場合は、`Packages/jp.vlat.core` をローカルパッケージとして追加し、
`VLatProject/Packages/manifest.json` と同じ UniVRM を依存に加えてください。作業前にはバックアップを取ってください。

## 使い方

1. 「VRMを開く」でアバターを読み込む（編集はプレビュー用の複製に対して行われます）
2. 編集したいメッシュ（顔など）を選び、グループを作る
3. 0°・45°・90° などの角度にキーを追加する
4. 頂点を選び、メインビューでドラッグして形を変える
5. サブビューで角度による変化を確かめ、保存する（`.vlat.json` と `.vlat.bin` ができます）

## テスト

```bash
python3 -m pip install "jsonschema>=4.10,<5"
python3 scripts/run_license_free_checks.py
```

Unity のテストは Test Runner（Window > General > Test Runner）から実行できます。
2026-11-21 時点で EditMode 357/357、PlayMode 4/4、Core 90/90 が通っています。
CI で Unity のテストを動かす設定は [docs/CI_SETUP.md](docs/CI_SETUP.md) を見てください。

## ドキュメント

- ファイル形式: [docs/spec/02_FORMAT_SPEC.md](docs/spec/02_FORMAT_SPEC.md)、[docs/product/KEYFORM_V0_2.md](docs/product/KEYFORM_V0_2.md)
- 設計: [docs/spec/03_ARCHITECTURE.md](docs/spec/03_ARCHITECTURE.md)、[docs/product/DECISIONS.md](docs/product/DECISIONS.md)
- VRChat に向けた検証実験: [docs/experiments/](docs/experiments/)
- このリポジトリに含めていない資料: [docs/PUBLIC_DISTRIBUTION.md](docs/PUBLIC_DISTRIBUTION.md)
- 貢献のしかた: [CONTRIBUTING.md](CONTRIBUTING.md) ／ 脆弱性の報告: [SECURITY.md](SECURITY.md)

## 名前について

Kakudoll（カクドール）は「角度（kakudo）」と「doll」を合わせた名前です。
開発初期の名前「VLat」は、ファイル形式（`.vlat.json`）とコード内の名前にそのまま残っています。

## ライセンス

MIT License です。第三者のアバターや、その画像・データは含んでいません（[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)）。

---

**English** — Kakudoll is a Unity editor tool (pre-beta) for shaping how an anime-style VRM avatar looks from
different viewing angles, without modifying the original assets. It does not run in VRChat yet. MIT licensed.
