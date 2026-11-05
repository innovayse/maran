using System.Buffers.Binary;
using System.Text;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel the SFTP key endpoint tests run against: the real Host on real PostgreSQL with the agent's
/// SFTP seam replaced by a <see cref="RecordingKeySftpAgent"/>, two accounts (<c>acme</c> and
/// <c>beta</c>) each with an owner login (<c>owner-a</c> and <c>owner-b</c>) and one SFTP login named
/// <c>deploy</c>, and one administrator (<c>admin</c>).
/// </summary>
/// <remarks>
/// Rows are written and read through a context resolved outside a request, whose caller is anonymous.
/// Writing needs no filter, but every read here bypasses it (<c>IgnoreQueryFilters</c>, which tests may
/// use and <c>backend/src</c> may not), because the fail-closed tenant filter shows an anonymous caller
/// no row at all; the filter under test is the one on the endpoints, which a signed-in customer reaches.
/// </remarks>
public static class SftpKeyWorld
{
    /// <summary>Boots the panel and seeds the world described on the type.</summary>
    /// <param name="database">The test's own database.</param>
    /// <param name="agent">The agent double the key endpoints will drive.</param>
    /// <returns>The booted factory, which the caller disposes.</returns>
    public static async Task<WebApplicationFactory<Program>> BootAsync(
        TestDatabase database,
        RecordingKeySftpAgent agent)
    {
        var factory = EndpointHarness.CreateFactory(
            database,
            services =>
            {
                services.RemoveAll<IAgentSftpClient>();
                services.AddSingleton<IAgentSftpClient>(agent);
            });

        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, acme) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        var (_, beta) = await EndpointHarness.SeedAccountAsync(factory, "beta");
        await EndpointHarness.SeedCustomerAsync(factory, "owner-a", acme);
        await EndpointHarness.SeedCustomerAsync(factory, "owner-b", beta);
        await SeedLoginAsync(factory, "acme", "deploy");
        await SeedLoginAsync(factory, "beta", "deploy");

        return factory;
    }

    /// <summary>Stores one more SFTP login for an account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account's system user name, which prefixes the full name.</param>
    /// <param name="name">The login's name without the prefix.</param>
    /// <returns>The login's identifier.</returns>
    public static async Task<Guid> SeedLoginAsync(
        WebApplicationFactory<Program> factory,
        string accountName,
        string name)
    {
        var accountId = await SubUserTeamWorld.AccountIdAsync(factory, accountName);

        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var login = new SftpUser(Guid.NewGuid(), accountId, name, $"{accountName}_{name}", now);
        sftp.SftpUsers.Add(login);
        await sftp.SaveChangesAsync();

        return login.Id;
    }

    /// <summary>Reads a login's identifier by its full name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="fullName">The login as the host holds it, <c>acme_deploy</c> for instance.</param>
    /// <returns>The login's identifier.</returns>
    public static async Task<Guid> LoginIdAsync(WebApplicationFactory<Program> factory, string fullName)
    {
        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();

        // An anonymous caller sees no row through the tenant filter; see the remarks on the type.
        return await sftp.SftpUsers
            .IgnoreQueryFilters()
            .Where(login => login.FullName == fullName)
            .Select(login => login.Id)
            .SingleAsync();
    }

    /// <summary>Stores one key for a login directly, bypassing the endpoint and the agent.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="loginId">The login the key belongs to.</param>
    /// <param name="label">The key's label.</param>
    /// <param name="line">The key line; a real or a synthetic key.</param>
    /// <returns>The key's identifier.</returns>
    public static async Task<Guid> SeedKeyAsync(
        WebApplicationFactory<Program> factory,
        Guid loginId,
        string label,
        string line)
    {
        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        // Both the login and its keys are filtered rows; the anonymous seed caller sees neither.
        var login = await sftp.SftpUsers
            .IgnoreQueryFilters()
            .Include(row => row.Keys)
            .SingleAsync(row => row.Id == loginId);
        var added = login.AddKey(Guid.NewGuid(), label, SftpPublicKey.Parse(line).Value, now);
        Assert.True(added.IsSuccess);
        await sftp.SaveChangesAsync();

        return added.Value.Id;
    }

    /// <summary>Stores <paramref name="count"/> distinct synthetic keys for a login, one per index.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="loginId">The login the keys belong to.</param>
    /// <param name="count">How many keys to store.</param>
    /// <returns>The task that stores them.</returns>
    public static async Task SeedSyntheticKeysAsync(WebApplicationFactory<Program> factory, Guid loginId, int count)
    {
        for (var index = 0; index < count; index++)
        {
            await SeedKeyAsync(factory, loginId, $"filler {index}", SyntheticEd25519(index));
        }
    }

    /// <summary>Reads every key row the host holds, with the tenant filter bypassed.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>Every row, oldest first.</returns>
    public static async Task<IReadOnlyList<SftpUserKey>> KeyRowsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();

        // A test bypasses the filter to see every tenant's rows; banning it here would put a
        // suppression on the assertions that verify the rule (rules/csharp.md).
        return await sftp.SftpUserKeys.IgnoreQueryFilters().AsNoTracking().OrderBy(key => key.CreatedAt).ToListAsync();
    }

    /// <summary>Builds a well-formed Ed25519 key line that differs from every other index.</summary>
    /// <param name="index">Which synthetic key; the index is written into the 32 key bytes.</param>
    /// <returns>A line the panel's grammar accepts. It is not a usable key: no private half exists.</returns>
    private static string SyntheticEd25519(int index)
    {
        var algorithm = Encoding.ASCII.GetBytes("ssh-ed25519");
        var blob = new byte[4 + algorithm.Length + 4 + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(0, 4), (uint)algorithm.Length);
        algorithm.CopyTo(blob, 4);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + algorithm.Length, 4), 32);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(blob.Length - 32, 4), index + 1);

        return "ssh-ed25519 " + Convert.ToBase64String(blob);
    }
}
