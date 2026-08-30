using Maran.SharedKernel.Results;

namespace Maran.SharedKernel.Tests.Results;

/// <summary>Behavioral contract of Result&lt;T&gt;.</summary>
public sealed class ResultTests
{
    /// <summary>Ok result carries value.</summary>
    [Fact]
    public void Ok_result_carries_value()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    /// <summary>Failed result carries error and guards value.</summary>
    [Fact]
    public void Failed_result_carries_error_and_guards_value()
    {
        var result = Result<int>.Fail(Error.Of("SitesDomainTaken"));

        Assert.False(result.IsSuccess);
        Assert.Equal("SitesDomainTaken", result.Error!.Code);
        Assert.Throws<InvalidOperationException>(() =>
        {
            return _ = result.Value;
        });
    }

    /// <summary>Match routes to the correct branch.</summary>
    [Fact]
    public void Match_routes_to_the_correct_branch()
    {
        var ok = Result<int>.Ok(1).Match(v =>
        {
            return $"ok:{v}";
        }, e =>
        {
            return $"err:{e.Code}";
        });
        var fail = Result<int>.Fail(Error.Of("SitesDomainTaken")).Match(v =>
        {
            return $"ok:{v}";
        }, e =>
        {
            return $"err:{e.Code}";
        });

        Assert.Equal("ok:1", ok);
        Assert.Equal("err:SitesDomainTaken", fail);
    }

    /// <summary>Non generic ok result succeeds with no error.</summary>
    [Fact]
    public void Non_generic_ok_result_succeeds_with_no_error()
    {
        var result = Result.Ok();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    /// <summary>Non generic fail result carries the error.</summary>
    [Fact]
    public void Non_generic_fail_result_carries_the_error()
    {
        var result = Result.Fail(Error.Of("SitesDomainTaken"));

        Assert.False(result.IsSuccess);
        Assert.Equal("SitesDomainTaken", result.Error!.Code);
    }

    /// <summary>Non generic match routes to the correct branch.</summary>
    [Fact]
    public void Non_generic_match_routes_to_the_correct_branch()
    {
        var ok = Result.Ok().Match(() =>
        {
            return "ok";
        }, e =>
        {
            return $"err:{e.Code}";
        });
        var fail = Result.Fail(Error.Of("SitesDomainTaken")).Match(() =>
        {
            return "ok";
        }, e =>
        {
            return $"err:{e.Code}";
        });

        Assert.Equal("ok", ok);
        Assert.Equal("err:SitesDomainTaken", fail);
    }
}
