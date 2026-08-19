## Summary

<!-- Explain the user-visible or maintainer-visible outcome in 2–5 sentences. -->

## Linked issue

Closes QG-___
GitHub issue: #

## Type of change

- [ ] Feature
- [ ] Bug fix
- [ ] False-positive reduction
- [ ] Provider compatibility
- [ ] Performance
- [ ] Refactor with no behavior change
- [ ] Documentation / sample
- [ ] Build / CI / release

## Behavioral contract

<!-- What behavior changes? What behavior must remain unchanged? -->

### Before

<!-- Short description or failing output. -->

### After

<!-- Short description or passing output. -->

## Evidence and tests

- [ ] Unit tests added or updated
- [ ] Integration tests added or updated
- [ ] Parallel/session-isolation behavior considered
- [ ] Provider behavior covered or explicitly not applicable
- [ ] Failure/output snapshot updated where applicable
- [ ] Manual sample/quickstart command executed

Commands run:

```text
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
dotnet pack -c Release
```

## Privacy and security review

- [ ] No connection strings, credentials, private URLs, or real customer/employer data
- [ ] SQL and logs are synthetic or redacted
- [ ] Parameter values remain disabled by default
- [ ] New captured fields are documented and covered by redaction tests
- [ ] The original application exception/behavior is not hidden or modified

## Performance review

- [ ] Hot-path allocations and synchronization were considered
- [ ] Stack-trace capture remains optional and bounded
- [ ] No unsupported performance claim was added
- [ ] Benchmark added/updated if the hot path changed

## Public API and compatibility

- [ ] Public API change is intentional and documented
- [ ] net8.0 and net10.0 build/test
- [ ] Breaking-change risk is called out
- [ ] Report/schema compatibility is preserved or versioned
- [ ] Release-note category and labels are correct

## Documentation

- [ ] README or docs updated
- [ ] Sample updated when developer experience changed
- [ ] Known limitations / false-positive guidance updated
- [ ] XML documentation added for public APIs

## Reviewer focus

<!-- Tell the reviewer where uncertainty or risk is highest. -->

## Screenshots / output

<!-- Add redacted output only. -->

## Final checklist

- [ ] PR is focused and does not include unrelated refactoring
- [ ] PR title follows: `<type>(<scope>): <summary> [QG-###]`
- [ ] Commits are safe to squash
- [ ] All conversations are resolved
- [ ] CI is green
