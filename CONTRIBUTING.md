# Contributing to CarKit.Maui

See the design document and stage plan ([docs/specification.md](docs/specification.md)), the manual verification steps
([docs/manual-verification.md](docs/manual-verification.md)) and the release checklist
([docs/release-checklist.md](docs/release-checklist.md)). Names of types, namespaces and projects
in the specification are binding: change the specification first.

This project follows the [code of conduct](CODE_OF_CONDUCT.md). Report vulnerabilities as described in
[SECURITY.md](SECURITY.md), not in public issues.

## Requirements

- .NET SDK **10.0.201** or a later patch of the 10.0.2xx band. `global.json` (`rollForward: latestPatch`) rejects other
  bands such as 10.0.3xx. Installer: [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
  or:

  ```bash
  ./dotnet-install.sh --version 10.0.201
  ```

- The MAUI workloads for this SDK. Workloads are installed per SDK band, so run the command **in the repository
  directory**, where `global.json` selects SDK 10.0.2xx; outside it the command would target the default SDK. Add `sudo`
  if .NET is installed in `/usr/local/share/dotnet` (the `.pkg` installer).

  ```bash
  dotnet workload install maui-ios maui-android --version 10.0.204.1
  ```

- Check in the repository directory: `dotnet --version` → `10.0.201` (or a later 10.0.2xx), `dotnet workload --version`
  → `10.0.204.1`.
- Xcode 26.4 (matching the iOS 26.4 workload) and an iOS simulator. Xcode 26.0 only for the tests of the `net10.0-ios`
  build (iOS 26.0 reference pack): `IOS_ASSET=26.0 eng/run-device-tests.sh ios` builds through `DEVELOPER_DIR` without
  `xcode-select`.
- Android SDK with platform API 36 and JDK 17.
- Local .NET tools (DocFX for the documentation): `dotnet tool restore`.

## Build and test

```bash
dotnet build CarKit.Maui.slnx
dotnet test CarKit.Maui.slnx
```

The build treats warnings as errors; CI also passes `-warnaserror` so MSBuild warnings (`CARKIT00x`) fail it.
Public API changes need the PublicAPI files of every platform (`src/*/PublicAPI/<net|ios|ios26.4|android>/`):

```bash
python3 -I eng/accept-public-api.py src/CarKit.Maui.Core   # after a build that reports RS0016/RS0017
```

New API goes to `PublicAPI.Unshipped.txt`; a release moves it to `PublicAPI.Shipped.txt` (the release gate requires an
empty `Unshipped` file, only `#nullable enable`).

| Project | Contents |
| --- | --- |
| `src/` | The six packages and the Android source generator |
| `tests/CarKit.Maui.Core.Tests` | Model, session, validator, rules, `FakeCarHost` |
| `tests/CarKit.Maui.Tests` | Hosting, renderer logic, module logic and sample screens (`net10.0`, linked sources) |
| `tests/CarKit.Maui.Build.Tests` | Source generator, MSBuild targets, release targets and the release gate |
| `tests/CarKit.Maui.DeviceTests` | Renderers on the iOS simulator and the Android emulator |
| `samples/` | Parking and category flows, Media, Communication (+ Intents Extension), Navigation |
| `eng/` | Scripts: device tests, PublicAPI, NuGet smoke test, quickstart check, documentation, release gate |

## Device tests

`tests/CarKit.Maui.DeviceTests` is a MAUI app that renders every template with the real renderers and checks the native
objects (`CP*` on CarPlay, Car App Library models on Android Auto) without a car:

```bash
eng/run-device-tests.sh ios       # booted iPhone simulator or the first available (CARKIT_SIMULATOR=<UDID>)
eng/run-device-tests.sh android   # emulator or device from `adb devices` (ANDROID_SERIAL=<serial>)
```

The result is the line `CARKIT-DEVICE-TESTS: PASS n FAIL m` and exit code 0 for `FAIL 0`; the full output is in
`artifacts/device-tests-<platform>.log`. Options: `CONFIGURATION=Release` (AOT on the simulator), `IOS_ASSET=26.0`
(the `net10.0-ios` build of the libraries), `DEVICE_TESTS_FILTER=<part of a test name>` and `DEVICE_TESTS_REPEAT=<n>`
to investigate a flaky test. The test app has no CarPlay entitlements (`CarKitIosEntitlements=false`), because the audio
entitlement lowers the limits of the platform for the whole app. An incremental build does not link changed entitlements
into the executable, so delete `bin` and `obj` after changing them.

## Packages, documentation and the quickstart

```bash
dotnet pack CarKit.Maui.slnx -c Release -o artifacts/packages   # 6 .nupkg + 6 .snupkg, version 0.1.0-dev
eng/nuget-smoke/smoke.sh                                         # consumers of the packages pick the right iOS build
eng/quickstart/run.sh                                            # runs the steps of docs/site/articles/quickstart.md
eng/docs/build.sh                                                # documentation site in artifacts/docs/_site
```

`eng/quickstart/run.sh` executes the code blocks of the guide that carry `<!-- quickstart: … -->` markers on a new
`dotnet new maui` project, so keep the markers when you edit the guide. The documentation build fails on any DocFX
warning (broken links and bookmarks, unresolved `xref`).

## Releases

Version `0.1.0` comes from `VersionPrefix` in `Directory.Build.props`. Local and CI builds get the `-dev` suffix; a
release build (`-p:CarKitRelease=true`, or a `v*` tag in GitHub Actions) has the version of the tag. Release packs stop
with:

| Code | Reason |
| --- | --- |
| `CARKITREL001` | No `PackageLicenseExpression` or `PackageLicenseFile` |
| `CARKITREL002` | No repository URL or commit (pack from a git clone with an `origin` remote) |
| `CARKITREL003` | The tag does not match `VersionPrefix` |

The release gate checks the finished packages:

```bash
dotnet run --project eng/ReleaseGate -c Release -- --packages artifacts/packages --tag v0.1.0-beta.1 --repo .
```

Builds without git metadata (a source archive) can pass `-p:CarKitSourceRevisionId=<commit> -p:CarKitRepositoryUrl=<url>`
to get Source Link. `.github/workflows/release.yml` runs the whole sequence on a `v*` tag and pushes to NuGet when the
`NUGET_API_KEY` secret exists; the steps before the first release are in [docs/release-checklist.md](docs/release-checklist.md).
