using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Sftp.Commands.CreateSftpUser;
using Maran.Modules.Sftp.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Turns FTPS on for the featured customer's first site, then creates every account's SFTP and FTPS
/// logins. The generated passwords are discarded.
/// </summary>
public sealed class FileTransferSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public FileTransferSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "file-transfer"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var featured = context.World.Accounts.Single(account =>
        {
            return account.Name == context.World.FeaturedCustomerAccountName;
        });

        await _runner.RunAsync<FtpsStatusDto>(
            Name,
            new EnableFtpsCommand(
                featured.Sites[0].Domain,
                string.Empty,
                DemoSeedCommandRunner.Origin,
                DemoSeedCommandRunner.Agent),
            cancellationToken);

        foreach (var account in context.World.Accounts)
        {
            var accountId = context.AccountId(account.Name);

            foreach (var name in account.SftpUsers)
            {
                await _runner.RunAsync<CreatedSftpUserDto>(
                    Name,
                    new CreateSftpUserCommand(
                        accountId,
                        name,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);
            }

            foreach (var name in account.FtpUsers)
            {
                await _runner.RunAsync<CreatedFtpUserDto>(
                    Name,
                    new CreateFtpUserCommand(
                        accountId,
                        name,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);
            }
        }
    }
}
