//! What one search walk is looking for, and as whom.

use crate::files::model::search_input::SearchInput;

/// The request a walk answers, the uid it runs as and the device it must stay on.
///
/// `uid` and `device` are not part of the request: the first is the account the
/// forked child runs as and the second is read off the start directory once the
/// walk has opened it. They travel with the request because every directory the
/// walk reads is judged against all three, and passing them as one value keeps
/// the device of one search from being checked against another's uid.
#[derive(Debug, Clone, Copy)]
pub(crate) struct SearchScope<'a> {
    /// The request: start directory, query and bounds.
    pub input: &'a SearchInput,

    /// The account's uid; only directories it owns are entered.
    pub uid: u32,

    /// The device of the start directory; a directory on another device is
    /// reported but not entered (`find -xdev`).
    pub device: u64,
}
