//! Every file-transfer login one account holds, and what was left uncovered.

use crate::logins::model::account_login::AccountLogin;

/// The answer to "which credentials into this account's home exist right now".
///
/// One value for both protocols, because a caller deciding whether a suspension
/// took hold must not have to remember to ask twice — a second question that
/// can be forgotten is the shape of the defect this module was made to close.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountLoginSet {
    /// One entry per login this panel created for the account, in name order.
    ///
    /// Empty means the account holds none, which is a suspended state and not a
    /// failure to look: a password database that cannot be read is an error and
    /// never an empty list.
    pub logins: Vec<AccountLogin>,

    /// How many passwd entries share the account's uid without being one of
    /// its jailed logins.
    ///
    /// Carried so that a suspension can say what it did not turn. Those entries
    /// were not created by this panel — an operator's own `useradd
    /// --non-unique`, or a login of a neighbouring account's jail sharing the
    /// uid — and this agent locks none of them: they are not its entries, and
    /// locking a credential nobody asked it to touch would take away access
    /// somebody deliberately arranged. Reporting the number is the honest
    /// answer; acting on them is not this agent's to do.
    ///
    /// It is a COUNT and not a list on purpose: the panel's question is whether
    /// the attestation covered everything, and a name here would be a name the
    /// panel had no business learning from a credential it does not own.
    pub unmanaged: u32,
}
