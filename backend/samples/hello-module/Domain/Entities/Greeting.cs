namespace Maran.Samples.Hello.Domain.Entities;

/// <summary>
/// One greeting the sample module stores in <c>hello.Greetings</c>. The newest one is what
/// <c>GET api/v1/hello</c> answers with. It belongs to the server and to no customer, so it carries no
/// account id and the context applies no tenant filter to it.
/// </summary>
public sealed class Greeting
{
    /// <summary>The longest greeting the column holds.</summary>
    public const int TextMaxLength = 200;

    /// <summary>Identity of the greeting.</summary>
    public Guid Id { get; private set; }

    /// <summary>What the greeting says.</summary>
    public string Text { get; private set; }

    /// <summary>When the greeting was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a greeting.</summary>
    /// <param name="id">Identity of the greeting.</param>
    /// <param name="text">What it says; not blank and at most <see cref="TextMaxLength"/> characters.</param>
    /// <param name="createdAt">When it was written, from the panel's clock.</param>
    /// <exception cref="ArgumentException">The text is blank or longer than the column.</exception>
    public Greeting(Guid id, string text, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > TextMaxLength)
        {
            throw new ArgumentException($"A greeting is 1 to {TextMaxLength} characters.", nameof(text));
        }

        Id = id;
        Text = text;
        CreatedAt = createdAt;
    }

    /// <summary>The parameterless constructor EF Core materialises rows through.</summary>
    private Greeting()
    {
        Text = string.Empty;
    }
}
