namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>One task an instrumented handler opened, and everything it did to it afterwards.</summary>
public sealed class RecordedTask
{
    /// <summary>The id handed back to the handler.</summary>
    public Guid Id { get; }

    /// <summary>The kind the handler opened it under.</summary>
    public string Kind { get; }

    /// <summary>What the handler said the operation acts on.</summary>
    public string Subject { get; }

    /// <summary>The correlation id the handler passed, or null.</summary>
    public string? CorrelationId { get; }

    /// <summary>Every stage the handler reported, in order.</summary>
    public List<(int Percent, string Line)> Reports { get; } = [];

    /// <summary>Whether the handler closed it as finished.</summary>
    public bool Completed { get; set; }

    /// <summary>The code the handler closed it as failed under, or null.</summary>
    public string? FailureCode { get; set; }

    /// <summary>Records a task the handler has just opened.</summary>
    /// <param name="id">The id handed back to the handler.</param>
    /// <param name="kind">The kind it was opened under.</param>
    /// <param name="subject">What the operation acts on.</param>
    /// <param name="correlationId">The correlation id the handler passed, or null.</param>
    public RecordedTask(Guid id, string kind, string subject, string? correlationId)
    {
        Id = id;
        Kind = kind;
        Subject = subject;
        CorrelationId = correlationId;
    }
}
