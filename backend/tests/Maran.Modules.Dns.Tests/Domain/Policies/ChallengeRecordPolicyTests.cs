using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// The law of the one record the certificate facility may write: what a value is, what a hostname is, and where
/// the record sits. Each refusing case is paired with an accepting one, because a policy that refused everything
/// would pass every test that only hands it broken input.
/// </summary>
public sealed class ChallengeRecordPolicyTests
{
    /// <summary>A real digest-shaped value: forty-three base64url characters.</summary>
    private static readonly string Digest = "Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERUY";

    /// <summary>The fixture really is forty-three characters, so the accepting cases test what they claim.</summary>
    [Fact]
    public void The_fixture_digest_is_forty_three_characters()
    {
        Assert.Equal(ChallengeRecordPolicy.ValueLength, Digest.Length);
    }

    /// <summary>A forty-three character base64url value is a challenge value.</summary>
    [Fact]
    public void A_forty_three_character_base64url_value_is_a_challenge_value()
    {
        Assert.True(ChallengeRecordPolicy.IsValue(Digest));
        Assert.True(ChallengeRecordPolicy.IsValue(new string('-', 43)));
        Assert.True(ChallengeRecordPolicy.IsValue(new string('_', 43)));
    }

    /// <summary>Text that could be anything but a digest is not a challenge value.</summary>
    [Theory]
    [InlineData("v=spf1 -all")]
    [InlineData("")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERU")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERUYx")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERU=")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERU\"")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERU\n")]
    [InlineData("Zm9vYmFyLWJhei1xdXV4XzEyMzQ1Njc4OTBBQkNERU ")]
    public void Text_that_is_not_a_digest_is_not_a_challenge_value(string value)
    {
        Assert.False(ChallengeRecordPolicy.IsValue(value));
    }

    /// <summary>Nothing is not a challenge value.</summary>
    [Fact]
    public void Nothing_is_not_a_challenge_value()
    {
        Assert.False(ChallengeRecordPolicy.IsValue(null));
    }

    /// <summary>A plain lower-case host name is a hostname a challenge can be placed under.</summary>
    [Theory]
    [InlineData("example.com")]
    [InlineData("www.shop.example.com")]
    [InlineData("xn--bcher-kva.example")]
    public void A_plain_lower_case_host_name_is_accepted(string hostname)
    {
        Assert.True(ChallengeRecordPolicy.IsHostname(hostname));
    }

    /// <summary>A wildcard, a trailing dot, an upper-case letter, the challenge label and a single label are refused.</summary>
    [Theory]
    [InlineData("*.example.com")]
    [InlineData("example.com.")]
    [InlineData("Example.com")]
    [InlineData("_acme-challenge.example.com")]
    [InlineData("example")]
    [InlineData("exa mple.com")]
    [InlineData("example.com\n")]
    [InlineData("")]
    public void A_name_with_more_than_one_spelling_or_none_is_refused(string hostname)
    {
        Assert.False(ChallengeRecordPolicy.IsHostname(hostname));
    }

    /// <summary>The candidate zones of a name are itself and each parent of two labels or more, longest first.</summary>
    [Fact]
    public void The_candidate_zones_of_a_name_are_itself_and_its_parents_longest_first()
    {
        var candidates = ChallengeRecordPolicy.ZoneCandidates("a.b.example.com");

        Assert.Equal(["a.b.example.com", "b.example.com", "example.com"], candidates);
    }

    /// <summary>The record sits at the label for the apex and beneath it for a deeper name.</summary>
    [Fact]
    public void The_record_name_is_the_label_at_the_apex_and_beneath_it_for_a_deeper_name()
    {
        Assert.Equal("_acme-challenge", ChallengeRecordPolicy.RecordNameIn("example.com", "example.com"));
        Assert.Equal("_acme-challenge.www", ChallengeRecordPolicy.RecordNameIn("example.com", "www.example.com"));
        Assert.Equal("_acme-challenge.a.b", ChallengeRecordPolicy.RecordNameIn("example.com", "a.b.example.com"));
    }

    /// <summary>A challenge name is the label itself or anything beneath it, and nothing that merely starts like it.</summary>
    [Fact]
    public void A_challenge_name_is_the_label_or_anything_beneath_it()
    {
        Assert.True(ChallengeRecordPolicy.IsChallengeName("_acme-challenge"));
        Assert.True(ChallengeRecordPolicy.IsChallengeName("_acme-challenge.www"));
        Assert.False(ChallengeRecordPolicy.IsChallengeName("_acme-challenged"));
        Assert.False(ChallengeRecordPolicy.IsChallengeName("www._acme-challenge"));
        Assert.False(ChallengeRecordPolicy.IsChallengeName("@"));
    }
}
