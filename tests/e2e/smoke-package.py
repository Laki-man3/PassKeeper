#!/usr/bin/env python3
"""Verify and exercise an extracted desktop release package, never the publish directory.

macOS: python3 -I tests/e2e/smoke-package.py package.zip output-directory
Linux: run under dbus-run-session + Xvfb with a window manager and an unlocked test keyring.
Set PASSKEEPER_SMOKE_PIN=1 to require native keyring/PIN checks.
"""
import argparse
import os
import pathlib
import re
import subprocess
import sys
import tarfile
import tempfile
import zipfile


def run(command, **kwargs):
    return subprocess.run(command, check=True, **kwargs)


def safe_names(names):
    for name in names:
        path = pathlib.PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts:
            raise ValueError(f"Unsafe archive member: {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    package = args.package.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryDirectory(prefix="passkeeper-package-") as directory:
        extracted = pathlib.Path(directory)
        if sys.platform == "darwin":
            with zipfile.ZipFile(package) as archive:
                safe_names(archive.namelist())
                executable_info = archive.getinfo("PassKeeper.app/Contents/MacOS/PassKeeper")
                if not (executable_info.external_attr >> 16) & 0o111:
                    raise AssertionError("ZIP does not preserve the executable permission")
            run(["/usr/bin/ditto", "-x", "-k", str(package), str(extracted)])
            app = extracted / "PassKeeper.app"
            executable = app / "Contents/MacOS/PassKeeper"
            run(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(app)])
            for dylib in (app / "Contents/Resources/runtime").rglob("*.dylib"):
                run(["/usr/bin/codesign", "--verify", "--strict", str(dylib)])
            # Launch Services checks the actual .app layout, unlike starting apphost directly.
            command = ["/usr/bin/open", "-n", "-W", "-a", str(app), "--args", "--smoke-test", str(output)]
        elif sys.platform.startswith("linux"):
            with tarfile.open(package) as archive:
                safe_names(archive.getnames())
                if any(member.issym() or member.islnk() or member.isdev() for member in archive.getmembers()):
                    raise ValueError("Unexpected link or device in Linux package")
                archive.extractall(extracted)
            executables = list(extracted.glob("PassKeeper-*/PassKeeper"))
            if len(executables) != 1:
                raise AssertionError("Expected one packaged PassKeeper executable")
            executable = executables[0]
            command = [str(executable), "--smoke-test", str(output)]
        else:
            raise RuntimeError("This smoke test is for macOS and Linux desktop packages")

        if not os.access(executable, os.X_OK):
            raise AssertionError("Packaged executable lost its executable permission")
        with (output / "process.log").open("w") as log:
            try:
                run(command, stdout=log, stderr=subprocess.STDOUT, timeout=120)
            finally:
                # A failed Launch Services wait must not leave a test app behind on a runner.
                if sys.platform == "darwin":
                    subprocess.run(["/usr/bin/pkill", "-f", "^" + re.escape(str(executable)) + " --smoke-test "], check=False)
        result = output / "result.txt"
        if result.exists():
            print(result.read_text())
        # `open -W` does not propagate the application's exit code. Require its success marker too.
        if not (output / "passed").is_file():
            raise AssertionError("Native UI smoke test did not complete successfully")
        for screenshot in ("01-create.png", "02-vault.png", "03-reopened.png"):
            if (output / screenshot).stat().st_size < 1000:
                raise AssertionError(f"Missing/empty UI screenshot: {screenshot}")
        print(f"Package smoke passed: {package.name}")


if __name__ == "__main__":
    main()
