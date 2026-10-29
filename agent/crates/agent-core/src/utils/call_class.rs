//! How long each kind of program this agent starts is allowed to run.

use std::time::Duration;

/// The kind of call a program is started for, which decides its deadline.
///
/// **A deadline means "wedged", never "slow".** Every value below sits well
/// above the slowest legitimate run of the programs in its class, because a
/// kill that fires on a slow but healthy run turns an availability problem into
/// a half-applied host: a rolled-back valid write, a half-loaded database, a
/// half-removed home. The numbers are argued from the programs' documented
/// behaviour and from limits this tree states elsewhere; they are not measured
/// under load, and a class that proves too tight is raised here, in one place.
///
/// One global number is not used, because the classes differ by three orders of
/// magnitude: a state query is milliseconds and a database dump is hours. A
/// single value low enough to bound a query would kill every backup, and one
/// high enough for a backup would leave a wedged validator holding the
/// host-wide configuration lock for half a day.
///
/// The caller picks the class at each call site; this type only maps it to a
/// duration.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CallClass {
    /// A read-only question put to the host: `getent`, `id`, `quota`,
    /// `passwd -S`, `systemctl is-active` and `show`, `nft list`, an `openssl`
    /// read of a certificate or key, `crontab -l`.
    Query,
    /// A pure validator run under the configuration-tree lock: `nginx -t`,
    /// `php-fpm -t`. Kept short on purpose, because every second of it is a
    /// second every other tenant's write waits.
    Validate,
    /// Every `systemctl` that starts, stops, restarts, reloads or runs
    /// `daemon-reload`, including the validators that mutate (the FTPS and jail
    /// mount units). A restart may legitimately take the service manager's stop
    /// timeout plus its start timeout, 90 s + 90 s by default, and the service
    /// manager's own verdict must arrive before ours.
    Reload,
    /// A short state-changing program: `useradd`, `usermod`, `passwd -l`/`-u`,
    /// `chpasswd`, `setquota`, `pkill`, a single-path `chmod` or `chgrp`, the
    /// crontab install, `nft -f`, `openssl` key generation.
    Mutation,
    /// One database-client statement.
    Database,
    /// The blocking `systemctl start maran-php-install@<package>.service`.
    /// That unit is `Type=oneshot` with `TimeoutStartSec=1800`, so `systemctl`
    /// returns only when the unit leaves "activating", which can be 1800 s plus
    /// the unit's stop timeout (90 s by default) later. This deadline is above
    /// that sum, never equal to it, or the agent would kill its own client in
    /// the instant the service manager reports. The two numbers move together:
    /// a change to the unit's `TimeoutStartSec` is a change here.
    PackageInstall,
    /// A read-only `tar --list` of ONE backup artifact, to pre-scan its members
    /// before a restore extracts anything. The work scales with the artifact
    /// ceiling (at most 64 GiB compressed) and never with the customer's home,
    /// and it mutates nothing, so a gzip stream that expands enormously costs
    /// one hour of one core and not twelve. Raised here, in one place, if a
    /// real artifact proves it tight.
    Listing,
    /// Work whose length scales with the customer's data: `mysqldump`, `tar`
    /// create and extract, `load_dump`, `userdel --remove` (it removes a
    /// whole home), and any recursive `chgrp` or `chmod` over a home. Twelve
    /// hours is not a budget for a large backup; it is the point past which the
    /// process is not working at all.
    ///
    /// One exception, stated here so this table is not read as a promise: the
    /// ACCOUNT-SIDE extraction of a restore does not get twelve hours. It runs
    /// under `ACCOUNT_ARCHIVER_DEADLINE` (100 s), because `fork_as_account`
    /// kills its child at 120 s and the inner kill has to fire first. A home
    /// that takes longer than that to extract is not restored, and the failure
    /// reads as an archiver refusal.
    Bulk,
}

impl CallClass {
    /// The longest a program of this class may run before its whole process
    /// group is killed.
    ///
    /// | class | deadline | why this and not less |
    /// |---|---|---|
    /// | `Query` | 60 s | a `getent` on a stalled directory service is tens of seconds |
    /// | `Validate` | 60 s | `nginx -t` resolves upstream names at parse time |
    /// | `Reload` | 240 s | 90 s stop + 90 s start of the service manager, plus slack |
    /// | `Mutation` | 120 s | the slowest of these is a quota or key operation |
    /// | `Database` | 300 s | a statement waiting on a metadata lock |
    /// | `PackageInstall` | 2100 s | the install unit's 1800 s + its 90 s stop + slack |
    /// | `Listing` | 3 600 s | `tar --list` of ONE artifact (at most 64 GiB compressed): about 18 MB/s sustained; scales with the artifact ceiling, not the home, and mutates nothing |
    /// | `Bulk` | 43 200 s | a dump or archive of a large account |
    #[must_use]
    pub const fn deadline(self) -> Duration {
        let seconds = match self {
            Self::Query | Self::Validate => 60,
            Self::Reload => 240,
            Self::Mutation => 120,
            Self::Database => 300,
            Self::PackageInstall => 2_100,
            Self::Listing => 3_600,
            Self::Bulk => 43_200,
        };

        Duration::from_secs(seconds)
    }
}

#[cfg(test)]
#[path = "../tests/utils/call_class_tests.rs"]
mod tests;
