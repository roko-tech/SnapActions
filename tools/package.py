"""Build and verify a fresh Windows package, without replacing a running installation."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def run(*command, env=None, timeout=300):
    subprocess.run(command, cwd=ROOT, env=env, check=True, timeout=timeout)


def checksum(path):
    with path.open("rb") as file:
        return hashlib.file_digest(file, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("SnapActions packaging and native checks require Windows.")
    output = (args.output or ROOT / "artifacts" / datetime.now(timezone.utc).strftime("package-%Y%m%d-%H%M%S")).resolve()
    if output.exists() and any(output.iterdir()):
        parser.error("Output must be a new or empty directory; existing packages are never overwritten.")
    output.mkdir(parents=True, exist_ok=True)
    publish = output / "publish"
    receipt = output / "checks"
    receipt.mkdir()
    run("dotnet", "restore", "SnapActions.Tests/SnapActions.Tests.csproj", "--locked-mode")
    run("dotnet", "test", "SnapActions.Tests/SnapActions.Tests.csproj", "-c", "Release", "--no-restore", "-warnaserror",
        "--logger", "trx;LogFileName=tests.trx", "--results-directory", str(receipt))
    run("node", "--test", "browser-extension/tests/selection.test.cjs")
    run("dotnet", "publish", "SnapActions/SnapActions.csproj", "-c", "Release", "-r", "win-x64", "--self-contained",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none", "-p:RestoreLockedMode=true", "-warnaserror", "-o", str(publish))
    environment = os.environ.copy()
    environment["SNAPACTIONS_DATA_DIR"] = str(receipt / "ui")
    run(str(publish / "SnapActions.exe"), "--self-test", env=environment, timeout=60)
    result = json.loads((receipt / "ui" / "self-test.json").read_text(encoding="utf-8"))
    if not result["passed"]:
        raise RuntimeError(result["failure"])
    run(os.sys.executable, "browser-extension/tests/native-host-smoke.py", str(publish / "SnapActions.exe"), timeout=45)
    files = sorted(p for p in publish.rglob("*") if p.is_file())
    (publish / "SHA256SUMS").write_text("".join(f"{checksum(p)}  {p.relative_to(publish).as_posix()}\n" for p in files), encoding="utf-8")
    version = ET.parse(ROOT / "SnapActions" / "SnapActions.csproj").findtext(".//Version")
    archive = output / f"SnapActions-{version}-win-x64.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        for path in sorted(p for p in publish.rglob("*") if p.is_file()):
            package.write(path, path.relative_to(publish))
    (output / "SHA256SUMS").write_text(f"{checksum(archive)}  {archive.name}\n", encoding="utf-8")
    print(json.dumps({"package": str(archive), "sha256": checksum(archive), "checks": result["checks"]}, indent=2))


if __name__ == "__main__":
    main()
