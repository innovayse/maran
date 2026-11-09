using Maran.Samples.Hello.Common;
using Maran.Samples.Hello.Persistence;

namespace Maran.Samples.Hello.Queries.GetGreeting;

/// <summary>Handles <see cref="GetGreetingQuery"/> with one read of <c>hello.Greetings</c>.</summary>
public sealed class GetGreetingQueryHandler
{
    /// <summary>What the module says before anything has been written.</summary>
    public const string DefaultText = "Hello from a packaged module";

    /// <summary>The module's database context.</summary>
    private readonly HelloDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public GetGreetingQueryHandler(HelloDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the newest greeting.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the greeting; this operation never fails.</returns>
    public async Task<Result<GreetingDto>> HandleAsync(GetGreetingQuery query, CancellationToken cancellationToken)
    {
        var text = await _dbContext.Greetings
            .OrderByDescending(greeting => greeting.CreatedAt)
            .Select(greeting => greeting.Text)
            .FirstOrDefaultAsync(cancellationToken);

        return Result<GreetingDto>.Ok(new GreetingDto(text ?? DefaultText));
    }
}
