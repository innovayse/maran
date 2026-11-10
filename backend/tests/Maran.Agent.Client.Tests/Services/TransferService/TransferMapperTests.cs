using Maran.Agent.Client.Services.TransferService;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>Every enum value of the source rpcs maps, and a value this build does not know is refused.</summary>
/// <remarks>
/// The census tests walk the wire enum's own defined values, so a member added to the proto without a
/// matching arm in the mapper fails here by name instead of reaching an administrator as a refused read.
/// </remarks>
public sealed class TransferMapperTests
{
    /// <summary>A number no enum in the proto defines.</summary>
    private const int Undefined = 99;

    /// <summary>The defined values of the failure reason enum, one theory row each.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceFailureReason> FailureReasons()
    {
        return Rows<Wire.SourceFailureReason>();
    }

    /// <summary>The defined values of the inventory step enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceInventoryStep> Steps()
    {
        return Rows<Wire.SourceInventoryStep>();
    }

    /// <summary>The defined values of the warning kind enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceWarningKind> WarningKinds()
    {
        return Rows<Wire.SourceWarningKind>();
    }

    /// <summary>The defined values of the site backend enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceSiteBackend> SiteBackends()
    {
        return Rows<Wire.SourceSiteBackend>();
    }

    /// <summary>The defined values of the source kind enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceKind> Kinds()
    {
        return Rows<Wire.SourceKind>();
    }

    /// <summary>The defined values of the database server enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceDatabaseServer> DatabaseServers()
    {
        return Rows<Wire.SourceDatabaseServer>();
    }

    /// <summary>The defined values of the web server enum.</summary>
    /// <returns>One row per defined value.</returns>
    public static TheoryData<Wire.SourceWebServer> WebServers()
    {
        return Rows<Wire.SourceWebServer>();
    }

    /// <summary>Each failure reason maps to the mirror member of the same name; "unspecified" stays unspecified.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(FailureReasons))]
    public void Every_failure_reason_maps_to_the_member_of_the_same_name(Wire.SourceFailureReason wire)
    {
        var mapped = TransferMapper.ToReason(wire);

        Assert.Equal(wire.ToString(), mapped.ToString());
    }

    /// <summary>A failure reason from a newer agent is read as unspecified, because the code beside it still speaks.</summary>
    [Fact]
    public void An_undefined_failure_reason_is_unspecified()
    {
        Assert.Equal(SourceFailureReason.Unspecified, TransferMapper.ToReason((Wire.SourceFailureReason)Undefined));
    }

    /// <summary>Each step maps to the member of the same name; "not a step" and an unknown number map to nothing.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(Steps))]
    public void Every_step_maps_to_the_member_of_the_same_name(Wire.SourceInventoryStep wire)
    {
        var mapped = TransferMapper.ToStep(wire);

        if (wire == Wire.SourceInventoryStep.Unspecified)
        {
            Assert.Null(mapped);
        }
        else
        {
            Assert.Equal(wire.ToString(), mapped!.Value.ToString());
        }
    }

    /// <summary>An undefined step is refused.</summary>
    [Fact]
    public void An_undefined_step_is_refused()
    {
        Assert.Null(TransferMapper.ToStep((Wire.SourceInventoryStep)Undefined));
    }

    /// <summary>Each warning kind maps to the member of the same name; "not a warning" maps to nothing.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(WarningKinds))]
    public void Every_warning_kind_maps_to_the_member_of_the_same_name(Wire.SourceWarningKind wire)
    {
        var mapped = TransferMapper.ToWarning(new Wire.SourceWarning { Kind = wire, Subject = "alice" });

        if (wire == Wire.SourceWarningKind.Unspecified)
        {
            Assert.Null(mapped);
        }
        else
        {
            Assert.Equal(wire.ToString(), mapped!.Kind.ToString());
            Assert.Equal("alice", mapped.Subject);
        }
    }

    /// <summary>An undefined warning kind is refused.</summary>
    [Fact]
    public void An_undefined_warning_kind_is_refused()
    {
        Assert.Null(TransferMapper.ToWarning(new Wire.SourceWarning { Kind = (Wire.SourceWarningKind)Undefined }));
    }

    /// <summary>Each site backend maps to the member of the same name; "not determined" becomes the explicit unknown.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(SiteBackends))]
    public void Every_site_backend_maps_to_a_member(Wire.SourceSiteBackend wire)
    {
        var account = AccountWithSite(wire);

        var mapped = TransferMapper.ToAccount(account);

        var backend = Assert.Single(mapped!.Sites).Backend;
        var expected = wire == Wire.SourceSiteBackend.Unspecified ? nameof(SourceSiteBackend.Unknown) : wire.ToString();
        Assert.Equal(expected, backend.ToString());
    }

    /// <summary>An undefined site backend refuses the whole account.</summary>
    [Fact]
    public void An_undefined_site_backend_refuses_the_account()
    {
        Assert.Null(TransferMapper.ToAccount(AccountWithSite((Wire.SourceSiteBackend)Undefined)));
    }

    /// <summary>A PHP version is carried only by a PHP site.</summary>
    [Fact]
    public void A_php_version_is_carried_only_by_a_php_site()
    {
        var php = AccountWithSite(Wire.SourceSiteBackend.Php);
        var plain = AccountWithSite(Wire.SourceSiteBackend.Static);

        Assert.Equal("8.3", TransferMapper.ToAccount(php)!.Sites[0].PhpVersion);
        Assert.Null(TransferMapper.ToAccount(plain)!.Sites[0].PhpVersion);
    }

    /// <summary>Each source kind maps to the member of the same name; "not determined" is the absence of a kind.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_source_kind_maps_to_the_member_of_the_same_name(Wire.SourceKind wire)
    {
        var mapped = TransferMapper.ToTestedSource(new Wire.TestedSource { Kind = wire });

        if (wire == Wire.SourceKind.Unspecified)
        {
            Assert.Null(mapped!.Kind);
        }
        else
        {
            Assert.Equal(wire.ToString(), mapped!.Kind!.Value.ToString());
        }
    }

    /// <summary>Each database server maps to its member; "none or unknown" is the absence of one.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(DatabaseServers))]
    public void Every_database_server_maps_to_its_member(Wire.SourceDatabaseServer wire)
    {
        var mapped = TransferMapper.ToTestedSource(new Wire.TestedSource { DatabaseServer = wire });

        if (wire == Wire.SourceDatabaseServer.Unspecified)
        {
            Assert.Null(mapped!.DatabaseServer);
        }
        else
        {
            Assert.Equal(wire.ToString().ToUpperInvariant(), mapped!.DatabaseServer!.Value.ToString().ToUpperInvariant());
        }
    }

    /// <summary>Each web server maps to the member of the same name; "none or unknown" is the absence of one.</summary>
    /// <param name="wire">A defined wire value.</param>
    [Theory]
    [MemberData(nameof(WebServers))]
    public void Every_web_server_maps_to_the_member_of_the_same_name(Wire.SourceWebServer wire)
    {
        var mapped = TransferMapper.ToTestedSource(new Wire.TestedSource { WebServer = wire });

        if (wire == Wire.SourceWebServer.Unspecified)
        {
            Assert.Null(mapped!.WebServer);
        }
        else
        {
            Assert.Equal(wire.ToString(), mapped!.WebServer!.Value.ToString());
        }
    }

    /// <summary>A value of any probe enum this build has no member for refuses the whole observation.</summary>
    /// <remarks>
    /// The inverse of the census tests above: the same call with a defined value succeeds there, so a mapper
    /// that refused everything would fail them and one that accepted everything would fail this.
    /// </remarks>
    /// <param name="which">Which probe member carries the undefined value.</param>
    [Theory]
    [InlineData("kind")]
    [InlineData("database")]
    [InlineData("web")]
    public void An_undefined_probe_value_refuses_the_observation(string which)
    {
        var tested = new Wire.TestedSource();
        if (which == "kind")
        {
            tested.Kind = (Wire.SourceKind)Undefined;
        }
        else if (which == "database")
        {
            tested.DatabaseServer = (Wire.SourceDatabaseServer)Undefined;
        }
        else
        {
            tested.WebServer = (Wire.SourceWebServer)Undefined;
        }

        Assert.Null(TransferMapper.ToTestedSource(tested));
    }

    /// <summary>A size is reported only beside its known flag.</summary>
    [Fact]
    public void A_size_is_reported_only_when_it_was_measured()
    {
        var measured = new Wire.SourceAccount { Name = "a", DiskBytes = 5, DiskKnown = true };
        var unmeasured = new Wire.SourceAccount { Name = "a", DiskBytes = 5, DiskKnown = false };

        Assert.Equal(5L, TransferMapper.ToAccount(measured)!.DiskBytes);
        Assert.Null(TransferMapper.ToAccount(unmeasured)!.DiskBytes);
    }

    /// <summary>Every value an enum defines, as theory rows.</summary>
    /// <typeparam name="T">The wire enum.</typeparam>
    /// <returns>One row per defined value, so a member added to the proto is exercised without an edit here.</returns>
    private static TheoryData<T> Rows<T>()
        where T : struct, Enum
    {
        var rows = new TheoryData<T>();
        foreach (var value in Enum.GetValues<T>())
        {
            rows.Add(value);
        }

        return rows;
    }

    /// <summary>An account message holding one site of the given backend.</summary>
    /// <param name="backend">The wire backend of the site.</param>
    /// <returns>The account.</returns>
    private static Wire.SourceAccount AccountWithSite(Wire.SourceSiteBackend backend)
    {
        var account = new Wire.SourceAccount { Name = "alice", Home = "/home/alice" };
        account.Sites.Add(new Wire.SourceSite
        {
            Domain = "example.test",
            Backend = backend,
            PhpVersion = "8.3",
            DocumentRoot = "/home/alice/public_html",
        });

        return account;
    }
}
