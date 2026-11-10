using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Queries.GetZoneTemplateVariables;
using Maran.Modules.Dns.Tests.TestSupport;

namespace Maran.Modules.Dns.Tests.Queries.GetZoneTemplateVariables;

/// <summary>The editor is told the closed catalogue by the server: the six variables and where each is allowed.</summary>
public sealed class GetZoneTemplateVariablesQueryHandlerTests
{
    /// <summary>The record types whose value may name the apex.</summary>
    private static readonly RecordType[] ApexTypes =
        [RecordType.Cname, RecordType.Mx, RecordType.Txt, RecordType.Srv, RecordType.Caa, RecordType.Ns];

    /// <summary>The catalogue lists the six variables, with their words and their record types.</summary>
    [Fact]
    public async Task The_catalogue_lists_the_six_variables_with_their_record_types()
    {
        using var context = new DnsTestContext();

        var result = await context.TemplateVariables.HandleAsync(new GetZoneTemplateVariablesQuery(), CancellationToken.None);

        Assert.Equal(
            ["{apex}", "{ipv4}", "{ipv6}", "{ns1}", "{ns2}", "{hostmaster}"],
            result.Value.Select(variable =>
            {
                return variable.Token;
            }));
        Assert.Equal("DnsTemplateVariableApex", result.Value[0].Description);
        Assert.Equal([RecordType.A, RecordType.Txt], result.Value.Single(variable =>
        {
            return variable.Token == "{ipv4}";
        }).RecordTypes);
        Assert.Equal(
            ApexTypes.Order(),
            result.Value[0].RecordTypes.Order());
        Assert.Equal([RecordType.Txt], result.Value.Single(variable =>
        {
            return variable.Token == "{hostmaster}";
        }).RecordTypes);
    }
}
