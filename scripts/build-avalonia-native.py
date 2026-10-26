#!/usr/bin/env python3
"""Rebuild the narrowly patched, pinned Avalonia macOS ARM64 backend (Xcode required)."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
from concurrent.futures import ThreadPoolExecutor

ROOT = pathlib.Path(__file__).resolve().parents[1]
VENDOR = ROOT / "third_party/avalonia-native"
WORK = ROOT / ".tools/avalonia-native-memory-fix"


def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install", action="store_true", help="Update the checked-in dylib and its SHA-256")
    args = parser.parse_args()
    manifest = json.loads((VENDOR / "upstream-files.json").read_text())

    def fetch(entry):
        target = WORK / entry["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        # Always restore pristine upstream files, including app.mm, before patching.
        run("curl", "--fail", "--silent", "--show-error", "--retry", "2",
            "https://raw.githubusercontent.com/AvaloniaUI/Avalonia/" + manifest["commit"] + "/" + entry["path"], "-o", str(target))
        data = target.read_bytes()
        digest = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        if digest != entry["gitBlobSha1"]:
            raise ValueError("Upstream source hash differs: " + entry["path"])

    with ThreadPoolExecutor(max_workers=8) as pool:
        list(pool.map(fetch, manifest["files"]))
    subprocess.run(["patch", "--batch", "--forward", "-p1", "-i", str(VENDOR / "files-opened.patch")], cwd=WORK, check=True)
    # Isolate this generator from the application's build properties and central versions.
    (WORK / "Directory.Build.props").write_text("<Project />\n")
    (WORK / "Generate.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultItems>false</EnableDefaultItems><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageReference Include="MicroCom.CodeGenerator.MSBuild" Version="0.11.6" PrivateAssets="all" />
    <MicroComIdl Include="src/Avalonia.Native/avn.idl" CppHeaderPath="native/Avalonia.Native/inc/avalonia-native.h" />
  </ItemGroup>
</Project>\n''')
    run("bash", "scripts/dotnet.sh", "build", str(WORK / "Generate.csproj"), "-c", "Release",
        "-p:RestorePackagesPath=" + str(ROOT / ".cache/nuget"))
    run("xcodebuild", "-quiet", "-project", str(WORK / "native/Avalonia.Native/src/OSX/Avalonia.Native.OSX.xcodeproj"),
        "-configuration", "Release", "CONFIGURATION_BUILD_DIR=" + str(WORK / "output"),
        "ARCHS=arm64", "ONLY_ACTIVE_ARCH=YES", "GCC_WARN_INHIBIT_ALL_WARNINGS=YES", "CODE_SIGNING_ALLOWED=NO")
    binary = WORK / "output/libAvalonia.Native.OSX.dylib"
    run("lipo", str(binary), "-verify_arch", "arm64")
    if args.install:
        destination = VENDOR / "osx-arm64"
        destination.mkdir(exist_ok=True)
        installed = destination / "libGridMapperAvaloniaNative.dylib"
        shutil.copy2(binary, installed)
        shutil.copy2(VENDOR / "LICENSE.txt", destination / "LICENSE.txt")
        (destination / "README.txt").write_text(
            "Avalonia.Native 12.1.3, MIT; upstream " + manifest["commit"] + "\n"
            "Grid Mapper patch: release file/URL activation COM arrays after callbacks.\n"
            "Source, patch and rebuild instructions: third_party/avalonia-native in the Grid Mapper repository.\n")
        (destination / "SHA256.txt").write_text(hashlib.sha256(installed.read_bytes()).hexdigest() + "  " + installed.name + "\n")
        print("Installed", installed)
    else:
        print("Built", binary, "(pass --install to replace the bundled copy)")


if __name__ == "__main__":
    main()
