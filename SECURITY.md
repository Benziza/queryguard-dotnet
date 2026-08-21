# Security Policy

QueryGuard.NET observes Entity Framework Core database commands. That makes both the
data it captures and the packages it publishes part of its security surface. This
document explains what to expect and how to report a problem.

## Supported versions

QueryGuard is in preview. Only the most recent preview receives fixes.

| Version | Supported | Notes |
| --- | --- | --- |
| `0.1.0-preview.*` (latest) | Yes | Security fixes ship as a new preview |
| `0.1.0-preview.*` (older) | No | Upgrade to the latest preview |

Once `0.1.0` is released, the supported window will be documented in
[SUPPORT.md](./SUPPORT.md).

## Reporting a vulnerability

**Do not open a public issue for a security vulnerability.**

Report privately through either channel:

1. **GitHub private vulnerability reporting** (preferred):
   [open a draft advisory](https://github.com/Benziza/queryguard-dotnet/security/advisories/new).
   This keeps the report, the discussion, and the fix in one private place.
2. **Email**: benizizamohamed@gmail.com with `QueryGuard security` in the subject.

Please include:

- affected package and version;
- .NET and EF Core version, and the database provider;
- a description of the impact, not only the symptom;
- a **synthetic** reproduction: no production credentials, connection strings,
  customer data, or private schema details;
- any known mitigation or workaround.

### What to expect

| Stage | Target |
| --- | --- |
| Acknowledgement | within 72 hours |
| Initial assessment and severity | within 7 days |
| Fix or documented mitigation | depends on severity; you will receive a timeline with the assessment |
| Public disclosure | after a fix is available, coordinated with you |

I will credit you in the advisory and release notes unless you ask me not to.
If a report turns out not to be a vulnerability, you will get a clear explanation
rather than silence.

## Scope

In scope:

- QueryGuard capturing, retaining, or emitting data it documents as excluded: parameter
  values, connection strings, credentials, or unbounded SQL samples.
- A reporter bypassing the central redaction policy.
- QueryGuard altering application behavior: modifying generated SQL, suppressing a command,
  changing a response, or replacing or hiding the original exception.
- Vulnerabilities in the release and publishing pipeline, including workflow permissions.
- Denial of service caused by unbounded retention on the query hot path.

Out of scope:

- SQL injection or other vulnerabilities in *your* application code that QueryGuard merely
  observes.
- Deliberately opting in to a documented, non-default capture setting
  (for example `CaptureParameterValues = true`) and then sharing the output publicly.
  QueryGuard documents the risk; the choice to enable it is yours.
- Vulnerabilities in EF Core, ASP.NET Core, or a database provider. Report those to the
  respective project.
- Findings from automated scanners with no demonstrated impact.

## Privacy defaults you can rely on

These are contractual defaults, verified by the redaction test matrix. Changing any of
them requires an ADR and a release note.

- Parameter **values** are not captured.
- Connection strings are never captured, logged, or serialized.
- SQL is not injected into HTTP responses.
- Stack traces are not collected unless explicitly enabled, and then only once per
  fingerprint.
- Retained samples per fingerprint are bounded.
- Redaction is applied centrally, before any reporter writes output.

See [docs/decisions/0004-parameter-privacy.md](./docs/decisions/0004-parameter-privacy.md)
for the reasoning behind these defaults.

## Supply chain

- Third-party GitHub Actions are pinned to full commit SHAs and updated through Dependabot.
- Workflow tokens are read-only by default; write permissions are granted per job.
- Packages are published from a tagged release running in a protected `release`
  environment, using short-lived credentials where nuget.org trusted publishing is
  available. See
  [docs/decisions/0012-trusted-publishing.md](./docs/decisions/0012-trusted-publishing.md).
- Symbol packages and SourceLink are published so consumers can verify what they run.
