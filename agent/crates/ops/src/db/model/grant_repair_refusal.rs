//! Why one row of the server's grant table was left exactly as it was found.

use std::fmt::{Display, Formatter, Result as FmtResult};

/// The reason a grant-table row was refused rather than repaired.
///
/// **A refusal is the answer for anything the repair cannot classify with
/// certainty**, and every variant here means the same thing about the host: the
/// row was not touched. The pass rewrites a customer's live database access, so
/// a row it does not recognise as one this panel itself issued is reported to an
/// operator instead of being guessed at — the alternative is an operation that
/// silently rewrites a hand-made grant an administrator depends on.
///
/// Every variant is reported per row, with the row's own host, database and
/// user, so "left alone" is something an operator reads rather than assumes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum GrantRepairRefusal {
    /// The row grants on a host other than the one this panel grants from.
    ///
    /// Every grant `create_database` issues is `@'localhost'` and nothing else,
    /// so a row at another host — `%`, a hostname, a network — was made by
    /// somebody else, whatever its database name looks like. It is reported
    /// rather than ignored because such a row carries the same unescaped
    /// wildcard and is therefore the same class of exposure, reachable from
    /// further away; repairing it is an operator's decision, because only they
    /// know what depends on it.
    HostIsNotLocalhost,

    /// The database or user name is not one this agent could have created.
    ///
    /// Both halves must decode — at the LAST separator, against the whole
    /// account name and never a prefix of it — and both must decode to the SAME
    /// account, because `create_database` only ever pairs an account's own
    /// database with that account's own user. A row pairing a stranger's user
    /// with a panel-shaped database fails here, which is the shape a previous
    /// defect on this branch had: a classifier that mistook a stranger's row for
    /// the account's own.
    NotThePanelsNaming,

    /// The row's privileges are not the ones `create_database` grants.
    ///
    /// The repair re-issues `GRANT ALL PRIVILEGES`, so it may only ever run
    /// against a row that already holds exactly that. A narrower grant an
    /// operator made by hand on a panel-shaped name would be **escalated** by a
    /// blind re-grant, which is a worse outcome than the wildcard it came to
    /// fix.
    UnrecognisedPrivileges,

    /// The database name holds a backslash in an arrangement this panel never
    /// writes.
    ///
    /// A repaired row holds `\_` for every separator and nothing else, so a name
    /// that is escaped in part, escaped twice, or escaping some other character
    /// came from somewhere else — or from a repair nobody here performed. It is
    /// refused rather than re-escaped, because re-escaping a name whose stored
    /// form is not understood is how a working grant is destroyed.
    PartiallyOrUnfamiliarlyEscaped,
}

impl Display for GrantRepairRefusal {
    /// Writes the operator-facing English the report and the log line carry.
    fn fmt(&self, formatter: &mut Formatter<'_>) -> FmtResult {
        let reason = match self {
            Self::HostIsNotLocalhost => {
                "the grant is not for 'localhost', so this panel did not issue it"
            }
            Self::NotThePanelsNaming => {
                "the database and user names are not a pair this panel could have created"
            }
            Self::UnrecognisedPrivileges => {
                "the row does not hold exactly the ALL PRIVILEGES grant this panel issues"
            }
            Self::PartiallyOrUnfamiliarlyEscaped => {
                "the stored database pattern is escaped in a form this panel never writes"
            }
        };

        formatter.write_str(reason)
    }
}
