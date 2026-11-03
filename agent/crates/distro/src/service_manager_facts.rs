//! The service manager and what the host reports about itself through it: unit directory, run-now, clock, machine id and the closed set of monitored units.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The service manager and what the host reports about itself through it: unit directory, run-now, clock, machine id and the closed set of monitored units.
pub trait ServiceManagerFacts: Send + Sync {
    /// Absolute path of the service manager binary, for the process-execution
    /// allow-list.
    ///
    /// Every reload the agent performs runs this program. It is asked of the
    /// adapter rather than written where it is used for two reasons: a family
    /// that is not systemd would answer differently, and `ops` must name no
    /// absolute binary path at all — `scripts/lib/check-structure.sh` rejects
    /// one, and a bare `"systemctl"` written to get past that check would be
    /// a program resolved through `PATH` by a root process, which is worse
    /// than the literal it replaced.
    fn service_manager(&self) -> AdapterBinary;

    /// Absolute path of `systemd-run`, for the process-execution allow-list.
    ///
    /// It is how a customer's cron entry is started by hand (issue #158): the
    /// service manager runs the entry as the account, inside the account's
    /// `user-<uid>.slice`, under a runtime ceiling it enforces itself.
    fn systemd_run_binary(&self) -> AdapterBinary;

    /// Absolute path of `timedatectl`, for the process-execution allow-list.
    ///
    /// Only `timedatectl show` is ever run against it, to read whether the
    /// kernel clock is synchronised (issue #150). Nothing in this crate sets
    /// the time, the time zone or the synchronisation service.
    fn timedatectl_binary(&self) -> AdapterBinary;

    /// Directory a unit file must be written to for the service manager to
    /// read it.
    ///
    /// The agent installs one bind-mount unit per account with an SFTP user, so
    /// the mount survives a reboot instead of vanishing with the process that
    /// made it. The path is a fact of the service manager rather than a
    /// location the agent owns, which is why it is asked here and is not an
    /// `AgentPaths` constant.
    fn systemd_unit_directory(&self) -> &'static str;

    /// Where this family keeps the host's `machine-id`, the value a licence
    /// fingerprint is computed from.
    ///
    /// An adapter fact rather than a literal in `ops` for the reason this whole
    /// trait exists: today both families are systemd and name the same file, so
    /// the two arms are identical — and an identical pair that is NAMED here is
    /// what makes the day they diverge a one-line change instead of a hunt
    /// through `ops`. `/var/lib/dbus/machine-id` is not an alternative on either:
    /// it is a symlink to this path, verified by reading it, so a reader must not
    /// treat the two as separate sources that could disagree.
    fn machine_id_path(&self) -> &'static str;

    /// The closed set of units whose state the panel reports, in this fixed
    /// order: web server, database, cron, OpenSSH.
    ///
    /// Closed, and a fixed-size array rather than a slice, on purpose. Status
    /// reporting never accepts a unit name from a caller, so no rpc can ask
    /// about an arbitrary unit; and growing the set is then a type change every
    /// family must answer rather than a line one family can be left out of.
    ///
    /// Each element is the corresponding accessor's own answer — see
    /// [`WebServerFacts::nginx_service`](crate::web_server_facts::WebServerFacts::nginx_service), [`DatabaseFacts::mysql_service`](crate::database_facts::DatabaseFacts::mysql_service), [`SchedulingFacts::cron_service`](crate::scheduling_facts::SchedulingFacts::cron_service)
    /// and [`FileTransferFacts::ssh_service`](crate::file_transfer_facts::FileTransferFacts::ssh_service) — so a unit cannot be named correctly in one
    /// place and staler here.
    ///
    /// Two absences are deliberate. php-fpm is not in the set because its unit
    /// name carries a PHP version ([`PhpFacts::php_fpm_service`](crate::php_facts::PhpFacts::php_fpm_service)) and there is no
    /// one unit to name; the firewall is not, because a unit that loads a
    /// ruleset and then `RemainAfterExit`s is not answering the question this
    /// set asks. The firewall unit is not named by this crate at all: the agent
    /// applies rules with [`FirewallFacts::nft_binary`](crate::firewall_facts::FirewallFacts::nft_binary) and never through systemd, and
    /// the one place a unit name and a boot-time include target ARE needed is
    /// the installer's `installer/lib/87-firewall.sh`, which is shell and reads
    /// nothing from here.
    ///
    /// # `is-active` on the SSH unit is NOT the question "is SSH up"
    ///
    /// Read this before turning this set into statuses. On the Debian family
    /// the enabled unit is `ssh.socket`, not `ssh.service`: on the Ubuntu 24.04
    /// polygon `ssh.socket` is the only entry in `sockets.target.wants/`,
    /// `ssh.service` is absent from `multi-user.target.wants/` entirely, and the
    /// socket declares `Accept=no` with `RequiredBy=ssh.service`. So after a
    /// boot on which nobody has connected yet, `systemctl is-active ssh` —
    /// which resolves to `ssh.service` — reads **inactive** on a host whose SSH
    /// is listening and completely healthy.
    ///
    /// The mechanism decides what the fix has to be, so it is stated exactly.
    /// `Accept=no` means the socket hands its LISTENING file descriptor to one
    /// `ssh.service`, which runs `sshd -D` and stays running; the family ships
    /// no `ssh@.service` template and neither unit sets `StopWhenUnneeded`
    /// (all four measured on the image). The service is therefore inactive from
    /// boot until the FIRST connection triggers it, and active from then on —
    /// the window CLOSES and does not reopen. It is not a per-connection unit
    /// that comes and goes between logins: that shape is `Accept=yes` plus a
    /// per-connection `sshd@.service` running `sshd -i`, which is what the RHEL
    /// family's `sshd.socket` is — and that socket is the one alma9 does NOT
    /// enable.
    ///
    /// A monitor that maps this inactive state to "stopped" therefore invents
    /// an SSH outage on every freshly booted Debian-family host until someone
    /// happens to log in. Because the window is bounded that way, treating a
    /// socket-activated unit's inactive state as "not yet started" — asking the
    /// SOCKET whether it is listening — settles it; polling per connection
    /// would be answering a question this shape never asks. The hazard is also
    /// one-sided: the RHEL family enables `sshd.service` in
    /// `multi-user.target.wants/` and does not enable its `sshd.socket` at all,
    /// so the naive read is right there and wrong here.
    ///
    /// Four `&'static str`s cannot carry "ask this one's socket too", so the
    /// warning is prose until the element type stops being a bare unit name.
    fn managed_units(&self) -> [&'static str; 4];
}
