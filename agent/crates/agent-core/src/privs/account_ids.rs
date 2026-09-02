//! Resolving a hosting account's numeric ids from the system's user database.

use std::ffi::CString;
use std::mem::MaybeUninit;

use super::priv_error::PrivError;
use crate::validation::system::name::AccountName;

/// First buffer size handed to `getpwnam_r` when `sysconf` has no opinion.
const INITIAL_BUFFER: usize = 1024;

/// Ceiling on the buffer growth loop. A `passwd` entry that does not fit in 64 KiB
/// is not a large entry, it is a broken or hostile user database, and growing
/// without a bound would let one turn a lookup into an allocation attack.
const MAXIMUM_BUFFER: usize = 64 * 1024;

/// Where `useradd` records the lowest id it will hand to a human account.
const LOGIN_DEFS: &str = "/etc/login.defs";

/// The id floor to assume when `/etc/login.defs` cannot be read, does not say, or
/// says something unusable. Applies to `UID_MIN` and `GID_MIN` alike.
///
/// 1000 is the value shipped by every distribution in Maran's support matrix, and
/// the fallback is safe in the only direction that matters: if the host's real
/// `UID_MIN` were *lower* than 1000 we would refuse some accounts the host
/// considers human, which is a false rejection an operator can see and report. A
/// wrong answer in the other direction — accepting a system account — is the one
/// that cannot be seen, and this constant cannot produce it, because no
/// distribution places a service account above 1000 by default.
const FALLBACK_MINIMUM_ID: u32 = 1000;

/// The numeric identity a forked child drops to before touching customer files.
///
/// Constructed only by [`AccountIds::resolve`], so holding one is proof that the
/// account exists in the system's user database and is not root — the same
/// "valid by construction" shape as [`AccountName`] (rules/rust.md "Validation
/// first"). The fields are private for that reason: a caller cannot assemble a
/// pair of numbers and hand it to the fork.
///
/// The primary group id is taken from the account's own `passwd` entry rather
/// than being supplied by the caller, because the caller is ultimately the panel
/// API and the whole design of this module is that it does not trust it — and it
/// is then checked, in [`AccountIds::resolve`], rather than merely read.
///
/// DO NOT CACHE A VALUE OF THIS TYPE. Ids are safe against uid recycling only
/// because they are resolved by *name* at the moment of use: an account deleted
/// and recreated, or a uid reissued to a different tenant, invalidates every
/// `AccountIds` resolved before it. The type is `Copy` for ergonomics inside a
/// single operation, and `Copy` is exactly what makes it easy to stash one in a
/// struct or a map and reuse it later. Resolve again instead; the lookup is a
/// single `getpwnam_r`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountIds {
    /// The account's user id. Never 0 and never below the host's id floor;
    /// [`AccountIds::resolve`] refuses root and system accounts.
    uid: libc::uid_t,
    /// The account's primary group id, from the same `passwd` entry as `uid`.
    /// Held to the same two rules as `uid`.
    gid: libc::gid_t,
}

impl AccountIds {
    /// Looks `username` up in the system's user database.
    ///
    /// Uses `getpwnam_r` rather than parsing `/etc/passwd` or running `id`: the
    /// file is not the only source of accounts (nss may answer from sssd, LDAP or
    /// systemd-userdb), a hand-written parser is one malformed line away from
    /// resolving the wrong id, and running `id` would be a shell-shaped hole in a
    /// module whose entire purpose is to not have one.
    ///
    /// The reentrant `_r` form specifically: the non-reentrant `getpwnam` returns
    /// a pointer into a static buffer shared by every thread in the process, and
    /// the agent is a multi-threaded daemon.
    ///
    /// # Errors
    ///
    /// Returns [`PrivError::NoSuchAccount`] when the database has no entry for
    /// the name; [`PrivError::RootAccount`] when the entry resolves to uid 0;
    /// [`PrivError::RootGroup`] when its primary group is gid 0;
    /// [`PrivError::SystemAccount`] when either id is below the host's floor for a
    /// human account; and [`PrivError::LookupFailed`] when the database itself
    /// could not be read — which is deliberately distinct from the account being
    /// absent, because treating an unreadable database as "no such account" is how
    /// a lookup failure turns into a wrong answer.
    pub fn resolve(username: &AccountName) -> Result<Self, PrivError> {
        // `AccountName` cannot contain a NUL, so this conversion cannot fail; it
        // is still handled rather than unwrapped, because a root process does not
        // panic on a reasoning step (rules/rust.md).
        let name = CString::new(username.as_str()).map_err(|_| PrivError::NoSuchAccount)?;

        let mut capacity = suggested_buffer();
        loop {
            let mut entry = MaybeUninit::<libc::passwd>::uninit();
            let mut found: *mut libc::passwd = std::ptr::null_mut();
            let mut buffer = vec![0 as libc::c_char; capacity];

            // SAFETY: `name` is a live NUL-terminated C string for the duration of
            // the call; `entry` and `found` are live, correctly aligned, writable
            // locals of exactly the types `getpwnam_r` expects; `buffer` is a live
            // allocation of `capacity` bytes and `capacity` is what we pass as its
            // length. `getpwnam_r` writes only into those three and does not retain
            // any of the pointers after it returns. Nothing here is shared with
            // another thread.
            let code = unsafe {
                libc::getpwnam_r(
                    name.as_ptr(),
                    entry.as_mut_ptr(),
                    buffer.as_mut_ptr(),
                    capacity,
                    &raw mut found,
                )
            };

            if code == libc::ERANGE {
                capacity = capacity.saturating_mul(2);
                if capacity > MAXIMUM_BUFFER {
                    return Err(PrivError::LookupFailed {
                        errno: libc::ERANGE,
                    });
                }
                continue;
            }

            if code != 0 {
                // POSIX lets an implementation report "not found" either as a null
                // result with a 0 return or as one of these codes. Both mean the
                // same thing and neither means the database is broken.
                return Err(match code {
                    libc::ENOENT | libc::ESRCH | libc::EBADF | libc::EPERM => {
                        PrivError::NoSuchAccount
                    }
                    other => PrivError::LookupFailed { errno: other },
                });
            }

            if found.is_null() {
                return Err(PrivError::NoSuchAccount);
            }

            // SAFETY: `code == 0` and `found` is non-null, which is `getpwnam_r`'s
            // contract for "the entry was written into `entry`", so `entry` is
            // initialised. `found` points at `entry` itself; reading through
            // `entry` keeps the borrow visible to the compiler. Only the two
            // integer fields are read — never the `char *` fields, which point
            // into `buffer` and would dangle once it is dropped.
            let resolved = unsafe { entry.assume_init() };

            let (uid_minimum, gid_minimum) = account_id_floors();
            is_hosting_account(resolved.pw_uid, resolved.pw_gid, uid_minimum, gid_minimum)?;

            return Ok(Self {
                uid: resolved.pw_uid,
                gid: resolved.pw_gid,
            });
        }
    }

    /// The account's user id.
    #[must_use]
    pub fn uid(&self) -> libc::uid_t {
        self.uid
    }

    /// The account's primary group id.
    #[must_use]
    pub fn gid(&self) -> libc::gid_t {
        self.gid
    }
}

/// Refuses ids that belong to root or to a service identity.
///
/// Split out from [`AccountIds::resolve`] so the decision can be tested against
/// every interesting id without needing those accounts to exist on the machine
/// running the tests — the lookup needs a real user database, this does not.
///
/// Both ids are checked, not just the uid, and each against its OWN floor. An
/// account whose `passwd` entry carries `pw_gid == 0` produces a child running
/// with real and effective gid root, and the child's own verification passes,
/// correctly, because it received exactly what it asked for. Nothing downstream
/// of here would notice.
///
/// # Errors
///
/// Returns [`PrivError::RootAccount`] for uid 0, [`PrivError::RootGroup`] for
/// gid 0, and [`PrivError::SystemAccount`] when the uid is below `uid_minimum`
/// or the gid is below `gid_minimum`.
fn is_hosting_account(
    uid: libc::uid_t,
    gid: libc::gid_t,
    uid_minimum: u32,
    gid_minimum: u32,
) -> Result<(), PrivError> {
    // Root is named separately from the floor even though 0 is always below it,
    // because "you asked me to run as root" and "you asked me to run as nginx"
    // are different operator-facing events.
    if uid == 0 {
        return Err(PrivError::RootAccount);
    }
    if gid == 0 {
        return Err(PrivError::RootGroup);
    }
    if uid < uid_minimum || gid < gid_minimum {
        return Err(PrivError::SystemAccount);
    }

    Ok(())
}

/// The lowest user id and the lowest group id this host considers human.
///
/// Read from `/etc/login.defs`, which is the same file `useradd` consults when it
/// allocates an id, so the agent and the tool that created the account agree by
/// construction rather than by coincidence.
///
/// `UID_MIN` and `GID_MIN` are read as the separate settings `login.defs` defines
/// them to be, rather than one standing in for the other. They default to the
/// same 1000 on every distribution in the support matrix, which makes substituting
/// one for the other harmless *today* — but that is a coincidence, and it would
/// fail permissively: on a host where an administrator raised `GID_MIN` above
/// `UID_MIN`, a group id between the two would clear the uid floor while the host
/// itself considers it a system group.
///
/// An unreadable or absent file yields two fallbacks, which is the same answer an
/// empty file gives.
///
/// Deliberately not cached: the parse is a few lines over a file the page cache
/// already holds, and a cached floor would go stale if an operator retuned it.
fn account_id_floors() -> (u32, u32) {
    // `unwrap_or_default` rather than an early return: an unreadable file and a
    // file with neither key must give the same answer, and an empty string is
    // exactly a file with neither key.
    let contents = std::fs::read_to_string(LOGIN_DEFS).unwrap_or_default();

    (
        id_floor(&contents, "UID_MIN"),
        id_floor(&contents, "GID_MIN"),
    )
}

/// Reads one `<key> <value>` floor out of `login.defs` content.
///
/// Takes the content as a string rather than reading the file itself, so the
/// parser can be tested against a hostile `login.defs` — which is the only way to
/// cover the cases that matter, since the host's real file is well-formed.
///
/// Falls back to [`FALLBACK_MINIMUM_ID`] for every input that is not a usable
/// floor: a missing key, a missing value, a non-numeric or negative value, a
/// commented-out line, a `SYS_`-prefixed relative, and — the case that is not
/// obvious — an explicit **zero**.
fn id_floor(contents: &str, key: &str) -> u32 {
    contents
        .lines()
        .filter_map(|line| {
            // `split_whitespace` already skips leading whitespace and splits on
            // the tabs `/etc/login.defs` actually uses, so no `trim` is needed.
            let mut fields = line.split_whitespace();
            // The key must be the FIRST field, so neither a `#` comment nor a
            // `SYS_UID_MIN` line can answer for `UID_MIN`.
            (fields.next() == Some(key)).then(|| fields.next())?
        })
        // The `> 0` filter is NOT redundant with the fallback below, and it must
        // not be removed as a tidy-up: `"0".parse::<u32>()` SUCCEEDS, so without
        // it a floor of zero is a value like any other and `unwrap_or` never
        // runs. A zero floor then turns `id < minimum` into `id < 0`, which is
        // never true for an unsigned integer, so every system-account refusal
        // silently stops firing and `daemon`, `nginx` and `postgres` are hosting
        // accounts again. One line in a config file, no error, no log. Zero is
        // the single value that parses cleanly and means something dangerous, so
        // it is treated as no value at all.
        .find_map(|value| value.parse::<u32>().ok().filter(|id| *id > 0))
        .unwrap_or(FALLBACK_MINIMUM_ID)
}

/// The buffer size the C library recommends for a `passwd` entry.
///
/// Falls back to [`INITIAL_BUFFER`] when `sysconf` reports no limit (it returns
/// -1 for "unspecified"), which is the documented and common case on glibc.
fn suggested_buffer() -> usize {
    // SAFETY: `sysconf` reads no memory through pointers, takes an integer
    // constant defined by the platform, and has no thread-safety requirement.
    let suggestion = unsafe { libc::sysconf(libc::_SC_GETPW_R_SIZE_MAX) };

    if suggestion <= 0 {
        INITIAL_BUFFER
    } else {
        usize::try_from(suggestion).unwrap_or(INITIAL_BUFFER)
    }
}

#[cfg(test)]
#[path = "../tests/privs/account_ids_tests.rs"]
mod tests;
