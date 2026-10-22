#!/usr/bin/env python3
# Author: 0Shard
"""Exercise the real packaging script in disposable project directories."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


PROJECT_ROOT = Path(__file__).resolve().parents[1]


class ModPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory(prefix="eternal-package-test-")
        self.addCleanup(self.temp_dir.cleanup)
        self.project = Path(self.temp_dir.name)
        self.source = self.project / "Eternal"
        self.package = self.project / "Mods" / "Eternal"
        self.assemblies = self.source / "1.6" / "Assemblies" / "net472"
        self.assemblies.mkdir(parents=True)
        (self.source / "About").mkdir()
        for relative_path in ("About/About.xml", "About/PublishedFileId.txt", "LoadFolders.xml"):
            shutil.copyfile(PROJECT_ROOT / "Eternal" / relative_path, self.source / relative_path)
        for relative_path in (
            "Defs/Hediffs/Test.xml",
            "Languages/English/Keyed/Test.xml",
            "Patches/Test.xml",
            "Textures/UI/Gizmos/Eternal_Resurrection.png",
        ):
            path = self.source / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture content")
        (self.assemblies / "Eternal.dll").write_bytes(b"one shared net472 build\x00\xff")

    def run_package(self, expected_success=True):
        result = subprocess.run(
            ["bash", str(PROJECT_ROOT / "create-mod-package.sh")],
            cwd=self.project,
            capture_output=True,
            text=True,
            check=False,
        )
        if expected_success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def test_shared_dll_metadata_and_version_selection(self):
        self.run_package()
        metadata = ET.parse(self.package / "About" / "About.xml").getroot()
        self.assertEqual(
            [node.text for node in metadata.findall("supportedVersions/li")],
            ["1.6", "1.7"],
        )
        mapping = ET.parse(self.package / "LoadFolders.xml").getroot()
        self.assertEqual([node.tag for node in mapping], ["v1.6", "v1.7"])
        original = (self.assemblies / "Eternal.dll").read_bytes()
        for version in ("1.6", "1.7"):
            folders = [node.text for node in mapping.find(f"v{version}")]
            self.assertEqual(folders, ["/", version])
            # RimWorld reads Assemblies from each selected content folder.
            selected_dlls = []
            for folder in folders:
                content_root = self.package if folder == "/" else self.package / folder
                selected_dlls.extend((content_root / "Assemblies").glob("*.dll"))
            self.assertEqual(selected_dlls, [self.package / version / "Assemblies" / "Eternal.dll"])
            self.assertEqual(selected_dlls[0].read_bytes(), original)
        for relative_path in (
            "Defs/Hediffs/Test.xml",
            "Languages/English/Keyed/Test.xml",
            "Patches/Test.xml",
            "Textures/UI/Gizmos/Eternal_Resurrection.png",
        ):
            self.assertEqual(
                (self.package / relative_path).read_bytes(),
                (self.source / relative_path).read_bytes(),
            )

    def test_only_own_dll_is_packaged_and_local_symbols_are_preserved(self):
        (self.assemblies / "Eternal.pdb").write_bytes(b"own symbols")
        for relative_path in (
            "0Harmony.dll", "0Harmony.pdb", "Assembly-CSharp.dll", "UnityEngine.dll",
            "Eternal.Tests.dll", "Eternal.Tests.pdb", "Eternal.deps.json",
            "nested/Eternal.dll", "nested/Eternal.pdb", "nested/Dependency.dll",
        ):
            path = self.assemblies / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"excluded output")
        self.run_package()
        for version in ("1.6", "1.7"):
            outputs = self.package / version / "Assemblies"
            self.assertEqual({path.name for path in outputs.iterdir()}, {"Eternal.dll"})
        self.assertFalse(list(self.package.rglob("*.pdb")))
        self.assertEqual((self.assemblies / "Eternal.pdb").read_bytes(), b"own symbols")
        self.assertEqual(len(list(self.package.rglob("*.dll"))), 2)
        self.assertFalse(list(self.package.rglob("*.json")))

    def test_rebuild_removes_stale_distribution_files_and_preserves_local_symbols(self):
        (self.assemblies / "Eternal.pdb").write_bytes(b"old symbols")
        self.run_package()
        (self.package / "1.7" / "Assemblies" / "Unwanted.dll").write_bytes(b"stale")
        for version in ("1.6", "1.7"):
            (self.package / version / "Assemblies" / "Eternal.pdb").write_bytes(b"stale packaged symbols")
        (self.assemblies / "Eternal.dll").write_bytes(b"new release build")
        self.run_package()
        self.assertFalse(list(self.package.rglob("*.pdb")))
        self.assertEqual((self.assemblies / "Eternal.pdb").read_bytes(), b"old symbols")
        for version in ("1.6", "1.7"):
            outputs = self.package / version / "Assemblies"
            self.assertEqual({path.name for path in outputs.iterdir()}, {"Eternal.dll"})
            self.assertEqual((outputs / "Eternal.dll").read_bytes(), b"new release build")

    def test_missing_dll_fails_before_replacing_package(self):
        self.run_package()
        previous = (self.package / "1.7" / "Assemblies" / "Eternal.dll").read_bytes()
        (self.assemblies / "Eternal.dll").unlink()
        result = self.run_package(expected_success=False)
        self.assertIn("Eternal.dll", result.stdout)
        self.assertEqual((self.package / "1.7" / "Assemblies" / "Eternal.dll").read_bytes(), previous)

    def test_missing_load_mapping_fails_before_replacing_package(self):
        self.run_package()
        previous = (self.package / "LoadFolders.xml").read_bytes()
        (self.source / "LoadFolders.xml").unlink()
        result = self.run_package(expected_success=False)
        self.assertIn("LoadFolders.xml", result.stdout)
        self.assertEqual((self.package / "LoadFolders.xml").read_bytes(), previous)


if __name__ == "__main__":
    unittest.main()
