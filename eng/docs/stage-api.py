"""Stage 10: preparing the API reference for DocFX (spec: "Documentation and release decisions (Stage 10)", API reference).

stage <repo> <configuration> <directory>: copies the built dll + XML docs of the libraries to <directory>/{core,ios,android}
together with their references (dotnet msbuild -t:ResolveAssemblyReferences -getItem:ReferencePath). In dll mode DocFX 2.81 loads
references only from the dll directory (the `references` setting is ignored; Stage 10 probe).

dedupe <api directory>: the iOS and Android assets have the same UIDs for shared types (DocFX: DuplicateUids), so from the
Android metadata only the Android-only types remain, without namespace pages, with their own toc.yml; the CarKit.Maui namespace
page (Core and the platform library) stays with Core.
"""
import json
import os
import shutil
import subprocess
import sys

LIBRARIES = ("CarKit.Maui", "CarKit.Maui.Media", "CarKit.Maui.Communication", "CarKit.Maui.Navigation")
NET = ("CarKit.Maui.Core", "CarKit.Maui.Testing")


def references(repo, project, tfm, configuration):
    out = subprocess.run(
        ["dotnet", "msbuild", f"{repo}/src/{project}/{project}.csproj", f"-p:TargetFramework={tfm}",
         f"-p:Configuration={configuration}", "-t:ResolveAssemblyReferences", "-getItem:ReferencePath", "-nologo"],
        capture_output=True, text=True, check=True, cwd=repo).stdout
    return [item["FullPath"] for item in json.loads(out)["Items"]["ReferencePath"]]


def place(source, directory):
    target = os.path.join(directory, os.path.basename(source))
    if os.path.exists(target):
        return
    try:
        os.link(source, target)
    except OSError:
        shutil.copyfile(source, target)


def stage(repo, configuration, output):
    shutil.rmtree(output, ignore_errors=True)
    groups = {"core": [(p, "net10.0") for p in NET],
              "ios": [(p, "net10.0-ios26.4") for p in LIBRARIES],
              "android": [(p, "net10.0-android") for p in LIBRARIES]}
    for group, projects in groups.items():
        directory = os.path.join(output, group)
        os.makedirs(directory)
        for project, tfm in projects:
            for extension in (".dll", ".xml"):
                built = f"{repo}/src/{project}/bin/{configuration}/{tfm}/{project}{extension}"
                if not os.path.exists(built):
                    sys.exit(f"stage-api: missing {built} (build the libraries in {configuration} first)")
                place(built, directory)
            for reference in references(repo, project, tfm, configuration):
                place(reference, directory)


def dedupe(api):
    # The CarKit.Maui namespace is in Core and in the platform library: the namespace page stays with Core
    core = set(os.listdir(os.path.join(api, "core"))) - {"toc.yml"}
    for name in core & set(os.listdir(os.path.join(api, "ios"))):
        os.remove(os.path.join(api, "ios", name))
    ios = set(os.listdir(os.path.join(api, "ios"))) | core
    android_dir = os.path.join(api, "android")
    kept = []
    for name in sorted(os.listdir(android_dir)):
        path = os.path.join(android_dir, name)
        if not name.endswith(".yml") or name == "toc.yml":
            continue
        uid = name[:-4]
        namespace = uid in ("CarKit.Maui", "CarKit.Maui.Media", "CarKit.Maui.Communication", "CarKit.Maui.Navigation")
        if name in ios or namespace:
            os.remove(path)
        else:
            kept.append(uid)
    if not kept:
        sys.exit("stage-api: no Android-only types left after removing the shared ones")
    with open(os.path.join(android_dir, "toc.yml"), "w", encoding="utf-8") as toc:
        toc.write("### YamlMime:TableOfContent\n")
        for uid in kept:
            toc.write(f"- uid: {uid}\n  name: {uid.rsplit('.', 1)[-1]}\n")
    print(f"stage-api: {len(kept)} Android-only types: {', '.join(kept)}")


if __name__ == "__main__":
    if len(sys.argv) == 5 and sys.argv[1] == "stage":
        stage(os.path.abspath(sys.argv[2]), sys.argv[3], os.path.abspath(sys.argv[4]))
    elif len(sys.argv) == 3 and sys.argv[1] == "dedupe":
        dedupe(os.path.abspath(sys.argv[2]))
    else:
        sys.exit("usage: stage-api.py stage <repo> <configuration> <output> | dedupe <api directory>")
