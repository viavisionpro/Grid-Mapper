#!/usr/bin/env python3
"""Compile a machine-wide Windows installer from a verified portable package (NSIS 3)."""
import argparse
import importlib.util
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("app_packaging", ROOT / "scripts/package-app.py")
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)
RESERVED = {"CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            *("COM" + i for i in "123456789¹²³"), *("LPT" + i for i in "123456789¹²³")}
INSTALLER_FILES = {"uninstall.exe", "installation-receipt.ini", "metadata\\installation-receipt.ini", "nsis-copying.txt"}


def literal(value):
    """Escape a literal NSIS string; generated variables are added separately."""
    if any(ord(c) < 32 for c in value):
        raise ValueError("A packaging path contains a control character")
    return value.replace("$", "$$").replace('"', '$\\"')


def windows_path(relative):
    path = pathlib.PurePosixPath(relative)
    if path.is_absolute() or path.as_posix() != relative or not path.parts:
        raise ValueError("Unsafe Windows payload path: " + relative)
    for part in path.parts:
        if (part in (".", "..") or part.endswith((".", " ")) or
                any(ord(c) < 32 or c in '<>:"|?*\\' for c in part) or
                part.split(".")[0].rstrip(" ").upper() in RESERVED):
            raise ValueError("Unsafe Windows payload path: " + relative)
    return "\\".join(path.parts)


def payload_paths(manifest):
    if manifest["runtimeIdentifier"] != "win-x64":
        raise ValueError("Only a win-x64 payload is supported")
    version = manifest["version"]
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) or any(int(v) > 65535 for v in version.split(".")):
        raise ValueError("Installer requires a three-part numeric version")
    if "GridMapper.Desktop.exe" not in manifest["files"]:
        raise ValueError("The payload has no Windows application")
    paths = sorted([*manifest["files"], manifest.get("manifestPath", "package-manifest.json")])
    seen = set()
    for path in paths:
        normalized = windows_path(path).casefold()
        if normalized in seen or normalized in INSTALLER_FILES:
            raise ValueError("Windows payload path collision: " + path)
        seen.add(normalized)
    for path in seen:
        for parent in pathlib.PureWindowsPath(path).parents:
            if str(parent) != "." and (str(parent) in seen or str(parent) in INSTALLER_FILES):
                raise ValueError("Windows payload file/directory collision: " + path)
    return paths


def write_includes(package, manifest, stage, output):
    paths = payload_paths(manifest)
    install, uninstall = [], []
    parents = set()
    for relative in paths:
        windows = literal(windows_path(relative))
        parent = str(pathlib.PurePosixPath(relative).parent)
        subfolder = "" if parent == "." else "\\" + literal(windows_path(parent))
        install += [f'SetOutPath "$INSTDIR{subfolder}"', 'IfErrors installation_failed',
                    f'File "/oname={literal(pathlib.PurePosixPath(relative).name)}" "{literal(str(package / relative))}"',
                    'IfErrors installation_failed']
        if relative != "GridMapper.Desktop.exe":
            uninstall.append(f'!insertmacro DeleteInstalledFile "$INSTDIR\\{windows}"')
        for ancestor in pathlib.PurePosixPath(relative).parents:
            if str(ancestor) != ".": parents.add(str(ancestor))
    # Non-recursive removal leaves user-created projects, PNGs and subfolders intact.
    uninstall += [f'RMDir "$INSTDIR\\{literal(windows_path(p))}"' for p in sorted(parents, key=lambda p: (-p.count("/"), p))]
    for name, lines in (("install.nsh", install), ("uninstall.nsh", uninstall)):
        (stage / name).write_text("\n".join(lines) + "\n", encoding="utf-8")
    definitions = {"VERSION": manifest["version"], "OUTPUT_FILE": str(output),
                   "APP_ICON": str(ROOT / "src/GridMapper.Desktop/Assets/GridMapper.ico"),
                   "PAYLOAD_INSTALL": str(stage / "install.nsh"), "PAYLOAD_UNINSTALL": str(stage / "uninstall.nsh"),
                   "NSIS_COPYING": str(ROOT / "scripts/packaging/NSIS-COPYING.txt")}
    metadata = ['# -*- coding: utf-8 -*-'] + [f'!define {key} "{literal(value)}"' for key, value in definitions.items()]
    size = sum((package / p).stat().st_size for p in paths)
    metadata.append(f'!define SIZE_KIB {(size + 1023) // 1024}')
    (stage / "metadata.nsh").write_text("\n".join(metadata) + "\n", encoding="utf-8")
    return paths


def compiler_path(explicit):
    if explicit:
        return pathlib.Path(explicit).resolve()
    configured = os.environ.get("GRID_MAPPER_MAKENSIS") or shutil.which("makensis")
    if configured:
        return pathlib.Path(configured).resolve()
    local = ROOT / ".tools/nsis/makensis/3.13/bin/makensis"
    if local.is_file():
        return local
    raise ValueError("NSIS 3 is required. Set GRID_MAPPER_MAKENSIS or use --makensis; see BUILDING.md")


def build(package, compiler, nsis_dir=None):
    package = pathlib.Path(package).absolute()
    packaging.verify(package)
    manifest = json.loads((package / packaging.manifest_relative(package)).read_text(encoding="utf-8"))
    payload_paths(manifest)
    version = ET.parse(ROOT / "Directory.Build.props").findtext("PropertyGroup/Version")
    if manifest["version"] != version:
        raise ValueError("The portable package version is stale")
    env = os.environ.copy()
    data = pathlib.Path(nsis_dir) if nsis_dir else compiler.parent.parent / "share/nsis"
    if data.is_dir(): env["NSISDIR"] = str(data.resolve())
    compiler_version = subprocess.check_output([str(compiler), "-VERSION"], env=env, text=True).strip()
    if not compiler_version.startswith("v3."):
        raise ValueError("Only NSIS 3 compilers are supported")
    packages = ROOT / "artifacts/packages"
    output = packages / f"GridMapper-{version}-win-x64-setup.exe"
    report = output.with_suffix(".build.json")
    checksum = output.with_suffix(".exe.sha256")
    if any(path.exists() or path.is_symlink() for path in (output, report, checksum)):
        raise ValueError("Existing versioned installer preserved; increment the version before rebuilding")
    with tempfile.TemporaryDirectory(prefix=".installer-", dir=packages) as temporary:
        stage = pathlib.Path(temporary)
        partial = stage / output.name
        paths = write_includes(package, manifest, stage, partial)
        subprocess.run([str(compiler), "-NOCONFIG", "-WX", "-V2", "-INPUTCHARSET", "UTF8",
                        "-DBUILD_METADATA=" + str(stage / "metadata.nsh"),
                        str(ROOT / "scripts/packaging/windows.nsi")], env=env, check=True)
        if partial.read_bytes()[:2] != b"MZ":
            raise ValueError("Compiler did not produce a Windows executable")
        result = {"formatVersion": 1, "product": "Grid Mapper", "version": version,
                  "runtimeIdentifier": "win-x64", "compiler": "NSIS " + compiler_version,
                  "sourceCommit": manifest["sourceCommit"], "sourceDirty": manifest["sourceDirty"],
                  "portableManifestSha256": packaging.sha256(package / packaging.manifest_relative(package)),
                  "installerSha256": packaging.sha256(partial), "payloadFiles": len(paths),
                  "distributionSigned": False, "nativeWindowsAcceptance": "pending"}
        result["installerSourceCommit"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        result["installerSourceDirty"] = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True))
        staged_report = stage / report.name
        staged_report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        staged_checksum = stage / checksum.name
        staged_checksum.write_text(result["installerSha256"] + "  " + output.name + "\n")
        os.link(staged_report, report)
        os.link(staged_checksum, checksum)
        os.link(partial, output)
    print(output)
    print(f"Installer contains {len(paths)} verified portable files; native Windows acceptance is pending.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=pathlib.Path)
    parser.add_argument("--makensis")
    parser.add_argument("--nsis-dir")
    args = parser.parse_args()
    try:
        version = ET.parse(ROOT / "Directory.Build.props").findtext("PropertyGroup/Version")
        package = args.package or ROOT / "artifacts/packages" / f"Grid Mapper {version} Windows x64"
        build(package, compiler_path(args.makensis), args.nsis_dir)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Installer packaging failed: {error}\n")
