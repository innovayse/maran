//! The one spelling of an account's systemd slice.

/// The name of the slice systemd places a user's sessions under.
///
/// `user-<uid>.slice` is systemd-logind's own fixed naming on every family this
/// agent supports; it is built from the numeric uid the password database
/// answered, never from the account name, so nothing a caller spelled reaches
/// the unit name.
///
/// Two areas name it and must name the same unit: `ops::accounts` sets the
/// slice's CPU, memory and task ceilings (issue #133), and `ops::cron` starts a
/// run-now job inside it so those ceilings apply (issue #158). A second
/// spelling would let the ceilings land on one unit and the job on another.
#[must_use]
pub fn account_slice_unit(uid: u32) -> String {
    format!("user-{uid}.slice")
}

#[cfg(test)]
#[path = "../tests/utils/account_slice_unit_tests.rs"]
mod tests;
