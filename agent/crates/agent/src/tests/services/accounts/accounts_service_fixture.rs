//! The service under test, built the way `server.rs` builds it, over the
//! blocking host and the unreachable cascade hosts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::{Arc, Mutex};
use std::time::Instant;

use maran_distro::{DistroFamily, adapter_for};
use maran_ops::accounts::AccountOperations;

use crate::services::accounts::accounts_service::AccountsServiceImpl;

#[path = "accounts_service_blocking_host.rs"]
pub(crate) mod blocking_host;
use blocking_host::{BlockingSystemHost, FAST_ACCOUNT, SLOW_ACCOUNT};
#[path = "accounts_service_unused_hosts.rs"]
pub(crate) mod unused_hosts;
use unused_hosts::{
    UnusedCronHost, UnusedDbHost, UnusedFtpsHost, UnusedLoginsHost, UnusedPhpHost, UnusedSftpHost,
    UnusedSiteHost,
};

/// The service under test, and the cell its blocking host records into.
pub(crate) type ServiceUnderTest = AccountsServiceImpl<
    BlockingSystemHost,
    UnusedPhpHost,
    UnusedDbHost,
    UnusedSftpHost,
    UnusedFtpsHost,
    UnusedSiteHost,
    UnusedCronHost,
    UnusedLoginsHost,
>;

/// The service under test with the two cells its host records into: when the
/// blocking call began, and how many state-changing calls reached the host.
pub(crate) type ServiceAndCells = (
    Arc<ServiceUnderTest>,
    Arc<Mutex<Option<Instant>>>,
    Arc<Mutex<usize>>,
);

/// The password database of the two hosting accounts this file drives: ids
/// above any floor, each home exactly under `/home`.
pub(crate) fn standard_passwd() -> String {
    format!(
        "{SLOW_ACCOUNT}:x:1001:1001::/home/{SLOW_ACCOUNT}:/usr/sbin/nologin\n\
         {FAST_ACCOUNT}:x:1002:1002::/home/{FAST_ACCOUNT}:/usr/sbin/nologin\n"
    )
}

/// Builds the service the way `server.rs` builds it, over a host answering
/// `passwd`, and returns the cells the host records into.
pub(crate) fn service_over(passwd: String) -> ServiceAndCells {
    let blocking_began = Arc::new(Mutex::new(None));
    let effects = Arc::new(Mutex::new(0));
    let host = BlockingSystemHost {
        blocking_began: Arc::clone(&blocking_began),
        passwd,
        effects: Arc::clone(&effects),
    };
    let distro = adapter_for(DistroFamily::Debian);

    (
        Arc::new(AccountsServiceImpl::new(
            AccountOperations::new(
                host,
                distro,
                maran_agent_core::utils::free_space_floor::FreeSpaceFloor::default_floor(),
            ),
            UnusedPhpHost,
            UnusedDbHost,
            UnusedSftpHost,
            UnusedFtpsHost,
            UnusedSiteHost,
            UnusedCronHost,
            UnusedLoginsHost,
        )),
        blocking_began,
        effects,
    )
}

/// Builds the service over the two ordinary hosting accounts.
pub(crate) fn service() -> (Arc<ServiceUnderTest>, Arc<Mutex<Option<Instant>>>) {
    let (service, blocking_began, _) = service_over(standard_passwd());

    (service, blocking_began)
}
