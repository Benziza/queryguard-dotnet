# Changelog

All notable changes to QueryGuard.NET are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is below `1.0.0`, breaking changes may appear in a minor or preview
release — every one of them is listed here with migration notes.

Generated GitHub release notes list the merged pull requests. This file is the curated
record: breaking changes, privacy-relevant behavior, and report-schema compatibility.

## [Unreleased]

### Added

- Async-safe session propagation: `IQueryGuardSessionAccessor` with an `AsyncLocal`-backed
  default, nested scopes that restore the parent session on both the normal and the exception
  path, and out-of-order disposal detection.
- Core contracts: immutable `QueryRecord`, `QueryFingerprint`, `QueryFingerprintGroup`,
  `QueryFinding`, and `QueryGuardResult`; the `QueryGuardSession` lifecycle with a frozen
  `CompletedQueryGuardSession` snapshot; and the immutable fluent `QueryGuardPolicy`.
- Repository foundation: MIT license, community health files, issue forms, pull request
  template, CODEOWNERS, Dependabot configuration, and categorized release notes.
- Shared build configuration with nullable reference types, warnings as errors,
  deterministic builds, central package version management, and package validation.
- CI matrix building and testing `net8.0` and `net10.0` on Ubuntu and Windows, plus
  formatting verification, CodeQL analysis, and dependency review.

[Unreleased]: https://github.com/Benziza/queryguard-dotnet/commits/main
