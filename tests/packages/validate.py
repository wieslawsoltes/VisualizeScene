"""Restore and exercise every VisualizeScene nupkg outside the source tree."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

repo = Path(__file__).resolve().parents[2]
feed = Path(sys.argv[1]).resolve()
output = repo / "artifacts/package-validation"
output.mkdir(parents=True, exist_ok=True)
expected = {"Core", "Algorithms", "Layout", "Editing", "Serialization", "Rendering.Skia", "Controls.Uno"}
report = {"packages": [], "commands": [], "passed": False}
version = ET.parse(repo / "Directory.Build.props").findtext(".//Version")

try:
    found = set()
    for package in sorted(feed.glob("*.nupkg")):
        with zipfile.ZipFile(package) as archive:
            names = archive.namelist()
            metadata = ET.fromstring(archive.read(next(n for n in names if n.endswith(".nuspec"))))
            package_id = next(e.text for e in metadata.iter() if e.tag.split("}")[-1] == "id")
            found.add(package_id.removeprefix("VisualizeScene."))
            dlls = [n for n in names if n.startswith("lib/") and n.endswith(".dll")]
            assert dlls, f"{package_id}: package has no assemblies"
            report["packages"].append({"id": package_id, "file": package.name, "assemblies": dlls,
                "sha256": hashlib.sha256(package.read_bytes()).hexdigest()})
    assert found == expected, f"Expected seven packages; found {found}"
    with tempfile.TemporaryDirectory(prefix="visualizescene-package-consumer-") as directory:
        root = Path(directory)
        shutil.copy(repo / "global.json", root)
        for project in ["CoreConsumer", "UnoConsumer"]:
            shutil.copytree(repo / "tests/packages" / project, root / project)
        for project in root.rglob("*.csproj"):
            assert not list(ET.parse(project).iter("ProjectReference")), f"Source dependency in {project}"
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="tested-packages", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        ET.ElementTree(config).write(root / "NuGet.Config", encoding="unicode")
        env = os.environ.copy()
        # A fresh cache prevents an older 0.1.0 assembly from satisfying the test.
        env["NUGET_PACKAGES"] = str(root / ".packages")
        commands = [
            ["dotnet", "run", "--project", "CoreConsumer", "-c", "Release"],
            ["dotnet", "build", "UnoConsumer", "-c", "Release", "-f", "net10.0-desktop"],
            ["dotnet", "build", "UnoConsumer", "-c", "Release", "-f", "net10.0-browserwasm"],
        ]
        for command in commands:
            command.append(f"-p:VisualizeSceneVersion={version}")
            result = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            text = result.stdout
            log = f"command-{len(report['commands']) + 1}.log"
            (output / log).write_text(text, encoding="utf-8")
            report["commands"].append({"arguments": command, "exitCode": result.returncode, "log": log})
            print(text, flush=True)
            if result.returncode:
                raise RuntimeError(f"Package consumer failed: {command}")
        # No VisualizeScene dependency is allowed to resolve to a source project.
        for assets in root.glob("*/obj/project.assets.json"):
            data = json.loads(assets.read_text())
            libs = {key: value for key, value in data["libraries"].items() if key.startswith("VisualizeScene.")}
            assert libs and all(value["type"] == "package" for value in libs.values()), str(assets)
        report["passed"] = True
finally:
    (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
