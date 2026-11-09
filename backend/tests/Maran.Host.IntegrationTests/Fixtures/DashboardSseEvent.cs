using System.Text.Json;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>One server-sent event read off the dashboard stream: its name and its parsed JSON data.</summary>
/// <param name="Name">The event's name.</param>
/// <param name="Payload">The event's data, cloned so it outlives the document it was parsed from.</param>
public sealed record DashboardSseEvent(string Name, JsonElement Payload);
