#!/bin/zsh
# Stage 10: documentation site (docs/site, DocFX 2.81.0 from .config/dotnet-tools.json;
# spec: "Documentation and release decisions (Stage 10)").
# Usage: eng/docs/build.sh [--no-build]   (CONFIGURATION, Release by default; --no-build: libraries already built, e.g. in CI)
# Output: artifacts/docs/_site. DocFX warnings (dead link, bad tab, unresolved xref, missing API) are errors.
set -euo pipefail
repo=${0:A:h:h:h}
cd $repo
config=${CONFIGURATION:-Release}
if [[ ${1:-} != --no-build ]]; then
  for project in CarKit.Maui.Core CarKit.Maui.Testing CarKit.Maui CarKit.Maui.Media CarKit.Maui.Communication CarKit.Maui.Navigation; do
    dotnet build src/$project -c $config -warnaserror -nologo -v:q
  done
fi
# Relative links of repo documents outside the site (README, CONTRIBUTING, CHANGELOG, package READMEs)
python3 -I eng/docs/check-links.py $repo README.md CONTRIBUTING.md CHANGELOG.md src/*/README.md
dotnet tool restore > /dev/null
# The generated reference (docs/site/api/{core,ios,android}) is in .gitignore
rm -rf artifacts/docs docs/site/api/core docs/site/api/ios docs/site/api/android
python3 -I eng/docs/stage-api.py stage $repo $config artifacts/docs/api-src
dotnet docfx metadata docs/site/docfx.json --warningsAsErrors
python3 -I eng/docs/stage-api.py dedupe docs/site/api
dotnet docfx build docs/site/docfx.json --warningsAsErrors
test -f artifacts/docs/_site/index.html
test -f artifacts/docs/_site/articles/quickstart.html
echo "DOCS OK: artifacts/docs/_site"
