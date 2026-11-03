//! Taking away the key files whose login no longer exists (issue #338).

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

use crate::sftp::model::login_key_file::LoginKeyFile;
use crate::sftp::remove_key_file::remove_key_file;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// Removes every key file in the SFTP key directory whose login is not in the
/// password database, and answers how many files it removed.
///
/// # Why this exists
///
/// A login's key file is addressed by the login's NAME, and the agent keeps no
/// record of it elsewhere. Deletion now takes the file before the login, and a
/// creation purges a new name's file, but both are steps of a process that can
/// be killed between them, and a file can also be put there by hand or by an
/// agent older than those orders. A file that outlives its login is not inert:
/// the next login created under that name authenticates with its keys
/// (`delete_sftp_user` states the case). Sweeping at start-up closes what the
/// two ordering rules cannot, without giving the agent a state of its own — the
/// directory and the password database are both read as they are now.
///
/// # What it removes, and what it leaves
///
/// A file whose name has the shape of a login's key file (`LoginKeyFile::
/// login_named_by`: `[a-z0-9_]` holding the account separator, with or without
/// the `.suspended` suffix) and whose login is absent from the password
/// database. Everything else is left: a name of another shape is not something
/// this agent wrote, and a login that EXISTS keeps its file under both names
/// whichever account owns it.
///
/// # Order, and the race that remains
///
/// The directory is listed BEFORE the password database is read. A file that
/// was listed and whose login is then found missing was therefore written for a
/// login that existed when the file did. The window that remains is a login
/// created and given keys between the password read and the removal: its keys
/// are removed, which fails closed (the customer sets them again) and cannot
/// grant access. At start-up, where the sweep runs, no request has been
/// accepted yet and the window is empty.
///
/// A password database that cannot be read removes NOTHING: a login the sweep
/// cannot find is not a login that is gone.
///
/// # Errors
///
/// - [`SftpError::PasswdUnreadable`] when the password database cannot be read.
/// - [`SftpError::KeyFileFailed`] when the directory cannot be listed or a file
///   cannot be removed. Files removed before the failure stay removed, and a
///   repeat converges.
pub fn reconcile_orphan_key_files(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
) -> Result<usize, SftpError> {
    let files = host.key_file_names()?;
    if files.is_empty() {
        return Ok(0);
    }
    let logins = host.login_names(distro.passwd_database())?;

    let mut removed = 0;
    for file in &files {
        let Some(login) = LoginKeyFile::login_named_by(file) else {
            continue;
        };
        if logins.iter().any(|held| held == login) {
            continue;
        }

        remove_key_file(
            host,
            &Path::new(AgentPaths::SFTP_AUTHORIZED_KEYS_DIRECTORY).join(file),
        )?;
        removed += 1;
    }

    Ok(removed)
}

#[cfg(test)]
#[path = "../tests/sftp/reconcile_orphan_key_files_tests.rs"]
mod tests;
