# Launch materials

Drafts for announcing the first preview. Kept in the repository rather than in a notes app so the
claims in them are reviewable in a diff against the code that has to support them.

| File | What it is |
| --- | --- |
| [article.md](./article.md) | The technical write-up. Cross-post target: dev.to, a personal blog, or a GitHub Discussion |
| [demo.md](./demo.md) | A 90-second screen recording script — exact commands, expected output, and what to say |
| [posts.md](./posts.md) | Short community posts, each ending in a specific question rather than a request for stars |

## The rule these were written under

**Market the problem and the evidence, not the author.** The strongest thing this project has to say is
that an endpoint returned `200 OK` with correct data and executed the same query 51 times, and that a
budget assertion caught it in a test. That story needs no adjectives.

Concretely, nothing in these drafts may:

- claim a detection capability beyond "the same normalized SQL executed N times in this scope";
- quote a performance number that is not in [docs/benchmarks.md](../benchmarks.md) with its raw output;
- describe a provider as supported beyond its tier in [docs/providers/README.md](../providers/README.md);
- compare QueryGuard to a profiler or an APM as a replacement — they answer a different question;
- ask for stars, upvotes, or shares.

Each draft states the limits in the same voice as the capabilities. A tool that hides its weaknesses gets
distrusted the first time someone finds one, and launch is the worst possible moment to spend that trust.

## Before publishing anything

1. **The version must be real.** Every install command references `0.1.0-preview.1`. Publishing these
   before the packages are on nuget.org means the first thing a reader tries fails. See
   [docs/releasing.md](../releasing.md).
2. **Re-run the demo.** The output quoted in all three drafts came from a real run; if the sample or the
   seed data changed since, the numbers move and the drafts are wrong.
3. **Point feedback at one place.** The posts link to a single feedback thread so responses do not
   scatter across four sites.
