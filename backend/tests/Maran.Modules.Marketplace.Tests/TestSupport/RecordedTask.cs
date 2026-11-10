using Maran.Sdk.Contracts;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>One task a test began.</summary>
/// <param name="Id">The task id.</param>
/// <param name="Kind">The task kind.</param>
/// <param name="Subject">What the task acts on.</param>
/// <param name="Audience">Who sees the task.</param>
public sealed record RecordedTask(Guid Id, string Kind, string Subject, TaskAudience Audience);
