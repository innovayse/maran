//! Resolving a hosting account's numeric ids from the system's user database.

use std::ffi::{CStr, CString};
use std::mem::MaybeUninit;

use super::priv_error::PrivError;
use crate::agent_paths::AgentPaths;
use crate::utils::login_defs_floor::{DEFAULT_ID_FLOOR, LOGIN_DEFS_PATHS, login_defs_floor};
use crate::utils::login_defs_floor_reading::LoginDefsFloorReading;
use crate::validation::system::managed_account::ManagedAccount;
use crate::validation::system::name::AccountName;

/// First buffer size handed to `getpwnam_r` when `sysconf` has no opinion.
const INITIAL_BUFFER: usize = 1024;

/// Ceiling on the buffer growth loop. A `passwd` entry that does not fit in 64 KiB
/// is not a large entry, it is a broken or hostile user database, and growing
/// without a bound would let one turn a lookup into an allocation attack.
const MAXIMUM_BUFFER: usize = 64 * 1024;

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
    /// human account, when the name is on the reserved list, or when the entry's
    /// home is not `<ACCOUNT_HOME_ROOT>/<name>` (the same identity rules
    /// [`ManagedAccount`] applies, written once in that type and asked here so
    /// that every caller that acts as an account's uid — files, sites, ssl, php,
    /// backup, cron — refuses an operator's own login rather than only the
    /// lifecycle operations doing so); and [`PrivError::LookupFailed`] when the database itself
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
            // `entry` keeps the borrow visible to the compiler. The two integer
            // fields are read directly; the one `char *` field read below is
            // copied out while `buffer` is still alive.
            let resolved = unsafe { entry.assume_init() };

            // The numeric floors come first so uid 0 and gid 0 keep their specific
            // errors (`RootAccount`, `RootGroup`) instead of being reported as a
            // reserved name.
            let (uid_minimum, gid_minimum) = account_id_floors();
            is_hosting_account(resolved.pw_uid, resolved.pw_gid, uid_minimum, gid_minimum)?;

            // A null home from an unusual name service is an entry with no home at
            // all, which is not a managed home; refuse it before dereferencing.
            if resolved.pw_dir.is_null() {
                return Err(PrivError::SystemAccount);
            }

            // SAFETY: `resolved.pw_dir` is non-null (checked above) and is a
            // NUL-terminated string `getpwnam_r` wrote into `buffer` (its contract
            // when it returns 0 with a non-null result). `buffer` is still alive
            // here: it is dropped only when this loop iteration ends, after the
            // string has been copied out.
            let home = unsafe { CStr::from_ptr(resolved.pw_dir) }.to_string_lossy();
            is_managed_identity(username, &home)?;

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

/// Refuses a name or a home that no hosting account of this agent has.
///
/// The reserved list is the managed-account type's own (asked, not copied), and
/// the home must be exactly `<ACCOUNT_HOME_ROOT>/<name>` — what the agent gives
/// an account it creates. Split out like [`is_hosting_account`] so the decision
/// is tested without a real user database. Both refusals are
/// [`PrivError::SystemAccount`], one answer that names no rule.
///
/// # Errors
///
/// Returns [`PrivError::SystemAccount`] when `name` is reserved or `home` is not
/// the managed home of `name`.
fn is_managed_identity(name: &AccountName, home: &str) -> Result<(), PrivError> {
    ManagedAccount::refuse_reserved(name).map_err(|_| PrivError::SystemAccount)?;

    if home != format!("{}/{}", AgentPaths::ACCOUNT_HOME_ROOT, name.as_str()) {
        return Err(PrivError::SystemAccount);
    }

    Ok(())
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
pub(crate) fn is_hosting_account(
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
pub(crate) fn account_id_floors() -> (u32, u32) {
    (id_floor("UID_MIN"), id_floor("GID_MIN"))
}

/// Reads one floor the way the firewall's output chain reads `UID_MIN`: through the shared
/// [`login_defs_floor`], over the same list of files, the first that sets it deciding.
///
/// Every input that is not a usable floor gives [`DEFAULT_ID_FLOOR`]: a missing or unreadable file, a
/// missing key or value, a non-numeric or negative value, a commented-out line, a `SYS_`-prefixed relative
/// and, the case that is not obvious, an explicit **zero**. The zero is NOT redundant with the fallback:
/// `"0".parse::<u32>()` SUCCEEDS, so without it a floor of zero is a value like any other, `id < 0` is
/// never true for an unsigned integer, and every system-account refusal silently stops firing; `daemon`,
/// `nginx` and `postgres` are hosting accounts again. One line in a config file, no error, no log.
fn id_floor(key: &str) -> u32 {
    // An unreadable file reads as an empty one: the same answer as a file with neither key.
    let contents: Vec<String> = LOGIN_DEFS_PATHS
        .iter()
        .map(|path| std::fs::read_to_string(path).unwrap_or_default())
        .collect();

    id_floor_in(contents.iter().map(String::as_str), key)
}

/// The decision of [`id_floor`] over the TEXTS of the files, most authoritative first, so it can be
/// tested against a hostile `login.defs`, which the host's real, well-formed file cannot be.
fn id_floor_in<'a>(files: impl IntoIterator<Item = &'a str>, key: &str) -> u32 {
    for contents in files {
        match login_defs_floor(contents, key) {
            LoginDefsFloorReading::Unset => {}
            LoginDefsFloorReading::Set(value) => return value,
            LoginDefsFloorReading::Unusable => return DEFAULT_ID_FLOOR,
        }
    }

    DEFAULT_ID_FLOOR
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
