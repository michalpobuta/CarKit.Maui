#!/bin/zsh
# Stage 10: quickstart guide validation (spec: "Documentation and release decisions (Stage 10)", Quickstart guide):
# executes the steps and code of docs/site/articles/quickstart.md (apply.py) in eng/quickstart/work on a fresh
# `dotnet new maui`, with the CarKit packages from artifacts/packages (eng/quickstart/nuget.config, isolated cache
# eng/quickstart/packages), then for every `run` step:
#   ios     — simulator build with -warnaserror, Info.plist with the CarPlay scene role; with QUICKSTART_SIMULATOR=<UDID>
#             install, launch and a process check (then uninstall);
#   android — build with -warnaserror, a manifest with CarAppService, the POI category and MAP_TEMPLATES; with
#             QUICKSTART_ANDROID_SERIAL exactly the guide command (`dotnet build -t:Run -f net10.0-android`) on that device,
#             a process check (then uninstall).
# CARKIT_PACKAGE_VERSION (the CarKit.Maui.Core version from artifacts/packages by default), QUICKSTART_GUIDE (the guide from
# docs/site by default). Exit code 0 = "QUICKSTART OK".
set -euo pipefail
here=${0:A:h}
repo=${here:h:h}
guide=${QUICKSTART_GUIDE:-$repo/docs/site/articles/quickstart.md}
version=${CARKIT_PACKAGE_VERSION:-$(ls $repo/artifacts/packages/CarKit.Maui.Core.*.nupkg | sed -E 's/.*CarKit\.Maui\.Core\.(.*)\.nupkg/\1/')}
work=$here/work
app=$work/MyCarApp
bundle=com.companyname.mycarapp
rm -rf $work $here/packages/carkit.maui*(N)
mkdir -p $work
python3 -I $here/apply.py $guide $work $version
[[ -f $app/MyCarApp.csproj ]] || { echo "quickstart: the guide did not create MyCarApp/MyCarApp.csproj" >&2; exit 1; }

for platform in $(cat $work/run-steps.txt); do
  case $platform in
    ios)
      rid=iossimulator-$( [[ $(uname -m) == arm64 ]] && echo arm64 || echo x64 )
      dotnet build $app -f net10.0-ios -r $rid -warnaserror
      plist=$app/bin/Debug/net10.0-ios/$rid/MyCarApp.app/Info.plist
      plutil -p $plist | grep -q 'CPTemplateApplicationSceneSessionRoleApplication'
      plutil -p $plist | grep -q 'CarKitSceneDelegate'
      plutil -p $plist | grep -q '"UIApplicationSupportsMultipleScenes" => true'
      # Without these two entries the CarPlay Simulator window does not show the app or the host rejects its connection (empty screen)
      entitlements=$app/obj/Debug/net10.0-ios/$rid/Entitlements-Simulated.xcent
      plutil -p $entitlements | grep -q '"com.apple.developer.carplay-parking" => true'
      plutil -p $entitlements | grep -q '"application-identifier" => "FAKETEAMID\.'
      echo "quickstart: Info.plist has the CarPlay scene and the simulated entitlements the CarPlay window needs"
      if [[ -n ${QUICKSTART_SIMULATOR:-} ]]; then
        xcrun simctl bootstatus $QUICKSTART_SIMULATOR -b > /dev/null
        xcrun simctl install $QUICKSTART_SIMULATOR $app/bin/Debug/net10.0-ios/$rid/MyCarApp.app
        xcrun simctl launch --terminate-running-process $QUICKSTART_SIMULATOR $bundle
        sleep 8
        # The list in a variable: `… | grep -q` with pipefail ends with SIGPIPE when grep matches before the output ends
        # (a false "not running")
        services=$(xcrun simctl spawn $QUICKSTART_SIMULATOR launchctl list)
        [[ $services == *"UIKitApplication:$bundle"* ]] \
          || { echo "quickstart: $bundle is not running on the simulator" >&2; exit 1; }
        [[ -n ${QUICKSTART_SCREENSHOT_DIR:-} ]] && xcrun simctl io $QUICKSTART_SIMULATOR screenshot $QUICKSTART_SCREENSHOT_DIR/quickstart-ios.png > /dev/null
        echo "quickstart: the app runs on the iOS simulator"
        xcrun simctl terminate $QUICKSTART_SIMULATOR $bundle || true
        xcrun simctl uninstall $QUICKSTART_SIMULATOR $bundle
      fi
      ;;
    android)
      dotnet build $app -f net10.0-android -warnaserror
      manifest=$app/obj/Debug/net10.0-android/android/AndroidManifest.xml
      grep -q 'androidx.car.app.CarAppService' $manifest
      grep -q 'androidx.car.app.category.POI' $manifest
      grep -q 'androidx.car.app.MAP_TEMPLATES' $manifest
      echo "quickstart: AndroidManifest.xml has the CarAppService with the POI category"
      if [[ -n ${QUICKSTART_ANDROID_SERIAL:-} ]]; then
        adb -s $QUICKSTART_ANDROID_SERIAL uninstall $bundle > /dev/null 2>&1 || true
        # The guide command (step 8) on the given device
        (cd $app && dotnet build -t:Run -f net10.0-android -warnaserror "-p:AdbTarget=-s $QUICKSTART_ANDROID_SERIAL")
        sleep 8
        [[ -n $(adb -s $QUICKSTART_ANDROID_SERIAL shell pidof $bundle | tr -d '\r') ]] \
          || { echo "quickstart: $bundle is not running on $QUICKSTART_ANDROID_SERIAL" >&2; exit 1; }
        [[ -n ${QUICKSTART_SCREENSHOT_DIR:-} ]] && adb -s $QUICKSTART_ANDROID_SERIAL exec-out screencap -p > $QUICKSTART_SCREENSHOT_DIR/quickstart-android.png
        echo "quickstart: the app runs on $QUICKSTART_ANDROID_SERIAL"
        adb -s $QUICKSTART_ANDROID_SERIAL uninstall $bundle > /dev/null
      fi
      ;;
  esac
done
echo "QUICKSTART OK"
