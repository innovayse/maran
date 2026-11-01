//! SetSftpAuthorizedKeys: converge one SFTP login's key file to the set the
//! panel holds (issue #135).

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::DistroAdapter;
use maran_templates::sshd::authorized_keys::AuthorizedKeys;

use crate::accounts::take_account_lock;
use crate::safe_write::model::{ConfigFile, Validator};
use crate::sftp::model::account_jail::AccountJail;
use crate::sftp::model::authorized_key_set::AuthorizedKeySet;
use crate::sftp::model::login_key_file::LoginKeyFile;
use crate::sftp::remove_key_file::remove_key_file;
use crate::sftp::set_sftp_password::stored_password;
use crate::sftp::sftp_error::SftpError;
use crate::sftp::sftp_host::SftpHost;

/// The key file's mode. World-readable because sshd opens it with the LOGIN's
/// uid, which is the account's, and the file is root's; the directory above it
/// is 0711, so it cannot be listed. Public keys with their comments removed are
/// all it holds (threat note, "Accepted residual").
const KEY_FILE_MODE: u32 = 0o644;

/// `ssh-keygen`'s flag for printing a key file's fingerprints.
const FINGERPRINT: &str = "-l";

/// `ssh-keygen`'s flag naming the file to read.
const FILE: &str = "-f";

/// Replaces `user`'s whole key set with `keys`, and answers how many keys the
/// login now holds.
///
/// The panel's record is the truth, so this converges rather than edits: the
/// same set twice renders identical bytes, and an empty set removes the file —
/// both names of it — leaving password login exactly as it was.
///
/// # Which name the file is written under
///
/// The login's shadow field is read first and classified as the password path
/// classifies it (the `stored_password` reading `set_sftp_password` uses). A login that can authenticate gets its
/// keys at [`LoginKeyFile::active`], the path sshd reads; a locked one — a
/// suspended account — at [`LoginKeyFile::suspended`], which sshd never reads.
/// The other name is then removed, so exactly one copy exists. Without this, a
/// suspended customer adding a key would be a suspended customer logging in:
/// with `UsePAM yes` sshd does not consult the shadow lock for a key.
///
/// # The write
///
/// The file is rendered by `maran-templates` (`restrict <algorithm> <base64>`
/// per key) and written through `safe_write::write_unreloaded_config`, at mode
/// 0644, with `ssh-keygen -l -f <file>` as the validator: OpenSSH's own parser
/// reading the file sshd will read, after the rename and with a rollback on
/// refusal. There is no reload, because sshd opens the file at each
/// authentication.
///
/// # The lock and the login
///
/// The hosting account's never-waiting lock is taken at entry, the account
/// must be a managed hosting account, and the login must be one this account
/// holds — its passwd home is exactly this account's jail — which is the gate
/// `set_sftp_password` uses, for the same reason: `<account>_<name>` has no
/// unique decomposition, and without it a request authorised for one tenant
/// could write keys onto another tenant's login.
///
/// # Errors
///
/// - [`SftpError::AccountBusy`] when another operation for the account runs.
/// - [`SftpError::AccountNotManaged`] / [`SftpError::AccountMissing`] when the
///   account is not one this panel may act on.
/// - [`SftpError::NotFound`] when the account holds no such login.
/// - [`SftpError::StatusUnreadable`] / [`SftpError::SpawnFailed`] when the
///   login's shadow field cannot be read; nothing is written.
/// - [`SftpError::KeyFileFailed`] when the file could not be rendered,
///   written, validated or removed; a refused write restores the previous
///   file.
/// - [`SftpError::Bound`] when `getent` or the validator outran its deadline.
pub fn set_sftp_authorized_keys(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    user: &SftpUserName,
    keys: &AuthorizedKeySet,
) -> Result<usize, SftpError> {
    // Owned guard, released by every path out of this function.
    let _guard = take_account_lock(account).ok_or(SftpError::AccountBusy)?;

    set_sftp_authorized_keys_under_lock(host, distro, account, user, keys)
}

/// The key write itself, with the hosting account's lock ALREADY held.
///
/// Split from [`set_sftp_authorized_keys`] for the reason `set_sftp_password`
/// is split: the lock is a process-wide static that never waits, so a suite
/// driving the public entry for one account name on the harness's threads would
/// refuse itself.
///
/// # Errors
///
/// Every variant [`set_sftp_authorized_keys`] documents except
/// [`SftpError::AccountBusy`].
pub(crate) fn set_sftp_authorized_keys_under_lock(
    host: &dyn SftpHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    user: &SftpUserName,
    keys: &AuthorizedKeySet,
) -> Result<usize, SftpError> {
    let managed = host.managed_account(distro.passwd_database(), account)?;
    let jail = AccountJail::for_account(managed.name(), distro.systemd_unit_directory());
    let logins = host.account_logins(distro.passwd_database(), managed.name(), jail.directory())?;
    if !logins.iter().any(|held| held.as_str() == user.as_str()) {
        return Err(SftpError::NotFound);
    }

    let file = LoginKeyFile::for_login(user);
    if keys.is_empty() {
        remove_key_file(host, file.active())?;
        remove_key_file(host, file.suspended())?;
        return Ok(0);
    }

    // Read under the lock, so a suspension cannot land between this answer
    // and the write it decides.
    let (live, idle) = if stored_password(host, distro, user)?.can_authenticate() {
        (file.active(), file.suspended())
    } else {
        (file.suspended(), file.active())
    };

    let lines = keys.lines();
    let contents = AuthorizedKeys {
        login: user.as_str(),
        keys: &lines,
    }
    .render_config()
    .map_err(|_| SftpError::KeyFileFailed)?;

    let target = live.display().to_string();
    let keygen = distro.ssh_keygen_binary();
    let validator = Validator {
        program: &keygen,
        arguments: &[FINGERPRINT, FILE, &target],
        // Reads the file and writes nothing: a pure validation.
        class: CallClass::Validate,
    };
    host.write_key_file(
        &ConfigFile {
            target: live,
            contents: &contents,
            mode: KEY_FILE_MODE,
        },
        &validator,
    )?;
    remove_key_file(host, idle)?;

    Ok(keys.len())
}

#[cfg(test)]
#[path = "../tests/sftp/set_sftp_authorized_keys_tests.rs"]
mod tests;
