using Maran.Modules.ServerMigration.Commands.AddRemoteServer;

namespace Maran.Modules.ServerMigration.Tests.Commands.AddRemoteServer;

/// <summary>
/// The bounds a remote server's fields are held to. Every row of the refusing theory breaks exactly one field and names
/// the code that field answers with; the accepting theory feeds the same validator values it must accept, so a validator
/// mutated to refuse everything fails here.
/// </summary>
public sealed class AddRemoteServerCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly AddRemoteServerCommandValidator Validator = new();

    /// <summary>Builds a command from the fields under test.</summary>
    /// <param name="name">The label.</param>
    /// <param name="host">The address.</param>
    /// <param name="port">The port.</param>
    /// <param name="sshUser">The login.</param>
    /// <param name="allowedFrom">The key restriction.</param>
    /// <returns>The command.</returns>
    private static AddRemoteServerCommand Build(string name, string host, int port, string sshUser, string? allowedFrom)
    {
        return new AddRemoteServerCommand(name, host, port, sshUser, allowedFrom);
    }

    /// <summary>Values that are well formed in every field are accepted.</summary>
    /// <param name="name">The label.</param>
    /// <param name="host">The address.</param>
    /// <param name="port">The port.</param>
    /// <param name="sshUser">The login.</param>
    /// <param name="allowedFrom">The key restriction.</param>
    [Theory]
    [InlineData("old host", "old.example.net", 22, "maranmig", null)]
    [InlineData("a", "Old.Example.NET", 1, "root", "")]
    [InlineData("web 1", "203.0.113.5", 65535, "_svc-1", "203.0.113.9")]
    [InlineData("v6", "2001:db8::5", 2222, "m", "10.0.0.1, 192.168.0.0/24, 2001:db8::/32")]
    public void Well_formed_values_are_accepted(string name, string host, int port, string sshUser, string? allowedFrom)
    {
        var result = Validator.Validate(Build(name, host, port, sshUser, allowedFrom));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(error =>
        {
            return error.ErrorCode;
        })));
    }

    /// <summary>A label that is blank, or longer than a label may be, is refused.</summary>
    /// <param name="name">The label.</param>
    /// <param name="code">The code answered.</param>
    [Theory]
    [InlineData("", "RemoteServerNameRequired")]
    [InlineData("   ", "RemoteServerNameRequired")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890x", "RemoteServerNameMaxLength")]
    public void A_blank_or_overlong_label_is_refused(string name, string code)
    {
        var result = Validator.Validate(Build(name, "old.example.net", 22, "maranmig", null));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == code;
        });
    }

    /// <summary>An address that is not a name or an address literal is refused with its own code.</summary>
    /// <param name="host">The address.</param>
    /// <param name="code">The code answered.</param>
    [Theory]
    [InlineData("", "RemoteServerHostRequired")]
    [InlineData("not a host", "RemoteServerHostInvalid")]
    [InlineData("localhost", "RemoteServerHostInvalid")]
    [InlineData("old.example.net/path", "RemoteServerHostInvalid")]
    [InlineData("old.example.net:22", "RemoteServerHostInvalid")]
    [InlineData("1.2.3", "RemoteServerHostInvalid")]
    [InlineData("999.1.1.1", "RemoteServerHostInvalid")]
    [InlineData("fe80::1%eth0", "RemoteServerHostInvalid")]
    [InlineData("10.0.0.0/24", "RemoteServerHostInvalid")]
    public void An_address_that_is_neither_a_name_nor_a_literal_is_refused(string host, string code)
    {
        var result = Validator.Validate(Build("name", host, 22, "maranmig", null));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == code;
        });
    }

    /// <summary>A port outside 1 to 65535 is refused.</summary>
    /// <param name="port">The port.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_the_range_is_refused(int port)
    {
        var result = Validator.Validate(Build("name", "old.example.net", port, "maranmig", null));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == "RemoteServerPortInvalid";
        });
    }

    /// <summary>A login that is not the shape of a Linux user name is refused.</summary>
    /// <param name="sshUser">The login.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Root")]
    [InlineData("1abc")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("-lead")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
    public void A_login_that_is_not_a_system_user_name_is_refused(string sshUser)
    {
        var result = Validator.Validate(Build("name", "old.example.net", 22, sshUser, null));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == "RemoteServerSshUserInvalid";
        });
    }

    /// <summary>A key restriction that is not a list of addresses and ranges is refused, whatever it could do to the installed line.</summary>
    /// <param name="allowedFrom">The key restriction.</param>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.1/33")]
    [InlineData("10.0.0.5/24")]
    [InlineData("10.0.0.1,")]
    [InlineData("10.0.0.1\",command=\"x")]
    [InlineData("10.0.0.1 192.168.0.1")]
    [InlineData("*.example.net")]
    [InlineData("!10.0.0.1")]
    [InlineData("1.1.1.1,2.2.2.2,3.3.3.3,4.4.4.4,5.5.5.5,6.6.6.6,7.7.7.7,8.8.8.8,9.9.9.9,10.10.10.10,11.11.11.11,12.12.12.12,13.13.13.13,14.14.14.14,15.15.15.15,16.16.16.16,17.17.17.17")]
    public void A_key_restriction_that_is_not_a_list_of_ranges_is_refused(string allowedFrom)
    {
        var result = Validator.Validate(Build("name", "old.example.net", 22, "maranmig", allowedFrom));

        Assert.Contains(result.Errors, error =>
        {
            return error.ErrorCode == "RemoteServerAllowedFromInvalid";
        });
    }
}
