//! Whether the server holds a login of this panel.

use maran_agent_core::validation::db::db_user_name::DbUserName;

use crate::db::db_error::DbError;
use crate::db::db_host::DbHost;
use crate::db::user_host::USER_HOST;

/// Whether the server holds `user` at [`USER_HOST`].
///
/// Asked of `mysql.user` rather than of a grant listing, because a grant tells
/// which databases a login may reach and this question is only whether the login
/// is there at all: a login whose grants were all revoked is still a login whose
/// password can be set and which can be dropped.
///
/// The host is part of the question. `'shop'@'localhost'` and `'shop'@'%'` are
/// two different logins to MySQL, and answering "yes" off the name alone would
/// let an operation report success after altering nothing.
///
/// # Errors
///
/// - [`DbError::Unparsable`] when the count is not a number.
/// - Otherwise whatever the client failed with; see [`DbHost::execute`].
pub(crate) fn user_exists(host: &dyn DbHost, user: &DbUserName) -> Result<bool, DbError> {
    let printed = host.execute(&format!(
        "SELECT COUNT(*) FROM mysql.user WHERE user = '{}' AND host = '{USER_HOST}'",
        user.as_str()
    ))?;

    let count: u64 = printed.trim().parse().map_err(|_| DbError::Unparsable)?;

    Ok(count > 0)
}
