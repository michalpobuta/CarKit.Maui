#!/bin/zsh
# CarKit renderer tests (tests/CarKit.Maui.DeviceTests) on an iOS simulator or an Android emulator/device.
# Usage: eng/run-device-tests.sh ios|android
#   CONFIGURATION (Debug by default), CARKIT_SIMULATOR (UDID; the booted iPhone or the first available by default),
#   ANDROID_SERIAL (the first device from `adb devices` by default), DEVICE_TESTS_LOG (file with the full output).
#   Stage 9: IOS_ASSET=26.0 — the test app targets net10.0-ios26.0, so the CarKit libraries provide the net10.0-ios asset
#   (ref pack 26.0); the iOS 26.0 SDK requires Xcode 26.0, which XCODE_26_0 points to (/Applications/Xcode_26.app by default)
#   — only for `dotnet build` (DEVELOPER_DIR), without xcode-select; simctl stays with the selected Xcode.
#   Stage 10: DEVICE_TESTS_FILTER (a substring of the test name) and DEVICE_TESTS_REPEAT (repetitions in one process)
#   — investigating flaky tests.
# Exit code 0 only when the app printed "CARKIT-DEVICE-TESTS: PASS n FAIL 0".
set -euo pipefail

platform=${1:?usage: eng/run-device-tests.sh ios|android}
repo=${0:A:h:h}
project=$repo/tests/CarKit.Maui.DeviceTests/CarKit.Maui.DeviceTests.csproj
output=$repo/tests/CarKit.Maui.DeviceTests/bin
config=${CONFIGURATION:-Debug}
log=${DEVICE_TESTS_LOG:-$repo/artifacts/device-tests-$platform.log}
bundle=com.carkit.devicetests
mkdir -p ${log:h}

case $platform in
  ios)
    rid=iossimulator-$( [[ $(uname -m) == arm64 ]] && echo arm64 || echo x64 )
    udid=${CARKIT_SIMULATOR:-$(xcrun simctl list devices -j | python3 -I -c '
import json, sys
devices = [d for runtime, ds in json.load(sys.stdin)["devices"].items() if "iOS" in runtime for d in ds if d.get("isAvailable")]
phones = [d for d in devices if d["name"].startswith("iPhone")]
booted = [d for d in phones if d["state"] == "Booted"]
print((booted or phones)[0]["udid"] if phones else "")')}
    [[ -n $udid ]] || { echo "No iPhone simulator available" >&2; exit 2; }
    xcrun simctl bootstatus $udid -b > /dev/null
    if [[ ${IOS_ASSET:-} == 26.0 ]]; then
      xcode26=${XCODE_26_0:-/Applications/Xcode_26.app}
      [[ -d $xcode26 ]] || { echo "IOS_ASSET=26.0 needs Xcode 26.0 at $xcode26 (XCODE_26_0)" >&2; exit 2; }
      tfm=net10.0-ios26.0
      DEVELOPER_DIR=$xcode26/Contents/Developer dotnet build $project -f $tfm -c $config -r $rid -p:CarKitDeviceTestsIosVersion=26.0
    else
      tfm=net10.0-ios
      dotnet build $project -f $tfm -c $config -r $rid
    fi
    app=$output/$config/$tfm/$rid/CarKit.Maui.DeviceTests.app
    xcrun simctl install $udid $app
    # --console-pty waits for the process to end and passes through the app stdout; SIMCTL_CHILD_* go to the app environment
    SIMCTL_CHILD_CARKIT_DEVICE_TESTS_FILTER=${DEVICE_TESTS_FILTER:-} SIMCTL_CHILD_CARKIT_DEVICE_TESTS_REPEAT=${DEVICE_TESTS_REPEAT:-} \
      xcrun simctl launch --console-pty --terminate-running-process $udid $bundle > $log 2>&1 || true
    ;;
  android)
    serial=${ANDROID_SERIAL:-$(adb devices | awk 'NR > 1 && $2 == "device" { print $1; exit }')}
    [[ -n $serial ]] || { echo "No Android device or emulator" >&2; exit 2; }
    # Stage 6: an APK only for the device ABI (two ABIs mean ~2x native libraries, and the emulator /data has little space)
    case $(adb -s $serial shell getprop ro.product.cpu.abi | tr -d '\r') in
      x86_64) android_rid=android-x64 ;;
      *) android_rid=android-arm64 ;;
    esac
    # Debug without Fast Deployment: assemblies in the APK, because we install through adb install
    dotnet build $project -f net10.0-android -c $config -p:EmbedAssembliesIntoApk=true -p:RuntimeIdentifier=$android_rid
    # Uninstall first: "install -r" needs space for both versions (an emulator with a small /data partition)
    adb -s $serial uninstall $bundle > /dev/null 2>&1 || true
    adb -s $serial install $output/$config/net10.0-android/$android_rid/$bundle-Signed.apk > /dev/null
    # Stage 6: message notifications (POST_NOTIFICATIONS since Android 13) without the consent dialog
    adb -s $serial shell pm grant $bundle android.permission.POST_NOTIFICATIONS > /dev/null 2>&1 || true
    adb -s $serial shell am force-stop $bundle
    adb -s $serial logcat -c
    extras=()
    [[ -n ${DEVICE_TESTS_FILTER:-} ]] && extras+=(--es CARKIT_DEVICE_TESTS_FILTER $DEVICE_TESTS_FILTER)
    [[ -n ${DEVICE_TESTS_REPEAT:-} ]] && extras+=(--es CARKIT_DEVICE_TESTS_REPEAT $DEVICE_TESTS_REPEAT)
    adb -s $serial shell am start -n $bundle/carkit.maui.devicetests.MainActivity $extras > /dev/null
    # The app ends its process after the test; we wait for the summary line (5 min at most) or for the process to end without it
    for i in {1..300}; do
      adb -s $serial logcat -d -s CarKit.DeviceTests:I > $log
      grep -q "CARKIT-DEVICE-TESTS: PASS" $log && break
      if (( i > 15 )) && [[ -z $(adb -s $serial shell pidof $bundle) ]]; then
        adb -s $serial logcat -d | grep -E "monodroid|AndroidRuntime|FATAL" >> $log || true
        break
      fi
      sleep 1
    done
    ;;
  *)
    echo "usage: eng/run-device-tests.sh ios|android" >&2
    exit 2
    ;;
esac

grep -E "(PASS|FAIL|INFO) |CARKIT-DEVICE-TESTS" $log | sed -E 's/^.*CarKit\.DeviceTests: //' || true
summary=$(grep -o "CARKIT-DEVICE-TESTS: PASS [0-9]* FAIL [0-9]*" $log | tail -1 || true)
[[ -n $summary ]] || { echo "No test summary in $log" >&2; exit 1; }
[[ $summary == *"FAIL 0" ]]
