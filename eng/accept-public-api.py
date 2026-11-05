"""Appends missing symbols (RS0016) to PublicAPI/<platform>/PublicAPI.Unshipped.txt and removes nonexistent ones (RS0017).
Platform: net, android, ios (both iOS assets) or ios26.4 (a symbol only in the net10.0-ios26.4 asset; Stage 9).

Usage (from the repo directory): python3 -I eng/accept-public-api.py src/CarKit.Maui.Core [src/...]
"""
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

PATTERN = re.compile(
    r"(?:warning|error) (?P<code>RS0016|RS0017): Symbol '(?P<symbol>.+?)' "
    r"(?:is not part of the declared public API|is part of the declared API, but is either not public or could not be found)"
    r".*\[(?P<project>[^\]]+?\.csproj)(?:::TargetFramework=(?P<tfm>[^\]]+))?\]"
)


def platform_of(tfm: str | None) -> str:
    # Stage 9: the net10.0-ios26.4 asset = iOS API (ios/) + the 26.4 increment (ios26.4/)
    if tfm and "-ios26.4" in tfm:
        return "ios26.4"
    if tfm and "-ios" in tfm:
        return "ios"
    if tfm and "-android" in tfm:
        return "android"
    return "net"


def main(projects: list[str]) -> int:
    changes: dict[Path, dict[str, set[str]]] = defaultdict(lambda: {"RS0016": set(), "RS0017": set()})
    for project in projects:
        result = subprocess.run(
            ["dotnet", "build", project, "-p:TreatWarningsAsErrors=false", "-nologo", "-clp:NoSummary"],
            capture_output=True, text=True, check=False,
        )
        for match in PATTERN.finditer(result.stdout):
            project_dir = Path(match["project"]).parent
            target = project_dir / "PublicAPI" / platform_of(match["tfm"]) / "PublicAPI.Unshipped.txt"
            changes[target][match["code"]].add(match["symbol"])

    # A symbol reported by both iOS assets is shared (ios/); only what the 26.0 asset lacks goes to ios26.4/
    for target, codes in changes.items():
        if target.parent.name == "ios26.4":
            shared = changes.get(target.parent.parent / "ios" / "PublicAPI.Unshipped.txt")
            if shared:
                codes["RS0016"] -= shared["RS0016"]
                codes["RS0017"] -= shared["RS0017"]

    for target, codes in changes.items():
        lines = [l for l in target.read_text(encoding="utf-8").splitlines() if l.strip()]
        header = [l for l in lines if l.startswith("#")]
        body = {l for l in lines if not l.startswith("#")}
        body = (body | codes["RS0016"]) - codes["RS0017"]
        target.write_text("\n".join(header + sorted(body, key=str.lower)) + "\n", encoding="utf-8")
        print(f"{target}: +{len(codes['RS0016'])} -{len(codes['RS0017'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
