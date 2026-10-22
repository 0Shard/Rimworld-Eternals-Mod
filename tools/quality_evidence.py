#!/usr/bin/env python3
"""Author: 0Shard. Local SDK/Mono checks with preserved evidence and outputs."""

import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


class ProtectedExecution:
    """One cancellation lifecycle for every mutable SDK subprocess and restoration."""

    def __init__(self):
        self.cancelled = None
        self.deferred = False
        self.handlers = {}
        self.last_status = None

    def interrupted(self, signum, _frame):
        if self.cancelled is not None:
            return  # One cancellation owns cleanup; later signals cannot interrupt its transition.
        self.cancelled = signum
        if not self.deferred:
            raise InterruptedError(f"Interrupted by signal {self.cancelled}")

    def __enter__(self):
        for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            self.handlers[signum] = signal.signal(signum, self.interrupted)
        return self

    def __exit__(self, kind, _value, _traceback):
        for signum, handler in self.handlers.items():
            signal.signal(signum, handler)
        if kind is None and self.cancelled:
            raise InterruptedError(f"Interrupted by signal {self.cancelled}")

    @contextmanager
    def defer_cancellation(self):
        previous = self.deferred
        self.deferred = True
        try:
            yield
        finally:
            self.deferred = previous

    @staticmethod
    def signal_group(process, signum):
        try:
            os.killpg(process.pid, signum)
            return True
        except ProcessLookupError:
            return False  # The entire group has already exited.

    def run(self, command, output, environment=None):
        self.last_status = None
        # Defer delivery until Popen returns so a signal cannot orphan a child
        # between OS creation and registration of its process handle.
        with self.defer_cancellation():
            process = subprocess.Popen(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT,
                                       start_new_session=True, env={**os.environ, **(environment or {})})
        try:
            if self.cancelled:
                raise InterruptedError(f"Interrupted by signal {self.cancelled}")
            self.last_status = process.wait()
            return self.last_status
        except BaseException:
            with self.defer_cancellation():
                self.signal_group(process, signal.SIGTERM)
                try:
                    self.last_status = process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.signal_group(process, signal.SIGKILL)
                    self.last_status = process.wait()
                # A descendant can outlive the direct child; no writer in the
                # owned group may survive into artifact restoration.
                self.signal_group(process, signal.SIGKILL)
            raise


@contextmanager
def checkout_execution_lock():
    """Keep snapshot/build/restore exclusive even when callers choose different evidence roots."""
    directory = Path(tempfile.gettempdir()) / f"eternal-quality-locks-{os.getuid()}"
    directory.mkdir(mode=0o700, exist_ok=True)
    info = directory.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o700:
        raise ValueError(f"Unsafe quality lock directory: {directory}")
    identity = hashlib.sha256(str(ROOT.resolve()).encode()).hexdigest()
    path = directory / f"{identity}.lock"
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        info = os.fstat(descriptor)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) != 0o600:
            raise ValueError(f"Unsafe quality lock file: {path}")
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise ValueError("Quality check already running for this checkout; no snapshot/build started") from error
        print(f"Checkout quality lock acquired: {ROOT}", flush=True)
        try:
            yield
        finally:
            fcntl.flock(descriptor, fcntl.LOCK_UN)
            print("Checkout quality lock released", flush=True)
    finally:
        os.close(descriptor)


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def optional_assembly_paths(config):
    """Resolve Workshop defaults and exact reference DLL overrides from one configuration."""
    workshop = Path(os.environ.get(config["workshop_root_environment"], config["workshop_root"])).expanduser()
    paths = {name: (workshop / relative).resolve() for name, relative in config["optional_assemblies"].items()}
    for name, setting in config.get("reference_assemblies", {}).items():
        if name in paths:
            raise ValueError(f"Duplicate optional assembly configuration: {name}")
        value = os.environ.get(setting["environment"], setting["path"])
        if not value.strip():
            raise ValueError(f"Empty reference assembly path: {name}")
        path = Path(value).expanduser()
        paths[name] = (path if path.is_absolute() else ROOT / path).resolve()
    return paths


def runtime_target_capture(config):
    """Capture installed runtime inputs independently of the compiler reference set."""
    workshop = Path(os.environ.get(config["workshop_root_environment"], config["workshop_root"])).expanduser()
    policy = os.environ.get(config["optional_checks_environment"], config["optional_checks"])
    if policy not in ("report_unavailable", "require_installed"):
        raise ValueError("Unknown optional_checks policy")
    capture = ET.Element("runtimeTargets", schema="1", unavailablePolicy=policy)
    for name, setting in config.get("runtime_targets", {}).items():
        value = os.environ.get(setting["root_environment"], setting["root"])
        if not value.strip():
            raise ValueError(f"Empty runtime root: {name}")
        root = Path(value).expanduser()
        root = (root if root.is_absolute() else ROOT / root).resolve()
        target = ET.SubElement(capture, "runtime", version=name, root=str(root),
                               expectedVersion=setting["expected_version"],
                               expectedHarmonyVersion=setting["expected_harmony_version"],
                               unityRedirect=setting.get("unity_redirect", ""))
        missing = []

        def file_row(kind, path):
            row = ET.SubElement(target, kind, path=str(path.resolve()))
            if path.is_file():
                row.set("sha256", digest(path))
            else:
                missing.append(str(path))
            return row

        version_path = root / setting["version_file"]
        version_row = file_row("versionFile", version_path)
        if version_path.is_file():
            actual = version_path.read_text().strip()
            version_row.set("text", actual)
            if actual != setting["expected_version"]:
                target.set("mismatch", f"Version.txt: expected {setting['expected_version']}, found {actual}")
        managed = root / setting["managed_directory"]
        for kind, patterns in (("assembly", setting["assembly_patterns"]),
                               ("framework", setting.get("framework_patterns", []))):
            for pattern in patterns:
                paths = sorted(managed.glob(pattern))
                if not paths:
                    missing.append(str(managed / pattern))
                for path in paths:
                    file_row(kind, path)
        value = os.environ.get(setting["harmony_environment"], str(workshop / setting["harmony"]))
        if not value.strip():
            raise ValueError(f"Empty runtime Harmony path: {name}")
        harmony = Path(value).expanduser()
        file_row("harmony", harmony if harmony.is_absolute() else ROOT / harmony)
        if target.get("mismatch"):
            target.set("status", "mismatch")
        else:
            target.set("status", "unavailable" if missing else "available")
        for path in missing:
            ET.SubElement(target, "missing", path=path)
    return capture


def runtime_capture_outcomes(capture):
    """Missing inputs remain unavailable; known version mismatches always fail."""
    unavailable, failures = [], []
    required = capture.get("unavailablePolicy") == "require_installed"
    for target in capture.findall("runtime"):
        name = f"runtime-{target.get('version')}-metadata"
        if target.get("status") == "mismatch":
            failures.append({"name": name, "error": target.get("mismatch")})
        if target.findall("missing"):
            row = {"check": name, "reason": "runtime inputs absent", "paths": [r.get("path") for r in target.findall("missing")]}
            unavailable.append(row)
            if required:
                failures.append({"name": name, "error": "required runtime inputs absent"})
    return unavailable, failures


def collect_runtime_matrices(evidence, configuration, result):
    """Expose persisted metadata status separately from xUnit execution and game proof."""
    rows = []
    for path in sorted(evidence.glob(f"runtime-matrix-{configuration}-*.xml")):
        matrix = ET.parse(path).getroot()
        if matrix.tag != "runtimeMatrix" or matrix.get("configuration") != configuration:
            raise ValueError(f"Malformed runtime matrix: {path.name}")
        signatures = matrix.find("signatures")
        patches = matrix.find("harmonyTargets")
        row = {"configuration": configuration, "version": matrix.get("version"), "status": matrix.get("status"),
               "path": str(path), "sha256": digest(path),
               "signatures": len([r for r in signatures if r.tag != "clrSyntheticMember"]) if signatures is not None else 0,
               "clr_synthetic_members": len(signatures.findall("clrSyntheticMember")) if signatures is not None else 0,
               "annotated_patch_methods": len(patches.findall("annotated")) if patches is not None else 0,
               "dynamic_patches": len(patches.findall("dynamic")) if patches is not None else 0,
               "runtime_proof": "unavailable"}
        rows.append(row)
        if row["status"] not in ("metadata_pass", "metadata_fail", "unavailable"):
            raise ValueError(f"Unknown runtime matrix status: {path.name}")
        if row["status"] == "metadata_fail":
            result["process_failures"].append({"name": f"runtime-{row['version']}-{configuration}",
                                               "error": "runtime metadata mismatch", "evidence": str(path)})
        result["unavailable"].append({"check": f"runtime-{row['version']}-game-host",
                                      "reason": "metadata does not prove runtime loading, patch installation, or behavior"})
    result.setdefault("runtime_matrices", []).extend(rows)
    return rows


def compiled_manifest(config, evidence, configuration, identities, artifact):
    """Bind the loaded artifact to compiler items emitted by this exact MSBuild invocation."""
    raw = evidence / f"{configuration}-references.txt"
    if not raw.is_file():
        raise ValueError("MSBuild did not emit its actual Csc reference items")
    production = identities["production_project"]
    pinned = "Krafs.Rimworld.Ref/" + production["packages"]["Krafs.Rimworld.Ref"]
    assets_references = {str(Path(r["path"]).resolve()): r for r in production["compile_references"]}
    root = ET.Element("compiledBuild", schema="1", configuration=configuration,
                      rimworldPackage=pinned, optionalPolicy=os.environ.get(config["optional_checks_environment"], config["optional_checks"]))
    def entry(kind, path, **attributes):
        path = Path(path).resolve()
        if not path.is_file():
            raise ValueError(f"Missing manifest input: {path}")
        return ET.SubElement(root, kind, path=str(path), sha256=digest(path), **attributes)
    entry("project", ROOT / config["production_project"])
    entry("assets", (ROOT / config["production_project"]).parent / "obj/project.assets.json")
    identity_file = evidence / f"{configuration}-artifact.txt"
    if not identity_file.is_file() or not identity_file.read_text(encoding="utf-8-sig").strip():
        raise ValueError("MSBuild did not emit the actual artifact assembly identity")
    entry("artifact", artifact, identity=identity_file.read_text(encoding="utf-8-sig").strip())
    identity_rows = evidence / f"{configuration}-reference-identities.txt"
    if not identity_rows.is_file():
        raise ValueError("MSBuild did not emit complete compiler reference identities")
    lines = raw.read_text(encoding="utf-8-sig").splitlines()
    resolved_identities = identity_rows.read_text(encoding="utf-8-sig").splitlines()
    if len(lines) != len(resolved_identities) or any(not identity.strip() for identity in resolved_identities):
        raise ValueError("Compiler reference paths/identities count mismatch or empty identity")
    game_rows = []
    for line, resolved_identity in zip(lines, resolved_identities):
        fields = line.split("|")
        if len(fields) != 4:
            raise ValueError("Malformed compiler reference row")
        path, identity, package_id, package_version = fields
        path = str(Path(path).resolve())
        package = assets_references.get(path, {}).get("package", "")
        if package_id and package_version and package and package.lower() != f"{package_id}/{package_version}".lower():
            raise ValueError(f"Resolved package metadata disagrees with project.assets: {path}")
        if identity and identity != resolved_identity:
            raise ValueError(f"Compiler FusionName differs from resolved assembly identity: {path}")
        attributes = {"package": package, "identity": resolved_identity}
        row = entry("reference", path, **attributes)
        if Path(path).name == "Assembly-CSharp.dll":
            game_rows.append(row)
    if len(game_rows) != 1 or game_rows[0].get("package") != pinned or not game_rows[0].get("identity"):
        raise ValueError("Actual compiler game reference must match exactly one pinned, identified project.assets item")
    # Keep the SDK's raw item set independent of the supplied manifest. Consumers
    # locate this companion by configuration, never by a manifest-controlled path.
    authority = ET.Element("compilerCapture", configuration=configuration, rimworldPackage=pinned)
    for kind in ("project", "assets", "artifact"):
        authority.append(ET.fromstring(ET.tostring(root.find(kind))))
    for kind, path in (("items", raw), ("identities", identity_rows)):
        ET.SubElement(authority, kind, path=str(path.resolve()), sha256=digest(path))
    authority_path = evidence / f"compiler-capture-{configuration}.xml"
    ET.ElementTree(authority).write(authority_path, encoding="utf-8", xml_declaration=True)
    entry("capture", authority_path)
    if root.get("optionalPolicy") not in ("report_unavailable", "require_installed"):
        raise ValueError("Unknown optional_checks policy")
    for name, path in optional_assembly_paths(config).items():
        ET.SubElement(root, "optional", name=name, path=str(path))
    # Runtime files are never inserted into the canonical compiler capture.
    root.append(runtime_target_capture(config))
    target = evidence / f"compiled-build-{configuration}.xml"
    ET.ElementTree(root).write(target, encoding="utf-8", xml_declaration=True)
    return target


def inventory(config):
    """The same category/exclusion rules are used before and after changes."""
    result = {}
    for category, directory in config["owned_scopes"].items():
        files = sorted(path for path in (ROOT / directory).rglob("*.cs")
                       if not set(path.relative_to(ROOT).parts) & set(config["excluded_components"]))
        if category == "production":
            files = [path for path in files if "InGameTests" not in path.parts]
        rows = [{"path": str(path.relative_to(ROOT)), "lines": len(path.read_text().splitlines()),
                 "sha256": digest(path)} for path in files]
        result[category] = {"files": len(rows), "lines": sum(row["lines"] for row in rows), "inventory": rows}
    tooling = sorted(set(ROOT.glob("*.sh")) | set(ROOT.glob("*.json")) |
                     set(ROOT.glob(".editorconfig")) | set((ROOT / "tools").glob("*.py")) |
                     set((ROOT / "Eternal/Source").glob("*.props")) |
                     set((ROOT / "Eternal/Source").glob("*.targets")) |
                     set((ROOT / "Eternal/Source").glob("*.sh")) |
                     set((ROOT / "Eternal/Source").glob("*.sln")) |
                     set((ROOT / "Eternal/Source").rglob("*.csproj")) |
                     set((ROOT / ".github/workflows").glob("*.yml")) |
                     set((ROOT / "Eternal/Defs").rglob("*.xml")) |
                     set((ROOT / "Eternal/Patches").rglob("*.xml")) |
                     set((ROOT / "Eternal/About").glob("*.xml")) |
                     set((ROOT / "Eternal/Languages").rglob("*.xml")))
    tooling = [p for p in tooling if not set(p.relative_to(ROOT).parts) & set(config["excluded_components"])]
    result["tooling"] = {"files": len(tooling), "lines": sum(len(p.read_text().splitlines()) for p in tooling),
                         "inventory": [{"path": str(p.relative_to(ROOT)), "sha256": digest(p)} for p in tooling]}
    return result


def artifact_files(config):
    paths = set()
    for directory in config["artifact_roots"]:
        base = ROOT / directory
        if ROOT not in base.resolve().parents:
            raise ValueError(f"Build output escapes repository: {directory}")
        if base.exists():
            paths.update(p for p in base.rglob("*") if p.is_file() or p.is_symlink())
    return sorted(paths)


class ArtifactSnapshot:
    """Preserve current working bytes, including pre-existing untracked outputs."""

    def __init__(self, config, evidence):
        self.config = config
        self.directory = evidence / "original-artifacts"
        self.directory.mkdir()
        self.before = {}
        for path in artifact_files(config):
            if path.is_symlink():
                raise ValueError(f"Refusing symlinked build output: {path.relative_to(ROOT)}")
            relative = path.relative_to(ROOT)
            target = self.directory / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            self.before[str(relative)] = {"sha256": digest(path), "mode": stat.S_IMODE(path.stat().st_mode)}
        write_json(evidence / "artifact-before.json", self.before)

    def restore(self, evidence):
        for path in artifact_files(self.config):
            relative = str(path.relative_to(ROOT))
            if relative not in self.before:
                target = evidence / "created-artifacts" / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                path.unlink()
        for relative in self.before:
            target = ROOT / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(self.directory / relative, target)
        after = {relative: {"sha256": digest(ROOT / relative),
                            "mode": stat.S_IMODE((ROOT / relative).stat().st_mode)} for relative in self.before}
        write_json(evidence / "artifact-after.json", after)
        if after != self.before:
            raise RuntimeError("Artifact restoration mismatch")


def prerequisites(config):
    for executable in ("dotnet", "mono"):
        if not shutil.which(executable):
            raise ValueError(f"Missing prerequisite: {executable}")
    identities = {}
    runner = None
    for name in ("production_project", "test_project"):
        project = ROOT / config[name]
        assets_path = project.parent / "obj/project.assets.json"
        if not assets_path.is_file():
            raise ValueError(f"Missing restored assets: {assets_path.relative_to(ROOT)}; dependency installation is not performed")
        assets = json.loads(assets_path.read_text())
        references = {item.attrib["Include"]: item.attrib.get("Version")
                      for item in ET.parse(project).iter("PackageReference")}
        for package, version in references.items():
            if f"{package}/{version}" not in assets["libraries"]:
                raise ValueError(f"Assets do not match pinned {package}/{version} in {config[name]}")
        identities[name] = {"project": config[name], "assets_sha256": digest(assets_path),
                            "packages": references, "package_folders": list(assets["packageFolders"])}
        compile_references = []
        for target in assets["targets"].values():
            for library_name, library_target in target.items():
                library = assets["libraries"][library_name]
                if library.get("type") != "package":
                    continue
                for relative in library_target.get("compile", {}):
                    if not relative.endswith(".dll"):
                        continue
                    candidates = [Path(folder) / library["path"] / relative for folder in assets["packageFolders"]]
                    actual = next((p for p in candidates if p.is_file()), None)
                    if actual is None:
                        raise ValueError(f"Restored compile reference missing: {library_name}/{relative}")
                    compile_references.append({"package": library_name, "path": str(actual), "sha256": digest(actual)})
        identities[name]["compile_references"] = compile_references
        if name == "production_project":
            for library_name, library in assets["libraries"].items():
                if library_name.startswith("Microsoft.NETFramework.ReferenceAssemblies.net472/"):
                    relative = next((f for f in library["files"] if f.endswith("/mscorlib.dll")), None)
                    candidates = [Path(folder) / library["path"] / relative for folder in assets["packageFolders"]] if relative else []
                    actual = next((p for p in candidates if p.is_file()), None)
                    if actual:
                        identities["reference_mscorlib"] = {"path": str(actual), "sha256": digest(actual)}
        if name == "test_project":
            version = references["xunit.runner.console"]
            library = assets["libraries"][f"xunit.runner.console/{version}"]
            relative = Path(library["path"]) / "tools" / config["runner_framework"] / "xunit.console.exe"
            candidates = [Path(folder) / relative for folder in assets["packageFolders"]]
            runner = next((p for p in candidates if p.is_file()), None)
            if runner is None:
                raise ValueError(f"Pinned xUnit console runner missing in assets package folders: {relative}")
            identities["runner"] = {"path": str(runner), "sha256": digest(runner)}
    identities["sdk"] = subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip()
    identities["mono"] = subprocess.check_output(["mono", "--version"], cwd=ROOT, text=True).splitlines()[0]
    return runner, identities


def parse_tests(path, requested_classes=None):
    if not path.is_file():
        raise ValueError(f"Missing xUnit XML: {path.name}")
    root = ET.parse(path).getroot()
    tests = root.findall(".//test")
    if root.tag != "assemblies" or not tests:
        raise ValueError(f"Invalid xUnit XML or zero discovery: {path.name}")
    failures, skipped, cases, classes = [], [], [], {}
    for test in tests:
        name, test_class, outcome = test.get("name"), test.get("type"), test.get("result")
        if not name or not test_class or outcome not in ("Pass", "Fail", "Skip"):
            raise ValueError(f"Malformed xUnit test identity/outcome in {path.name}")
        cases.append({"name": name, "class": test_class, "outcome": outcome})
        counts = classes.setdefault(test_class, {"discovered": 0, "passed": 0, "failed": 0, "skipped": 0})
        counts["discovered"] += 1
        counts[{"Pass": "passed", "Fail": "failed", "Skip": "skipped"}[outcome]] += 1
        if test.get("result") == "Fail":
            failure = test.find("failure")
            if failure is None or not failure.get("exception-type"):
                raise ValueError(f"Malformed failure in {path.name}")
            failures.append({"name": test.get("name"), "exception_type": failure.get("exception-type")})
        elif test.get("result") == "Skip":
            skipped.append({"name": test.get("name"), "reason": "".join(test.find("reason").itertext())
                            if test.find("reason") is not None else "unspecified"})
    errors = [{"type": error.get("type"), "message": "".join(error.itertext())} for error in root.findall(".//error")]
    missing = sorted(set(requested_classes or []) - classes.keys())
    if missing:
        errors.append({"type": "InfrastructureFailure", "message": "Requested test classes not discovered: " + ", ".join(missing)})
    loader_failures = [failure for failure in failures if failure["exception_type"] in
                       ("System.TypeLoadException", "System.BadImageFormatException", "System.FileLoadException")]
    return {"discovered": len(tests), "failures": failures, "skipped": skipped,
            "loader_failures": loader_failures, "infrastructure_errors": errors,
            "test_cases": cases, "classes": classes, "missing_classes": missing}


def diagnostic_member(path, line):
    # Preserve member text when available; line numbers remain locations, never identities.
    lines = path.read_text().splitlines() if path.is_file() else []
    if 0 < line <= len(lines) and lines[line - 1].strip().startswith("using "):
        return lines[line - 1].strip().rstrip(";")
    signature = re.compile(r"\b(?:public|private|protected|internal)\b.*?\b(\w+)\s*(?:\(|=>|[;{=])")
    for text in reversed(lines[:line]):
        match = signature.search(text)
        if match:
            return match.group(1)
    return "<file>"


def parse_diagnostics(evidence):
    identities = {}
    pattern = re.compile(r"^(.*\.cs)\((\d+),\d+\):\s*(warning|error)\s+([A-Z]+\d+):\s*(.*?)(?:\s+\[.*\])?$", re.M)
    for log in evidence.glob("*.log"):
        for filename, line, severity, rule, message in pattern.findall(log.read_text(errors="replace")):
            path = Path(filename)
            if not path.is_absolute():
                path = ROOT / path
            try:
                relative = str(path.relative_to(ROOT))
            except ValueError:
                continue  # Support probes and package sources are outside the owned scope.
            row = {"id": rule, "file": relative, "member": diagnostic_member(path, int(line)),
                   "message": re.sub(r"\s*\(https://learn\.microsoft\.com/[^)]*\)$", "", message), "severity": severity}
            named = re.search(r"Private member '([^']+)'", row["message"])
            if named:
                row["member"] = named.group(1)
            identities[json.dumps(row, sort_keys=True)] = row
    return sorted(identities.values(), key=lambda row: (row["id"], row["file"], row["member"], row["message"]))


def comparison(before, after):
    def keyed(values):
        return {json.dumps(v, sort_keys=True): v for v in values}
    result = {}
    for category in ("failures", "diagnostics"):
        a, b = keyed(before.get(category, [])), keyed(after.get(category, []))
        result[category] = {"new": [b[k] for k in sorted(b.keys() - a.keys())],
                            "resolved": [a[k] for k in sorted(a.keys() - b.keys())],
                            "unchanged": [b[k] for k in sorted(a.keys() & b.keys())]}
    result["skipped"] = after.get("skipped", [])
    result["unavailable"] = after.get("unavailable", [])
    result["process_failures"] = after.get("process_failures", [])
    return result


def execute(args, lifecycle):
    config = json.loads((ROOT / "quality-check.config.json").read_text())
    baseline = json.loads(Path(args.baseline).read_text()) if args.baseline else None
    if args.evidence_dir:
        evidence_root = Path(args.evidence_dir).resolve()
        if not evidence_root.is_dir():
            raise ValueError("--evidence-dir must be an existing private directory")
    else:
        evidence_root = Path(tempfile.mkdtemp(prefix="eternal-quality-"))
    if evidence_root == ROOT or ROOT in evidence_root.parents:
        raise ValueError("Evidence must be outside the repository")
    if stat.S_IMODE(evidence_root.stat().st_mode) & 0o077:
        raise ValueError("Evidence directory must be private (mode 0700)")
    evidence = Path(tempfile.mkdtemp(prefix=args.mode + "-", dir=evidence_root))
    print(f"Evidence: {evidence}", flush=True)
    owned = inventory(config)
    write_json(evidence / "inventory.json", owned)
    csharp = [r["path"] for category, scope in owned.items() if category != "tooling" for r in scope["inventory"]]
    write_json(evidence / "format-files.json", sorted(csharp))
    result = {"mode": args.mode, "evidence": str(evidence), "stages": [], "failures": [], "skipped": [],
              "unavailable": [{"check": "game-host suites", "reason": "not executed by this runner; isolated RimWorld sessions are run separately"}],
              "process_failures": [], "diagnostics": []}
    write_json(evidence / "source-state.json", {
        "root": str(ROOT),
        "branch": subprocess.check_output(["git", "branch", "--show-current"], cwd=ROOT, text=True).strip(),
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "status": subprocess.check_output(["git", "status", "--short"], cwd=ROOT, text=True).splitlines(),
        "config_sha256": digest(ROOT / "quality-check.config.json")})
    snapshot = None

    def stage(name, command, environment=None):
        started = time.monotonic()
        with (evidence / f"{name}.log").open("w") as output:
            try:
                status = lifecycle.run(command, output, environment)
            except BaseException:
                record = {"name": name, "command": shlex.join(command), "exit_status": lifecycle.last_status,
                          "seconds": round(time.monotonic() - started, 3), "interrupted": True}
                result["stages"].append(record)
                result["process_failures"].append(record)
                raise
        record = {"name": name, "command": shlex.join(command), "exit_status": status,
                  "seconds": round(time.monotonic() - started, 3)}
        result["stages"].append(record)
        if status:
            result["process_failures"].append(record)
        print(f"{name}: exit {status} ({record['seconds']}s)", flush=True)
        return status

    def collect_tests(xml, label):
        tests = parse_tests(xml, args.classes)
        write_json(evidence / f"test-identities-{label}.json", tests)
        result["failures"].extend(tests["failures"])
        result["skipped"].extend(tests["skipped"])
        if tests["infrastructure_errors"]:
            result["process_failures"].append({"name": f"framework-{label}", "errors": tests["infrastructure_errors"]})
        if tests["loader_failures"]:
            result["process_failures"].append({"name": f"loader-{label}", "failures": tests["loader_failures"]})

    try:
        for name in args.classes or []:
            if not re.fullmatch(r"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+", name):
                raise ValueError(f"--classes requires fully qualified class names: {name}")
        runner, identities = prerequisites(config)
        write_json(evidence / "prerequisites.json", identities)
        # Exercise each selected rule against deliberately faulty private code. A quiet
        # repository is not evidence that a requested diagnostic exists in this SDK.
        sdks = subprocess.check_output(["dotnet", "--list-sdks"], cwd=ROOT, text=True)
        sdk_match = re.search(r"^" + re.escape(identities["sdk"]) + r" \[(.*)\]$", sdks, re.M)
        if not sdk_match or "reference_mscorlib" not in identities:
            raise ValueError("Cannot resolve installed SDK or restored net472 probe references")
        sdk = Path(sdk_match.group(1)) / identities["sdk"]
        probe = evidence / "diagnostic-probe"
        probe.mkdir()
        (probe / "Probe.cs").write_text(
            "using System;\nusing System.Collections.Generic;\n"
            "class Probe { private int assigned; private void Dead() {} public void Run() { "
            "assigned = 1; int dead = 1; dead = 2; Console.WriteLine(dead); new Exception(); "
            "try { throw new Exception(); } catch (Exception caught) { throw caught; } } }\n")
        selected = config["style_diagnostics"] + config["analyzer_diagnostics"]
        (probe / ".editorconfig").write_text("root = true\n[*.cs]\n" + "".join(
            f"dotnet_diagnostic.{rule}.severity = warning\n" for rule in selected))
        analyzer_directory = sdk / "Sdks/Microsoft.NET.Sdk"
        analyzer_paths = [analyzer_directory / p for p in (
            "analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll", "analyzers/Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll",
            "codestyle/cs/Microsoft.CodeAnalysis.CodeStyle.dll", "codestyle/cs/Microsoft.CodeAnalysis.CSharp.CodeStyle.dll")]
        if not all(p.is_file() for p in analyzer_paths):
            raise ValueError("Installed SDK analyzer assemblies are unavailable")
        status = stage("diagnostic-support", ["dotnet", str(sdk / "Roslyn/bincore/csc.dll"), "/nologo", "/target:library",
                        f"/out:{probe / 'Probe.dll'}", f"/doc:{probe / 'Probe.xml'}",
                        f"/reference:{identities['reference_mscorlib']['path']}",
                        *[f"/analyzer:{path}" for path in analyzer_paths],
                        f"/analyzerconfig:{probe / '.editorconfig'}", str(probe / "Probe.cs")])
        observed = set(re.findall(r"warning ([A-Z]+\d+):", (evidence / "diagnostic-support.log").read_text()))
        support = {rule: "observed" if rule in observed else "unavailable_or_unsupported" for rule in selected}
        write_json(evidence / "diagnostic-support.json", {"sdk": identities["sdk"], "rules": support,
                   "analyzers": {str(p): digest(p) for p in analyzer_paths}})
        if status or any(rule not in observed for rule in selected):
            result["unavailable"].extend({"check": rule, "reason": "diagnostic not observed in installed SDK probe"}
                                         for rule in selected if rule not in observed)
            raise ValueError("Selected diagnostic availability probe failed; see diagnostic-support.json")
        optional = {}
        for name, assembly in optional_assembly_paths(config).items():
            optional[name] = {"path": str(assembly), "available": assembly.is_file()}
            if assembly.is_file():
                optional[name]["sha256"] = digest(assembly)
            else:
                result["unavailable"].append({"check": name, "reason": "optional assembly absent; presence test is not integration proof"})
                if os.environ.get(config["optional_checks_environment"], config["optional_checks"]) == "require_installed":
                    result["process_failures"].append({"name": name, "error": "required optional assembly absent"})
        if config["optional_checks"] not in ("report_unavailable", "require_installed"):
            raise ValueError("Unknown optional_checks policy")
        write_json(evidence / "optional-assemblies.json", optional)
        runtime_capture = runtime_target_capture(config)
        ET.ElementTree(runtime_capture).write(evidence / "runtime-inputs.xml", encoding="utf-8", xml_declaration=True)
        unavailable, failures = runtime_capture_outcomes(runtime_capture)
        result["unavailable"].extend(unavailable)
        result["process_failures"].extend(failures)
        result["unavailable"].append({"check": "optional integrations on 1.7",
                                     "reason": "existing RJW/SOS2/VGE evidence targets 1.6; 1.7 integration proof unavailable"})
        for row in owned["tooling"]["inventory"]:
            path = ROOT / row["path"]
            if path.suffix == ".sh":
                stage("syntax-" + path.name, ["bash", "-n", str(path)])
            elif path.suffix in (".xml", ".csproj", ".props", ".targets"):
                ET.parse(path)
            elif path.suffix == ".json":
                json.loads(path.read_text())
            elif path.suffix == ".py":
                compile(path.read_text(), row["path"], "exec")
        write_json(evidence / "tooling-syntax.json", {"checked": [r["path"] for r in owned["tooling"]["inventory"]
                    if Path(r["path"]).suffix in (".xml", ".csproj", ".props", ".targets", ".json", ".py", ".sh")],
                    "unavailable": ["YAML semantic validation: no new parser dependency", "remote/publish/deploy execution: outside authorization"]})
        snapshot = ArtifactSnapshot(config, evidence)
        configurations = ["Debug"] if args.mode in ("focused", "format") else ["Debug", "Release"]
        for configuration in configurations:
            build = stage(f"build-{configuration}", ["dotnet", "build", config["solution"], "-c", configuration, "--no-restore",
                          f"-p:EternalQualityReferenceDirectory={evidence}"])
            if build:
                result["unavailable"].append({"check": f"tests-{configuration}", "reason": "build failed; stale output prohibited"})
                continue
            copies = evidence / configuration
            copies.mkdir()
            for directory in config["artifact_roots"]:
                base = ROOT / directory
                if base.is_dir() and "obj" not in base.parts:
                    shutil.copytree(base, copies / directory)
            write_json(copies / "hashes.json", {str(path.relative_to(copies)): digest(path)
                                                for path in copies.rglob("*") if path.is_file()})
            output = ROOT / config["test_output"].format(configuration=configuration)
            if not output.is_file():
                raise ValueError(f"Build succeeded but test assembly missing: {output}")
            manifest = compiled_manifest(config, evidence, configuration, identities, copies / output.relative_to(ROOT).parent / "Eternal.dll")
            xml = evidence / f"tests-{configuration}.xml"
            command = ["mono", str(runner), str(output), "-parallel", "none", "-xml", str(xml),
                       "-junit", str(evidence / f"tests-{configuration}.junit.xml")]
            for name in args.classes or []:
                command.extend(["-class", name])
            if args.classes and "Eternal.Tests.Compatibility.RuntimeVersionMatrixTests" not in args.classes:
                command.extend(["-class", "Eternal.Tests.Compatibility.RuntimeVersionMatrixTests"])
            stage(f"tests-{configuration}", command, {"ETERNAL_COMPILED_MANIFEST": str(manifest)})
            collect_tests(xml, configuration)
            matrices = collect_runtime_matrices(evidence, configuration, result)
            expected_versions = set(config.get("runtime_targets", {}))
            if {r["version"] for r in matrices} != expected_versions:
                raise ValueError(f"Missing runtime metadata matrix discovery for {configuration}")
        common = [config["solution"], "--no-restore", "--include", *csharp, "--verbosity", "diagnostic"]
        for kind, rules in (("style", config["style_diagnostics"]), ("analyzers", config["analyzer_diagnostics"])):
            stage(f"diagnostics-{kind}", ["dotnet", "format", kind, *common, "--verify-no-changes",
                                         "--diagnostics", *rules, "--severity", "info", "--report", str(evidence / f"{kind}.json")])
        verify = [] if args.mode == "format" else ["--verify-no-changes"]
        stage("format-whitespace", ["dotnet", "format", "whitespace", *common, *verify,
                                    "--report", str(evidence / "whitespace.json")])
        stage("format-braces", ["dotnet", "format", "style", *common, *verify, "--diagnostics",
                                *config["format_style_diagnostics"], "--severity", "info", "--report", str(evidence / "braces.json")])
        if args.mode == "format":
            stage("format-diff", ["git", "diff", "--", *csharp])
            # Verify assembled formatting changes against freshly built output.
            build = stage("build-post-format", ["dotnet", "build", config["solution"], "-c", "Debug", "--no-restore",
                          f"-p:EternalQualityReferenceDirectory={evidence}"])
            if not build:
                xml = evidence / "tests-post-format.xml"
                command = ["mono", str(runner), str(ROOT / config["test_output"].format(configuration="Debug")),
                           "-parallel", "none", "-xml", str(xml), "-junit", str(evidence / "tests-post-format.junit.xml")]
                for name in args.classes or []:
                    command.extend(["-class", name])
                if args.classes and "Eternal.Tests.Compatibility.RuntimeVersionMatrixTests" not in args.classes:
                    command.extend(["-class", "Eternal.Tests.Compatibility.RuntimeVersionMatrixTests"])
                output = ROOT / config["test_output"].format(configuration="Debug")
                manifest = compiled_manifest(config, evidence, "Debug", identities, output.parent / "Eternal.dll")
                stage("tests-post-format", command, {"ETERNAL_COMPILED_MANIFEST": str(manifest)})
                collect_tests(xml, "post-format")
                collect_runtime_matrices(evidence, "Debug", result)
        for report in ("style", "analyzers", "whitespace", "braces"):
            path = evidence / f"{report}.json"
            if not path.is_file() or not isinstance(json.loads(path.read_text()), list):
                raise ValueError(f"Missing or malformed formatter report: {report}.json")
    except (ValueError, OSError, ET.ParseError, KeyError, InterruptedError) as error:
        result["process_failures"].append({"name": "infrastructure", "error": str(error)})
        print(f"Infrastructure failure: {error}", file=sys.stderr)
    finally:
        if snapshot:
            try:
                with lifecycle.defer_cancellation():
                    snapshot.restore(evidence)
                result["artifacts_restored"] = True
            except (OSError, RuntimeError) as error:
                result["artifacts_restored"] = False
                result["process_failures"].append({"name": "restore", "error": str(error)})
        result["diagnostics"] = parse_diagnostics(evidence)
        # Debug and Release share stable identities; do not double-count cases.
        for field in ("failures", "skipped"):
            result[field] = list({json.dumps(v, sort_keys=True): v for v in result[field]}.values())
        if lifecycle.cancelled and not any(r.get("name") == "cancellation" for r in result["process_failures"]):
            result["process_failures"].append({"name": "cancellation", "signal": lifecycle.cancelled})
        result["exit_status"] = 1 if result["process_failures"] or result["failures"] else 0
        write_json(evidence / "outcomes.json", result)
        if args.baseline:
            write_json(evidence / "comparison.json", comparison(baseline, result))
    return result["exit_status"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="Run local checks, preserving current artifact bytes and modes")
    run.add_argument("mode", choices=("baseline", "focused", "format", "final"))
    run.add_argument("--classes", nargs="+", help="Fully qualified xUnit class names")
    run.add_argument("--evidence-dir", help="Existing private evidence directory outside the repository")
    run.add_argument("--baseline", help="Original outcomes.json for identity-based final comparison")
    inv = commands.add_parser("inventory", help="Print the reusable owned-source inventory")
    inv.add_argument("--output")
    manifest = commands.add_parser("manifest", help="Resolve exact restored compiler inputs for standalone metadata checks; no build or restore")
    manifest.add_argument("--artifact", required=True)
    args = parser.parse_args()
    if args.command == "inventory":
        result = inventory(json.loads((ROOT / "quality-check.config.json").read_text()))
        if args.output:
            write_json(Path(args.output), result)
        else:
            print(json.dumps(result, indent=2))
        return 0
    if args.command == "manifest":
        config = json.loads((ROOT / "quality-check.config.json").read_text())
        artifact = Path(args.artifact).resolve()
        configuration = artifact.parent.parent.name
        if configuration not in ("Debug", "Release") or not artifact.is_file():
            raise ValueError("Standalone artifact must be the current Debug/Release test-output copy")
        if artifact != ROOT / config["test_output"].format(configuration=configuration).replace("Eternal.Tests.dll", "Eternal.dll"):
            raise ValueError("Alternate artifacts require an explicit compiled-build manifest")
        evidence = Path(tempfile.mkdtemp(prefix="eternal-compile-manifest-"))
        # MSBuild ResolveReferences may update caches. Protect existing outputs even without compiling.
        with ProtectedExecution() as lifecycle, checkout_execution_lock():
            snapshot = ArtifactSnapshot(config, evidence)
            try:
                _, identities = prerequisites(config)
                command = ["dotnet", "msbuild", config["production_project"],
                           "-target:ResolveReferences,FindReferenceAssembliesForReferences,EternalCaptureArtifactIdentity", "-verbosity:quiet",
                           f"-property:Configuration={configuration}", f"-property:EternalQualityReferenceDirectory={evidence}"]
                with (evidence / "resolve.log").open("w") as log:
                    status = lifecycle.run(command, log)
                    if status:
                        raise ValueError(f"Standalone reference resolution exited {status}; see {evidence / 'resolve.log'}")
                target = compiled_manifest(config, evidence, configuration, identities, artifact)
            finally:
                with lifecycle.defer_cancellation():
                    snapshot.restore(evidence)
        print(target)
        return 0
    with ProtectedExecution() as lifecycle, checkout_execution_lock():
        return execute(args, lifecycle)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError) as error:
        print(f"Setup failure: {error}", file=sys.stderr)
        sys.exit(2)
