//! The adapter seam: behaviour that differs between distribution families.
//!
//! Operational code never branches on a distribution name; it asks the adapter
//! (rules/architecture.md). [`crate::adapter_for()`] is what chooses
//! the implementation.

use crate::account_programs::AccountPrograms;
use crate::backup_facts::BackupFacts;
use crate::database_facts::DatabaseFacts;
use crate::family::DistroFamily;
use crate::file_transfer_facts::FileTransferFacts;
use crate::firewall_facts::FirewallFacts;
use crate::package_facts::PackageFacts;
use crate::php_facts::PhpFacts;
use crate::scheduling_facts::SchedulingFacts;
use crate::service_manager_facts::ServiceManagerFacts;
use crate::web_server_facts::WebServerFacts;

/// Behaviour that differs between distribution families.
///
/// Deliberately narrow for now: package installation, service names and firewall
/// specifics are added additively by the plans that first need them, so that each
/// method arrives with a caller rather than as speculation.
///
/// Only facts that DIFFER between families belong here. A location the agent
/// owns itself and that is the same everywhere — its nginx include directory,
/// its certificate directory — is a constant on `maran_agent_core::agent_paths::AgentPaths`,
/// not a method every adapter repeats with the same literal.
///
/// # Which spelling a path answers, on a merged-`/usr` host
///
/// Both supported families merge `/usr`: `/bin` is a symlink to `usr/bin` and
/// `/sbin` to `usr/sbin`, so one file has two absolute names and a method here
/// has to pick one. The rule, and it is the ONLY one this crate uses:
///
/// > answer the name that is a documented interface, not the name that happens
/// > to resolve.
///
/// Concretely — `/bin/sh` because that is the shell path a crontab line and
/// every shebang name; `/sbin/nologin` because that is the path the RHEL family
/// documents; `/usr/sbin/nft` because that is where the `nftables` package
/// installs it on both families. Each is checked against its own authority, not
/// against the other two.
///
/// `command -v` is how an answer is CHECKED on a real host, never how it is
/// chosen. On a merged-`/usr` host it returns whichever spelling `PATH` reaches
/// first, which is a fact about `PATH` ordering rather than about where a file
/// belongs — it answers `/usr/bin/sh` on both polygon images, while the value
/// this crate returns for the same file is, correctly, `/bin/sh`.
pub trait DistroAdapter:
    Send
    + Sync
    + AccountPrograms
    + WebServerFacts
    + PhpFacts
    + PackageFacts
    + FileTransferFacts
    + BackupFacts
    + DatabaseFacts
    + ServiceManagerFacts
    + SchedulingFacts
    + FirewallFacts
{
    /// The family this adapter implements.
    fn family(&self) -> DistroFamily;
}
