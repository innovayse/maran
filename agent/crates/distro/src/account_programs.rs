//! The account-management programs, shells and spool directories: what creating, locking, signalling, quota-ing and deleting a system account runs and reads.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The account-management programs, shells and spool directories: what creating, locking, signalling, quota-ing and deleting a system account runs and reads.
pub trait AccountPrograms: Send + Sync {
    /// Absolute path of the shell given to an account that must not log in.
    ///
    /// A hosting account is not a person with a terminal: SFTP and cron work through
    /// it, and an interactive login is exactly what must not. The path differs between
    /// families — Debian ships it at `/usr/sbin/nologin`, RHEL documents `/sbin/nologin`
    /// — which is why it is asked of the adapter rather than written into an operation
    /// (rules/rust.md "Distro adapter": ops never hard-codes a platform path).
    fn nologin_shell(&self) -> &'static str;

    /// Absolute path of `useradd`, for the process-execution allow-list.
    ///
    /// The SFTP area creates a system login with it. Asked of the adapter for
    /// the reason [`ServiceManagerFacts::service_manager`](crate::service_manager_facts::ServiceManagerFacts::service_manager) is: `ops` names no absolute binary
    /// path of its own, and a bare `"useradd"` written to satisfy that check
    /// would be a program a root daemon resolves through `PATH`, which is worse
    /// than the literal it replaced.
    fn useradd_binary(&self) -> AdapterBinary;

    /// Absolute path of `userdel`, for the process-execution allow-list.
    fn userdel_binary(&self) -> AdapterBinary;

    /// Absolute path of `usermod`, for the process-execution allow-list.
    ///
    /// Suspending and unsuspending an account lock and unlock its password with
    /// this program.
    fn usermod_binary(&self) -> AdapterBinary;

    /// Absolute path of `pkill`, for the process-execution allow-list.
    ///
    /// The login area signals every process running as a suspended account's
    /// uid with it (`pkill --signal KILL --count --uid <uid>`), so that a
    /// suspension ends the transfer sessions the account already has open
    /// rather than only refusing new logins. It is asked of the adapter for the
    /// reason [`Self::useradd_binary`] is, and with one extra edge here: an
    /// unattributed hunk naming this exact path as a literal in `ops` has
    /// already appeared in this tree, and `maran structure` rejects it.
    ///
    /// It comes from `procps` on the Debian family and `procps-ng` on the RHEL
    /// family — two package names for one tool, which is why the path is a
    /// per-family answer even though both families currently agree on it.
    /// `agent/crates/agent/tests/binary_paths_on_a_real_host.rs` stats what this
    /// returns on a host of each family, so a family that stops shipping it
    /// fails by name rather than at exec time on a customer's server.
    fn pkill_binary(&self) -> AdapterBinary;

    /// Absolute path of `pgrep`, for the process-execution allow-list.
    ///
    /// Account deletion asks it whether any LIVE process of the uid is left
    /// (`pgrep --count --runstates <non-zombie states> --uid <uid>`), after
    /// `pkill` has signalled them. `pkill --signal 0` cannot be that check: it
    /// matches zombies, which hold nothing and cannot be killed, so a host whose
    /// orphans are not reaped promptly would refuse every deletion.
    ///
    /// Same package as [`Self::pkill_binary`] on both families (`procps`,
    /// `procps-ng`), so the installer needs no new dependency.
    fn pgrep_binary(&self) -> AdapterBinary;

    /// Absolute path of `atq`, for the process-execution allow-list.
    ///
    /// Account deletion lists the pending `at` jobs with it, so it can remove
    /// the ones the account owns before the account's uid is released (issue
    /// #139). The `at` package is OPTIONAL on a hosting server: neither family
    /// installs it by default, so a host without it has no `at` jobs to leave
    /// behind, and the caller treats an absent binary as "nothing to remove".
    /// For that reason it is the one tool whose presence the real-host path
    /// check does not demand (`binary_paths_on_a_real_host.rs`, optional
    /// section): absent is allowed, present must be at this path.
    ///
    /// `/usr/bin/atq` on both families (Debian `at`, RHEL `at`); on Debian it
    /// is a link to `at`. Not verified on a live host of every release.
    fn atq_binary(&self) -> AdapterBinary;

    /// Absolute path of `atrm`, for the process-execution allow-list.
    ///
    /// Removes one `at` job by id, after [`Self::atq_binary`] named it as the
    /// account's. Same package, same optional presence and same path on both
    /// families as `atq`; see there.
    fn atrm_binary(&self) -> AdapterBinary;

    /// Absolute path of `loginctl`, for the process-execution allow-list.
    ///
    /// Account deletion disables lingering and terminates the user's systemd
    /// user manager with it (`loginctl disable-linger`, `loginctl
    /// terminate-user`), so no `user@<uid>.service` outlives the account. It
    /// ships with systemd itself, which this agent already requires, so unlike
    /// `atq` its presence is demanded on a real host.
    ///
    /// `/usr/bin/loginctl` on both families (Debian `systemd`, RHEL
    /// `systemd`).
    fn loginctl_binary(&self) -> AdapterBinary;

    /// Absolute path of `ipcrm`, for the process-execution allow-list.
    ///
    /// Removes a System V shared-memory segment, semaphore set or message queue
    /// by id, after the account's objects were found in `/proc/sysvipc/*`. It
    /// comes from `util-linux`, an essential package on Debian and a base
    /// package on RHEL, so its presence is demanded on a real host.
    ///
    /// `/usr/bin/ipcrm` on both families.
    fn ipcrm_binary(&self) -> AdapterBinary;

    /// Directory the `at` daemon keeps its job files in.
    ///
    /// A platform fact: Debian's `at` spools to `/var/spool/cron/atjobs`, the
    /// RHEL family's to `/var/spool/at`. Account deletion reads it to confirm
    /// that no job file owned by the departed uid remains after `atrm` ran. The
    /// directory exists only where the `at` package is installed.
    fn at_spool_directory(&self) -> &'static str;

    /// The real directory holding per-user mail spool files, one regular file
    /// per login named after it.
    ///
    /// A platform fact: Debian's is `/var/mail` (and `/var/spool/mail` is a
    /// symlink to it), the RHEL family's is `/var/spool/mail` (and `/var/mail`
    /// is the symlink). The REAL directory is named, so it can be opened
    /// without following a link. Account deletion removes the departed
    /// account's spool from it, because `userdel --remove` was measured to
    /// leave it behind on the RHEL family.
    fn mail_spool_directory(&self) -> &'static str;

    /// Absolute path of `passwd`, for the process-execution allow-list.
    ///
    /// Needed for `passwd -S <user>`, which REPORTS whether a password can
    /// authenticate a login. That is the question the suspension attestation
    /// asks, and one letter is the whole answer.
    ///
    /// It is NOT the instrument for the other password question — is there a
    /// hash behind the lock? — because it cannot answer it: a login that never
    /// had a password and one locked over a real one both read `L` here (`LK`
    /// on the RHEL family). `getent_binary` covers that question, one entry at
    /// a time, and neither call opens the shadow database at large
    /// (rules/security.md item 8).
    ///
    /// On the RHEL family this program comes from its OWN package (`passwd`),
    /// separate from the `shadow-utils` package that supplies `useradd`,
    /// `usermod`, `userdel` and `chpasswd`; on the Debian family a single
    /// `passwd` package supplies all five. It is the only tool this adapter
    /// declares with that shape, and a minimal RHEL host without that package
    /// fails this call at exec time — which is why `installer/` names it.
    fn passwd_binary(&self) -> AdapterBinary;

    /// Absolute path of `getent`, for the process-execution allow-list.
    ///
    /// Needed for `getent shadow <user>`, which reports ONE account's raw
    /// shadow password field. The agent asks it exactly one question that
    /// `passwd -S` cannot answer: is there a password hash behind the lock, or
    /// is the login simply passwordless? Both read `L`/`LK` through `passwd
    /// -S`, and `usermod --unlock` refuses the second one — exiting 0 on the
    /// Debian family and 1 on the RHEL family, which is why an account could
    /// never be reactivated there.
    ///
    /// One key, one line: this is not "open the shadow database". The field is
    /// classified where it is read and the bytes are dropped, so no hash
    /// reaches a return value, an error, a log line or the wire
    /// (rules/security.md item 8).
    fn getent_binary(&self) -> AdapterBinary;

    /// Absolute path of `setquota`, for the process-execution allow-list.
    fn setquota_binary(&self) -> AdapterBinary;

    /// Absolute path of `quota`, for the process-execution allow-list.
    ///
    /// Read-only: it reports what an account is using. A host without the quota
    /// tools installed does NOT degrade gracefully — reading usage propagates
    /// the spawn failure and the request fails. That is worth knowing before
    /// shipping to a host that has no `quota` package, and it is stated here
    /// rather than implied, because an earlier version of this comment claimed
    /// the opposite and no test contradicted it.
    fn quota_binary(&self) -> AdapterBinary;

    /// Absolute path of `quotaon`, for the process-execution allow-list.
    ///
    /// Read-only in the sense this crate uses it: only `quotaon -p` is ever
    /// run against it, to ask whether the kernel is tracking quota accounting
    /// on a filesystem right now. Nothing in this crate calls `quotaon`
    /// without `-p` — turning accounting ON is an operator's decision, never
    /// this agent's (rules/architecture.md; the reasoning is repeated at the
    /// call site rather than here, so it is read where the risk is).
    fn quotaon_binary(&self) -> AdapterBinary;

    /// Absolute path of `id`, for the process-execution allow-list.
    ///
    /// The accounts area asks it for a name's numeric uid, so the answer covers
    /// every name source the host is configured with rather than only the local
    /// password file.
    fn id_binary(&self) -> AdapterBinary;

    /// Absolute path of `chmod`, for the process-execution allow-list.
    fn chmod_binary(&self) -> AdapterBinary;

    /// Absolute path of `chgrp`, for the process-execution allow-list.
    fn chgrp_binary(&self) -> AdapterBinary;

    /// Absolute path of `chpasswd`, for the process-execution allow-list.
    ///
    /// How a password reaches it, and why that way, belongs to the operation
    /// that sets one: `ops::sftp`'s `set_sftp_password` carries the reasoning
    /// beside the code that must keep it true.
    fn chpasswd_binary(&self) -> AdapterBinary;

    /// Absolute path of the host's local password database.
    ///
    /// The file the agent reads to find out which SFTP logins an account
    /// actually has, which is what an account deletion has to remove: the
    /// panel's rows say what it remembers creating, not what is on the machine.
    ///
    /// A platform fact rather than a location the agent owns, so it is asked
    /// here and is not an `AgentPaths` constant — the same reasoning as
    /// [`ServiceManagerFacts::systemd_unit_directory`](crate::service_manager_facts::ServiceManagerFacts::systemd_unit_directory), and the two families agree on
    /// this answer for the same reason: both are POSIX systems using the shadow
    /// suite.
    fn passwd_database(&self) -> &'static str;
}
