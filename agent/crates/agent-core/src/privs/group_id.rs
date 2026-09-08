//! Resolving a system group's numeric id from the system's group database.

use std::ffi::CString;
use std::mem::MaybeUninit;

use super::priv_error::PrivError;

/// First buffer size handed to `getgrnam_r` when `sysconf` has no opinion.
const INITIAL_BUFFER: usize = 1024;

/// Ceiling on the buffer growth loop. A `group` entry that does not fit in
/// 64 KiB is not a large entry, it is a broken or hostile group database, and
/// growing without a bound would let one turn a lookup into an allocation
/// attack. The same ceiling
/// [`AccountIds`](crate::privs::account_ids::AccountIds) puts on the user
/// database, for the same reason.
const MAXIMUM_BUFFER: usize = 64 * 1024;

/// The numeric id of a system group named by the distro adapter.
///
/// Constructed only by [`GroupId::resolve`], so holding one is proof that the
/// group exists in the system's group database and is not root's — the same
/// "valid by construction" shape as
/// [`AccountIds`](crate::privs::account_ids::AccountIds).
///
/// **This is a group the AGENT names, never a caller.** Its one use is the web
/// server's group, which a restore has to re-apply to an account's home root:
/// `AccountOperations` creates a home as `<account>:<web server group>` mode
/// `0750`, and a restore that put the account's own group back would break
/// every site the account owns with a 403 nobody could explain. The name comes
/// from the distro adapter and the number comes from here, because the two
/// families spell the group differently and neither spelling is a number.
///
/// It refuses gid 0 for the reason
/// [`PrivError::RootGroup`]
/// exists: a home group-owned by root is a home whose group bit means nothing
/// to the customer and everything to a process that should not be reading it.
/// A host whose web server group really is root is a host this agent refuses
/// to finish a restore on, loudly, rather than one it quietly hands a
/// root-grouped home.
///
/// DO NOT CACHE A VALUE OF THIS TYPE, for the reason `AccountIds` gives: a
/// group deleted and recreated takes a new number, and a stale one names
/// whoever received it. Resolve again; the lookup is a single `getgrnam_r`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GroupId(libc::gid_t);

impl GroupId {
    /// Looks `group` up in the system's group database.
    ///
    /// Uses `getgrnam_r` rather than parsing `/etc/group` or running `getent`,
    /// for the three reasons
    /// [`AccountIds::resolve`](crate::privs::account_ids::AccountIds::resolve)
    /// gives about the user database: the file is not the only source of
    /// groups (nss may answer from sssd, LDAP or systemd-userdb), a
    /// hand-written parser is one malformed line away from resolving the wrong
    /// id, and spawning `getent` would be a process spawn in a module whose
    /// purpose is to not need one.
    ///
    /// The reentrant `_r` form specifically: the plain `getgrnam` returns a
    /// pointer into a static buffer shared by every thread in the process, and
    /// the agent is a multi-threaded daemon.
    ///
    /// # Errors
    ///
    /// - [`PrivError::NoSuchGroup`] when the database has no entry for `group`,
    ///   including the case where the name cannot be a C string at all.
    /// - [`PrivError::RootGroup`] when the entry resolves to gid 0.
    /// - [`PrivError::GroupLookupFailed`] when the database itself could not be
    ///   read — deliberately distinct from the group being absent, because
    ///   treating an unreadable database as "no such group" is how a lookup
    ///   failure turns into a wrong answer.
    pub fn resolve(group: &str) -> Result<Self, PrivError> {
        // Handled rather than unwrapped: a root process does not panic on a
        // reasoning step (rules/rust.md). A name carrying a NUL is a name no
        // group database can hold, which is exactly "no such group".
        let name = CString::new(group).map_err(|_| PrivError::NoSuchGroup)?;

        let mut capacity = suggested_buffer();
        loop {
            let mut entry = MaybeUninit::<libc::group>::uninit();
            let mut found: *mut libc::group = std::ptr::null_mut();
            let mut buffer = vec![0 as libc::c_char; capacity];

            // SAFETY: `name` is a live NUL-terminated C string for the duration
            // of the call; `entry` and `found` are live, correctly aligned,
            // writable locals of exactly the types `getgrnam_r` expects;
            // `buffer` is a live allocation of `capacity` bytes and `capacity`
            // is what is passed as its length. `getgrnam_r` writes only into
            // those three and retains none of the pointers after it returns.
            // Nothing here is shared with another thread.
            let code = unsafe {
                libc::getgrnam_r(
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
                    return Err(PrivError::GroupLookupFailed {
                        errno: libc::ERANGE,
                    });
                }
                continue;
            }

            if code != 0 {
                // POSIX lets an implementation report "not found" either as a
                // null result with a 0 return or as one of these codes. Both
                // mean the same thing, and neither means the database broke.
                return Err(match code {
                    libc::ENOENT | libc::ESRCH | libc::EBADF | libc::EPERM => {
                        PrivError::NoSuchGroup
                    }
                    other => PrivError::GroupLookupFailed { errno: other },
                });
            }

            if found.is_null() {
                return Err(PrivError::NoSuchGroup);
            }

            // SAFETY: `code == 0` and `found` is non-null, which is
            // `getgrnam_r`'s contract for "the entry was written into `entry`",
            // so `entry` is initialised. Only the integer `gr_gid` field is
            // read — never the `char *` fields, which point into `buffer` and
            // would dangle once it is dropped.
            let resolved = unsafe { entry.assume_init() };

            if resolved.gr_gid == 0 {
                return Err(PrivError::RootGroup);
            }

            return Ok(Self(resolved.gr_gid));
        }
    }

    /// The group's numeric id.
    #[must_use]
    pub fn gid(&self) -> libc::gid_t {
        self.0
    }
}

/// The buffer size the platform suggests for one `group` entry.
///
/// Falls back to [`INITIAL_BUFFER`] when `sysconf` declines to answer, which it
/// is allowed to do; the growth loop above covers an answer that turns out to
/// be too small either way.
fn suggested_buffer() -> usize {
    // SAFETY: `sysconf` reads no memory through pointers, takes an integer
    // constant defined by the platform, and has no thread-safety requirement.
    let suggestion = unsafe { libc::sysconf(libc::_SC_GETGR_R_SIZE_MAX) };

    if suggestion <= 0 {
        INITIAL_BUFFER
    } else {
        usize::try_from(suggestion).unwrap_or(INITIAL_BUFFER)
    }
}

#[cfg(test)]
#[path = "../tests/privs/group_id_tests.rs"]
mod tests;
