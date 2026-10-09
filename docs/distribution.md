# Distribution and reproducible validation

## Browser

The app is a compiled Uno/.NET WebAssembly application. No separate JavaScript implementation is used to make the demo or tests appear functional.

```sh
dotnet workload install wasm-tools
dotnet publish samples/VisualizeScene.Workbench -c Release -f net10.0-browserwasm \
  -p:VisualizeSceneTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/VisualizeScene/ -o artifacts/browser-publish
```

`WasmShellWebAppBasePath` is the Uno bootstrap property. The project defaults to `./` for relocatable hosting. The older-looking name `WasmShellBasePath` has no effect and must not be relied upon.

Stage the folder containing `index.html`; it contains `_framework` and a versioned `package_*` folder. The build workflow stages that output under `/VisualizeScene/`, executes browser interaction tests, and uploads the exact staged site. The Pages workflow consumes only a successful **main-branch push** build from this repository, not a pull-request artifact.

## Desktop

The desktop workflow publishes self-contained applications on Linux, Windows and macOS runners:

| Artifact | Target |
|---|---|
| desktop-linux-x64 | Linux x64 |
| desktop-win-x64 | Windows x64 |
| desktop-osx-arm64 | macOS Apple Silicon |

```sh
dotnet publish samples/VisualizeScene.Workbench -c Release -f net10.0-desktop \
  -r linux-x64 --self-contained true \
  -p:VisualizeSceneTargetFrameworks=net10.0-desktop \
  -p:PublishSingleFile=true -o artifacts/desktop
```

`VisualizeSceneTargetFrameworks` only affects the Uno projects. Do not globally override `TargetFrameworks` for the whole project graph, because the portable libraries deliberately target plain `net10.0`.

These are unsigned development distributions, not signed installers or notarized releases. Native OS graphical execution, accessibility and driver behavior are not proven merely by publication. The workflow also executes portable model/layout/serialization tests on each native runner. Hardware graphics and native UI tests are separate validation obligations.

## Reusable libraries

The build workflow produces six framework-neutral NuGet packages in `packages` and the dual-target Uno control package in `uno-controls`. Each package includes its dependency metadata. The libraries have no dependency on the workbench application. No publication to nuget.org is performed by these workflows.

## Evidence

- `source`: source archive of the tested revision.
- `conformance`: JUnit XML from the portable contract tests.
- `browser-validation`: screenshots, browser exceptions/console logs, read-only diagnostic snapshot and test report.
- `browser`: actual WebAssembly deployment files.
- `packages`, `uno-controls`: reusable NuGet artifacts.
- `desktop-*`: self-contained desktop distributions, when the desktop workflow succeeds.

Do not substitute a successful compile for a functional test result. A report records only tests actually executed. Browser CI uses a software graphics adapter; it does not certify physical GPU throughput.
