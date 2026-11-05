#!/bin/zsh
# Stage 9: smoke test of the NuGet packages from artifacts/packages (spec: "New platform features decisions (Stage 9)", iOS assets):
# MAUI app net10.0-ios and net10.0-ios26.4 → asset lib/net10.0-ios26.4, MAUI library net10.0-ios → lib/net10.0-ios26.0;
# every build with -warnaserror. CarKit packages (version from artifacts/packages; CARKIT_PACKAGE_VERSION overrides) from an isolated
# cache (cleaned before every run).
set -euo pipefail
C=${0:A:h}
cd $C
rm -rf packages/carkit.maui packages/carkit.maui.core packages/carkit.maui.navigation AppPlain/bin AppPlain/obj App264/bin App264/obj LibPlain/bin LibPlain/obj
asset() {
  python3 -I -c '
import json, sys
d = json.load(open(sys.argv[1]))
for target, libs in d["targets"].items():
    if "/" in target:
        continue
    for name, lib in libs.items():
        if name.lower().startswith("carkit.maui/"):
            print(target, " ".join(lib.get("compile", {}).keys()))
' $1
}
package_version=${CARKIT_PACKAGE_VERSION:-$(ls ../../artifacts/packages/CarKit.Maui.Core.*.nupkg | sed -E 's/.*CarKit\.Maui\.Core\.(.*)\.nupkg/\1/')}
version=(-p:CarKitPackageVersion=$package_version)
rm -rf packages/carkit.maui.media packages/carkit.maui.communication packages/carkit.maui.testing
dotnet build AppPlain -c Debug -r iossimulator-arm64 -warnaserror $version
dotnet build App264 -c Debug -r iossimulator-arm64 -warnaserror $version
dotnet build LibPlain -c Debug -warnaserror $version
asset AppPlain/obj/project.assets.json | tee /dev/stderr | grep -q 'net10.0-ios26.4 lib/net10.0-ios26.4/CarKit.Maui.dll'
asset App264/obj/project.assets.json | tee /dev/stderr | grep -q 'net10.0-ios26.4 lib/net10.0-ios26.4/CarKit.Maui.dll'
asset LibPlain/obj/project.assets.json | tee /dev/stderr | grep -q 'net10.0-ios26.0 lib/net10.0-ios26.0/CarKit.Maui.dll'
# The generator and targets from the package work in the app: CarPlay scene roles and the entitlement in the app Info.plist
plutil -p AppPlain/bin/Debug/net10.0-ios/iossimulator-arm64/AppPlain.app/Info.plist | grep -q 'CarKitSceneDelegate'
# Stage 10: the category key and team prefix in the simulated entitlements (without them the CarPlay window does not show
# the app or is empty)
plutil -p AppPlain/obj/Debug/net10.0-ios/iossimulator-arm64/Entitlements-Simulated.xcent | grep -q '"com.apple.developer.carplay-maps" => true'
plutil -p AppPlain/obj/Debug/net10.0-ios/iossimulator-arm64/Entitlements-Simulated.xcent | grep -q '"application-identifier" => "FAKETEAMID\.'
echo "SMOKE OK"
