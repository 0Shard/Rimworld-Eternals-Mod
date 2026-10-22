#!/usr/bin/env python3
# Author: 0Shard
"""Prepare a private game copy and optionally run its own bounded test session."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import stat
import struct
import subprocess
import time
import xml.etree.ElementTree as ET


CHECKOUT = Path(__file__).resolve().parents[1]
EXCLUDED_GAME_ITEMS = {"mods", "config", "savedata", "userdata", "saves", "logs", "source"}
OFFICIAL_ORDER = ("ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
                  "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey")
NATIVE_ENV_DIRECTORIES = {"XDG_CONFIG_HOME": "engine-config", "XDG_CACHE_HOME": "engine-cache",
                          "XDG_DATA_HOME": "engine-data"}


def file_record(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return {"path": str(path), "size": path.stat().st_size, "sha256": digest.hexdigest()}


def require_file(path):
    if not path.is_file() or path.stat().st_size == 0:
        raise ValueError(f"Required nonempty file missing: {path}")


def require_artifact(path):
    require_file(path)
    data = path.read_bytes()
    try:
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        optional = pe + 24
        magic = struct.unpack_from("<H", data, optional)[0]
        directories = optional + {0x10B: 96, 0x20B: 112}[magic]
        cli_rva, cli_size = struct.unpack_from("<II", data, directories + 14 * 8)
        valid = data[:2] == b"MZ" and data[pe:pe + 4] == b"PE\0\0" and cli_rva and cli_size
    except (struct.error, KeyError):
        valid = False
    if path.name != "Eternal.dll" or not valid or b".NETFramework,Version=v4.7.2" not in data:
        raise ValueError(f"Artifact must be a managed net472 Eternal.dll: {path}")


def copy_item(source, destination):
    # Materialize file symlinks as copies; reject directory links to avoid copy cycles.
    if source.is_dir():
        if source.is_symlink() or any(p.is_symlink() and p.is_dir() for p in source.rglob("*")):
            raise ValueError(f"Directory symlinks are not accepted in game/mod inputs: {source}")
        shutil.copytree(source, destination, symlinks=False)
    else:
        shutil.copy2(source, destination, follow_symlinks=True)


def package_metadata(root, expected_package=None, version=None):
    path = root / "About" / "About.xml"
    require_file(path)
    metadata = ET.parse(path).getroot()
    package = metadata.findtext("packageId", "").lower()
    if not package or (expected_package and package != expected_package):
        raise ValueError(f"Unexpected package ID in {path}: {package}")
    if version and version not in [n.text for n in metadata.findall("supportedVersions/li")]:
        raise ValueError(f"{path} does not declare support for {version}")
    return package


def write_xml(path, root):
    ET.indent(root)
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def game_path(path, version):
    # A new Wine prefix's default Z: drive exposes the host's absolute filesystem.
    return "Z:" + str(path).replace("/", "\\") if version == "1.6" else str(path)


def environment_overrides(version, evidence):
    if version == "1.6":
        return {"WINEPREFIX": str(evidence / "wineprefix"), "WINEARCH": "win64"}
    # Backend logging and GPU caches may bypass RimWorld's savedatafolder argument.
    return {name: str(evidence / directory) for name, directory in NATIVE_ENV_DIRECTORIES.items()}


def automation_settings(config):
    automation = config.find("automation")
    value = automation.get("autosaveIntervalDays") if automation is not None else None
    if value is None:
        return {}
    try:
        interval = float(value)
    except ValueError as error:
        raise ValueError("automation autosaveIntervalDays must be finite and in (0, 35000]") from error
    if not math.isfinite(interval) or not 0 < interval <= 35000:
        raise ValueError("automation autosaveIntervalDays must be finite and in (0, 35000]")
    return {"autosaveIntervalDays": interval}


def launch_command(args, evidence):
    executable = evidence / "game" / ("RimWorldWin64.exe" if args.version == "1.6" else "RimWorld")
    command = [args.wine, str(executable)] if args.version == "1.6" else [str(executable)]
    for flag, path in (
        ("savedatafolder", evidence / "savedata"),
        ("eternal-test-config", evidence / "test-config.xml"),
        ("eternal-test-output", evidence / "reports"),
        ("eternal-test-session", evidence),
    ):
        command.append(f"-{flag}={game_path(path, args.version)}")
    command.append(f"-eternal-test-mode={args.mode}")
    if args.version == "1.6":
        command.extend(["-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "600"])
    if args.label_prefix:
        command.append(f"-eternal-test-label-prefix={args.label_prefix}")
    if args.initial_save is not None and args.initial_checkpoint is not None:
        for flag, path in (("eternal-test-resume", evidence / "savedata" / "Saves" / args.initial_save.name),
                           ("eternal-test-checkpoint", evidence / "reports" / "initial-checkpoint.xml")):
            command.append(f"-{flag}={game_path(path, args.version)}")
    forbidden = ("-savedatafolder", "-eternal-test-config", "-eternal-test-output", "-eternal-test-session",
                 "-eternal-test-resume", "-eternal-test-checkpoint")
    if any(arg.lower().startswith(forbidden) for arg in args.game_arg):
        raise ValueError("Extra game arguments cannot override isolated paths")
    return command + args.game_arg


def validate_initial_fixture(args):
    if (args.initial_save is None) != (args.initial_checkpoint is None):
        raise ValueError("--initial-save and --initial-checkpoint must be provided together")
    if args.initial_save is None:
        if args.initial_settings is not None:
            raise ValueError("--initial-settings requires --initial-save and --initial-checkpoint")
        return None
    save = args.initial_save.resolve()
    checkpoint = args.initial_checkpoint.resolve()
    if not args.initial_save.name.startswith("EternalSynthetic-") or args.initial_save.suffix != ".rws":
        raise ValueError("Initial save must be an EternalSynthetic-*.rws fixture")
    require_file(save)
    require_file(checkpoint)
    metadata = ET.parse(checkpoint).getroot()
    if metadata.tag != "persistenceCheckpoint" or metadata.get("schemaVersion") != "1":
        raise ValueError("Initial checkpoint must use persistenceCheckpoint schemaVersion 1")
    if metadata.get("saveFileName") != args.initial_save.name or metadata.get("saveName") != args.initial_save.stem:
        raise ValueError("Initial checkpoint saveName/saveFileName do not match the synthetic save")
    source_save_record = file_record(save)
    expected_hash = metadata.get("sourceSaveSha256")
    if expected_hash is not None and expected_hash.lower() != source_save_record["sha256"]:
        raise ValueError("Initial checkpoint sourceSaveSha256 does not match the synthetic save")
    fixture = {"source_save": source_save_record, "source_checkpoint": file_record(checkpoint),
               "checkpoint_attributes": dict(metadata.attrib)}
    if args.initial_settings is not None:
        fixture["settings"] = validate_initial_settings(args.initial_settings.resolve(), metadata)
    return fixture


def validate_initial_settings(directory, checkpoint):
    if not directory.is_dir():
        raise ValueError(f"Initial settings directory is missing: {directory}")
    listed = checkpoint.find("settingsFiles")
    if listed is None or any(node.tag != "file" for node in listed):
        raise ValueError("Initial checkpoint must list settingsFiles/file entries")
    records = []
    names = set()
    mod_settings = 0
    for node in listed:
        name = node.get("name", "")
        is_mod_settings = re.fullmatch(r"Mod_[^/\\]+_Eternal_Mod\.xml", name) is not None
        if name in names or not (is_mod_settings or name == "Eternal_HediffSettings.xml"):
            raise ValueError(f"Initial checkpoint contains duplicate or non-Eternal settings: {name}")
        names.add(name)
        mod_settings += int(is_mod_settings)
        path = directory / name
        require_file(path)
        source = file_record(path)
        expected_hash = node.get("sha256", "").lower()
        if expected_hash != source["sha256"]:
            raise ValueError(f"Initial settings hash does not match checkpoint: {name}")
        records.append({"name": name, "source": source})
    if mod_settings != 1:
        raise ValueError("Initial checkpoint must list exactly one Mod_*_Eternal_Mod.xml settings file")
    return records


def prepare(args):
    initial_fixture = validate_initial_fixture(args)
    evidence = args.evidence_dir.resolve()
    if evidence.is_relative_to(CHECKOUT):
        raise ValueError("Evidence directory must be outside the checkout")
    inputs = [args.game_root.resolve(), args.harmony_root.resolve(), args.mod_root.resolve()]
    if any(evidence.is_relative_to(root) or root.is_relative_to(evidence) for root in inputs):
        raise ValueError("Evidence directory must not overlap game/mod input directories")
    if evidence.exists() and (not evidence.is_dir() or stat.S_IMODE(evidence.stat().st_mode) != 0o700):
        raise ValueError("Existing evidence directory must have permissions 0700")
    session_outputs = ("game", "savedata", "reports", "wineprefix", "session.json", "test-config.xml") + tuple(NATIVE_ENV_DIRECTORIES.values())
    if any((evidence / name).exists() for name in session_outputs):
        raise ValueError("Evidence directory already contains game-host session outputs")
    if not args.prepare_only and (args.timeout is None or not math.isfinite(args.timeout) or args.timeout <= 0):
        raise ValueError("A positive --timeout is required when launching")
    game, harmony, mod = inputs
    require_file(game / "Version.txt")
    version_text = (game / "Version.txt").read_text(encoding="utf-8-sig").strip()
    if not re.match(rf"^{re.escape(args.version)}(?:\.|\s|$)", version_text):
        raise ValueError(f"Game version {version_text!r} does not match {args.version}")
    binary = game / ("RimWorldWin64.exe" if args.version == "1.6" else "RimWorld")
    managed = game / "RimWorldWin64_Data" / "Managed" if args.version == "1.6" else game
    for path in (binary, managed / "Assembly-CSharp.dll", args.config.resolve()):
        require_file(path)
    automation = automation_settings(ET.parse(args.config.resolve()).getroot())
    if args.version == "1.7":
        require_file(game / "UnityStubs.dll")
        require_file(game / "RimWorld.runtimeconfig.json")
    require_artifact(args.artifact.resolve())
    package_metadata(harmony, "brrainz.harmony", args.version)
    package_metadata(mod, "shard.eternal", args.version)
    require_file(harmony / args.version / "Assemblies" / "0Harmony.dll")
    for name in ("About", "Defs", "Languages", "Textures"):
        if not (mod / name).is_dir():
            raise ValueError(f"Required Eternal content missing: {mod / name}")
    mapping = ET.parse(mod / "LoadFolders.xml").getroot()
    if [n.text for n in mapping.findall(f"v{args.version}/li")] != ["/", args.version]:
        raise ValueError("Eternal LoadFolders must select root and matching version only")
    official = []
    for metadata in sorted((game / "Data").glob("*/About/About.xml")):
        package = package_metadata(metadata.parent.parent)
        if not package.startswith("ludeon.rimworld"):
            raise ValueError(f"Unofficial package in game Data: {metadata}")
        official.append(package)
    if "ludeon.rimworld" not in official:
        raise ValueError("Game Data/Core metadata is missing")
    official.sort(key=lambda p: (OFFICIAL_ORDER.index(p) if p in OFFICIAL_ORDER else len(OFFICIAL_ORDER), p))
    evidence.mkdir(mode=0o700, parents=True, exist_ok=True)
    destination = evidence / "game"
    destination.mkdir()
    for item in game.iterdir():
        if item.name.lower() not in EXCLUDED_GAME_ITEMS and not item.name.startswith("."):
            copy_item(item, destination / item.name)
    mods = destination / "Mods"
    mods.mkdir()
    harmony_copy = mods / "Harmony"
    harmony_copy.mkdir()
    for item in harmony.iterdir():
        if item.name == args.version or item.name in ("About", "Defs", "Patches", "Languages", "Textures") or (item.is_file() and not item.name.startswith(".")):
            copy_item(item, harmony_copy / item.name)
    # Use a session-specific mapping rather than retaining unrelated version mappings.
    load = ET.Element("loadFolders")
    selected = ET.SubElement(load, f"v{args.version}")
    for folder in ("/", args.version):
        ET.SubElement(selected, "li").text = folder
    write_xml(harmony_copy / "LoadFolders.xml", load)
    eternal_copy = mods / "Eternal"
    eternal_copy.mkdir()
    for name in ("About", "Defs", "Patches", "Languages", "Textures", "LoadFolders.xml"):
        if (mod / name).exists():
            copy_item(mod / name, eternal_copy / name)
        elif name != "Patches":
            raise ValueError(f"Required Eternal content missing: {mod / name}")
    assembly_dir = eternal_copy / args.version / "Assemblies"
    assembly_dir.mkdir(parents=True)
    shutil.copy2(args.artifact.resolve(), assembly_dir / "Eternal.dll")
    config_dir = evidence / "savedata" / "Config"
    config_dir.mkdir(parents=True)
    (evidence / "reports").mkdir()
    if initial_fixture is not None:
        saves = evidence / "savedata" / "Saves"
        saves.mkdir()
        for source_key, destination_path, copied_key in (
            ("source_save", saves / args.initial_save.name, "copied_save"),
            ("source_checkpoint", evidence / "reports" / "initial-checkpoint.xml", "copied_checkpoint"),
        ):
            shutil.copy2(initial_fixture[source_key]["path"], destination_path)
            copied = file_record(destination_path)
            if copied["sha256"] != initial_fixture[source_key]["sha256"]:
                raise ValueError(f"Initial fixture changed while copying: {initial_fixture[source_key]['path']}")
            initial_fixture[copied_key] = copied
    if args.version == "1.6":
        (evidence / "wineprefix").mkdir()
    else:
        for directory in NATIVE_ENV_DIRECTORIES.values():
            (evidence / directory).mkdir(mode=0o700)
    shutil.copy2(args.config.resolve(), evidence / "test-config.xml")
    mods_config = ET.Element("ModsConfigData")
    ET.SubElement(mods_config, "version").text = version_text.split()[0]
    active = ET.SubElement(mods_config, "activeMods")
    for package in ["brrainz.harmony", "ludeon.rimworld"] + [p for p in official if p != "ludeon.rimworld"] + ["shard.eternal"]:
        ET.SubElement(active, "li").text = package
    write_xml(config_dir / "ModsConfig.xml", mods_config)
    prefs = ET.Element("PrefsData")
    for name, value in (("screenWidth", "800"), ("screenHeight", "600"), ("fullscreen", "false"),
                        ("runInBackground", "true"), ("devMode", "true"), ("volumeMusic", "0"),
                        ("pauseOnLoad", "false")):
        ET.SubElement(prefs, name).text = value
    for name, value in automation.items():
        ET.SubElement(prefs, name).text = str(value)
    write_xml(config_dir / "Prefs.xml", prefs)
    if initial_fixture is not None:
        for setting in initial_fixture.get("settings", []):
            destination_path = config_dir / setting["name"]
            shutil.copy2(setting["source"]["path"], destination_path)
            setting["copied"] = file_record(destination_path)
            if setting["copied"]["sha256"] != setting["source"]["sha256"]:
                raise ValueError(f"Initial setting changed while copying: {setting['source']['path']}")
    assembly_paths = sorted(managed.glob("UnityEngine*.dll")) if args.version == "1.6" else [game / "UnityStubs.dll", game / "RimWorld.dll"]
    assembly_paths += [managed / "Assembly-CSharp.dll", managed / "mscorlib.dll", managed / "System.Runtime.dll", managed / "System.Private.CoreLib.dll"]
    record = {
        "status": "prepared", "version": version_text, "mode": args.mode,
        "source_game": str(game), "isolated_game": str(destination), "evidence_dir": str(evidence),
        "version_file": file_record(game / "Version.txt"), "binary": file_record(binary),
        "copied_binary": file_record(destination / binary.name),
        "assemblies": [file_record(p) for p in assembly_paths if p.is_file()],
        "artifact": file_record(args.artifact.resolve()),
        "copied_artifact": file_record(assembly_dir / "Eternal.dll"),
        "harmony": file_record(harmony / args.version / "Assemblies" / "0Harmony.dll"),
        "config": file_record(evidence / "test-config.xml"),
        "command": launch_command(args, evidence), "cwd": str(destination),
        "wineprefix": str(evidence / "wineprefix") if args.version == "1.6" else None,
        "environment_overrides": environment_overrides(args.version, evidence),
        "automation": automation,
        "pid": None, "exit_code": None,
    }
    if initial_fixture is not None:
        record["initial_fixture"] = initial_fixture
    save_record(evidence, record)
    return evidence, record


def save_record(evidence, record):
    path = evidence / "session.json"
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")


def terminate_owned_process(process):
    if process.poll() is None:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            process.wait(timeout=5)
            return
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)


def interrupt_host(signum, frame):
    raise KeyboardInterrupt


def test_report_status(evidence, mode, version):
    path = evidence / "reports" / "game-host-result.xml"
    if not path.is_file():
        return {"test_status": "missing", "test_error": f"Game-host report missing: {path}"}
    result = {}
    try:
        # Preserve the evidence hash even when the report records a failing test.
        result["game_host_report"] = file_record(path)
        report = ET.parse(path).getroot()
        actual_version = report.get("gameVersion", "").split()
        if report.tag != "gameHost" or report.get("mode") != mode:
            raise ValueError("Game-host report root or mode does not match the session")
        if not actual_version or actual_version[0] != version.split()[0]:
            raise ValueError("Game-host report version does not match the session")
        if report.get("success") not in ("true", "false"):
            raise ValueError("Game-host report success must be true or false")
        result["test_status"] = "passed" if report.get("success") == "true" else "failed"
    except (OSError, ValueError, ET.ParseError) as error:
        result.update(test_status="error", test_error=str(error))
    return result


def launch(args, evidence, record):
    environment = os.environ.copy()
    environment.update(environment_overrides(args.version, evidence))
    process = None
    started = time.monotonic()
    previous_handler = signal.signal(signal.SIGTERM, interrupt_host)
    try:
        with (evidence / "host.log").open("wb") as output:
            process = subprocess.Popen(record["command"], cwd=record["cwd"], env=environment,
                                       stdout=output, stderr=subprocess.STDOUT, start_new_session=True, shell=False)
            record.update(status="running", pid=process.pid)
            save_record(evidence, record)
            while True:
                remaining = args.timeout - (time.monotonic() - started)
                if remaining <= 0:
                    record["status"] = "timeout"
                    terminate_owned_process(process)
                    break
                try:
                    process.wait(timeout=min(15, remaining))
                    record["status"] = "exited"
                    break
                except subprocess.TimeoutExpired:
                    print(f"Game host PID {process.pid}: running, {remaining:.0f}s remaining", flush=True)
    except KeyboardInterrupt:
        record["status"] = "interrupted"
        if process:
            terminate_owned_process(process)
    except OSError as error:
        record.update(status="launch_failed", error=str(error))
    finally:
        signal.signal(signal.SIGTERM, previous_handler)
        record["exit_code"] = process.returncode if process else None
        record["elapsed_seconds"] = round(time.monotonic() - started, 3)
        record.update(test_report_status(evidence, args.mode, record["version"]))
        save_record(evidence, record)
    return 0 if record["status"] == "exited" and record["exit_code"] == 0 and record["test_status"] == "passed" else 1


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    for name in ("game-root", "harmony-root", "mod-root", "artifact", "config", "evidence-dir"):
        result.add_argument(f"--{name}", type=Path, required=True)
    result.add_argument("--version", choices=("1.6", "1.7"), required=True)
    result.add_argument("--mode", choices=("benchmark", "failure", "smoke"), required=True)
    result.add_argument("--prepare-only", action="store_true")
    result.add_argument("--timeout", type=float, help="Required launch deadline in seconds")
    result.add_argument("--wine", default="wine", help="Wine executable for 1.6 (default: wine)")
    result.add_argument("--label-prefix")
    result.add_argument("--initial-save", type=Path, help="Explicit EternalSynthetic-*.rws fixture to resume")
    result.add_argument("--initial-checkpoint", type=Path, help="Matching synthetic persistence checkpoint")
    result.add_argument("--initial-settings", type=Path, help="Prior Config directory; copy only checkpoint-listed Eternal settings")
    result.add_argument("--game-arg", action="append", default=[], help="Extra game argument; use --game-arg=-flag")
    return result


def main(argv=None):
    argument_parser = parser()
    args = argument_parser.parse_args(argv)
    try:
        evidence, record = prepare(args)
        print(f"Prepared isolated {args.version} session: {evidence}", flush=True)
        return 0 if args.prepare_only else launch(args, evidence, record)
    except (OSError, ValueError, ET.ParseError) as error:
        argument_parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
