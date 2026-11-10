using System.Text.Json;
using Maran.Host.Dashboard;
using Maran.Host.Tests.OpenApi.Fixtures;
using Maran.Modules.Accounts.Common;
using Maran.Sdk.Serialization;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.OpenApi;

/// <summary>
/// The document describes what the serializer writes, not the shape of the C# types: every endpoint the
/// host maps, a nullable member as nullable, a closed family of DTOs as its cases, and a secret as the
/// string it travels as. Each claim is read off the rendered JSON, and each has a control that must not
/// be affected, because a filter that changed everything would satisfy every test that only looks for change.
/// </summary>
public sealed class OpenApiWireShapeTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>The prefix of a reference to a named schema.</summary>
    private const string ComponentsPrefix = "#/components/schemas/";

    /// <summary>The booted hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiWireShapeTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>
    /// The minimal endpoints and the controller action that fronts the navigation badge are in the document.
    /// </summary>
    [Fact]
    public void Every_endpoint_the_host_maps_is_in_the_document()
    {
        using var document = JsonDocument.Parse(_documents.First);

        (string Method, string Path)[] expected =
        [
            ("get", "/api/v1/modules"),
            ("get", "/api/v1/dashboard"),
            ("get", "/api/v1/dashboard/attention-count"),
            ("get", "/api/v1/dashboard/first-run"),
            ("post", "/api/v1/search"),
            ("get", "/api/v1/mail-queue/count"),
        ];

        var missing = expected
            .Where(item => { return !OpenApiReader.TryOperation(document.RootElement, item.Method, item.Path, out _); })
            .Select(item => { return $"{item.Method} {item.Path}"; })
            .ToList();

        Assert.Empty(missing);
        Assert.False(OpenApiReader.TryOperation(document.RootElement, "get", "/api/v1/no-such-endpoint", out _));
    }

    /// <summary>A minimal endpoint's answer is described by its schema, and an anonymous one needs no credential.</summary>
    [Fact]
    public void A_minimal_endpoint_describes_its_answer_and_its_credential()
    {
        using var document = JsonDocument.Parse(_documents.First);

        Assert.True(OpenApiReader.TryOperation(document.RootElement, "get", "/api/v1/modules", out var modules));
        var body = modules.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.Equal("array", body.GetProperty("type").GetString());
        Assert.False(modules.TryGetProperty("security", out _));

        Assert.True(OpenApiReader.TryOperation(document.RootElement, "get", "/api/v1/dashboard/first-run", out var firstRun));
        Assert.True(firstRun.TryGetProperty("security", out _));
    }

    /// <summary>
    /// A member that is a nullable reference is described as nullable, and a member that is not stays a bare reference.
    /// </summary>
    [Theory]
    [InlineData("Modules.Tasks.Common.PanelTaskDto", "failure")]
    [InlineData("Modules.Identity.Common.LoginResultDto", "session")]
    [InlineData("Modules.Backups.Common.BackupAttentionDto", "newest")]
    [InlineData("Modules.Updates.Common.PanelUpdateDto", "available")]
    public void A_nullable_reference_member_is_described_as_nullable(string schemaName, string member)
    {
        using var document = JsonDocument.Parse(_documents.First);

        var property = SchemaOf(document, schemaName).GetProperty("properties").GetProperty(member);

        Assert.True(property.GetProperty("nullable").GetBoolean());
        var target = Assert.Single(property.GetProperty("allOf").EnumerateArray().ToList());
        Assert.StartsWith(ComponentsPrefix, target.GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }

    /// <summary>The control: a reference member that cannot be null stays a bare reference.</summary>
    [Fact]
    public void A_non_nullable_reference_member_stays_a_bare_reference()
    {
        using var document = JsonDocument.Parse(_documents.First);

        var status = SchemaOf(document, "Modules.Tasks.Common.PanelTaskDto").GetProperty("properties").GetProperty("status");

        Assert.True(status.TryGetProperty("$ref", out _));
        Assert.False(status.TryGetProperty("nullable", out _));
    }

    /// <summary>
    /// The base of a closed family is the choice between its cases, and each case states its discriminator value.
    /// </summary>
    [Theory]
    [InlineData("Modules.Accounts.Common.ResourceCeilingDto", "kind", "limited")]
    [InlineData("Modules.Accounts.Common.ResourceCountDto", "state", "counted")]
    [InlineData("Modules.Identity.Common.InvitationStatusDto", "state", "pending")]
    [InlineData("Modules.Monitoring.Common.QuotaBackingDto", "state", "enforced")]
    public void A_polymorphic_dto_is_described_as_the_choice_between_its_cases(
        string baseName,
        string discriminator,
        string caseValue)
    {
        using var document = JsonDocument.Parse(_documents.First);

        var schema = SchemaOf(document, baseName);
        var cases = schema.GetProperty("oneOf").EnumerateArray().ToList();
        Assert.True(cases.Count >= 2);
        Assert.Equal(discriminator, schema.GetProperty("discriminator").GetProperty("propertyName").GetString());

        var caseName = schema.GetProperty("discriminator").GetProperty("mapping").GetProperty(caseValue).GetString()!;
        Assert.Contains(cases, item => { return item.GetProperty("$ref").GetString() == caseName; });

        var derived = SchemaOf(document, caseName[ComponentsPrefix.Length..]);
        var member = derived.GetProperty("properties").GetProperty(discriminator);
        Assert.Equal(caseValue, Assert.Single(member.GetProperty("enum").EnumerateArray().ToList()).GetString());
        Assert.Contains(discriminator, derived.GetProperty("required").EnumerateArray().Select(item => { return item.GetString(); }));
    }

    /// <summary>The control: a DTO that is not a family is not turned into a choice.</summary>
    [Fact]
    public void A_dto_that_is_not_polymorphic_has_no_cases()
    {
        using var document = JsonDocument.Parse(_documents.First);

        var plan = SchemaOf(document, "Modules.Accounts.Common.PlanDto");

        Assert.False(plan.TryGetProperty("oneOf", out _));
        Assert.False(plan.TryGetProperty("discriminator", out _));
        Assert.True(plan.GetProperty("properties").EnumerateObject().Any());
    }

    /// <summary>
    /// What the serializer writes for a case carries exactly the members the document gives that case, so the
    /// schema is the wire and not an opinion about it.
    /// </summary>
    [Fact]
    public void A_case_is_described_with_the_members_the_serializer_writes_for_it()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var written = JsonSerializer.Serialize<ResourceCeilingDto>(new LimitedResourceCeilingDto(3), PanelJsonOptions.Wire);
        using var wire = JsonDocument.Parse(written);

        var described = SchemaOf(document, "Modules.Accounts.Common.LimitedResourceCeilingDto")
            .GetProperty("properties").EnumerateObject().Select(item => { return item.Name; }).Order().ToList();
        var actual = wire.RootElement.EnumerateObject().Select(item => { return item.Name; }).Order().ToList();

        Assert.Equal(actual, described);
        Assert.Equal("limited", wire.RootElement.GetProperty("kind").GetString());
    }

    /// <summary>A secret is described as the plain string it is written as, and no object stands in for it.</summary>
    [Fact]
    public void A_sensitive_string_is_described_as_a_string()
    {
        using var document = JsonDocument.Parse(_documents.First);

        var password = SchemaOf(document, "Modules.Ftp.Common.CreatedFtpUserDto").GetProperty("properties").GetProperty("password");
        Assert.Equal("string", password.GetProperty("type").GetString());
        Assert.False(document.RootElement.GetProperty("components").GetProperty("schemas")
            .TryGetProperty("SharedKernel.Security.SensitiveString", out _));

        var written = JsonSerializer.Serialize(new SensitiveString("s3cret"), PanelJsonOptions.Wire);
        Assert.Equal("\"s3cret\"", written);
    }

    /// <summary>A dashboard section is described as the converter writes it: loaded with data, or unavailable without.</summary>
    [Fact]
    public void A_dashboard_section_is_described_as_loaded_with_data_or_unavailable_without()
    {
        using var document = JsonDocument.Parse(_documents.First);

        var section = SchemaOf(document, "Host.Dashboard.DashboardSectionOfSystem.Int32");
        var cases = section.GetProperty("oneOf").EnumerateArray().ToList();
        Assert.Equal(2, cases.Count);

        var loaded = cases.Single(item => { return item.GetProperty("properties").TryGetProperty("data", out _); });
        var unavailable = cases.Single(item => { return !item.GetProperty("properties").TryGetProperty("data", out _); });
        Assert.Equal("loaded", loaded.GetProperty("properties").GetProperty("state").GetProperty("enum")[0].GetString());
        Assert.Equal("unavailable", unavailable.GetProperty("properties").GetProperty("state").GetProperty("enum")[0].GetString());

        var written = JsonSerializer.Serialize(DashboardSection.Loaded(7), PanelJsonOptions.Wire);
        using var wire = JsonDocument.Parse(written);
        Assert.Equal("loaded", wire.RootElement.GetProperty("state").GetString());
        Assert.Equal(7, wire.RootElement.GetProperty("data").GetInt32());
    }

    /// <summary>Finds one named schema.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="name">The schema's component name.</param>
    /// <returns>The schema; the test fails when the document has none by that name.</returns>
    private static JsonElement SchemaOf(JsonDocument document, string name)
    {
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(name, out var schema), $"the document has no schema named {name}");
        return schema;
    }
}
