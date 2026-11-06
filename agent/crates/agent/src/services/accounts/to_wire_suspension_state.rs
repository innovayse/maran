//! The one mapping from an account's observed suspension state onto the wire payload.

use maran_ops::accounts::AccountSuspensionState;

use crate::proto::{GetAccountSuspensionStateOk, SftpLoginSuspensionFact};
use crate::services::accounts::to_login_password_state::to_login_password_state;
use crate::services::accounts::to_transfer_protocol::to_transfer_protocol;
use crate::services::accounts::to_wire_site_listing::to_wire_site_listing;

/// Converts what the host was observed to be doing for an account into the
/// `GetAccountSuspensionStateOk` the contract carries.
#[must_use]
pub fn to_wire_suspension_state(state: AccountSuspensionState) -> GetAccountSuspensionStateOk {
    let (sites_directory_readable, sites) = to_wire_site_listing(state.sites);
    GetAccountSuspensionStateOk {
        login_locked: state.login_locked,
        login_password_state: to_login_password_state(state.login_password) as i32,
        sites_directory_readable,
        sites,
        cron_entries_total: state.cron.entries_total,
        cron_entries_suspended: state.cron.entries_suspended,
        cron_foreign_lines: state.cron.foreign_lines,
        // Every login the account holds, of BOTH protocols, in the one
        // repeated field the contract has today. Reporting only the SFTP half
        // would hide from the panel exactly the credential `ops::logins`
        // exists to make visible; the protocol beside each name, and the count
        // of entries this agent does not manage, reach the wire with the field
        // that carries them.
        sftp_logins: state
            .logins
            .logins
            .into_iter()
            .map(|login| SftpLoginSuspensionFact {
                username: login.name,
                locked: login.locked,
                protocol: to_transfer_protocol(login.protocol) as i32,
            })
            .collect(),
        // The number the enumeration deliberately cannot speak for: passwd
        // entries on this account's uid whose home is NEITHER jail. The agent
        // locks none of them — they are not its entries — so the attestation
        // REPORTS the count instead of refusing on it, which is the shape
        // `cron_foreign_lines` set for the same question about a crontab. A
        // suspension that said nothing about them would be claiming a silence
        // it never achieved.
        unmanaged_logins: state.logins.unmanaged,
    }
}
