#!/usr/bin/env python3
"""Author: 0Shard. Independent XML and disposable cancellation regressions."""

import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch

sys.dont_write_bytecode = True
MODULE = Path(os.environ.get("ETERNAL_QUALITY_TEST_MODULE", Path(__file__).with_name("quality_evidence.py")))
spec = importlib.util.spec_from_file_location("quality", MODULE)
quality = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quality)


class ReferenceAssemblyPathTests(unittest.TestCase):
    def setUp(self):
        self.config = {
            "workshop_root_environment": "ETERNAL_FIXTURE_WORKSHOP",
            "workshop_root": "fixture-workshop",
            "optional_assemblies": {"Other": "123/Other.dll"},
            "reference_assemblies": {"RJW": {"path": ".references/pinned/RJW.dll",
                                              "environment": "ETERNAL_FIXTURE_RJW"}}
        }

    def test_default_reference_is_checkout_relative_and_workshop_remains_independent(self):
        with patch.dict(os.environ, {}, clear=True):
            paths = quality.optional_assembly_paths(self.config)
        self.assertEqual((quality.ROOT / ".references/pinned/RJW.dll").resolve(), paths["RJW"])
        self.assertEqual(Path("fixture-workshop/123/Other.dll").resolve(), paths["Other"])

    def test_exact_dll_environment_override_wins_even_when_file_is_absent(self):
        with patch.dict(os.environ, {"ETERNAL_FIXTURE_RJW": "/tmp/absent-rjw/RJW.dll",
                                     "ETERNAL_FIXTURE_WORKSHOP": "/tmp/other-workshop"}, clear=True):
            paths = quality.optional_assembly_paths(self.config)
        self.assertEqual(Path("/tmp/absent-rjw/RJW.dll"), paths["RJW"])
        self.assertEqual(Path("/tmp/other-workshop/123/Other.dll"), paths["Other"])
        self.assertFalse(paths["RJW"].is_file())

    def test_relative_override_is_resolved_against_checkout(self):
        with patch.dict(os.environ, {"ETERNAL_FIXTURE_RJW": "alternate/RJW.dll"}, clear=True):
            paths = quality.optional_assembly_paths(self.config)
        self.assertEqual((quality.ROOT / "alternate/RJW.dll").resolve(), paths["RJW"])

    def test_blank_override_and_duplicate_source_fail_explicitly(self):
        with patch.dict(os.environ, {"ETERNAL_FIXTURE_RJW": " "}, clear=True):
            with self.assertRaisesRegex(ValueError, "Empty reference assembly path: RJW"):
                quality.optional_assembly_paths(self.config)
        self.config["optional_assemblies"]["RJW"] = "123/RJW.dll"
        with self.assertRaisesRegex(ValueError, "Duplicate optional assembly configuration: RJW"):
            quality.optional_assembly_paths(self.config)


class RuntimeTargetCaptureTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="eternal-runtime-fixture-")
        self.addCleanup(self.directory.cleanup)
        self.base = Path(self.directory.name)
        self.config = {
            "workshop_root_environment": "ETERNAL_FIXTURE_WORKSHOP",
            "workshop_root": str(self.base / "workshop"),
            "optional_checks_environment": "ETERNAL_FIXTURE_POLICY",
            "optional_checks": "report_unavailable",
            "runtime_targets": {"1.7": {
                "root": str(self.base / "game"), "root_environment": "ETERNAL_FIXTURE_RUNTIME",
                "version_file": "Version.txt", "expected_version": "1.7.4977 rev926",
                "managed_directory": ".", "assembly_patterns": ["Assembly-CSharp.dll", "UnityStubs.dll"],
                "framework_patterns": ["mscorlib.dll"], "harmony": "123/1.7/0Harmony.dll",
                "harmony_environment": "ETERNAL_FIXTURE_HARMONY", "expected_harmony_version": "3.0.0.0",
                "unity_redirect": "UnityStubs"}}
        }

    def populate(self, root=None, harmony=None, version="1.7.4977 rev926"):
        root = root or self.base / "game"
        root.mkdir(parents=True)
        (root / "Version.txt").write_text(version + "\n")
        for file in ("Assembly-CSharp.dll", "UnityStubs.dll", "mscorlib.dll"):
            (root / file).write_bytes(file.encode())
        harmony = harmony or self.base / "workshop/123/1.7/0Harmony.dll"
        harmony.parent.mkdir(parents=True)
        harmony.write_bytes(b"fixture harmony")

    def capture(self, environment=None):
        with patch.dict(os.environ, environment or {}, clear=True):
            return quality.runtime_target_capture(self.config)

    def test_exact_runtime_paths_hashes_and_version_are_separate_from_compiler(self):
        self.populate()
        capture = self.capture()
        target = capture.find("runtime")
        self.assertEqual("available", target.get("status"))
        self.assertEqual("1.7.4977 rev926", target.find("versionFile").get("text"))
        self.assertEqual("UnityStubs", target.get("unityRedirect"))
        self.assertEqual([], target.findall("reference"))
        for row in target:
            self.assertEqual(quality.digest(Path(row.get("path"))), row.get("sha256"))
        self.assertEqual(([], []), quality.runtime_capture_outcomes(capture))

    def test_environment_overrides_root_and_exact_harmony_independently(self):
        root, harmony = self.base / "alternate", self.base / "another/0Harmony.dll"
        self.populate(root, harmony)
        capture = self.capture({"ETERNAL_FIXTURE_RUNTIME": str(root), "ETERNAL_FIXTURE_HARMONY": str(harmony)})
        target = capture.find("runtime")
        self.assertEqual(str(root), target.get("root"))
        self.assertEqual(str(harmony), target.find("harmony").get("path"))
        self.assertEqual("available", target.get("status"))

    def test_missing_inputs_report_unavailable_and_require_policy_fails(self):
        capture = self.capture()
        unavailable, failures = quality.runtime_capture_outcomes(capture)
        self.assertEqual(1, len(unavailable))
        self.assertEqual([], failures)
        self.assertEqual(5, len(unavailable[0]["paths"]))
        capture = self.capture({"ETERNAL_FIXTURE_POLICY": "require_installed"})
        self.assertEqual(1, len(quality.runtime_capture_outcomes(capture)[1]))

    def test_known_version_mismatch_fails_even_with_missing_other_inputs(self):
        self.populate(version="1.6.4566 rev606")
        (self.base / "game/UnityStubs.dll").unlink()
        capture = self.capture()
        self.assertEqual("mismatch", capture.find("runtime").get("status"))
        unavailable, failures = quality.runtime_capture_outcomes(capture)
        self.assertEqual(1, len(unavailable))
        self.assertEqual(1, len(failures))
        self.assertIn("expected 1.7.4977 rev926", failures[0]["error"])

    def test_blank_paths_or_unknown_policy_fail(self):
        for variable in ("ETERNAL_FIXTURE_RUNTIME", "ETERNAL_FIXTURE_HARMONY"):
            with self.assertRaisesRegex(ValueError, "Empty runtime"):
                self.capture({variable: " "})
        with self.assertRaisesRegex(ValueError, "Unknown optional_checks"):
            self.capture({"ETERNAL_FIXTURE_POLICY": "ignore"})

    def test_metadata_matrix_failure_and_unavailability_do_not_become_runtime_pass(self):
        matrix = ET.Element("runtimeMatrix", configuration="Debug", version="1.7", status="metadata_fail")
        signatures = ET.SubElement(matrix, "signatures")
        ET.SubElement(signatures, "method", status="mismatch")
        patches = ET.SubElement(matrix, "harmonyTargets")
        ET.SubElement(patches, "annotated")
        ET.SubElement(patches, "dynamic", status="runtime_unavailable")
        ET.ElementTree(matrix).write(self.base / "runtime-matrix-Debug-1.7.xml")
        result = {"process_failures": [], "unavailable": []}
        rows = quality.collect_runtime_matrices(self.base, "Debug", result)
        self.assertEqual("unavailable", rows[0]["runtime_proof"])
        self.assertEqual(1, rows[0]["signatures"])
        self.assertEqual(1, rows[0]["dynamic_patches"])
        self.assertEqual(1, len(result["process_failures"]))
        self.assertEqual(1, len(result["unavailable"]))

    def test_artifact_guard_includes_both_shared_dll_locations(self):
        config = json.loads((MODULE.parents[1] / "quality-check.config.json").read_text())
        self.assertIn("Eternal/1.6/Assemblies", config["artifact_roots"])
        self.assertIn("Eternal/1.7/Assemblies", config["artifact_roots"])
        framework = config["runtime_targets"]["1.7"]["framework_patterns"]
        self.assertIn("System.Collections.dll", framework)
        self.assertIn("System.Security.Cryptography.dll", framework)


class ClassDiscoveryTests(unittest.TestCase):
    def parse(self, rows, requested):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "tests.xml"
            path.write_text("<assemblies><assembly><collection>" + rows + "</collection></assembly></assemblies>")
            return quality.parse_tests(path, requested)

    def test_one_present_class_cannot_hide_missing_class(self):
        result = self.parse('<test type="Fixture.Present" name="Fixture.Present.Case" result="Pass"/>',
                            ["Fixture.Present", "Fixture.Missing"])
        self.assertEqual(["Fixture.Missing"], result["missing_classes"])
        self.assertEqual(1, result["classes"]["Fixture.Present"]["passed"])

    def test_every_requested_class_discovered_with_outcomes(self):
        result = self.parse('<test type="Fixture.A" name="Fixture.A.Case" result="Pass"/>'
                            '<test type="Fixture.B" name="Fixture.B.Case" result="Fail"><failure exception-type="Xunit.Sdk.TrueException"/></test>',
                            ["Fixture.A", "Fixture.B"])
        self.assertEqual([], result["missing_classes"])
        self.assertEqual(1, result["classes"]["Fixture.B"]["failed"])
        self.assertEqual("Fixture.B", result["test_cases"][1]["class"])

    def test_skip_is_discovered_but_never_reported_as_pass(self):
        result = self.parse('<test type="Fixture.Optional" name="Fixture.Optional.Case" result="Skip"><reason>unavailable</reason></test>',
                            ["Fixture.Optional", "Fixture.Missing"])
        self.assertEqual(["Fixture.Missing"], result["missing_classes"])
        self.assertEqual({"discovered": 1, "passed": 0, "failed": 0, "skipped": 1}, result["classes"]["Fixture.Optional"])
        self.assertEqual("unavailable", result["skipped"][0]["reason"])

    def test_missing_malformed_zero_discovery_still_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "tests.xml"
            with self.assertRaises(ValueError):
                quality.parse_tests(path)
            path.write_text("<bad")
            with self.assertRaises(Exception):
                quality.parse_tests(path)
            path.write_text("<assemblies><assembly/></assemblies>")
            with self.assertRaises(ValueError):
                quality.parse_tests(path)


class CancellationTests(unittest.TestCase):
    def wait_file(self, path, process):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline and not path.exists() and process.poll() is None:
            time.sleep(0.02)
        self.assertTrue(path.exists(), f"Child never reached {path.name}; resolver exit {process.poll()}")

    def cancel(self, mode, signum):
        with tempfile.TemporaryDirectory(prefix="eternal-cancel-fixture-") as directory:
            base = Path(directory)
            root = base / "repo"
            output = root / "out/Debug/net472/Eternal.dll"
            output.parent.mkdir(parents=True)
            output.write_bytes(b"original output")
            output.chmod(0o640)
            evidence = base / "evidence"
            evidence.mkdir(mode=0o700)
            sdk = base / "sdk/fake/Sdks/Microsoft.NET.Sdk"
            for relative in ("analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll", "analyzers/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll",
                             "codestyle/cs/Microsoft.CodeAnalysis.CodeStyle.dll", "codestyle/cs/Microsoft.CodeAnalysis.CSharp.CodeStyle.dll"):
                path = sdk / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
            rules = ["IDE0005", "IDE0051", "IDE0052", "IDE0059", "CA1806", "CA2200"]
            config = {"artifact_roots": ["out"], "test_output": "out/{configuration}/net472/Eternal.Tests.dll",
                      "production_project": "Eternal.csproj", "solution": "Eternal.sln", "style_diagnostics": rules[:4],
                      "analyzer_diagnostics": rules[4:], "optional_checks_environment": "UNUSED_OPTIONAL_ENV",
                      "optional_checks": "report_unavailable", "workshop_root_environment": "UNUSED_WORKSHOP_ENV",
                      "workshop_root": str(base), "optional_assemblies": {}}
            (root / "quality-check.config.json").write_text(json.dumps(config))
            child = base / "dotnet"
            child.write_text("#!/usr/bin/env python3\nimport os,sys,time\nfrom pathlib import Path\n"
                             f"base=Path({str(base)!r})\noutput=Path({str(output)!r})\n"
                             "if '--list-sdks' in sys.argv:\n print('fake ['+str(base/'sdk')+']'); sys.exit(0)\n"
                             "if any('csc.dll' in a for a in sys.argv):\n"
                             f" print('\\n'.join('warning '+r+': fixture' for r in {rules!r})); sys.exit(0)\n"
                             "output.write_bytes(b'transient child output');output.chmod(0o600)\n"
                             "(base/'ready').write_text(str(os.getpid()))\n"
                             "while True:\n output.write_bytes(b'writer alive');time.sleep(.02)\n")
            child.chmod(0o700)
            harness = base / "harness.py"
            harness.write_text("import importlib.util,sys,time\nfrom pathlib import Path\n"
                               "sys.dont_write_bytecode=True\n"
                               f"spec=importlib.util.spec_from_file_location('quality',{str(MODULE)!r})\n"
                               "q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)\n"
                               f"q.ROOT=Path({str(root)!r})\n"
                               "q.inventory=lambda c: {'tooling': {'inventory': []}}\n"
                               "q.prerequisites=lambda c: ('unused',{'sdk':'fake','reference_mscorlib':{'path':'unused'}})\n"
                               "check_output=q.subprocess.check_output\n"
                               "q.subprocess.check_output=lambda command,**kw: 'fixture\\n' if command[0]=='git' else check_output(command,**kw)\n"
                               "restore=q.ArtifactSnapshot.restore\n"
                               f"def slow_restore(self,evidence):\n Path({str(base / 'restoring')!r}).touch();time.sleep(.5);restore(self,evidence)\n"
                               "q.ArtifactSnapshot.restore=slow_restore\n"
                               "sys.exit(q.main())\n")
            arguments = ["manifest", "--artifact", str(output)] if mode == "manifest" else ["run", "focused", "--evidence-dir", str(evidence)]
            with (base / "resolver.log").open("w") as log:
                process = subprocess.Popen([sys.executable, str(harness), *arguments], stdout=log, stderr=log,
                                           env={**os.environ, "PATH": str(base) + os.pathsep + os.environ["PATH"]})
                writer = None
                try:
                    self.wait_file(base / "ready", process)
                    writer = int((base / "ready").read_text())
                    process.send_signal(signum)
                    self.wait_file(base / "restoring", process)
                    # An additional terminating signal must not interrupt restore.
                    process.send_signal(signal.SIGTERM)
                    contender = subprocess.run([sys.executable, str(harness), *arguments], stdout=subprocess.PIPE,
                                               stderr=subprocess.STDOUT, text=True, timeout=10,
                                               env={**os.environ, "PATH": str(base) + os.pathsep + os.environ["PATH"]})
                    self.assertNotEqual(0, contender.returncode)
                    self.assertIn("already running", contender.stdout)
                    status = process.wait(timeout=10)
                    self.assertNotEqual(0, status)
                    self.assertEqual(b"original output", output.read_bytes())
                    self.assertEqual(0o640, output.stat().st_mode & 0o777)
                    with self.assertRaises(ProcessLookupError):
                        os.kill(writer, 0)
                    time.sleep(.1)
                    self.assertEqual(b"original output", output.read_bytes())
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.wait()
                    if writer is not None:
                        try:
                            os.kill(writer, signal.SIGKILL)
                        except ProcessLookupError:
                            writer = None

    def test_manifest_sigterm_restores_after_join_and_holds_lock(self):
        self.cancel("manifest", signal.SIGTERM)

    def test_manifest_sighup_restores_after_join_and_holds_lock(self):
        self.cancel("manifest", signal.SIGHUP)

    def test_run_sigterm_uses_same_protected_lifecycle(self):
        self.cancel("run", signal.SIGTERM)


if __name__ == "__main__":
    unittest.main()
