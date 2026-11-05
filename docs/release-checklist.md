# Release checklist

Three parts: the owner steps (in order), an automated part and a manual part. Tests in CarPlay Simulator, Android Auto DHU and on a real car are manual: check off only what was actually done on a device, in CarPlay Simulator or in the DHU. Each release adds a row to the "Results" table.

State at the end of Stage 10 (2026-11-04): the automated part is green locally; of the manual part only the quickstart guide in the CarPlay Simulator window was done (no DHU, car or signed builds in the implementer's environment); the owner steps remain to be done. The Media, Communication and Navigation samples were not opened in the CarPlay window: start with them, because until Stage 10 no CarKit app was shown in that window.

## Owner steps (in this order)

- [x] **1. Choose a license.** MIT (2026-11-05): `LICENSE` in the repo root, `PackageLicenseExpression` and `Copyright` in `Directory.Build.props`. To change the license, change both files. Without a license the release pack fails with the error `CARKITREL001`, and the gate with the `license` check.
- [ ] **2. GitHub repository and first commits.** Create an empty repo on GitHub (public: Source Link fetches sources from `raw.githubusercontent.com`), then in the project directory (an example, `<account>` and the repo name to be filled in):

  ```bash
  git add .
  git commit -m "CarKit.Maui 0.1.0-beta.1"
  git branch -M main
  git remote add origin https://github.com/<account>/CarKit.Maui.git
  git push -u origin main
  ```

  The repo address reaches the packages by itself (Source Link reads `origin`): `repository url`, `commit`, `PackageProjectUrl`.
- [ ] **3. First `ci.yml` run.** After the push to `main`, check for a green run (Actions). The step "Device tests (iOS simulator, net10.0-ios asset)" warns when the runner image has no Xcode 26.0. The step "Release gate (informational)" always looks green (`continue-on-error`): open its log and expect `RELEASE-GATE: PASS 8 FAIL 0` (after steps 1–2 nothing should fail). The runner has no emulator acceleration, so the Android device tests are local only (`eng/run-device-tests.sh android`). Then in Settings → Advanced Security enable private vulnerability reporting, which `SECURITY.md` relies on, and protect `main` with a required CI check if you accept contributions.
- [ ] **4. Apple entitlements.** Request the CarPlay entitlement for the app category through Apple's form (developer.apple.com/contact/carplay), accept the CarPlay Entitlement Addendum; once it is granted, enable the capability in the App ID (Additional Capabilities) and create a new provisioning profile. Details: `docs/site/articles/entitlements.md`. Also needed for the manual scenarios on an iPhone and in TestFlight for the sample app or your own.
- [ ] **5. Google Play approvals.** For apps published in the store: Play Console → Advanced settings → Form factors → Android Auto, release to a testing track, quality review (car app quality guidelines). The library itself does not go through the review; it applies to the apps that use it (to start with, at least verifying the sample on a testing track, if needed).
- [ ] **6. Manual scenarios.** Do the manual part below and enter the result in the "Results" table. Everything is required before 1.0. For a beta, open at least the Media, Communication and Navigation samples in the CarPlay window.
- [ ] **7. Trial release without publishing.** The first real release-mode pack and Source Link from a real clone are verified only by GitHub, and `release.yml` without the secret ends at the artifacts:

  ```bash
  git tag v0.1.0-alpha.1
  git push origin v0.1.0-alpha.1
  ```

  Expected: the workflow is green, `RELEASE-GATE: PASS 8 FAIL 0` is in the "Release gate" log, the `packages` artifact has 6 × `.nupkg` and 6 × `.snupkg` in version `0.1.0-alpha.1`, and there is a message about the skipped push.
- [ ] **8. The `NUGET_API_KEY` secret.** A NuGet.org API key with the scope "push new packages and package versions" for `CarKit.Maui*` (check that the identifiers are free) in Settings → Secrets and variables → Actions.
- [ ] **9. Date in CHANGELOG and the tag.** In `CHANGELOG.md` replace "Unreleased" with the release date, commit, then:

  ```bash
  git push origin main
  git tag v0.1.0-beta.1
  git push origin v0.1.0-beta.1
  ```

  The tag triggers `.github/workflows/release.yml`: build, tests, release pack (version = tag, `CARKITREL003` on a mismatch with `VersionPrefix`), release gate, smoke test, quickstart guide, documentation, artifacts, `dotnet nuget push` (`.nupkg` and `.snupkg`). The next betas use `v0.1.0-beta.2` and so on. For 1.0, first set `VersionPrefix` to `1.0.0`, after all mandatory scenarios pass.
- [ ] **10. Documentation site.** Enable GitHub Pages (Settings → Pages → Source: GitHub Actions) and after a successful release run the "Documentation site" workflow manually from the `main` branch (`.github/workflows/docs.yml`). Enter the site address in `src/*/README.md` at the next release.

## Automated part

Locally (macOS, Xcode 26.4, simulators and emulator from `CONTRIBUTING.md`):

```bash
dotnet build CarKit.Maui.slnx -c Release -warnaserror
dotnet test tests/CarKit.Maui.Core.Tests -c Release --no-build
dotnet test tests/CarKit.Maui.Tests -c Release --no-build
dotnet test tests/CarKit.Maui.Build.Tests -c Release --no-build
eng/run-device-tests.sh ios                                   # iOS 26.0 and iOS 26.4: CARKIT_SIMULATOR=<UDID>
CONFIGURATION=Release eng/run-device-tests.sh ios             # Release (AOT)
IOS_ASSET=26.0 eng/run-device-tests.sh ios                    # net10.0-ios asset (Xcode 26.0 through DEVELOPER_DIR)
eng/run-device-tests.sh android
dotnet pack CarKit.Maui.slnx -c Release --no-build -o artifacts/packages
eng/nuget-smoke/smoke.sh                                      # package smoke test
eng/quickstart/run.sh                                         # quickstart guide steps and code (QUICKSTART_SIMULATOR, QUICKSTART_ANDROID_SERIAL: launch on a device)
eng/docs/build.sh --no-build                                  # documentation without DocFX warnings
dotnet run --project eng/ReleaseGate -c Release -- --packages artifacts/packages --tag v0.1.0-dev --repo .
```

The release gate (`eng/ReleaseGate`) checks: `packages`, `version`, `license`, `repository`, `readme`, `symbols`, `sourcelink`, `publicapi`. The tag in `--tag` must be the package version (locally `v0.1.0-dev`, in a release the git tag). Locally, in a repo without a commit, FAIL for `repository` and `sourcelink` is expected. With `-p:CarKitSourceRevisionId=<commit> -p:CarKitRepositoryUrl=<url>` at pack time all checks pass. Without those properties a full pass is possible only on GitHub after steps 1–2.

## Manual part: mandatory scenarios before each release

Steps for the sample (CarPlay Simulator, DHU, launch without the phone UI, Release builds on a device): [manual-verification.md](manual-verification.md).

- [ ] Car connection and disconnection while data is loading
- [ ] Starting a car session without the app open on the phone (iOS locked, Android without `Activity`)
- [ ] Switching the light/dark theme during a session
- [ ] Push to the depth limit and the validator message
- [ ] Updating a list during an item spinner
- [ ] Audio: interruption by a phone call and playback resuming
- [ ] Navigation: reroute, alert, pause, end, second screen (Dashboard, CarPlay cluster, Android Auto cluster), voice prompts with ducking, map frame measurement (`nav.LogFrameTimes`) — steps: [manual-verification.md](manual-verification.md) (Stage 8)
- [ ] Reconnecting after a process restart
- [ ] New platform features: iOS 26.4 features on a device with iOS 26.4 and the fallback on older iOS, route sharing in a car with that feature, an app built from `net10.0-ios26.0` (the 26.0 asset) — steps: [manual-verification.md](manual-verification.md) (Stage 9)
- [x] Quickstart guide in CarPlay Simulator: `MyCarApp` from `eng/quickstart/work/MyCarApp` (I/O → External Displays → CarPlay) — list, details, alert (2026-11-04, iPhone 17 iOS 26.0, local dev packages)
- [ ] Quickstart guide in the DHU: list, details, alert

## Results

| Version | Date | Device / car | Result | Notes |
| --- | --- | --- | --- | --- |
| | | | | |
