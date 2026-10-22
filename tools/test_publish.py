from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile


HELPERS = Path(__file__).with_name("publish_helpers.sh").resolve()


class PublishLocalTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="eternal-publish-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def helper(self, function, *arguments):
        return subprocess.run(
            ["bash", "-euo", "pipefail", "-c", 'source "$1"; shift; "$@"',
             "local-publish-test", str(HELPERS), function, *map(str, arguments)],
            capture_output=True, text=True, check=True)

    def test_export_removes_tracked_private_guidance_and_config(self):
        repo = self.root / "repo"
        repo.mkdir()
        subprocess.run(["git", "init", "-q", str(repo)], check=True)
        private = ["AGENTS.md", ".agents/skills/private/SKILL.md", ".codex/config.toml",
                   "Eternal/AGENTS.md", ".planning/probe.md", ".claude/settings.json",
                   ".gsd/probe", "docs/private.md", "publish.sh"]
        for name in private + ["Eternal/About/About.xml", "README.md"]:
            target = repo / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("sentinel: " + name)
        subprocess.run(["git", "-C", str(repo), "add", "--", *private,
                        "Eternal/About/About.xml", "README.md"], check=True)
        subprocess.run(["git", "-C", str(repo), "-c", "user.name=0Shard",
                        "-c", "user.email=local@example.invalid", "commit", "-qm", "fixture"], check=True)
        export = self.root / "export"
        export.mkdir()
        self.helper("publish_stage_export", repo, export)
        leaked = [name for name in private if (export / name).exists()]
        self.assertEqual([], leaked)
        self.assertTrue((export / "Eternal/About/About.xml").is_file())
        self.assertTrue((export / "README.md").is_file())

    def check_retry(self, backend):
        package = self.root / "Mods" / "Eternal"
        package.mkdir(parents=True)
        (package / "removed.txt").write_text("old")
        (package / "kept.txt").write_text("first")
        archive = self.root / "same-version.zip"
        self.helper("publish_create_archive", package, archive, backend)
        (package / "removed.txt").unlink()
        (package / "kept.txt").write_text("second")
        (package / "new file.txt").write_text("new")
        self.helper("publish_create_archive", package, archive, backend)
        with zipfile.ZipFile(archive) as actual:
            files = {name for name in actual.namelist() if not name.endswith("/")}
            self.assertEqual({"Eternal/kept.txt", "Eternal/new file.txt"}, files)
            self.assertEqual(b"second", actual.read("Eternal/kept.txt"))

    def test_tree_and_index_guards_reject_private_paths(self):
        export = self.root / "export"
        export.mkdir()
        (export / "AGENTS.md").write_text("private")
        with self.assertRaises(subprocess.CalledProcessError):
            self.helper("publish_assert_public_tree", export)
        subprocess.run(["git", "init", "-q", str(export)], check=True)
        subprocess.run(["git", "-C", str(export), "add", "AGENTS.md"], check=True)
        with self.assertRaises(subprocess.CalledProcessError):
            self.helper("publish_assert_public_index", export)

    def test_zip_retry_drops_removed_package_entries(self):
        self.check_retry("zip")

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell unavailable")
    def test_powershell_retry_matches_current_package(self):
        self.check_retry("pwsh")

    def test_archive_failure_preserves_previous_archive(self):
        archive = self.root / "existing.zip"
        archive.write_bytes(b"previous published candidate")
        with self.assertRaises(subprocess.CalledProcessError):
            self.helper("publish_create_archive", self.root / "absent", archive, "zip")
        self.assertEqual(b"previous published candidate", archive.read_bytes())


if __name__ == "__main__":
    unittest.main()
