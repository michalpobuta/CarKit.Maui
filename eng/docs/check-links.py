"""Stage 10: relative links in repo documents outside the DocFX site (README, CONTRIBUTING, CHANGELOG, package READMEs).

Usage: check-links.py <repo> <file.md>... — every link [text](path) without a scheme must point to an existing file
(#… anchors are skipped: DocFX checks them on the documentation site). Exit code 1 on a dead link.
"""
import os
import re
import sys

LINK = re.compile(r"\]\(([^)\s]+)\)")

repo = os.path.abspath(sys.argv[1])
broken = []
for document in sys.argv[2:]:
    path = os.path.join(repo, document)
    for number, line in enumerate(open(path, encoding="utf-8"), 1):
        for target in LINK.findall(line):
            if re.match(r"^[a-z]+:", target) or target.startswith("#"):
                continue
            file = target.split("#", 1)[0]
            if not os.path.exists(os.path.normpath(os.path.join(os.path.dirname(path), file))):
                broken.append(f"{document}:{number}: {target}")
for item in broken:
    print(f"broken link: {item}")
if broken:
    sys.exit(1)
print(f"links OK: {', '.join(sys.argv[2:])}")
