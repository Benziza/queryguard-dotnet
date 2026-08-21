# Contributing

Thanks for helping QueryGuard.NET. Focused changes are easier to review and more likely to merge.

## Before you start

Small fixes can go straight to a pull request. This includes typos, broken links, small documentation
improvements, and narrow test corrections.

Open an issue first when a change affects:

- a public API
- query capture or fingerprinting
- privacy or redaction
- detector behavior or default policy
- provider support claims

Search the [issues](https://github.com/Benziza/queryguard-dotnet/issues) and
[discussions](https://github.com/Benziza/queryguard-dotnet/discussions) before starting a larger change.
For a false positive, use the dedicated report form. Those reports are especially valuable.

## Development setup

| Requirement | Version |
| --- | --- |
| .NET SDK | 10.0.100 or later, see [`global.json`](./global.json) |
| .NET 8 runtime | Needed only to run the `net8.0` test pass locally |
| Docker | Needed only for PostgreSQL, SQL Server, and MySQL provider tests |

```bash
git clone https://github.com/Benziza/queryguard-dotnet.git
cd queryguard-dotnet
dotnet restore QueryGuard.slnx
dotnet build QueryGuard.slnx -c Release
dotnet test QueryGuard.slnx -c Release
```

If only the .NET 10 runtime is installed, run:

```bash
dotnet test QueryGuard.slnx -c Release -f net10.0
```

Provider tests skip when Docker is unavailable. CI runs them with real databases.

Before marking a pull request ready, also run:

```bash
dotnet format QueryGuard.slnx --verify-no-changes
```

## Rules that matter

- Keep one behavior change per pull request.
- Add a test for behavior changes and bug fixes.
- Keep claims precise. QueryGuard reports repeated-query candidates, not semantic proof.
- Never capture parameter values or connection strings.
- Apply redaction before data reaches a reporter.
- Use synthetic schemas and data in tests, samples, and documentation.
- Do not change generated SQL, suppress commands, alter responses, or replace application exceptions.
- Keep the interceptor stateless. Session state belongs in `QueryGuardSession`.
- Do not block asynchronous work with `.Result` or `.Wait()`.
- Document every public API. Warnings are treated as errors.

The full code rules and rationale are in [docs/coding-standards.md](./docs/coding-standards.md).

## Pull requests

Use a clear branch name and commit message. Maintainer branches follow this pattern:

```text
feat/QG-123-short-description
fix/QG-123-short-description
```

External contributors do not need to rename an existing branch to match it.

The pull request template asks three things: what changed, why, and how it was tested. That is enough
for small changes. For public API, capture, privacy, or hot-path changes, also use
[docs/review-checklist.md](./docs/review-checklist.md).

Draft pull requests are welcome. Keep the scope focused, explain unusually large diffs, and resolve
review conversations before merge. The repository uses squash merges, so the final pull request title
becomes the commit message.

## Documentation site

Build the local site with:

```bash
dotnet tool restore
dotnet docfx docs/docfx.json --serve
```

Then open <http://localhost:8080>. CI treats documentation warnings as errors.

Links from `docs/` to files outside that folder need a full GitHub URL because those files are not
copied into the site. Links to generated API pages should target the `.yml` file. DocFX rewrites the
extension.

## Good first contributions

Look for the [`good first issue`](https://github.com/Benziza/queryguard-dotnet/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22)
label. Useful contributions include:

- synthetic SQL fixtures for a new provider
- intentional repeated-query scenarios
- redaction edge cases
- documentation and sample improvements

## Security and conduct

Do not open a public issue for a vulnerability. Follow [SECURITY.md](./SECURITY.md).

This project follows the [Contributor Covenant](./CODE_OF_CONDUCT.md).
