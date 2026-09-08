//! The one environment every process this agent spawns is given.

use std::process::Command;

/// The locale variable every spawn here sets.
///
/// `LC_ALL` and not `LANG`, because `LC_ALL` overrides every other locale
/// variable — one assignment settles the question whatever the daemon's own
/// environment holds. It is public so a test can assert on the child's real
/// environment by name rather than restating the string.
pub const LOCALE_VARIABLE: &str = "LC_ALL";

/// The locale every spawn here runs under.
///
/// `C`, so the diagnostics a caller reads back are the ones its matching was
/// written against. This started as the accounts host's own pin, added because
/// `quota` links gettext and a translated header made its parse silently yield
/// "unlimited", and because `remove_crontab` decides "there was nothing to
/// remove" by reading `crontab`'s own message — a message in another language
/// is a refusal it cannot recognise, which made an account with no crontab
/// undeletable. Nothing sets a locale on the agent's unit, so before this pin
/// the daemon's environment decided it.
pub const LOCALE_VALUE: &str = "C";

/// The search-path variable every spawn here sets.
pub const PATH_VARIABLE: &str = "PATH";

/// The search path every spawn here runs under.
///
/// The agent itself never needs this: every program it starts is named by an
/// absolute path from the `DistroAdapter`, and that was verified call site by
/// call site rather than assumed. It is set for the programs those programs
/// start — `apt-get` and `dnf` run packaged helpers and maintainer scripts by
/// bare name, and a package manager handed no `PATH` at all is a PHP install
/// that fails in a way nothing here would explain. So the choice is not
/// "`PATH` or no `PATH`"; it is WHICH `PATH`, taken away from whoever last
/// edited the unit file.
///
/// Distro-owned directories only. `/usr/local/sbin` and `/usr/local/bin` are
/// deliberately ABSENT even though they head systemd's compiled-in default,
/// because they are the unmanaged directories this product itself installs
/// into and the ones a third-party package or an operator writes to — which is
/// precisely the door the `--gzip` finding walked through. Nothing the agent
/// spawns, or that those programs spawn, lives there.
pub const PATH_VALUE: &str = "/usr/sbin:/usr/bin:/sbin:/bin";

/// The COMPLETE environment of every process this agent spawns.
///
/// Two entries, and the emptiness around them is the point. Each is here
/// because something measured needs it; anything not named here is not merely
/// unset by omission, it is CLEARED — see [`apply_child_environment`].
///
/// It is public so that a test can assert the child's real environment against
/// this list rather than restating it, which is what makes "and nothing else"
/// a checkable property instead of a comment.
pub const CHILD_ENVIRONMENT: [(&str, &str); 2] =
    [(PATH_VARIABLE, PATH_VALUE), (LOCALE_VARIABLE, LOCALE_VALUE)];

/// Clears `command`'s inherited environment and sets [`CHILD_ENVIRONMENT`].
///
/// Every spawn in this workspace goes through this function — the shared
/// `spawn_argv`, and equally the handful of spawns that are deliberately
/// different because they need standard input or a bounded read.
///
/// # Why clearing, and what it is and is not worth
///
/// A child of this daemon inherits the daemon's environment, and the daemon's
/// environment comes from its unit and from `/etc/maran/agent.env`. Two
/// variables in it are executable instructions rather than settings:
///
/// - **`LD_PRELOAD`** is honoured by the dynamic loader of EVERY child, shell
///   or not, so it is not one area's problem: it puts attacker code inside
///   `tar`, `nft`, `useradd` and the database client alike.
/// - **`BASH_ENV`** names a file bash sources before running a non-interactive
///   script. On the RHEL family `/bin/sh` IS bash, and `tar`'s
///   `--use-compress-program` forks `/bin/sh -c` on the write side, so a
///   `BASH_ENV` in the daemon's environment runs as root on every backup.
///
/// Both preconditions require writing a root-owned file, so this is
/// **defence in depth, not a privilege boundary**: an attacker who can set
/// them could already run code as root. What it removes is *persistence* — a
/// quiet, durable foothold that fires on every scheduled operation — and it
/// removes it for every spawn at once rather than for the one that was noticed.
///
/// Clearing also settles a class nobody had enumerated: `TAR_OPTIONS`, which
/// GNU tar reads as extra command-line options; `POSIXLY_CORRECT`, which
/// changes several tools' parsing; and `MYSQL_HOST`/`MYSQL_TCP_PORT`/
/// `MYSQL_PWD`, which could point a customer's database dump at a server the
/// operator did not choose.
///
/// # What is deliberately not restored
///
/// - **`HOME`, `USER`, `LOGNAME`.** Nothing spawned here reads them. The
///   database client would consult `$HOME/.my.cnf` if `HOME` were set, and
///   this product deliberately does not authenticate that way — the connection
///   is the local socket, authenticated by the agent's uid — so leaving `HOME`
///   unset removes an option file from the path of a root client rather than
///   losing anything. The one behaviour this changes: an operator's private
///   `/root/.my.cnf` no longer participates in a dump.
/// - **`TZ`.** Unset means the host's `/etc/localtime`, which is the timestamp
///   an operator reading an archive expects; inheriting the daemon's `TZ` would
///   have made it whatever the unit happened to say.
/// - **Everything else**, by construction. A variable earns a line in
///   [`CHILD_ENVIRONMENT`] with a reason, or it does not reach a child.
pub fn apply_child_environment(command: &mut Command) -> &mut Command {
    command.env_clear().envs(CHILD_ENVIRONMENT)
}
