//! What this host can be observed to be doing for an account right now.

use crate::accounts::StoredPassword;
use crate::cron::AccountCronSuspension;
use crate::logins::AccountLoginSet;
use crate::sites::SiteSuspensionFact;

/// The evidence a caller needs before it may report an account as suspended.
///
/// Every field is an OBSERVATION taken from the machine at the moment it was
/// asked for. Nothing here is stored, and nothing here is the panel's belief
/// read back: suspension's residue is on the host — a vhost still serving, a
/// login still unlocked — so a check that consulted the panel's own rows would
/// be green over a serving site.
///
/// What it does NOT observe is as much a part of the contract as what it does,
/// and is stated here rather than left to be discovered from an absent field:
///
/// - **Databases.** An account's databases keep accepting connections while it
///   is suspended. That is an open product decision, not an oversight.
/// - **Foreign cron lines.** Lines the panel did not write are not suppressed
///   and this agent will not rewrite them, so they keep firing. They are
///   COUNTED — [`AccountCronSuspension::foreign_lines`] — rather than silently
///   left out, because a suspension that said nothing about them would be
///   claiming a silence it did not achieve.
/// - **Logins this panel did not create.** A passwd entry sharing the account's
///   uid whose home is neither of the account's two jails is not locked, for
///   the same reason a foreign cron line is not deleted, and it is counted the
///   same way — [`AccountLoginSet::unmanaged`].
/// - **The panel's own web login.** Nothing here observes it; it is not on
///   this host.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountSuspensionState {
    /// True when the account's own passwd entry is locked, as `passwd -S`
    /// reports it.
    ///
    /// The account's own login and no other; its file-transfer logins are
    /// separate passwd entries, reported separately in [`Self::logins`].
    pub login_locked: bool,

    /// What the account's own shadow password field actually holds.
    ///
    /// Carried BESIDE [`Self::login_locked`] rather than instead of it,
    /// because the two answer different questions and only one boolean was
    /// ever available to answer both. `passwd -S` — which produces
    /// `login_locked` — reports a login locked over a password and a login
    /// that never had one as the same thing, and every account this agent
    /// creates is the second kind. A caller deciding whether a SUSPENSION took
    /// hold wants "can a password authenticate this", which either field
    /// answers; a caller deciding whether a REACTIVATION completed wants "is
    /// anything of the customer's still held down", which only this one can.
    ///
    /// Holds no hash and no field bytes: [`StoredPassword`] classifies at the
    /// point of reading and drops them.
    pub login_password: StoredPassword,

    /// True when the vhost directory could actually be listed.
    ///
    /// Carried because [`Self::sites`] going empty is the one way this answer
    /// can silently stop observing anything: an unreadable directory and an
    /// account with no sites produce the same empty list, and the empty list
    /// is the one that reads as "everything is suspended".
    pub sites_directory_readable: bool,

    /// One entry per vhost this host serves for the account, whether or not
    /// the panel remembers creating it.
    pub sites: Vec<SiteSuspensionFact>,

    /// What the account's crontab holds and how much of it is suppressed.
    ///
    /// Counted out of the crontab itself. The Cron module keeps no rows at all,
    /// so a database-side check would answer green over a firing crontab —
    /// which is this whole type's defect wearing different clothes.
    pub cron: AccountCronSuspension,

    /// Every file-transfer login this host holds for the account — SFTP and
    /// FTPS alike — and how many entries on the account's uid were not among
    /// them.
    ///
    /// Separate from [`Self::login_locked`], and that separation is the point:
    /// these are their own passwd entries sharing the account's uid, so the
    /// `usermod --lock` that produces `login_locked` reaches none of them.
    /// Until they were observed here, a suspended customer kept a working
    /// write credential into their home.
    ///
    /// **Both protocols in one field, deliberately.** A caller that had to ask
    /// twice is a caller that can ask once and believe the answer, which is the
    /// same defect one protocol later.
    ///
    /// An empty [`AccountLoginSet::logins`] means the account holds no
    /// file-transfer login, which is a suspended state. A password database
    /// that could not be enumerated is an error, never an empty list.
    pub logins: AccountLoginSet,
}
