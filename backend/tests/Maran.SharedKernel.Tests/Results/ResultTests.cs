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
        var result = Result<int>.Fail(Error.Of("SitesDomainTaken", ErrorType.Conflict));

        Assert.False(result.IsSuccess);
        Assert.Equal("SitesDomainTaken", result.Error!.Code);
        Assert.Throws<InvalidOperationException>(() =>
        {
            return _ = result.Value;
        });
    }

    /// <summary>A failed result may carry a problem extension, and hands it back untouched.</summary>
    [Fact]
    public void Failed_result_may_carry_a_problem_extension()
    {
        var extension = ProblemExtension.Of("restore", new { DatabasesRestored = 1u });

        var result = Result<int>.Fail(Error.Of("RestorePartial", ErrorType.Failure), extension);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestorePartial", result.Error!.Code);
        Assert.Same(extension, result.Extension);
    }

    /// <summary>A failure built without an extension carries none.</summary>
    /// <remarks>
    /// The negative half of the seat: most failures have nothing to publish, and the pipeline reads
    /// null as "write only the standing members" — so a phantom extension here would put an empty
    /// member on every failure response in the panel.
    /// </remarks>
    [Fact]
    public void Failed_result_without_an_extension_carries_none()
    {
        var result = Result<int>.Fail(Error.Of("SitesDomainTaken", ErrorType.Conflict));

        Assert.Null(result.Extension);
    }

    /// <summary>A success never carries an extension; the facts of a success ride the value.</summary>
    [Fact]
    public void Ok_result_carries_no_extension()
    {
        var result = Result<int>.Ok(42);

        Assert.Null(result.Extension);
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
        var fail = Result<int>.Fail(Error.Of("SitesDomainTaken", ErrorType.Conflict)).Match(v =>
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
        var result = Result.Fail(Error.Of("SitesDomainTaken", ErrorType.Conflict));

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
        var fail = Result.Fail(Error.Of("SitesDomainTaken", ErrorType.Conflict)).Match(() =>
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
