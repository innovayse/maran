//! The bind-mount unit that puts an account's home inside its SFTP jail.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the `.mount` unit that binds an account's real home into the
/// root-owned SFTP jail it is chrooted to.
///
/// A unit rather than a `mount` call, and that is the point of the file: a
/// mount made imperatively is gone at the next boot, and every SFTP login for
/// the account then lands in an empty jail with no sign of what happened. An
/// enabled unit is re-established by the service manager on every boot, so the
/// jail is correct by construction rather than for as long as nobody reboots.
///
/// Both fields are absolute paths built by the agent from a validated
/// `AccountName` — never a caller-supplied path. That matters here because a
/// unit file is line-oriented: a newline in either value would append
/// directives of somebody else's choosing to a file the service manager runs as
/// root (rules/security.md §4). The values are validated, not escaped.
#[derive(Template)]
// A config file is not a document: HTML-escaping a unit's paths would corrupt
// them silently. Values reaching a template are validated
// (rules/rust.md "Validation first"), which is what makes an escaper needless
// here rather than merely inconvenient.
#[template(path = "systemd/mount_unit.mount.j2", escape = "none")]
pub struct MountUnit<'a> {
    /// The account the jail belongs to, for the unit's description.
    pub account: &'a str,
    /// Absolute path of the directory being mounted — the account's real home.
    pub source_directory: &'a str,
    /// Absolute path of the mount point inside the jail.
    ///
    /// The unit's file name must be this path escaped, or the service manager
    /// refuses to load the unit at all; deriving the two from one value is the
    /// caller's job (`maran-ops`'s `AccountJail`).
    pub mount_point: &'a str,
}

impl MountUnit<'_> {
    /// Renders the unit text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
