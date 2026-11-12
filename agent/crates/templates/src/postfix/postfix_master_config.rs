//! The whole `master.cf` the agent writes for the host's Postfix.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the complete `master.cf`: the public SMTP service, the two
/// authenticated submission services, and the internal services Postfix needs.
///
/// It takes two values: the path of the table of this host's own client addresses
/// (see [`super::postfix_self_clients::PostfixSelfClients`]), which the port 25
/// service consults before accepting a connection, and the path of the
/// `header_checks` table of the local-submission service (see
/// [`super::postfix_local_submitter_headers::PostfixLocalSubmitterHeaders`]). Every service is unchrooted, because the sockets it talks
/// to are absolute paths outside the queue directory and the two families ship
/// different defaults for the chroot column; that decision is the whole reason
/// the file is Maran's and not the package's.
#[derive(Template)]
#[template(path = "postfix/master.cf.j2", escape = "none")]
pub struct PostfixMasterConfig {
    /// Absolute path of the CIDR table of this host's own addresses. A cidr table
    /// is read as written and needs no index, so there is no map type to name.
    pub self_clients_path: String,
    /// Absolute path of the `header_checks` table the local-submission service
    /// (`cleanup_local`) runs, a regexp table that needs no index.
    pub local_submitter_headers_path: String,
}

impl PostfixMasterConfig {
    /// Renders the complete `master.cf` text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template and this type have
    /// drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
