"""Stage 10: executes the steps of the guide "Your first CarPlay & Android Auto app in 15 minutes"
(docs/site/articles/quickstart.md).

Usage: apply.py <guide.md> <work directory> <CarKit package version>

A step is an HTML marker `<!-- quickstart: <verb> <arguments> -->`, followed (after blank lines) by a ``` code block:
  command                         — every line of the block is a command; `cd X` changes the current directory of the
                                    following steps; `dotnet add package <Id>` gets `--version <version>` instead of
                                    `--prerelease` (the only substitution: the packages come from artifacts/packages)
  file <path>                     — the block is the entire content of the file (created or replaced)
  replace <path> <regex>          — lines of the file matching the expression: the first is replaced by the block, the
                                    rest removed
  insert-before <path> <text>     — the block is inserted before the last line equal to the text (after trimming whitespace)
  run <ios|android>               — the platform to build and run through run.sh (recorded in <directory>/run-steps.txt);
                                    the block must be exactly the guide command from RUN_COMMANDS, because run.sh
                                    executes its equivalent
  skip                            — a block that is not executed (e.g. DHU); any other block without a marker is an error
An unknown verb, a block without a marker, a typo in a marker, no match, no run step for iOS or Android
and a failed command end the script with an error, so that the guide cannot silently skip a step.
"""
import os
import re
import shlex
import subprocess
import sys

MARKER = re.compile(r"^\s*<!-- quickstart: (?P<verb>[a-z-]+)(?: (?P<args>.*?))? -->\s*$")
FENCE = re.compile(r"^\s*```")
RUN_COMMANDS = {"ios": "dotnet build -t:Run -f net10.0-ios", "android": "dotnet build -t:Run -f net10.0-android"}


def blocks(guide):
    lines = open(guide, encoding="utf-8").read().splitlines()
    i = 0
    while i < len(lines):
        match = MARKER.match(lines[i])
        if not match:
            if re.match(r"^\s*<!--\s*quickstart", lines[i]):
                sys.exit(f"quickstart: malformed marker at line {i + 1}: {lines[i]}")
            if FENCE.match(lines[i]):
                sys.exit(f"quickstart: code block at line {i + 1} has no quickstart marker (use skip for a block that is not executed)")
            i += 1
            continue
        j = i + 1
        while j < len(lines) and not lines[j].strip():
            j += 1
        if j >= len(lines) or not FENCE.match(lines[j]):
            sys.exit(f"quickstart: marker at line {i + 1} is not followed by a code block")
        k = j + 1
        while k < len(lines) and not FENCE.match(lines[k]):
            k += 1
        if k >= len(lines):
            sys.exit(f"quickstart: code block after line {i + 1} is not closed")
        yield i + 1, match.group("verb"), match.group("args") or "", lines[j + 1:k]
        i = k + 1


def read(path):
    if not os.path.exists(path):
        sys.exit(f"quickstart: {path} does not exist")
    return open(path, encoding="utf-8-sig").read().splitlines()


def write(path, lines):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as file:
        file.write("\n".join(lines) + "\n")


def main(guide, work, version):
    cwd = os.path.abspath(work)
    runs = []
    steps = 0
    for line, verb, args, body in blocks(guide):
        steps += 1
        print(f"quickstart: step {steps} (line {line}): {verb} {args}".rstrip(), flush=True)
        if verb == "skip":
            continue
        if verb == "command":
            for command in (c.strip() for c in body):
                if not command:
                    continue
                if command.startswith("cd "):
                    cwd = os.path.join(cwd, command[3:].strip())
                    continue
                argv = shlex.split(command)
                if argv[:3] == ["dotnet", "add", "package"]:
                    argv = [arg for arg in argv if arg != "--prerelease"] + ["--version", version]
                print(f"quickstart: $ {' '.join(argv)}", flush=True)
                subprocess.run(argv, cwd=cwd, check=True)
        elif verb == "file":
            write(os.path.join(cwd, args), body)
        elif verb == "replace":
            path, _, pattern = args.partition(" ")
            regex = re.compile(pattern)
            source = read(os.path.join(cwd, path))
            matched = [n for n, text in enumerate(source) if regex.search(text)]
            if not matched:
                sys.exit(f"quickstart: no line of {path} matches {pattern} (line {line} of the guide)")
            result = []
            for n, text in enumerate(source):
                if n == matched[0]:
                    result.extend(body)
                elif n not in matched:
                    result.append(text)
            write(os.path.join(cwd, path), result)
        elif verb == "insert-before":
            path, _, anchor = args.partition(" ")
            source = read(os.path.join(cwd, path))
            positions = [n for n, text in enumerate(source) if text.strip() == anchor]
            if not positions:
                sys.exit(f"quickstart: {path} has no line '{anchor}' (line {line} of the guide)")
            source[positions[-1]:positions[-1]] = body
            write(os.path.join(cwd, path), source)
        elif verb == "run":
            if args not in RUN_COMMANDS:
                sys.exit(f"quickstart: unknown run platform '{args}' (line {line} of the guide)")
            if [c.strip() for c in body if c.strip()] != [RUN_COMMANDS[args]]:
                sys.exit(f"quickstart: the {args} run step must be `{RUN_COMMANDS[args]}` (line {line} of the guide)")
            runs.append(args)
        else:
            sys.exit(f"quickstart: unknown step '{verb}' (line {line} of the guide)")
    if sorted(runs) != ["android", "ios"]:
        sys.exit(f"quickstart: the guide must have one run step for ios and one for android, found: {runs}")
    write(os.path.join(os.path.abspath(work), "run-steps.txt"), runs)
    print(f"quickstart: {steps} steps applied, run: {' '.join(runs)}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit("usage: apply.py <guide.md> <work directory> <CarKit package version>")
    main(sys.argv[1], sys.argv[2], sys.argv[3])
