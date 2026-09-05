//! The numeric identity an SFTP login is given, so its files are the account's.

/// The user and group ids the password database holds for one hosting account.
///
/// It exists because an SFTP login has to BE the account, numerically, and
/// `useradd` will only be told that as two numbers.
///
/// # Why a login shares its account's ids
///
/// An account's home is `<account>:<web server group> 0750` — owner `rwx`,
/// group `r-x`, and nothing for anybody else. That mode is not negotiable: it
/// is what lets nginx and php-fpm read a site while keeping every other tenant
/// out, and the SFTP jail exists precisely so the home never has to change.
///
/// A login with an identity of its own is therefore in the third category —
/// "anybody else" — and can neither read nor write a single file of the account
/// it was created for. The only two ways out are to widen the home, which
/// breaks the isolation the whole layout rests on, or to give the login the
/// account's own uid and gid, which is what this type carries. The second is
/// also the one that behaves correctly afterwards: a file uploaded over SFTP
/// comes out owned by the account, exactly as one the account created itself,
/// so nothing later has to repair ownership.
///
/// Adding the login to the web server's group instead would be the worst of the
/// three. That group can traverse EVERY account's home by design, so it would
/// hand one customer's file-transfer credential read access to every other
/// customer on the host.
///
/// A named pair rather than two `u32` parameters, because the two are
/// interchangeable at a call site and swapping them produces a login that is
/// silently wrong rather than a compile error.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountOwnership {
    /// The account's user id, which the login is created with.
    pub uid: u32,
    /// The account's primary group id, which the login is created with.
    pub gid: u32,
}
