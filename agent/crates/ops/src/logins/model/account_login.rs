//! One file-transfer login, and whether the host will still let it in.

use crate::logins::model::login_protocol::LoginProtocol;

/// What one `<account>_<name>` passwd entry can be observed to be right now.
///
/// Observed per login and never inferred from the account's own state. That
/// inference is precisely the defect this type exists to detect: a file
/// transfer login is a separate passwd entry sharing the account's uid, so
/// `usermod --lock <account>` leaves every one of them authenticating, and a
/// suspended customer kept a working WRITE credential into their home for as
/// long as nobody looked.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AccountLogin {
    /// The login's full system name, as the password database spells it.
    ///
    /// A `String` and not one of the two validated name types, because this
    /// type is what a caller reads about BOTH protocols: a field that could
    /// hold only one of them would force the enumeration to answer in two
    /// shapes. Nothing is constructed from this value — the name each login is
    /// ACTED on by is rebuilt through its own validated constructor at the
    /// point of the enumeration, so a name that could not be rebuilt is never
    /// reported here in the first place.
    pub name: String,

    /// Which daemon serves this login.
    pub protocol: LoginProtocol,

    /// True when `passwd -S` reports that entry's password as locked.
    ///
    /// The password database's own answer about that exact entry. It is not
    /// read out of `/etc/shadow`: one letter is what is wanted, and no hash
    /// needs to enter this process to get it.
    pub locked: bool,
}
