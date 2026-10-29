using System.Collections.Frozen;

namespace Maran.Modules.Accounts.Domain.Policies;

/// <summary>
/// Which account names the panel will not create because the host already gives them, or may give
/// them, another meaning: a system identity, a daemon's user, a group other software trusts, the
/// panel's own identity, a role mailbox, or the default login of a cloud image.
/// </summary>
/// <remarks>
/// <para>
/// <b>Advice and an early, localized refusal; the agent is the boundary.</b> The agent refuses the
/// same names through its own managed-account type, which is what actually protects the host
/// (rules/security.md item 1). This copy exists so an administrator is told "reserved" in their own
/// language instead of an agent error after the request was accepted. The two lists are the table in
/// <c>docs/superpowers/notes/2026-10-26-account-reserved-names-threat-note.md</c>, entry for entry;
/// each side asserts that table literally in its tests, which is the only control that keeps them
/// equal.
/// </para>
/// <para>
/// <b>Why this stays in Accounts</b> (rules/csharp.md "Security/, Utilities/ and a module's
/// Common/"). Its correctness is defined by the host's contract for account names, and only this
/// module decides what an account is called, so the first question answers "yes". It holds no key and
/// no dial an operator turns (not <c>Security/</c>), and it is asked in one place (not
/// <c>Utilities/</c>); a second asker would move it.
/// </para>
/// <para>
/// <b>Matching.</b> The whole name, exactly and case-sensitively, after the name has passed the
/// account alphabet <c>[a-z][a-z0-9_]{2,29}</c>; a hyphenated system name such as <c>www-data</c> is
/// excluded by spelling, not by this list. Plus one prefix rule: every name starting with
/// <see cref="PanelPrefix"/>. The list is best effort for daemon users that do not exist yet and
/// makes no claim of completeness; for a user that already exists the agent's uid floor and home
/// check are the control.
/// </para>
/// </remarks>
public static class ReservedAccountNamePolicy
{
    /// <summary>The prefix of every name reserved for the panel's own services (<c>maran_api</c>, ...).</summary>
    public const string PanelPrefix = "maran_";

    /// <summary>
    /// The exact reserved names, in the six classes of the threat note: base system identities,
    /// daemon identities, the panel's own identity, groups and roles other software trusts, role
    /// mailboxes, and default cloud-image logins.
    /// </summary>
    public static readonly IReadOnlySet<string> ExactNames = new[]
    {
        // A. Superuser and base system identities.
        "root", "daemon", "bin", "sys", "sync", "games", "man", "mail", "news", "uucp", "proxy", "backup",
        "list", "irc", "gnats", "nobody", "nogroup", "adm", "halt", "shutdown", "operator", "gopher", "ftp",
        "www", "nfsnobody",

        // B. Daemon identities of software a hosting server installs or sits beside.
        "nginx", "apache", "httpd", "sshd", "dbus", "polkitd", "rpc", "rpcuser", "chrony", "ntp", "tss",
        "tcpdump", "avahi", "colord", "sssd", "unbound", "named", "bind", "mysql", "mariadb", "postgres",
        "redis", "memcached", "mongodb", "mongod", "rabbitmq", "elasticsearch", "postfix", "dovecot",
        "dovenull", "exim", "opendkim", "clamav", "amavis", "spamd", "vsftpd", "saslauth", "mailnull",
        "smmsp", "squid", "haproxy", "varnish", "tomcat", "git",

        // C. The panel's own identity (the prefix rule covers the rest).
        "maran",

        // D. Groups and role names other software trusts.
        "sudo", "wheel", "admin", "staff", "users", "shadow", "disk", "tty", "utmp", "dialout", "cdrom",
        "docker", "lxd", "kvm", "libvirt",

        // E. Role mailbox names.
        "administrator", "hostmaster", "postmaster", "webmaster", "abuse", "security", "noc",

        // F. Default login users of cloud images.
        "ubuntu", "debian", "almalinux", "rocky", "centos", "fedora", "ec2_user", "cloud_user", "vagrant",
        "azureuser", "opc",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="name"/> may not be given to a hosting account.</summary>
    /// <param name="name">A name that has already passed the account alphabet.</param>
    /// <returns><c>true</c> when the name is on the list or starts with <see cref="PanelPrefix"/>.</returns>
    public static bool IsReserved(string name)
    {
        return ExactNames.Contains(name) || name.StartsWith(PanelPrefix, StringComparison.Ordinal);
    }
}
