# ADR-0008: Target net8.0 and net10.0, and skip net9.0

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-005, QG-014
- **Next review:** 2026-11-10 (.NET 8 end of support)

## Context

A library's target framework list is a compatibility promise. Too narrow and nobody can adopt
it; too wide and every dependency decision becomes a compromise.

The .NET support picture at the time of this decision:

| Version | Type | End of support |
| --- | --- | --- |
| .NET 8 | LTS | 2026-11-10 |
| .NET 9 | STS | 2026-11-10 |
| .NET 10 | LTS | 2028-11-14 |

The relevant asymmetry: .NET 8 and .NET 9 leave support on the *same day*, but .NET 8 is where
production applications actually are. Real teams — including the ones with exactly the EF Core
performance problem QueryGuard solves — sit on the previous LTS, not on the STS.

## Decision

**Multi-target `net8.0` and `net10.0`. Do not target `net9.0`.**

EF Core versions are pinned per target framework, so a consumer never gets a mismatched pair:

| Target framework | EF Core | Microsoft.Extensions.* |
| --- | --- | --- |
| `net8.0` | 8.0.x | 8.0.x |
| `net10.0` | 10.0.x | 10.0.x |

This is implemented with conditional versions in `Directory.Packages.props`, so it is one
place to change rather than one per project.

Corollaries:

- CI builds **and tests** both target frameworks, on Ubuntu and Windows. A compile-only pass
  on `net8.0` would not catch EF Core 8 behavior differences, which are the reason for
  multi-targeting in the first place.
- C# is written to compile on both targets. A language or BCL feature is used because it makes
  the code clearer, never because it is new.
- Removing a target framework is a breaking change and needs its own ADR.

## Rejected alternatives

**`net10.0` only.** Simplest build, half the CI time, one EF Core version. It also excludes
every team on .NET 8 — a large share of the addressable users, at exactly the moment the project
needs its first real adopters.

**`net8.0` only.** Would work on .NET 10 through roll-forward, but it forfeits anything newer,
and it signals a library that is not keeping up.

**Adding `net9.0`.** A third build, a third EF Core line, and a third test matrix, for a
framework that reaches end of support on the same day as .NET 8 and has a smaller installed
base. Pure cost.

**`netstandard2.0`.** Would technically widen reach to .NET Framework. Rejected: the modern
EF Core and ASP.NET Core APIs QueryGuard is built on are not available there, so it would mean a
different, worse product wearing the same name.

## Consequences

- Two EF Core major versions must be supported simultaneously, so anything version-specific is
  isolated behind conditional compilation with a comment explaining why.
- Test projects multi-target too, which means running the `net8.0` pass locally needs the .NET 8
  runtime installed. `CONTRIBUTING.md` documents running `-f net10.0` locally and letting CI
  cover both.
- Package validation verifies the shipped framework list is coherent.

## Revisit when

**2026-11-10**, when .NET 8 reaches end of support. At that point the decision is not automatic:
if .NET 8 usage among QueryGuard consumers is still material, keeping the target for a defined
window is a reasonable service to users, provided the security implications of an unsupported
runtime are stated plainly. If usage has moved on, `net8.0` is dropped in a minor release with a
migration note.
