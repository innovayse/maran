using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="ReservedAccountNamePolicy"/>. The expected names are spelled out
/// here, entry for entry from the threat note, and are deliberately not read back from the policy:
/// a test that iterated the policy's own set would agree with any broken version of it.
/// </summary>
public sealed class ReservedAccountNamePolicyTests
{
    /// <summary>The names of the threat note's table, in its six classes.</summary>
    public static readonly string[] NoteNames =
    [
        "root", "daemon", "bin", "sys", "sync", "games", "man", "mail", "news", "uucp", "proxy", "backup",
        "list", "irc", "gnats", "nobody", "nogroup", "adm", "halt", "shutdown", "operator", "gopher", "ftp",
        "www", "nfsnobody",
        "nginx", "apache", "httpd", "sshd", "dbus", "polkitd", "rpc", "rpcuser", "chrony", "ntp", "tss",
        "tcpdump", "avahi", "colord", "sssd", "unbound", "named", "bind", "mysql", "mariadb", "postgres",
        "redis", "memcached", "mongodb", "mongod", "rabbitmq", "elasticsearch", "postfix", "dovecot",
        "dovenull", "exim", "opendkim", "clamav", "amavis", "spamd", "vsftpd", "saslauth", "mailnull",
        "smmsp", "squid", "haproxy", "varnish", "tomcat", "git",
        "maran",
        "sudo", "wheel", "admin", "staff", "users", "shadow", "disk", "tty", "utmp", "dialout", "cdrom",
        "docker", "lxd", "kvm", "libvirt",
        "administrator", "hostmaster", "postmaster", "webmaster", "abuse", "security", "noc",
        "ubuntu", "debian", "almalinux", "rocky", "centos", "fedora", "ec2_user", "cloud_user", "vagrant",
        "azureuser", "opc",
    ];

    /// <summary>Every name in the note is reserved.</summary>
    public static TheoryData<string> EveryNoteName()
    {
        var data = new TheoryData<string>();
        foreach (var name in NoteNames)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>Every reserved name is reserved.</summary>
    [Theory]
    [MemberData(nameof(EveryNoteName))]
    public void Every_reserved_name_is_reserved(string name)
    {
        Assert.True(ReservedAccountNamePolicy.IsReserved(name));
    }

    /// <summary>The policy holds no name the note does not list, so the two lists are equal.</summary>
    [Fact]
    public void The_policy_lists_exactly_the_names_of_the_note()
    {
        Assert.Equal(NoteNames.Length, NoteNames.Distinct().Count());
        Assert.True(ReservedAccountNamePolicy.ExactNames.SetEquals(NoteNames));
    }

    /// <summary>A name that only begins with a reserved name is a different name and is not reserved.</summary>
    [Theory]
    [InlineData("rooted")]
    [InlineData("wwwsite")]
    [InlineData("mysqlfan")]
    [InlineData("xroot")]
    public void A_name_that_only_begins_with_a_reserved_name_is_not_reserved(string name)
    {
        Assert.False(ReservedAccountNamePolicy.IsReserved(name));
    }

    /// <summary>Every name starting with the panel prefix is reserved, listed or not.</summary>
    [Theory]
    [InlineData("maran_api")]
    [InlineData("maran_x")]
    [InlineData("maran_agent_2")]
    public void Every_name_starting_with_the_panel_prefix_is_reserved(string name)
    {
        Assert.True(ReservedAccountNamePolicy.IsReserved(name));
    }

    /// <summary>The prefix is the underscore form only, so a name merely starting with the word is not reserved.</summary>
    [Theory]
    [InlineData("marantha")]
    [InlineData("marana")]
    public void A_name_starting_with_the_word_but_not_the_prefix_is_not_reserved(string name)
    {
        Assert.False(ReservedAccountNamePolicy.IsReserved(name));
    }

    /// <summary>Matching is case-sensitive: case is the alphabet's job, not this list's.</summary>
    [Fact]
    public void Matching_is_case_sensitive()
    {
        Assert.False(ReservedAccountNamePolicy.IsReserved("Root"));
    }

    /// <summary>Ordinary account names are not reserved.</summary>
    [Theory]
    [InlineData("acme")]
    [InlineData("client_42")]
    [InlineData("a23")]
    public void A_valid_ordinary_name_is_not_reserved(string name)
    {
        Assert.False(ReservedAccountNamePolicy.IsReserved(name));
    }
}
