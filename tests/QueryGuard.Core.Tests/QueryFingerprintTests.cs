using System;
using System.Collections.Generic;
using Xunit;

namespace QueryGuard.Tests;

public class QueryFingerprintTests
{
    [Fact]
    public void Fingerprints_are_equal_when_their_identifiers_match()
    {
        // Identity is the ID alone. Two records for the same query can retain differently
        // truncated sample text without landing in separate groups.
        var first = new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1");
        var second = new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1 /* different sample */");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Fingerprints_with_different_identifiers_are_not_equal()
    {
        var first = new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1");
        var second = new QueryFingerprint("QG-FP-DEADBEEF", "SELECT 1");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Identifier_comparison_is_case_sensitive_and_culture_independent()
    {
        var upper = new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1");
        var lower = new QueryFingerprint("QG-FP-1a2b3c4d", "SELECT 1");

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public void A_fingerprint_is_usable_as_a_dictionary_key()
    {
        var groups = new Dictionary<QueryFingerprint, int>
        {
            [new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1")] = 1,
        };

        groups[new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1")]++;

        Assert.Single(groups);
        Assert.Equal(2, groups[new QueryFingerprint("QG-FP-1A2B3C4D", "SELECT 1")]);
    }

    [Fact]
    public void An_identifier_is_required()
    {
        Assert.Throws<ArgumentException>(() => new QueryFingerprint(" ", "SELECT 1"));
        Assert.Throws<ArgumentException>(() => new QueryFingerprint(string.Empty, "SELECT 1"));
    }

    [Fact]
    public void Normalized_sql_is_required_but_may_be_empty()
    {
        Assert.Throws<ArgumentNullException>(() => new QueryFingerprint("QG-FP-0", null!));

        // An empty command text is degenerate but not a reason to throw on the command path.
        var fingerprint = new QueryFingerprint("QG-FP-0", string.Empty);
        Assert.Equal(string.Empty, fingerprint.NormalizedSql);
    }

    [Fact]
    public void Comparing_against_null_or_another_type_is_false_rather_than_throwing()
    {
        var fingerprint = TestData.Fingerprint();

        Assert.False(fingerprint.Equals(null));

        // A fingerprint is not equal to its own identifier string. Reporters and dictionary
        // lookups pass values through `object`, so the `Equals(object)` overload has to handle an
        // unrelated type by returning false rather than throwing.
        object identifierAsObject = fingerprint.Id;
        Assert.False(fingerprint.Equals(identifierAsObject));
    }

    [Fact]
    public void The_string_representation_is_the_identifier()
    {
        // Reporters and log templates interpolate the fingerprint directly, so this must be the
        // short ID rather than the whole SQL statement.
        var fingerprint = TestData.Fingerprint();

        Assert.Equal(fingerprint.Id, fingerprint.ToString());
        Assert.StartsWith(QueryFingerprint.IdPrefix, fingerprint.ToString(), StringComparison.Ordinal);
    }
}
