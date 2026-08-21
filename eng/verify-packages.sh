#!/usr/bin/env bash
#
# Verifies the packed output before it can be published.
#
# `dotnet pack` succeeding proves a package was produced, not that it is usable. Metadata that is
# missing or wrong is invisible at pack time and permanent after publish: a NuGet version cannot be
# replaced, only unlisted. So the structural checks here are assertions rather than a printed listing
# a human is trusted to read.
#
# The last section is the part that actually matters: it installs the packed packages into a throwaway
# project from a local feed and runs code against them. Everything before it inspects a zip file;
# this proves the package works as a package — that the assemblies land in the right lib folders,
# that the dependency graph resolves, and that the public API is reachable from outside the solution.
#
# Usage: eng/verify-packages.sh [package-directory]

set -euo pipefail

PACKAGE_DIR="${1:-artifacts/packages}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Every shipping package, and the frameworks each one must carry. See ADR-0008.
EXPECTED_PACKAGES=(
  QueryGuard.Core
  QueryGuard.EntityFrameworkCore
  QueryGuard.AspNetCore
  QueryGuard.AspNetCore.Testing
  QueryGuard.Testing
  QueryGuard.Reporting
)
EXPECTED_FRAMEWORKS=(net8.0 net10.0)

failures=0

fail() {
  echo "  FAIL: $1"
  failures=$((failures + 1))
}

pass() {
  echo "  ok: $1"
}

require_entry() {
  local package="$1" pattern="$2" description="$3"

  # Read the full archive listing. With pipefail, grep -q can close the pipe after the first match and
  # make unzip report a broken pipe, which turns a present entry into an intermittent failure.
  if unzip -Z1 "$package" | grep -E "$pattern" >/dev/null; then
    pass "$description"
  else
    fail "$description (no entry matching '$pattern')"
  fi
}

echo "Verifying packages in $PACKAGE_DIR"
echo

if [ ! -d "$PACKAGE_DIR" ]; then
  echo "FAIL: $PACKAGE_DIR does not exist. Run 'dotnet pack' first."
  exit 1
fi

# ---------------------------------------------------------------------------
# Structure and metadata
# ---------------------------------------------------------------------------

package_version=""

for name in "${EXPECTED_PACKAGES[@]}"; do
  echo "$name"

  # Guard against the glob matching a sibling: QueryGuard.Core.* would also match
  # QueryGuard.Core.Something if such a package ever existed.
  nupkg=$(find "$PACKAGE_DIR" -maxdepth 1 -name "$name.[0-9]*.nupkg" -not -name "*.symbols.nupkg" | head -n 1)

  if [ -z "$nupkg" ]; then
    fail "no .nupkg was produced"
    echo
    continue
  fi

  version="${nupkg##*/$name.}"
  version="${version%.nupkg}"
  pass "packed as $name $version"

  # Every package ships from one build, so one version. A mismatch means a stale artifact is in the
  # directory, which is exactly the sort of thing that gets published by accident.
  if [ -z "$package_version" ]; then
    package_version="$version"
  elif [ "$version" != "$package_version" ]; then
    fail "version $version does not match $package_version from the other packages"
  fi

  # A symbol package per shipping package, or source stepping silently does not work for consumers.
  if [ -f "$PACKAGE_DIR/$name.$version.snupkg" ]; then
    pass "symbol package present"
    require_entry "$PACKAGE_DIR/$name.$version.snupkg" "\.pdb$" "portable PDB in the symbol package"
  else
    fail "no .snupkg — consumers would get no symbols"
  fi

  for framework in "${EXPECTED_FRAMEWORKS[@]}"; do
    require_entry "$nupkg" "^lib/$framework/$name\.dll$" "assembly for $framework"

    # The XML doc file travels with the assembly. Without it, IntelliSense in a consuming project
    # shows nothing for a public API whose documentation is part of its contract.
    require_entry "$nupkg" "^lib/$framework/$name\.xml$" "XML documentation for $framework"
  done

  require_entry "$nupkg" "^PACKAGE\.md$" "README shown on nuget.org"
  require_entry "$nupkg" "^queryguard-icon\.png$" "package icon"
  require_entry "$nupkg" "^LICENSE$" "licence file"

  nuspec=$(unzip -p "$nupkg" "$name.nuspec")

  # SourceLink writes the repository URL and the exact commit into the nuspec. A consumer stepping
  # into source needs both, and its presence here is what makes the package traceable to a commit.
  #
  # This is a metadata check, not proof that stepping works end to end — that needs a debugger
  # against a published symbol server. What it does catch is the failure that actually happens:
  # packing outside a git checkout, which silently drops the commit and leaves the metadata blank.
  if grep -q '<repository type="git"' <<<"$nuspec" && grep -qE '<repository[^>]+commit="[0-9a-f]{40}"' <<<"$nuspec"; then
    pass "repository URL and commit recorded for SourceLink"
  else
    fail "nuspec has no git repository URL and commit — SourceLink would not resolve"
  fi

  if grep -q '<license type="expression">MIT</license>' <<<"$nuspec"; then
    pass "MIT licence expression"
  else
    fail "licence expression missing or not MIT"
  fi

  if grep -qE '<description>.{80,}</description>' <<<"$nuspec"; then
    pass "description is substantial"
  else
    fail "description is missing or too short to be useful on nuget.org"
  fi

  echo
done

# ---------------------------------------------------------------------------
# The command-line tool
# ---------------------------------------------------------------------------
#
# A different shape from the libraries: tools ship under tools/<tfm>/any with a settings file and no
# lib/ folder, so the loop above would have failed it for the wrong reasons. Checked separately rather
# than skipped, because an unverified shipping package is how QueryGuard.Testing came to ship without
# the dependency that made it usable.

TOOL_NAME=QueryGuard.Cli
echo "$TOOL_NAME"

tool_nupkg=$(find "$PACKAGE_DIR" -maxdepth 1 -name "$TOOL_NAME.[0-9]*.nupkg" -not -name "*.symbols.nupkg" | head -n 1)

if [ -z "$tool_nupkg" ]; then
  fail "no .nupkg was produced"
else
  tool_version="${tool_nupkg##*/$TOOL_NAME.}"
  tool_version="${tool_version%.nupkg}"
  pass "packed as $TOOL_NAME $tool_version"

  if [ "$tool_version" != "$package_version" ]; then
    fail "version $tool_version does not match $package_version from the libraries"
  fi

  for framework in "${EXPECTED_FRAMEWORKS[@]}"; do
    require_entry "$tool_nupkg" "^tools/$framework/any/queryguard\.dll$" "tool assembly for $framework"

    # Without this the package installs and the command does not exist, which is a confusing way to
    # fail — `dotnet tool install` reports success.
    require_entry "$tool_nupkg" "^tools/$framework/any/DotnetToolSettings\.xml$" "tool manifest for $framework"
  done

  require_entry "$tool_nupkg" "^PACKAGE\.md$" "README shown on nuget.org"
  require_entry "$tool_nupkg" "^LICENSE$" "licence file"

  tool_nuspec=$(unzip -p "$tool_nupkg" "$TOOL_NAME.nuspec")

  if grep -q '<packageTypes>' <<<"$tool_nuspec" && grep -q 'DotnetTool' <<<"$tool_nuspec"; then
    pass "declared as a DotnetTool package type"
  else
    fail "nuspec does not declare the DotnetTool package type; it would install as a library"
  fi

  if grep -qE '<repository[^>]+commit="[0-9a-f]{40}"' <<<"$tool_nuspec"; then
    pass "repository commit recorded for SourceLink"
  else
    fail "nuspec has no git repository commit"
  fi
fi

echo

if [ "$failures" -gt 0 ]; then
  echo "$failures package check(s) failed."
  exit 1
fi

# ---------------------------------------------------------------------------
# Consumer smoke test
# ---------------------------------------------------------------------------
#
# Restores the packed packages into a project outside the solution, so nothing resolves through a
# ProjectReference. If the assemblies were in the wrong lib folder, a dependency were missing from
# the nuspec, or a type were internal by accident, this is where it surfaces.

echo "Consumer smoke test against $package_version"

workspace=$(mktemp -d)
trap 'rm -rf "$workspace"' EXIT

# NuGet reads these paths itself, so they have to be native. Under Git Bash on Windows a POSIX-style
# `/c/...` path reaches NuGet verbatim and is resolved as `C:\c\...`, which fails with a confusing
# "the local source doesn't exist" pointing at a path that is almost right.
to_native_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s' "$1"
  fi
}

feed="$(to_native_path "$(cd "$PACKAGE_DIR" && pwd)")"
packages_folder="$(to_native_path "$workspace/packages")"

mkdir -p "$workspace/consumer"
cd "$workspace/consumer"

# Only two sources, declared explicitly: whatever is on the machine running this must not influence
# the result. <clear /> also stops a stale copy in the global packages folder from masking a broken
# package, together with the throwaway packages directory below.
cat >NuGet.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="$packages_folder" />
  </config>
</configuration>
XML

cat >Consumer.csproj <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="QueryGuard.EntityFrameworkCore" Version="$package_version" />
    <PackageReference Include="QueryGuard.AspNetCore.Testing" Version="$package_version" />
    <PackageReference Include="QueryGuard.Testing" Version="$package_version" />
    <PackageReference Include="QueryGuard.Reporting" Version="$package_version" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.11" />
  </ItemGroup>
</Project>
XML

# A real repeated query through a real EF Core provider, then the assertions a consumer would write.
# Anything less would prove the package restores, not that it works.
cat >Program.cs <<'CSHARP'
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using QueryGuard;
using QueryGuard.AspNetCore.Testing;
using QueryGuard.EntityFrameworkCore;
using QueryGuard.Reporting;
using QueryGuard.Testing;

var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<CatalogContext>()
    .UseSqlite(connection)
    .AddInterceptors(new QueryGuardCommandInterceptor(QueryGuardScope.DefaultAccessor, new QueryFingerprintFactory()))
    .Options;

using var db = new CatalogContext(options);
db.Database.EnsureCreated();
db.Widgets.AddRange(new Widget { Name = "a" }, new Widget { Name = "b" }, new Widget { Name = "c" });
db.SaveChanges();
db.ChangeTracker.Clear();

var policy = QueryGuardPolicy.Create("consumer").WithMaxOccurrencesPerFingerprint(2);

using var scope = QueryGuardScope.Start("consumer-smoke", policy);

foreach (var id in new[] { 1, 2, 3 })
{
    _ = db.Widgets.AsNoTracking().FirstOrDefault(widget => widget.Id == id);
}

var result = scope.Complete();

if (result.ReadCommandCount != 3)
{
    throw new InvalidOperationException($"Expected 3 recorded reads, got {result.ReadCommandCount}.");
}

if (result.IsSuccess)
{
    throw new InvalidOperationException("Expected the occurrence budget to fail.");
}

// The reporting package too, since a consumer writing CI output depends on it rendering.
var report = new QueryGuardJsonReporter().Render(result);

if (!report.Contains("\"schemaVersion\"", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The JSON report is missing its schema version.");
}

Console.WriteLine($"Consumer smoke test passed: {result.ReadCommandCount} reads, {result.FailureCount} failure(s).");

_ = (Func<WebApplicationFactory<ConsumerEntryPoint>, QueryGuardWebApplicationMeasurement<ConsumerEntryPoint>>)
    CompileWebHelper;

// Kept as a compile-only check. Starting a WebApplicationFactory needs a real web entry point, which
// the package integration tests provide.
static QueryGuardWebApplicationMeasurement<ConsumerEntryPoint> CompileWebHelper(
    WebApplicationFactory<ConsumerEntryPoint> factory)
    => factory.TrackQueries<ConsumerEntryPoint, CatalogContext>("consumer-web");

internal sealed class CatalogContext : DbContext
{
    public CatalogContext(DbContextOptions<CatalogContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();
}

internal sealed class ConsumerEntryPoint
{
}

internal sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
CSHARP

dotnet run --configuration Release

cd "$REPO_ROOT"

# ---------------------------------------------------------------------------
# Tool smoke test
# ---------------------------------------------------------------------------
#
# Installs the packed tool and runs the workflow a user follows: record a baseline from reports, then
# verify against it. Everything above inspects a zip; this proves the command exists and works.

echo
echo "Tool smoke test against $package_version"

tool_dir="$workspace/tool"
work_dir="$workspace/tool-work/reports"
mkdir -p "$tool_dir" "$work_dir"

if ! dotnet tool install QueryGuard.Cli \
      --version "$package_version" \
      --tool-path "$(to_native_path "$tool_dir")" \
      --add-source "$feed" >/dev/null 2>&1; then
  echo "  FAIL: could not install the tool from the local feed"
  exit 1
fi

echo "  ok: installed"

# One report, in the shape QueryGuardJsonReporter writes.
cat >"$work_dir/companies.json" <<'JSON'
{
  "schemaVersion": "1.0",
  "scope": "GET /api/companies",
  "policy": "companies",
  "summary": { "totalCommands": 3, "readCommands": 3, "distinctQueries": 2 },
  "queryGroups": [
    { "fingerprint": "QG-FP-AAAAAAAA", "occurrences": 2 },
    { "fingerprint": "QG-FP-BBBBBBBB", "occurrences": 1 }
  ],
  "findings": []
}
JSON

queryguard="$tool_dir/queryguard"
cd "$workspace/tool-work"

if ! "$queryguard" baseline record --reports reports --baseline baseline.json >/dev/null 2>&1; then
  echo "  FAIL: 'baseline record' did not succeed"
  exit 1
fi

if ! grep -q '"scope": "GET /api/companies"' baseline.json; then
  echo "  FAIL: the recorded baseline does not contain the scope"
  exit 1
fi

echo "  ok: baseline record wrote the scope"

if ! "$queryguard" verify --reports reports --baseline baseline.json >/dev/null 2>&1; then
  echo "  FAIL: 'verify' reported a change against a baseline it just recorded"
  exit 1
fi

echo "  ok: verify is clean against its own baseline"

# Now make it worse and check the exit code, since that is what a build depends on.
sed -i 's/"readCommands": 3/"readCommands": 51/; s/"occurrences": 2/"occurrences": 50/' reports/companies.json

if "$queryguard" verify --reports reports --baseline baseline.json --fail-on-regression >/dev/null 2>&1; then
  echo "  FAIL: a regression did not produce a non-zero exit with --fail-on-regression"
  exit 1
fi

echo "  ok: a regression exits non-zero when asked to"

if ! "$queryguard" verify --reports reports --baseline baseline.json >/dev/null 2>&1; then
  echo "  FAIL: a regression exited non-zero without --fail-on-regression"
  exit 1
fi

echo "  ok: a regression alone does not fail the build"

cd "$REPO_ROOT"

echo
echo "All package checks passed."
