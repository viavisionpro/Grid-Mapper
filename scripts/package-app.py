#!/usr/bin/env python3
"""Package self-contained Grid Mapper applications, retaining dependency notices.

Only Python's standard library is needed. macOS archives use ditto to retain
bundle metadata and executable permissions. This does not sign for distribution.
"""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def metadata(nuspec):
    root = ET.parse(nuspec).getroot()
    namespace = root.tag.split("}")[0] + "}"
    return root.find(namespace + "metadata"), namespace


def copy_notices(deps, runtime, target):
    notices = target / "Licenses"
    notices.mkdir()
    cache = ROOT / ".cache/nuget"
    mit = (ROOT / "scripts/packaging/MIT.txt").read_text()
    index = []
    packages = [(key, lib.get("path", key.lower())) for key, lib in deps["libraries"].items()
                if lib["type"] == "package"]
    for framework in runtime["runtimeOptions"]["includedFrameworks"]:
        if framework["name"] != "Microsoft.NETCore.App":
            raise ValueError("Unsupported bundled framework: " + framework["name"])
        package = "Microsoft.NETCore.App.Runtime." + deps["runtimeTarget"]["name"].split("/")[-1]
        key = package + "/" + framework["version"]
        packages.append((key, key.lower()))
    for key, relative in sorted(set(packages)):
        package = cache / relative
        nuspecs = list(package.glob("*.nuspec"))
        if len(nuspecs) != 1:
            raise ValueError("Restore the locked dependency before packaging: " + key)
        info, ns = metadata(nuspecs[0])
        license_node = info.find(ns + "license")
        if license_node is None or license_node.get("type") not in ("file", "expression"):
            raise ValueError("License declaration needs explicit handling: " + key)
        if license_node.get("type") == "expression" and license_node.text != "MIT":
            raise ValueError("License expression needs explicit handling: " + key)
        destination = notices / key.replace("/", "-")
        destination.mkdir()
        shutil.copy2(nuspecs[0], destination / "package.nuspec")
        files = [p for p in package.iterdir() if p.is_file()
                 and any(word in p.name.lower() for word in ("license", "licence", "notice", "copying"))]
        if license_node is not None and license_node.get("type") == "file":
            declared = package / license_node.text
            if not declared.is_file() or not declared.resolve().is_relative_to(package.resolve()):
                raise ValueError("Missing declared license: " + key)
            if declared not in files:
                files.append(declared)
        for source in files:
            shutil.copy2(source, destination / source.name)
        has_license = license_node.get("type") == "file" or any(
            any(word in p.name.lower() for word in ("license", "licence", "copying")) for p in files)
        if not has_license:
            if license_node is None or license_node.get("type") != "expression" or license_node.text != "MIT":
                raise ValueError("License needs explicit handling: " + key)
            copyright_text = info.findtext(ns + "copyright")
            if not copyright_text:
                raise ValueError("Missing copyright notice: " + key)
            (destination / "LICENSE.txt").write_text(copyright_text + "\n\n" + mit, encoding="utf-8")
        index.append(key)
    for name in ("OFL.txt", "OFL-NotoSansMono.txt"):
        shutil.copy2(ROOT / "src/GridMapper.Rendering/Assets" / name, notices / name)
    shutil.copy2(ROOT / "THIRD_PARTY_NOTICES.md", notices / "THIRD_PARTY_NOTICES.md")
    (notices / "INDEX.txt").write_text("Bundled dependency notices (NuGet metadata retained):\n" +
                                      "\n".join(index) + "\nNoto Sans / Noto Sans Mono: SIL OFL 1.1\n", encoding="utf-8")
    return index



def manifest_relative(folder):
    """Keep historical releases verifiable while new metadata stays in a folder."""
    folder = pathlib.Path(folder)
    legacy = folder / "package-manifest.json"
    if legacy.exists() or legacy.is_symlink():
        return "package-manifest.json"
    return ("Contents/Resources/Metadata/package-manifest.json" if folder.suffix == ".app"
            else "Metadata/package-manifest.json")


def create_package_host(published, destination, rid, runtime, assembly="GridMapper.Desktop", app_binary=None):
    version = next(f["version"] for f in runtime["runtimeOptions"]["includedFrameworks"]
                   if f["name"] == "Microsoft.NETCore.App")
    executable = "apphost.exe" if rid.startswith("win-") else "apphost"
    package = "Microsoft.NETCore.App.Host." + rid
    candidates = [ROOT / ".cache/nuget" / package.lower() / version / "runtimes" / rid / "native" / executable]
    dotnet = ROOT / ".tools/dotnet"
    # The current-platform host pack is shipped with the pinned SDK.
    candidates.append(dotnet / "packs" / package / version / "runtimes" / rid / "native" / executable)
    template = next((p for p in candidates if p.is_file()), None)
    if template is None:
        # A developer may use an installed SDK instead of the optional local SDK.
        installations = subprocess.check_output(["bash", str(ROOT / "scripts/dotnet.sh"), "--list-sdks"], cwd=ROOT, text=True)
        for line in installations.splitlines():
            if " [" not in line or not line.endswith("]"): continue
            sdk_root = pathlib.Path(line.split(" [", 1)[1][:-1]).parent
            candidate = sdk_root / "packs" / package / version / "runtimes" / rid / "native" / executable
            if candidate.is_file(): template = candidate; break
    if template is None:
        raise ValueError("Restore the apphost pack before packaging: " + package + "/" + version)
    subprocess.run(["bash", str(ROOT / "scripts/dotnet.sh"), "msbuild",
                    str(ROOT / "scripts/packaging/CreatePackageHost.proj"), "-nologo",
                    "-p:SourceHost=" + str(template), "-p:DestinationHost=" + str(destination),
                    "-p:AppBinary=" + (app_binary or "Runtime/" + assembly + ".dll"),
                    "-p:AppAssembly=" + str(published / (assembly + ".dll")),
                    "-p:WindowsGui=" + str(rid.startswith("win-")).lower(),
                    "-p:SignMac=" + str(rid.startswith("osx-")).lower()], cwd=ROOT, check=True)


def copy_application_layout(published, payload, rid, runtime, assembly="GridMapper.Desktop"):
    """One native apphost, explicit editable resources, self-contained Runtime."""
    executable = assembly + (".exe" if rid.startswith("win-") else "")
    runtime_folder = payload / "Runtime"
    shutil.copytree(published, runtime_folder,
                    ignore=shutil.ignore_patterns("*.pdb", "*.dbg", ".DS_Store", executable))
    for name in ("README.md", "README.ru.md", "LICENSE", "NDI-ABI-NOTICE.txt", "NDI-LOGO-NOTICE.md",
                 "Localizations", "NDI"):
        source = runtime_folder / name
        if source.exists(): shutil.move(str(source), payload / name)
    config = runtime_folder / (assembly + ".runtimeconfig.json")
    options = json.loads(config.read_text(encoding="utf-8"))
    options["runtimeOptions"].setdefault("configProperties", {})["GridMapper.DistributionLayout"] = ("macos-resources-v1" if rid == "osx-arm64" and payload.name == "Resources" else "runtime-subdirectory-v1")
    config.write_text(json.dumps(options, indent=2) + "\n", encoding="utf-8")
    if rid == "osx-arm64" and payload.name == "Resources":
        launcher = payload.parent / "MacOS" / executable
        launcher.parent.mkdir(parents=True, exist_ok=True)
        create_package_host(published, launcher, rid, runtime, assembly, "../Resources/Runtime/" + assembly + ".dll")
    else:
        create_package_host(published, payload / executable, rid, runtime, assembly)


def verify_application_layout(folder, manifest):
    if manifest.get("layoutVersion") != 2: return
    mac = manifest["runtimeIdentifier"] == "osx-arm64"
    sealed = bool(manifest.get("bundleSignature"))
    payload = folder / ("Contents/Resources" if sealed else "Contents/MacOS") if mac else folder
    exe = "GridMapper.Desktop" if mac else "GridMapper.Desktop.exe"
    allowed_files = ({exe} if not sealed else {"GridMapper.icns"}) | {"README.md", "README.ru.md", "LICENSE", "NDI-ABI-NOTICE.txt", "NDI-LOGO-NOTICE.md"}
    allowed_dirs = {"Runtime", "Localizations", "Licenses", "NDI"} | ({"Metadata"} if sealed or not mac else set())
    if {p.name for p in payload.iterdir() if p.is_file()} != allowed_files:
        raise ValueError("Unexpected files beside the application launcher")
    if {p.name for p in payload.iterdir() if p.is_dir()} != allowed_dirs:
        raise ValueError("Unexpected application resource folders")
    launcher = folder / "Contents/MacOS" / exe if sealed else payload / exe
    binary = b"../Resources/Runtime/GridMapper.Desktop.dll\0" if sealed else b"Runtime/GridMapper.Desktop.dll\0"
    if sealed and {p.name for p in launcher.parent.iterdir()} != {exe}:
        raise ValueError("Only the launcher belongs in Contents/MacOS")
    if binary not in launcher.read_bytes():
        raise ValueError("Launcher is not bound to the packaged Runtime assembly")
    runtime = payload / "Runtime"
    for name in ("GridMapper.Desktop.dll", "GridMapper.Desktop.deps.json", "GridMapper.Desktop.runtimeconfig.json",
                 "libhostfxr.dylib" if mac else "hostfxr.dll"):
        if not (runtime / name).is_file(): raise ValueError("Missing Runtime file: " + name)
    config = json.loads((runtime / "GridMapper.Desktop.runtimeconfig.json").read_text(encoding="utf-8"))
    if config["runtimeOptions"]["configProperties"].get("GridMapper.DistributionLayout") != ("macos-resources-v1" if sealed else "runtime-subdirectory-v1"):
        raise ValueError("Application resource layout is not configured")
    if {p.name for p in (payload / "NDI").iterdir()} != {"README.md"}:
        raise ValueError("NDI Runtime must not be bundled")

MAC_SIGNATURE_FILES = {"Contents/MacOS/GridMapper.Desktop", "Contents/_CodeSignature/CodeResources"}


def sign_macos_payload(folder):
    # NuGet native libraries can carry invalid upstream signatures. Sign inside
    # out before hashing the payload; already valid libraries retain their bytes.
    for library in sorted((folder / "Contents").rglob("*.dylib")):
        valid = subprocess.run(["codesign", "--verify", "--strict", str(library)], capture_output=True)
        if valid.returncode:
            subprocess.run(["codesign", "--force", "--sign", "-", "--timestamp=none", str(library)], check=True)


def seal_macos_bundle(folder):
    # An ad-hoc integrity seal is not a Developer ID signature or notarization.
    # The manifest is sealed with the resources; it excludes only the launcher
    # and seal itself to avoid a circular hash dependency.
    subprocess.run(["codesign", "--force", "--sign", "-", "--timestamp=none", str(folder)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(folder)], check=True)


def write_manifest(folder, version, rid, dependencies):
    manifest_name = manifest_relative(folder)
    (folder / manifest_name).parent.mkdir(parents=True, exist_ok=True)
    mac_seal = rid == "osx-arm64" and tuple(map(int, version.split("."))) >= (0, 1, 1)
    files = {p.relative_to(folder).as_posix(): {"size": p.stat().st_size, "sha256": sha256(p)}
             for p in sorted(folder.rglob("*")) if p.is_file() and (not mac_seal or p.relative_to(folder).as_posix() not in MAC_SIGNATURE_FILES)}
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True))
    manifest = {"formatVersion": 1, "layoutVersion": 2, "manifestPath": manifest_name, "product": "Grid Mapper", "version": version,
                "runtimeIdentifier": rid, "sourceCommit": commit, "sourceDirty": dirty,
                "distributionSigned": False, "dependencies": dependencies, "files": files}
    if mac_seal: manifest["bundleSignature"] = "adhoc-v1"
    (folder / manifest_name).write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def verify(folder):
    folder = pathlib.Path(folder)
    if folder.is_symlink():
        raise ValueError("Unsafe package root symlink")
    manifest_name = manifest_relative(folder)
    manifest_path = folder / manifest_name
    if any(p.is_symlink() for p in manifest_path.parents if p.is_relative_to(folder)):
        raise ValueError("Unsafe package symlink in manifest path")
    if manifest_path.is_symlink():
        raise ValueError("Unsafe manifest symlink")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest["formatVersion"] != 1:
        raise ValueError("Unsupported package manifest")
    if "layoutVersion" in manifest and manifest["layoutVersion"] != 2:
        raise ValueError("Unsupported package layout")
    if manifest.get("manifestPath", "package-manifest.json") != manifest_name:
        raise ValueError("Unexpected package manifest location")
    paths = list(folder.rglob("*"))
    for path in paths:
        if path.is_symlink():
            raise ValueError("Unsafe package symlink: " + path.relative_to(folder).as_posix())
    actual = {p.relative_to(folder).as_posix() for p in paths if p.is_file()}
    signed = manifest.get("bundleSignature")
    if signed is not None and (signed != "adhoc-v1" or manifest["runtimeIdentifier"] != "osx-arm64"):
        raise ValueError("Unsupported bundle signature")
    signature_files = MAC_SIGNATURE_FILES if signed else set()
    if signed and set(manifest["files"]) & signature_files:
        raise ValueError("Circular bundle-signature hashes")
    if actual != set(manifest["files"]) | {manifest_name} | signature_files:
        raise ValueError("Package file set differs from its manifest")
    for relative, expected in manifest["files"].items():
        path = folder / relative
        if not path.resolve().is_relative_to(folder.resolve()) or path.is_symlink():
            raise ValueError("Unsafe manifest entry: " + relative)
        if path.stat().st_size != expected["size"] or sha256(path) != expected["sha256"]:
            raise ValueError("Package file changed: " + relative)
    if manifest["runtimeIdentifier"] == "osx-arm64" and tuple(map(int, manifest["version"].split("."))) >= (0, 0, 54):
        info = plistlib.loads((folder / "Contents/Info.plist").read_bytes())
        if not info.get("NSLocalNetworkUsageDescription") or "_ndi._tcp" not in info.get("NSBonjourServices", []):
            raise ValueError("NDI local network usage / Bonjour declarations are missing")
    verify_application_layout(folder, manifest)
    if signed:
        subprocess.run(["codesign", "--verify", "--deep", "--strict", str(folder)], check=True)
    print(f"Verified {manifest['version']} {manifest['runtimeIdentifier']}: {len(manifest['files'])} files")


def preserve_existing_package(staged, output):
    """Versioned folders are immutable; reruns never delete an open application."""
    if not output.exists():
        os.replace(staged, output)
        return
    verify(output)
    old = json.loads((output / manifest_relative(output)).read_text(encoding="utf-8"))
    new = json.loads((staged / manifest_relative(staged)).read_text(encoding="utf-8"))
    if old != new:
        raise ValueError("Existing versioned package differs. Increment the version; existing package was preserved: " + str(output))
    print("Unchanged package preserved: " + str(output))


def package(rid):
    version = ET.parse(ROOT / "Directory.Build.props").findtext("PropertyGroup/Version")
    staging_root = ARTIFACTS / "packages"
    archive = staging_root / f"GridMapper-{version}-{rid}.zip"
    checksum = archive.with_suffix(".zip.sha256")
    if any(p.exists() or p.is_symlink() for p in (archive, checksum)):
        raise ValueError("Existing versioned archive preserved; increment the version before rebuilding: " + str(archive))
    published = ARTIFACTS / "publish" / rid
    deps = json.loads((published / "GridMapper.Desktop.deps.json").read_text())
    runtime = json.loads((published / "GridMapper.Desktop.runtimeconfig.json").read_text())
    if "GridMapper.Desktop/" + version not in deps["libraries"] or not deps["runtimeTarget"]["name"].endswith("/" + rid):
        raise ValueError("Published version/RID is stale; publish this version first")
    options = runtime["runtimeOptions"]
    if options.get("framework") or options.get("frameworks") or not options.get("includedFrameworks"):
        raise ValueError("A self-contained publish is required")
    required = (["GridMapper.Desktop", "libcoreclr.dylib", "libhostfxr.dylib", "libSkiaSharp.dylib", "libHarfBuzzSharp.dylib"]
                if rid == "osx-arm64" else ["GridMapper.Desktop.exe", "coreclr.dll", "hostfxr.dll", "libSkiaSharp.dll", "libHarfBuzzSharp.dll"])
    for name in required:
        if not (published / name).is_file():
            raise ValueError("Missing autonomous runtime file: " + name)
    staging_root.mkdir(parents=True, exist_ok=True)
    name = "Grid Mapper.app" if rid == "osx-arm64" else f"Grid Mapper {version} Windows x64"
    output = staging_root / version / name if rid == "osx-arm64" else staging_root / name
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".packaging-", dir=staging_root) as temporary:
        folder = pathlib.Path(temporary) / name
        payload = folder / "Contents/Resources" if rid == "osx-arm64" else folder
        copy_application_layout(published, payload, rid, runtime)
        dependencies = copy_notices(deps, runtime, payload)
        if rid == "osx-arm64":
            plist = {"CFBundleName": "Grid Mapper", "CFBundleDisplayName": "Grid Mapper",
                     "CFBundleIdentifier": "com.viavisionpro.gridmapper", "CFBundleExecutable": "GridMapper.Desktop",
                     "CFBundlePackageType": "APPL", "CFBundleShortVersionString": version, "CFBundleVersion": version,
                     "NSHighResolutionCapable": True,
                     "NSLocalNetworkUsageDescription": "Grid Mapper shares selected screens as NDI sources on your local network.",
                     "NSBonjourServices": ["_ndi._tcp"], "LSMinimumSystemVersion": "14.0", "CFBundleIconFile": "GridMapper.icns",
                     "CFBundleDocumentTypes": [{"CFBundleTypeName": "Grid Mapper project", "CFBundleTypeRole": "Editor",
                                                "CFBundleTypeIconFile": "GridMapper.icns", "LSHandlerRank": "Alternate",
                                                "LSItemContentTypes": ["com.viavisionpro.gridmapper.project"]}],
                     "UTExportedTypeDeclarations": [{"UTTypeIdentifier": "com.viavisionpro.gridmapper.project",
                                                     "UTTypeDescription": "Grid Mapper project", "UTTypeConformsTo": ["public.json"],
                                                     "UTTypeTagSpecification": {"public.filename-extension": ["gpm"],
                                                                                "public.mime-type": "application/vnd.gridmapper.project+json"}}]}
            resources = folder / "Contents/Resources"
            resources.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / "src/GridMapper.Desktop/Assets/GridMapper.icns", resources / "GridMapper.icns")
            with (folder / "Contents/Info.plist").open("wb") as stream:
                plistlib.dump(plist, stream)
            os.chmod(folder / "Contents/MacOS/GridMapper.Desktop", 0o755)
        if rid == "osx-arm64": sign_macos_payload(folder)
        write_manifest(folder, version, rid, dependencies)
        if rid == "osx-arm64": seal_macos_bundle(folder)
        verify(folder)
        preserve_existing_package(folder, output)
    with tempfile.TemporaryDirectory(prefix=".archive-", dir=staging_root) as temporary:
        partial = pathlib.Path(temporary) / archive.name
        if rid == "osx-arm64":
            subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(output), str(partial)], check=True)
        else:
            with zipfile.ZipFile(partial, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
                for path in sorted(output.rglob("*")):
                    if path.is_file():
                        zipped.write(path, path.relative_to(output.parent))
        # Link within the same filesystem: atomically refuse an existing name.
        os.link(partial, archive)
    with checksum.open("x", encoding="utf-8") as stream:
        stream.write(sha256(archive) + "  " + archive.name + "\n")
    print(output)
    print(archive)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=["osx-arm64", "win-x64"])
    parser.add_argument("--verify", type=pathlib.Path)
    args = parser.parse_args()
    if bool(args.rid) == bool(args.verify):
        parser.error("choose --rid or --verify")
    try:
        verify(args.verify) if args.verify else package(args.rid)
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Packaging failed: {error}\n")
