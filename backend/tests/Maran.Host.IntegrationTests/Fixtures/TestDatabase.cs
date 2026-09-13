namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// One test's own database on the assembly's shared PostgreSQL server.
/// </summary>
/// <remarks>
/// It presents the same <c>GetConnectionString()</c> the container itself used to, so a test class
/// asks for its database exactly where it used to ask for its container — and gets a fresh, empty one
/// per test method, because xUnit builds a new instance of the class for every test. That per-test
/// isolation is not a nicety: these tests seed a user named <c>admin</c> against a unique index, so
/// two of them sharing one database would collide.
/// </remarks>
public sealed class TestDatabase
{
    /// <summary>The shared server the database is created on.</summary>
    private readonly PostgresFixture _server;

    /// <summary>The connection string of this test's database, once it has been created.</summary>
    private string _connectionString = string.Empty;

    /// <summary>Binds to the shared server without creating anything yet.</summary>
    /// <param name="server">The PostgreSQL server this assembly shares.</param>
    public TestDatabase(PostgresFixture server)
    {
        _server = server;
    }

    /// <summary>Creates this test's database. Called from the test class's <c>InitializeAsync</c>.</summary>
    public async Task CreateAsync()
    {
        _connectionString = await _server.CreateDatabaseAsync();
    }

    /// <summary>The connection string reaching this test's database.</summary>
    /// <returns>The connection string.</returns>
    public string GetConnectionString()
    {
        return _connectionString;
    }
}
