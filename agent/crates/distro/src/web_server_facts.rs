//! The web server and TLS tooling: nginx, the identity it runs as, and openssl.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").

use crate::adapter_binary::AdapterBinary;

/// The web server and TLS tooling: nginx, the identity it runs as, and openssl.
pub trait WebServerFacts: Send + Sync {
    /// Absolute path of the nginx binary, for the process-execution allow-list.
    ///
    /// Both families install nginx at `/usr/sbin/nginx`, but the value still comes
    /// from the adapter rather than a literal in `ops`: a binary is only spawnable
    /// because this trait named it, not because the path happens to agree today.
    ///
    /// Like every `*_binary` method below that names a program the agent
    /// executes, it returns an [`AdapterBinary`], a type only this crate can
    /// construct, so the allow-list is enforced by the compiler: a seam that
    /// takes `&AdapterBinary` cannot be handed a string from anywhere else.
    /// (`sh_binary` stays text on purpose: it is written into a crontab line
    /// and never executed by the agent.)
    fn nginx_binary(&self) -> AdapterBinary;

    /// Name of the nginx service unit, for `systemctl reload`/`restart`.
    fn nginx_service(&self) -> &'static str;

    /// The user the web server runs as, which must own nothing and be able to
    /// read a site's document root.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family. The
    /// document root's group is set to this so the server can read files the
    /// account owns without either of them being able to write the other's.
    fn web_server_user(&self) -> &'static str;

    /// The group an account's home directory is group-owned by, so the web
    /// server can traverse into a document root the account owns.
    ///
    /// `www-data` on the Debian family, `nginx` on the RHEL family — the same
    /// word as the user on both, and asked as its own question all the same:
    /// a group is what a directory is owned by, and the two names agreeing is a
    /// fact about these two distributions rather than a rule.
    fn web_server_group(&self) -> &'static str;

    /// Absolute path of the openssl binary, for the process-execution
    /// allow-list.
    ///
    /// The certificate operations run it to read a certificate's public key
    /// and its expiry, and to generate the self-signed placeholder a site
    /// serves before a real certificate arrives. Both families install it at
    /// the same path today; it is still asked of the adapter, because `ops`
    /// names no absolute binary path of its own.
    fn openssl_binary(&self) -> AdapterBinary;
}
