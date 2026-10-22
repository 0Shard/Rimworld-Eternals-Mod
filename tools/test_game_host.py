#!/usr/bin/env python3
# Author: 0Shard
"""Fixture-only game-host checks; these tests do not launch a game or Wine."""

import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

import game_host


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data if isinstance(data, bytes) else data.encode())


def artifact_bytes():
    data = bytearray(512)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 88, 0x10B)
    struct.pack_into("<II", data, 88 + 96 + 14 * 8, 4096, 72)
    return bytes(data) + b".NETFramework,Version=v4.7.2"


class GameHostTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="eternal-host-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / "original-game"
        self.harmony = self.root / "original-harmony"
        self.mod = self.root / "original-eternal"
        self.evidence = self.root / "private-session"
        self.artifact = self.root / "artifact" / "Eternal.dll"
        self.config = self.root / "input-config.xml"
        write(self.artifact, artifact_bytes())
        write(self.config, "<testConfig />")
        write(self.harmony / "About" / "About.xml", "<ModMetaData><packageId>brrainz.harmony</packageId><supportedVersions><li>1.6</li><li>1.7</li></supportedVersions></ModMetaData>")
        write(self.harmony / "LoadFolders.xml", "<loadFolders />")
        write(self.harmony / "LICENSE", "fixture license")
        for version in ("1.5", "1.6", "1.7"):
            write(self.harmony / version / "Assemblies" / "0Harmony.dll", f"Harmony {version}")
        for name in ("About", "Defs", "Patches", "Languages", "Textures"):
            shutil.copytree(game_host.CHECKOUT / "Eternal" / name, self.mod / name)
        shutil.copyfile(game_host.CHECKOUT / "Eternal" / "LoadFolders.xml", self.mod / "LoadFolders.xml")
        write(self.mod / "1.6" / "Assemblies" / "Eternal.dll", "stale original DLL")
        write(self.mod / "1.7" / "Assemblies" / "Eternal.dll", "different stale DLL")
        self.create_game("1.7")

    def create_game(self, version):
        if self.game.exists():
            shutil.rmtree(self.game)
        write(self.game / "Version.txt", f"{version}.9999 rev123\n")
        if version == "1.7":
            write(self.game / "RimWorld", "native fixture executable")
            (self.game / "RimWorld").chmod(0o755)
            managed = self.game
            write(managed / "UnityStubs.dll", "native stubs")
            write(managed / "RimWorld.runtimeconfig.json", '{"runtimeOptions":{"tfm":"net10.0"}}')
            write(self.game / "Assets.zip", "native assets")
            write(self.game / "libactual.so", "native library")
            (self.game / "libalias.so").symlink_to("libactual.so")
        else:
            write(self.game / "RimWorldWin64.exe", "Windows fixture executable")
            managed = self.game / "RimWorldWin64_Data" / "Managed"
            write(self.game / "UnityPlayer.dll", "Unity player")
            write(managed / "UnityEngine.CoreModule.dll", "Unity engine")
        write(managed / "Assembly-CSharp.dll", "game assembly")
        for folder, package in (("Core", "Ludeon.RimWorld"), ("Royalty", "Ludeon.RimWorld.Royalty"), ("Anomaly", "Ludeon.RimWorld.Anomaly")):
            write(self.game / "Data" / folder / "About" / "About.xml", f"<ModMetaData><packageId>{package}</packageId></ModMetaData>")
        for name in ("Mods", "Config", "SaveData", "Saves", "UserData", "Logs", "Source"):
            write(self.game / name / "private-original.txt", "must not copy")

    def args(self, version="1.7", prepare_only=True, extra=()):
        arguments = [
            "--game-root", str(self.game), "--harmony-root", str(self.harmony),
            "--mod-root", str(self.mod), "--artifact", str(self.artifact),
            "--config", str(self.config), "--evidence-dir", str(self.evidence),
            "--version", version, "--mode", "smoke",
        ]
        arguments += ["--prepare-only"] if prepare_only else ["--timeout", "45"]
        return game_host.parser().parse_args(arguments + list(extra))

    def source_snapshot(self):
        return {str(p): p.read_bytes() for root in (self.game, self.harmony, self.mod) for p in root.rglob("*") if p.is_file()}

    def write_mock_report(self, evidence, version="1.7.9999", mode="smoke", success="true", root="gameHost"):
        # Synthetic fixture evidence verifies the host gate, without proving gameplay.
        write(evidence / "reports" / "game-host-result.xml", f'<{root} mode="{mode}" gameVersion="{version}" success="{success}" />')

    def create_initial_fixture(self):
        save = self.root / "prior-session" / "EternalSynthetic-0123456789abcdef.rws"
        checkpoint = self.root / "prior-session" / "checkpoint.xml"
        write(save, "<savegame><syntheticFixture /></savegame>")
        write(save.parent / "UnrelatedColony.rws", "never copy this unrelated save")
        root = ET.Element("persistenceCheckpoint", schemaVersion="1", saveName=save.stem,
                          saveFileName=save.name, savePath=str(save),
                          sourceSaveSha256=hashlib.sha256(save.read_bytes()).hexdigest())
        ET.ElementTree(root).write(checkpoint, encoding="utf-8", xml_declaration=True)
        return save, checkpoint

    def create_initial_settings(self, checkpoint):
        directory = self.root / "prior-session" / "Config"
        root = ET.parse(checkpoint).getroot()
        listed = ET.SubElement(root, "settingsFiles")
        for name in ("Mod_Eternal_Eternal_Mod.xml", "Eternal_HediffSettings.xml"):
            path = directory / name
            write(path, f"<settings><nondefaultFixture>{name}</nondefaultFixture></settings>")
            ET.SubElement(listed, "file", name=name, sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        for name in ("ModsConfig.xml", "Prefs.xml", "Credentials.xml"):
            write(directory / name, "must not copy this configuration")
        ET.ElementTree(root).write(checkpoint, encoding="utf-8", xml_declaration=True)
        return directory

    def test_native_copy_is_private_complete_and_does_not_change_inputs(self):
        before = self.source_snapshot()
        evidence, record = game_host.prepare(self.args())
        self.assertEqual(before, self.source_snapshot())
        self.assertEqual(evidence.stat().st_mode & 0o777, 0o700)
        copied_game = evidence / "game"
        self.assertEqual({p.name for p in (copied_game / "Mods").iterdir()}, {"Harmony", "Eternal"})
        self.assertFalse(list(copied_game.rglob("private-original.txt")))
        self.assertFalse(any(p.is_symlink() for p in copied_game.rglob("*")))
        self.assertTrue((copied_game / "Assets.zip").is_file())
        self.assertEqual((copied_game / "libalias.so").read_bytes(), b"native library")
        self.assertFalse((copied_game / "Mods" / "Harmony" / "1.6").exists())
        self.assertFalse((copied_game / "Mods" / "Eternal" / "1.6").exists())
        self.assertEqual(record["artifact"]["sha256"], record["copied_artifact"]["sha256"])
        self.assertEqual(record["binary"]["sha256"], record["copied_binary"]["sha256"])
        write(copied_game / "libalias.so", "copy changed independently")
        self.assertEqual((self.game / "libactual.so").read_bytes(), b"native library")
        active = ET.parse(evidence / "savedata" / "Config" / "ModsConfig.xml").findall("activeMods/li")
        self.assertEqual([p.text for p in active], ["brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.anomaly", "shard.eternal"])
        self.assertEqual((evidence / "test-config.xml").read_bytes(), self.config.read_bytes())
        self.assertIsNone(record["pid"])
        self.assertEqual(json.loads((evidence / "session.json").read_text())["status"], "prepared")

    def test_wine_command_uses_private_prefix_and_windows_paths(self):
        self.create_game("1.6")
        args = self.args("1.6", False, ("--game-arg=-eternal-test-autostart",))
        evidence, record = game_host.prepare(args)
        self.assertEqual(record["command"][0], "wine")
        self.assertIn("-savedatafolder=Z:" + str(evidence / "savedata").replace("/", "\\"), record["command"])
        self.assertIn("-eternal-test-autostart", record["command"])
        self.assertEqual(record["wineprefix"], str(evidence / "wineprefix"))
        self.write_mock_report(evidence, version="1.6.9999")
        process = mock.Mock(pid=12345, returncode=0)
        process.wait.return_value = 0
        with mock.patch.object(game_host.subprocess, "Popen", return_value=process) as popen:
            self.assertEqual(game_host.launch(args, evidence, record), 0)
        self.assertEqual(popen.call_args.kwargs["env"]["WINEPREFIX"], str(evidence / "wineprefix"))
        self.assertTrue(popen.call_args.kwargs["start_new_session"])
        self.assertFalse(popen.call_args.kwargs["shell"])
        self.assertEqual(record["pid"], 12345)
        self.assertEqual(record["exit_code"], 0)

    def test_native_launch_command_and_exit_are_recorded_with_mock_only(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        self.assertEqual(record["command"][0], str(evidence / "game" / "RimWorld"))
        self.assertIn(f"-eternal-test-config={evidence / 'test-config.xml'}", record["command"])
        self.assertFalse(any("nosteam" in arg for arg in record["command"]))
        self.assertNotIn("-screen-fullscreen", record["command"])
        process = mock.Mock(pid=12345, returncode=7)
        self.write_mock_report(evidence)
        with mock.patch.object(game_host.subprocess, "Popen", return_value=process):
            self.assertEqual(game_host.launch(args, evidence, record), 1)
        self.assertEqual(record["exit_code"], 7)

    def test_native_xdg_environment_is_private_without_changing_home(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        original = {"HOME": "/original-home", "CODEX_HOME": "/original-codex",
                    "XDG_CONFIG_HOME": "/original-config", "XDG_CACHE_HOME": "/original-cache",
                    "XDG_DATA_HOME": "/original-data"}
        process = mock.Mock(pid=12345, returncode=0)
        self.write_mock_report(evidence)
        with mock.patch.dict(game_host.os.environ, original), \
                mock.patch.object(game_host.subprocess, "Popen", return_value=process) as popen:
            self.assertEqual(game_host.launch(args, evidence, record), 0)
            self.assertEqual({name: game_host.os.environ[name] for name in original}, original)
        environment = popen.call_args.kwargs["env"]
        self.assertEqual(environment["HOME"], original["HOME"])
        self.assertEqual(environment["CODEX_HOME"], original["CODEX_HOME"])
        expected = {"XDG_CONFIG_HOME": evidence / "engine-config",
                    "XDG_CACHE_HOME": evidence / "engine-cache",
                    "XDG_DATA_HOME": evidence / "engine-data"}
        self.assertEqual(record["environment_overrides"], {name: str(path) for name, path in expected.items()})
        for name, path in expected.items():
            self.assertEqual(environment[name], str(path))
            self.assertTrue(path.is_dir())
            self.assertEqual(path.stat().st_mode & 0o777, 0o700)

    def test_fresh_window_preferences_for_both_versions(self):
        for version in ("1.6", "1.7"):
            with self.subTest(version=version):
                self.create_game(version)
                write(self.game / "Config" / "Prefs.xml", "<PrefsData><fullscreen>true</fullscreen><screenWidth>9999</screenWidth><pauseOnLoad>true</pauseOnLoad></PrefsData>")
                self.evidence = self.root / f"prefs-session-{version}"
                evidence, _ = game_host.prepare(self.args(version))
                prefs = ET.parse(evidence / "savedata" / "Config" / "Prefs.xml").getroot()
                self.assertEqual(prefs.tag, "PrefsData")
                self.assertEqual({node.tag: node.text for node in prefs}, {
                    "screenWidth": "800", "screenHeight": "600", "fullscreen": "false",
                    "runInBackground": "true", "devMode": "true", "volumeMusic": "0",
                    "pauseOnLoad": "false",
                })
                self.assertEqual(ET.parse(self.game / "Config" / "Prefs.xml").findtext("screenWidth"), "9999")
                self.assertEqual(ET.parse(self.game / "Config" / "Prefs.xml").findtext("pauseOnLoad"), "true")

    def test_zero_exit_requires_successful_matching_report(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        process = mock.Mock(pid=12345, returncode=0)
        self.write_mock_report(evidence)
        with mock.patch.object(game_host.subprocess, "Popen", return_value=process):
            self.assertEqual(game_host.launch(args, evidence, record), 0)
        self.assertEqual(record["test_status"], "passed")
        self.assertEqual(record["game_host_report"], game_host.file_record(evidence / "reports" / "game-host-result.xml"))

    def test_zero_exit_with_missing_or_failing_report_is_not_success(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        process = mock.Mock(pid=12345, returncode=0)
        for expected in ("missing", "failed"):
            with self.subTest(report=expected):
                if expected == "failed":
                    self.write_mock_report(evidence, success="false")
                with mock.patch.object(game_host.subprocess, "Popen", return_value=process):
                    self.assertEqual(game_host.launch(args, evidence, record), 1)
                saved = json.loads((evidence / "session.json").read_text())
                self.assertEqual(saved["exit_code"], 0)
                self.assertEqual(saved["test_status"], expected)
                if expected == "failed":
                    self.assertEqual(saved["game_host_report"]["sha256"], game_host.file_record(evidence / "reports" / "game-host-result.xml")["sha256"])

    def test_malformed_or_mismatched_report_is_error_and_hashed(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        process = mock.Mock(pid=12345, returncode=0)
        invalid_reports = (
            '<gameHost',
            '<other mode="smoke" gameVersion="1.7.9999" success="true" />',
            '<gameHost mode="benchmark" gameVersion="1.7.9999" success="true" />',
            '<gameHost mode="smoke" gameVersion="1.6.9999" success="true" />',
            '<gameHost mode="smoke" gameVersion="1.7.9998" success="true" />',
            '<gameHost mode="smoke" gameVersion="1.7.9999" />',
        )
        for xml in invalid_reports:
            with self.subTest(xml=xml):
                report_path = evidence / "reports" / "game-host-result.xml"
                write(report_path, xml)
                with mock.patch.object(game_host.subprocess, "Popen", return_value=process):
                    self.assertEqual(game_host.launch(args, evidence, record), 1)
                self.assertEqual(record["test_status"], "error")
                self.assertEqual(record["game_host_report"]["sha256"], game_host.file_record(report_path)["sha256"])

    def test_timeout_terminates_only_owned_process_group(self):
        args = self.args(prepare_only=False)
        evidence, record = game_host.prepare(args)
        process = mock.Mock(pid=23456, returncode=-15)
        process.poll.return_value = None
        with mock.patch.object(game_host.subprocess, "Popen", return_value=process), \
                mock.patch.object(game_host.time, "monotonic", side_effect=[0, 46, 47]), \
                mock.patch.object(game_host.os, "killpg") as killpg:
            self.assertEqual(game_host.launch(args, evidence, record), 1)
        killpg.assert_called_once_with(23456, game_host.signal.SIGTERM)
        self.assertEqual(record["status"], "timeout")

    def test_wrong_version_or_insufficient_artifact_rejected(self):
        with self.assertRaisesRegex(ValueError, "does not match"):
            game_host.prepare(self.args("1.6"))
        write(self.artifact, "not a managed assembly")
        with self.assertRaisesRegex(ValueError, "managed net472"):
            game_host.prepare(self.args())
        self.assertFalse(self.evidence.exists())

    def test_invalid_permissions_and_reused_sessions_rejected(self):
        self.evidence.mkdir(mode=0o755)
        with self.assertRaisesRegex(ValueError, "0700"):
            game_host.prepare(self.args())
        self.evidence.chmod(0o700)
        game_host.prepare(self.args())
        with self.assertRaisesRegex(ValueError, "already contains"):
            game_host.prepare(self.args())

    def test_checkout_evidence_and_path_overrides_rejected(self):
        args = self.args()
        args.evidence_dir = game_host.CHECKOUT / "disallowed-evidence"
        with self.assertRaisesRegex(ValueError, "outside the checkout"):
            game_host.prepare(args)
        with self.assertRaisesRegex(ValueError, "override"):
            game_host.launch_command(self.args(extra=("--game-arg=-savedatafolder=/normal",)), self.evidence)

    def test_nonfinite_timeout_rejected_before_copy(self):
        args = self.args(prepare_only=False)
        args.timeout = float("nan")
        with self.assertRaisesRegex(ValueError, "positive --timeout"):
            game_host.prepare(args)
        self.assertFalse(self.evidence.exists())

    def test_initial_fixture_copy_provenance_and_paths_for_both_versions(self):
        save, checkpoint = self.create_initial_fixture()
        originals = (save.read_bytes(), checkpoint.read_bytes())
        for version in ("1.6", "1.7"):
            with self.subTest(version=version):
                self.create_game(version)
                self.evidence = self.root / f"resume-{version}"
                args = self.args(version, extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint),
                                                 "--game-arg=-eternal-test-persistence=verify"))
                evidence, record = game_host.prepare(args)
                copied_save = evidence / "savedata" / "Saves" / save.name
                copied_checkpoint = evidence / "reports" / "initial-checkpoint.xml"
                self.assertEqual(copied_save.read_bytes(), originals[0])
                self.assertEqual(copied_checkpoint.read_bytes(), originals[1])
                self.assertEqual((save.read_bytes(), checkpoint.read_bytes()), originals)
                self.assertEqual([p.name for p in copied_save.parent.iterdir()], [save.name])
                for flag, path in (("eternal-test-resume", copied_save), ("eternal-test-checkpoint", copied_checkpoint)):
                    expected_path = "Z:" + str(path).replace("/", "\\") if version == "1.6" else str(path)
                    self.assertIn(f"-{flag}={expected_path}", record["command"])
                self.assertIn("-eternal-test-persistence=verify", record["command"])
                provenance = json.loads((evidence / "session.json").read_text())["initial_fixture"]
                for source_key, copied_key, data in (("source_save", "copied_save", originals[0]),
                                                     ("source_checkpoint", "copied_checkpoint", originals[1])):
                    expected_hash = hashlib.sha256(data).hexdigest()
                    self.assertEqual(provenance[source_key]["sha256"], expected_hash)
                    self.assertEqual(provenance[copied_key]["sha256"], expected_hash)
                write(copied_save, "isolated continuation changed the copied save")
                self.assertEqual(save.read_bytes(), originals[0])

    def test_initial_fixture_requires_paired_inputs_and_synthetic_filename(self):
        save, checkpoint = self.create_initial_fixture()
        for option, path in (("--initial-save", save), ("--initial-checkpoint", checkpoint)):
            with self.subTest(option=option), self.assertRaisesRegex(ValueError, "provided together"):
                game_host.prepare(self.args(extra=(option, str(path))))
        normal_save = save.parent / "UnrelatedColony.rws"
        args = self.args(extra=("--initial-save", str(normal_save), "--initial-checkpoint", str(checkpoint)))
        with self.assertRaisesRegex(ValueError, "EternalSynthetic"):
            game_host.prepare(args)
        self.assertFalse(self.evidence.exists())

    def test_initial_checkpoint_identity_and_hash_must_match(self):
        save, checkpoint = self.create_initial_fixture()
        args = self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint)))
        original = checkpoint.read_bytes()
        for field, value, message in (("saveName", "EternalSynthetic-other", "do not match"),
                                      ("saveFileName", "EternalSynthetic-other.rws", "do not match"),
                                      ("sourceSaveSha256", "0" * 64, "sourceSaveSha256"),
                                      ("schemaVersion", "2", "schemaVersion")):
            with self.subTest(field=field):
                root = ET.fromstring(original)
                root.set(field, value)
                ET.ElementTree(root).write(checkpoint, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, message):
                    game_host.prepare(args)
        root = ET.fromstring(original)
        root.tag = "unrelatedCheckpoint"
        ET.ElementTree(root).write(checkpoint, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "persistenceCheckpoint"):
            game_host.prepare(args)
        self.assertFalse(self.evidence.exists())

    def test_initial_checkpoint_without_optional_hash_and_override_rejection(self):
        save, checkpoint = self.create_initial_fixture()
        root = ET.parse(checkpoint).getroot()
        root.attrib.pop("sourceSaveSha256")
        ET.ElementTree(root).write(checkpoint, encoding="utf-8")
        args = self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint)))
        game_host.prepare(args)
        for flag in ("resume", "checkpoint"):
            args.game_arg = [f"-eternal-test-{flag}=/outside-session"]
            with self.assertRaisesRegex(ValueError, "override"):
                game_host.launch_command(args, self.evidence)

    def test_initial_settings_copy_only_listed_eternal_files_and_preserve_fresh_config(self):
        save, checkpoint = self.create_initial_fixture()
        settings = self.create_initial_settings(checkpoint)
        originals = {p.name: p.read_bytes() for p in settings.iterdir()}
        args = self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint),
                                "--initial-settings", str(settings)))
        evidence, record = game_host.prepare(args)
        config = evidence / "savedata" / "Config"
        self.assertEqual({p.name for p in config.iterdir()}, {
            "ModsConfig.xml", "Prefs.xml", "Mod_Eternal_Eternal_Mod.xml", "Eternal_HediffSettings.xml",
        })
        self.assertEqual(ET.parse(config / "Prefs.xml").findtext("screenWidth"), "800")
        self.assertIn("shard.eternal", [n.text for n in ET.parse(config / "ModsConfig.xml").findall("activeMods/li")])
        copied_settings = record["initial_fixture"]["settings"]
        self.assertEqual({s["name"] for s in copied_settings}, {"Mod_Eternal_Eternal_Mod.xml", "Eternal_HediffSettings.xml"})
        for setting in copied_settings:
            expected_hash = hashlib.sha256(originals[setting["name"]]).hexdigest()
            self.assertEqual(setting["source"]["sha256"], expected_hash)
            self.assertEqual(setting["copied"]["sha256"], expected_hash)
            self.assertEqual((config / setting["name"]).read_bytes(), originals[setting["name"]])
        write(config / "Mod_Eternal_Eternal_Mod.xml", "modified isolated settings")
        self.assertEqual({p.name: p.read_bytes() for p in settings.iterdir()}, originals)
        persisted = json.loads((evidence / "session.json").read_text())
        self.assertEqual(persisted["initial_fixture"]["settings"], copied_settings)

    def test_initial_settings_require_fixture_and_checkpoint_list(self):
        save, checkpoint = self.create_initial_fixture()
        directory = checkpoint.parent
        with self.assertRaisesRegex(ValueError, "requires --initial-save"):
            game_host.prepare(self.args(extra=("--initial-settings", str(directory))))
        with self.assertRaisesRegex(ValueError, "settingsFiles"):
            game_host.prepare(self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint),
                                               "--initial-settings", str(directory))))
        self.assertFalse(self.evidence.exists())

    def test_initial_settings_reject_non_eternal_names_duplicate_or_changed_files(self):
        save, checkpoint = self.create_initial_fixture()
        directory = self.create_initial_settings(checkpoint)
        original = checkpoint.read_bytes()
        args = self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint),
                                "--initial-settings", str(directory)))
        for name in ("Prefs.xml", "Credentials.xml", "../Mod_Outside_Eternal_Mod.xml", "Mod_Eternal_Eternal_Mod.xml"):
            with self.subTest(name=name):
                root = ET.fromstring(original)
                ET.SubElement(root.find("settingsFiles"), "file", name=name, sha256="0" * 64)
                ET.ElementTree(root).write(checkpoint, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "duplicate or non-Eternal"):
                    game_host.prepare(args)
        checkpoint.write_bytes(original)
        write(directory / "Eternal_HediffSettings.xml", "unexpected changed settings")
        with self.assertRaisesRegex(ValueError, "hash does not match"):
            game_host.prepare(args)
        self.assertFalse(self.evidence.exists())

    def test_initial_settings_requires_one_mod_file_and_allows_optional_hediff_file(self):
        save, checkpoint = self.create_initial_fixture()
        directory = self.create_initial_settings(checkpoint)
        root = ET.parse(checkpoint).getroot()
        listed = root.find("settingsFiles")
        listed.remove(listed[0])
        ET.ElementTree(root).write(checkpoint, encoding="utf-8")
        args = self.args(extra=("--initial-save", str(save), "--initial-checkpoint", str(checkpoint),
                                "--initial-settings", str(directory)))
        with self.assertRaisesRegex(ValueError, "exactly one"):
            game_host.prepare(args)
        listed.clear()
        path = directory / "Mod_Eternal_Eternal_Mod.xml"
        ET.SubElement(listed, "file", name=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        ET.ElementTree(root).write(checkpoint, encoding="utf-8")
        evidence, _ = game_host.prepare(args)
        self.assertTrue((evidence / "savedata" / "Config" / path.name).is_file())
        self.assertFalse((evidence / "savedata" / "Config" / "Eternal_HediffSettings.xml").exists())

    def test_configured_automation_interval_only_changes_isolated_preferences(self):
        write(self.config, '<gameTest><automation autosaveIntervalDays="1000" /></gameTest>')
        original_config = self.config.read_bytes()
        for version in ("1.6", "1.7"):
            with self.subTest(version=version):
                self.create_game(version)
                original_prefs = self.game / "Config" / "Prefs.xml"
                write(original_prefs, '<PrefsData><autosaveIntervalDays>1</autosaveIntervalDays></PrefsData>')
                self.evidence = self.root / f"automation-{version}"
                evidence, record = game_host.prepare(self.args(version))
                prefs = ET.parse(evidence / "savedata" / "Config" / "Prefs.xml")
                self.assertEqual(float(prefs.findtext("autosaveIntervalDays")), 1000)
                self.assertEqual(ET.parse(original_prefs).findtext("autosaveIntervalDays"), "1")
                self.assertEqual(self.config.read_bytes(), original_config)
                self.assertEqual((evidence / "test-config.xml").read_bytes(), original_config)
                self.assertEqual(record["automation"], {"autosaveIntervalDays": 1000})
                self.assertEqual(json.loads((evidence / "session.json").read_text())["automation"], record["automation"])

    def test_unconfigured_automation_leaves_autosave_preferences_unspecified(self):
        for xml in ("<gameTest />", "<gameTest><automation /></gameTest>"):
            with self.subTest(xml=xml):
                write(self.config, xml)
                self.evidence = self.root / ("no-automation" if "automation" not in xml else "no-interval")
                evidence, record = game_host.prepare(self.args())
                self.assertIsNone(ET.parse(evidence / "savedata" / "Config" / "Prefs.xml").find("autosaveIntervalDays"))
                self.assertEqual(record["automation"], {})

    def test_automation_interval_rejects_invalid_or_overflowing_values_before_copy(self):
        for value in ("", "invalid", "0", "-1", "NaN", "inf", "-inf", "35000.01"):
            with self.subTest(value=value):
                write(self.config, f'<gameTest><automation autosaveIntervalDays="{value}" /></gameTest>')
                with self.assertRaisesRegex(ValueError, "autosaveIntervalDays must be finite"):
                    game_host.prepare(self.args())
                self.assertFalse(self.evidence.exists())
        self.assertEqual(game_host.automation_settings(ET.fromstring('<gameTest><automation autosaveIntervalDays="35000" /></gameTest>')),
                         {"autosaveIntervalDays": 35000})


if __name__ == "__main__":
    unittest.main()
