//! The config-write protocol for a file that must not exist yet.

use std::path::Path;

use crate::safe_write::model::write_intent::WriteIntent;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::render_validate_swap::swap_in;
use crate::safe_write::{ConfigHost, SafeWriteError};

/// Creates `target` with `contents` through the full config-write protocol —
/// the same sequence, under the same lock, as
/// [`super::render_validate_swap::write_config`] — with two refusals that a
/// replacement does not have: the target must not exist, and a validator that
/// exits 0 still refuses the write when its standard error contains any of
/// `refused_warnings`.
///
/// # Why a creation needs both
///
/// A replacement and a creation fail differently when two of them race. Two
/// replacements of one file serialise on the config-tree lock and the second
/// one wins, which is the point of replacing. Two creations do not have a
/// second winner: the operation that asked for a file that is not there yet is
/// asking to INTRODUCE something, and finding it already there means somebody
/// else introduced it first. Overwriting it would hand the first caller's
/// configuration to the second.
///
/// The warnings are the same question asked about the rest of the tree. A web
/// server resolves a request by its `Host` header alone, so a new vhost that
/// names a host another vhost already serves is a second claim on that host —
/// and `nginx -t` exits 0 on it, warning only that it "ignored" one of the two
/// (measured on nginx 1.24.0:
/// `conflicting server name "<host>" on 0.0.0.0:80, ignored`, on standard
/// error, exit status 0; `sites::create_vhost` says which of the other shipped
/// versions were read rather than measured). Which file then answers is
/// decided by the include glob's sort order, not by anybody's ownership, so an
/// exit status cannot be the whole verdict for a creation. The caller supplies the warnings because
/// it is the one that knows which names the new file introduces; this protocol
/// has no opinion on what a configuration says.
///
/// Both checks are made inside the critical section, after the capture and
/// after the rename respectively, so they hold for any number of concurrent
/// creations in this process. Neither reaches past it: the lock is
/// process-local (see `config_tree_lock`), and a file written by something
/// other than this agent between the capture and the rename is refused by the
/// kernel's no-replace rename rather than by the lock.
///
/// # Errors
///
/// Returns [`SafeWriteError::TargetExists`] when `target` already exists, with
/// nothing touched. Returns [`SafeWriteError::RefusedWarning`] when the
/// validator exited 0 but printed one of `refused_warnings`, with the new file
/// removed again. Every other failure is exactly
/// [`super::render_validate_swap::write_config`]'s, with the same restoration —
/// which for a creation always means the new file is removed, because there
/// was nothing before it.
pub fn create_config(
    host: &dyn ConfigHost,
    target: &Path,
    contents: &str,
    validator: &Validator<'_>,
    reload: &Reload<'_>,
    refused_warnings: &[String],
) -> Result<(), SafeWriteError> {
    swap_in(
        host,
        target,
        contents,
        validator,
        reload,
        &WriteIntent::Create { refused_warnings },
    )
}

#[cfg(test)]
#[path = "../tests/safe_write/create_config_tests.rs"]
mod tests;
