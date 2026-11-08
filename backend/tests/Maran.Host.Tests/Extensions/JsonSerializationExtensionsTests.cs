using System.Text.Json;
using Maran.Host.Extensions;
using Maran.Sdk.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Extensions;

/// <summary>
/// Behavioral contract of <see cref="JsonSerializationExtensions.AddPanelJsonSerialization"/>: both
/// API surfaces write a value exactly as the server-sent-event stream's <see cref="PanelJsonOptions.Wire"/>
/// does, so the SPA's one set of types describes a response body and a stream frame alike.
/// </summary>
public sealed class JsonSerializationExtensionsTests
{
    /// <summary>A payload with every case the two transports could spell differently: a null, an enum, a name.</summary>
    private static readonly Sample Payload = new("alpha", null, DayOfWeek.Thursday);

    /// <summary>A null member is written as <c>null</c> by the stream, and not left out.</summary>
    [Fact]
    public void The_stream_writes_a_null_member_as_null()
    {
        var written = JsonSerializer.Serialize(Payload, PanelJsonOptions.Wire);

        Assert.Equal("{\"name\":\"alpha\",\"note\":null,\"day\":\"thursday\"}", written);
    }

    /// <summary>The controllers surface writes the payload exactly as the stream does.</summary>
    [Fact]
    public void The_controllers_surface_writes_exactly_what_the_stream_writes()
    {
        using var provider = BuildProvider();
        var mvc = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;

        Assert.Equal(
            JsonSerializer.Serialize(Payload, PanelJsonOptions.Wire),
            JsonSerializer.Serialize(Payload, mvc.JsonSerializerOptions));
    }

    /// <summary>The minimal-endpoint surface writes the payload exactly as the stream does.</summary>
    [Fact]
    public void The_minimal_endpoint_surface_writes_exactly_what_the_stream_writes()
    {
        using var provider = BuildProvider();
        var http = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;

        Assert.Equal(
            JsonSerializer.Serialize(Payload, PanelJsonOptions.Wire),
            JsonSerializer.Serialize(Payload, http.SerializerOptions));
    }

    /// <summary>Builds a container holding only the panel's JSON registrations.</summary>
    /// <returns>The provider, to be disposed by the caller.</returns>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPanelJsonSerialization();
        return services.BuildServiceProvider();
    }

    /// <summary>The payload the tests serialize.</summary>
    /// <param name="Name">A plain text member.</param>
    /// <param name="Note">A member that is null.</param>
    /// <param name="Day">An enum member.</param>
    private sealed record Sample(string Name, string? Note, DayOfWeek Day);
}
