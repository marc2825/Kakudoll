"""check_public_export.py の回帰テスト。一時Gitリポジトリを作り、履歴にだけ残る禁止fileや作者情報を検出できることを確かめる。

  python3 -B -m unittest discover -s scripts/tests -p "test_*.py"

認証情報・個人メール・個人パス・商用アバター名に見える値は、このfile自体が check_public_export.py や
Gitleaks に掛からないよう、実行時に文字列を連結して作る。どれも実在しないダミー値。
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_public_export as cpe  # noqa: E402

CHECKER = SCRIPTS / "check_public_export.py"
LEDGER_EXCLUDED = "docs/internal/WORKING_NOTES.md"
FAKE_GH_VALUE = "gh" + "p_" + "Q7" * 18           # github-token の形をしたダミー
OWNER_ADDRESS = "owner" + "@" + "mail.invalid"     # 許可リストに載せる前提の作者メール（ダミー）
PERSONAL_DIR = "/" + "home/" + "alice" + "/work"   # 個人homeに見えるダミーpath
MODEL_WORD = "Shin" + "ano"                        # 商用アバター名の検出用
LFS_POINTER = "version https://git-lfs.github.com/spec/v1\noid sha256:" + "0" * 64 + "\nsize 4096\n"

_saved_environ: dict[str, str] = {}


def setUpModule() -> None:
    # 開発者のglobal/system設定（署名・hook・autocrlf等）と、親processのGIT_*変数の影響を受けないようにする。
    _saved_environ.update(os.environ)
    for name in [n for n in os.environ if n.startswith("GIT_")]:
        del os.environ[name]
    os.environ.update(GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1", PYTHONIOENCODING="utf-8")


def tearDownModule() -> None:
    os.environ.clear()
    os.environ.update(_saved_environ)


class TempRepo:
    """除外台帳を持つ最小の公開版リポジトリ。"""

    def __init__(self, root: Path, email: str = "tester@example.com"):
        self.root = root
        root.mkdir(parents=True)
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.name", "Kakudoll Test")
        self.git("config", "user.email", email)
        self.git("config", "commit.gpgsign", "false")
        self.git("config", "tag.gpgsign", "false")
        ledger = {
            "format": "vlat-public-export-ledger/1",
            "excluded": [{"path": LEDGER_EXCLUDED, "sha256": hashlib.sha256(b"internal\n").hexdigest(),
                          "bytes": 9, "reason": "internal-working-doc"}],
        }
        self.write("docs/PUBLIC_EXPORT_LEDGER.json", json.dumps(ledger, indent=2) + "\n")
        self.write("README.md", "# test\n")
        self.initial = self.commit("initial")

    def git(self, *args: str, stdin: bytes | None = None) -> str:
        proc = subprocess.run(["git", *args], cwd=self.root, input=stdin, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if proc.returncode:
            raise AssertionError(f"git {args[0]} failed: {proc.stderr.decode(errors='replace')}")
        return proc.stdout.decode().strip()

    def write(self, rel: str, content: str | bytes) -> None:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode() if isinstance(content, str) else content)

    def remove(self, rel: str) -> None:
        (self.root / rel).unlink()

    def commit(self, message: str) -> str:
        # 開発者のglobal ignore（~/.config/git/ignore）に左右されないよう -f で追加する
        self.git("add", "-A", "-f")
        return self.commit_index(message)

    def commit_index(self, message: str) -> str:
        """作業treeを読まず、indexのままcommitする（plumbingで入れたgitlink/symlinkを残すため）。"""
        self.git("commit", "-q", "--allow-empty", "-m", message)
        return self.git("rev-parse", "HEAD")

    def add_index_entry(self, mode: str, sha: str, rel: str) -> None:
        self.git("update-index", "--add", "--cacheinfo", f"{mode},{sha},{rel}")

    def hash_object(self, data: bytes) -> str:
        return self.git("hash-object", "-w", "--stdin", stdin=data)


class HistoryAuditTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.repo = TempRepo(Path(self.temp.name) / "repo")

    def tearDown(self) -> None:
        self.temp.cleanup()

    def history(self, allowed: set[str] | None = None) -> tuple[list[str], str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            findings = cpe.check_history(self.repo.root, allowed or set())
        return findings, out.getvalue()

    def tree_failures(self) -> list[str]:
        failures, _ = cpe.check_tree(self.repo.root)
        return failures

    def run_cli(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run([sys.executable, "-B", str(CHECKER), str(self.repo.root), *args],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8")

    def assertHistoryFinding(self, findings: list[str], path: str, kind: str) -> None:
        matches = [f for f in findings if f.startswith(f"history {path} (") and kind in f]
        self.assertTrue(matches, f"{path}: '{kind}' が検出されない\n" + "\n".join(findings))

    def test_clean_history_passes(self) -> None:
        self.repo.write("Packages/core/Runtime/Applier.cs", "class Applier {}\n")
        self.repo.commit("add runtime")
        self.repo.write("README.md", "# test\n\nSee [the ledger](docs/PUBLIC_EXPORT_LEDGER.json).\n")
        self.repo.commit("update readme")
        self.assertEqual(self.tree_failures(), [])
        findings, summary = self.history()
        self.assertEqual(findings, [])
        self.assertIn("3 commits", summary)
        result = self.run_cli("--git-history")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn("PASS", result.stdout)

    def test_forbidden_files_added_then_deleted_are_detected(self) -> None:
        planted = {
            "Assets/Avatar/model.vrm": (b"glTF\x00binary", "公開しない種類のfile（.vrm）"),
            "experiments/VRCRealAvatars/results/run-1/surface.json": ("{}\n", "商用アバター由来データの置き場"),
            LEDGER_EXCLUDED: ("internal\n", "除外台帳で非配布"),
            "config/.env.local": ("MODE=dev\n", "公開しない種類のfile"),
            "scripts/__pycache__/tool.cpython-312.pyc": (b"\x00\x0d\x0d\x0a", "Python bytecode"),
            "docs/product/evidence/2026-10-25-editor-1e/1e-avatar-front.png": (b"\x89PNG", "Seed-san"),
            "docs/notes.md": (f"measured on {MODEL_WORD}\n", "商用アバター名"),
        }
        for rel, (content, _) in planted.items():
            self.repo.write(rel, content)
        self.repo.commit("add files")
        for rel in planted:
            self.repo.remove(rel)
        self.repo.commit("remove files")
        self.assertEqual(self.tree_failures(), [], "現行treeはcleanのはず")
        findings, _ = self.history()
        for rel, (_, kind) in planted.items():
            self.assertHistoryFinding(findings, rel, kind)

    def test_same_blob_at_allowed_and_forbidden_paths(self) -> None:
        # 同じblobが許可されたpathに残り、禁止pathのcopyは削除済み。blobごとに1pathだけ見ると見逃す。
        content = '{"vertices": [1, 2, 3]}\n'
        self.repo.write("data/sample.json", content)
        self.repo.commit("add sample")
        self.repo.write("experiments/VRCImageDifference/results/r1/sample.json", content)
        self.repo.write("Assets/sample.fbx", content)
        self.repo.commit("copy sample")
        self.repo.remove("experiments/VRCImageDifference/results/r1/sample.json")
        self.repo.remove("Assets/sample.fbx")
        self.repo.commit("remove copies")
        findings, _ = self.history()
        self.assertHistoryFinding(findings, "experiments/VRCImageDifference/results/r1/sample.json", "商用アバター由来データの置き場")
        self.assertHistoryFinding(findings, "Assets/sample.fbx", "公開しない種類のfile（.fbx）")
        self.assertFalse([f for f in findings if f.startswith("history data/sample.json ")], findings)

    def test_rename_into_forbidden_path_and_back(self) -> None:
        self.repo.write("notes/body.txt", "mesh notes\n")
        self.repo.commit("add notes")
        self.repo.git("mv", "notes/body.txt", "notes/body.blend")
        self.repo.commit_index("rename to blend")
        self.repo.git("mv", "notes/body.blend", "notes/body.txt")
        self.repo.commit_index("rename back")
        self.assertEqual(self.tree_failures(), [])
        findings, _ = self.history()
        self.assertHistoryFinding(findings, "notes/body.blend", "公開しない種類のfile（.blend）")

    def test_secret_in_deleted_blob_is_reported_without_value(self) -> None:
        self.repo.write("config/settings.json", json.dumps({"upload": FAKE_GH_VALUE, "cache": PERSONAL_DIR + "/cache"}) + "\n")
        self.repo.commit("add settings")
        self.repo.remove("config/settings.json")
        self.repo.commit("remove settings")
        result = self.run_cli("--git-history")
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn("history config/settings.json", result.stdout)
        self.assertIn("github-token (1 match(es))", result.stdout)
        self.assertIn("personal-posix-home (1 match(es))", result.stdout)
        self.assertNotIn(FAKE_GH_VALUE, result.stdout)
        self.assertNotIn(FAKE_GH_VALUE[4:12], result.stdout)
        self.assertNotIn(PERSONAL_DIR, result.stdout)

    def test_commit_message_is_scanned(self) -> None:
        self.repo.write("src/a.cs", "class A {}\n")
        sha = self.repo.commit(f"fix build\n\nlog was in {PERSONAL_DIR}/build.log\n")
        findings, _ = self.history()
        self.assertIn(f"commit {sha[:12]}: message: personal-posix-home (1 match(es))", findings)

    def test_lfs_pointer_gitlink_and_symlink_are_reported(self) -> None:
        self.repo.write("Assets/Textures/albedo.png", LFS_POINTER)
        self.repo.commit("add lfs pointer")
        self.repo.add_index_entry(cpe.GITLINK_MODE, hashlib.sha1(b"other repository").hexdigest(), "vendor/sdk")
        self.repo.add_index_entry(cpe.SYMLINK_MODE, self.repo.hash_object(b"../outside/settings.json"), "docs/link.json")
        self.repo.commit_index("add submodule and symlink")
        findings, _ = self.history()
        self.assertHistoryFinding(findings, "Assets/Textures/albedo.png", "Git LFSポインタ")
        self.assertHistoryFinding(findings, "vendor/sdk", "submodule（gitlink）")
        self.assertHistoryFinding(findings, "docs/link.json", "symlink")
        tree = self.tree_failures()
        self.assertTrue(any(f.startswith("Assets/Textures/albedo.png: Git LFSポインタ") for f in tree), tree)
        self.assertTrue(any(f.startswith("vendor/sdk: submodule（gitlink）") for f in tree), tree)

    def test_symlink_in_working_tree_is_reported(self) -> None:
        try:
            os.symlink("README.md", self.repo.root / "docs" / "README-link.md")
        except (OSError, NotImplementedError) as error:
            self.skipTest(f"symlinkを作れない環境: {error}")
        failures = self.tree_failures()
        self.assertTrue(any(f.startswith("docs/README-link.md: symlink") for f in failures), failures)

    def test_author_email_requires_owner_allow_list(self) -> None:
        repo = TempRepo(Path(self.temp.name) / "owner-repo", email=OWNER_ADDRESS)
        self.repo = repo
        repo.write("src/a.cs", "class A {}\n")
        repo.commit(f"add a\n\nCo-authored-by: Owner <{OWNER_ADDRESS}>\n")
        denied = self.run_cli("--git-history")
        self.assertEqual(denied.returncode, 1, denied.stdout)
        self.assertIn("author email が許可されていない個人アドレス", denied.stdout)
        self.assertIn("committer email が許可されていない個人アドレス", denied.stdout)
        self.assertIn("message: email-address (1 match(es))", denied.stdout)
        self.assertNotIn(OWNER_ADDRESS, denied.stdout)
        # 複数指定・カンマ区切り・大文字小文字の違いを受け付ける
        allowed = self.run_cli("--git-history", "--allow-email", "other@example.org, " + OWNER_ADDRESS.upper())
        self.assertEqual(allowed.returncode, 0, allowed.stdout)
        # 許可リストは作者情報とメッセージだけに効き、file内容の個人メールは引き続き検出する
        repo.write("docs/contact.md", f"mail: {OWNER_ADDRESS}\n")
        repo.commit("add contact")
        repo.remove("docs/contact.md")
        repo.commit("remove contact")
        content = self.run_cli("--git-history", "--allow-email", OWNER_ADDRESS)
        self.assertEqual(content.returncode, 1, content.stdout)
        self.assertIn("history docs/contact.md", content.stdout)
        self.assertIn("email-address (1 match(es))", content.stdout)
        self.assertNotIn(OWNER_ADDRESS, content.stdout)

    def test_non_branch_refs_are_audited(self) -> None:
        repo = self.repo
        # refs/replace: 置き換えが有効だと、元のcommitにある禁止fileが見えなくなる
        repo.write("Assets/raw.psd", b"8BPS")
        hidden = repo.commit("add psd")
        repo.remove("Assets/raw.psd")
        repo.commit("remove psd")
        clean = repo.git("commit-tree", f"{repo.initial}^{{tree}}", "-p", repo.initial, "-m", "clean")
        repo.git("replace", hidden, clean)
        self.assertNotIn("Assets/raw.psd", repo.git("ls-tree", "-r", "--name-only", hidden))
        # tag だけが指す（branchは削除済みの）commit と、tagger email
        repo.git("checkout", "-q", "-b", "side")
        repo.write("leak/scene.glb", b"glTF")
        repo.commit("side work")
        repo.git("-c", f"user.email={OWNER_ADDRESS}", "tag", "-a", "v0-side", "-m", "side snapshot")
        repo.git("checkout", "-q", "main")
        repo.git("branch", "-D", "side")
        # refs/stash 相当（通常はpushされないが、手元で検査すると対象になる）
        blob = repo.hash_object(b"pem")
        tree = repo.git("mktree", stdin=f"100644 blob {blob}\tsecret.pem\n".encode())
        repo.git("update-ref", "refs/stash", repo.git("commit-tree", tree, "-m", "wip"))
        # refs/notes: note本文は文章として検査する
        repo.git("notes", "add", "-m", f"reviewed by {OWNER_ADDRESS}")

        findings, summary = self.history()
        self.assertHistoryFinding(findings, "Assets/raw.psd", "公開しない種類のfile（.psd）")
        self.assertHistoryFinding(findings, "leak/scene.glb", "公開しない種類のfile（.glb）")
        self.assertHistoryFinding(findings, "secret.pem", "公開しない種類のfile（.pem）")
        self.assertTrue(any(f.startswith("tag ") and "tagger email" in f for f in findings), findings)
        self.assertTrue(any(f.startswith("history ") and "email-address" in f
                            and cpe.NOTES_PATH.search(f.split(" ")[1].replace("/", "")) for f in findings), findings)
        for namespace in ("heads 1", "notes 1", "replace 1", "stash 1", "tags 1"):
            self.assertIn(namespace, summary)
        self.assertFalse([f for f in findings if OWNER_ADDRESS in f])

    def test_shallow_clone_is_rejected(self) -> None:
        self.repo.write("src/a.cs", "class A {}\n")
        self.repo.commit("second")
        shallow = Path(self.temp.name) / "shallow"
        subprocess.run(["git", "clone", "-q", "--depth", "1", self.repo.root.as_uri(), str(shallow)],
                       check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.repo.root = shallow
        findings, _ = self.history()
        self.assertEqual(len(findings), 1)
        self.assertIn("shallow clone", findings[0])


if __name__ == "__main__":
    unittest.main()
