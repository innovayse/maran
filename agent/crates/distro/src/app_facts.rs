//! The app store's platform facts: the package behind each app of the closed catalogue and how
//! a family prepares Apache for the panel's vhosts.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").
//! The service names, the nginx and Apache binaries and the Node.js launcher paths are asked of
//! [`WebServerFacts`](crate::WebServerFacts), and the PHP package of
//! [`PhpFacts`](crate::PhpFacts): they are facts of those subjects before they are facts of the
//! app store.

use crate::adapter_binary::AdapterBinary;
use crate::nodejs_packaging::NodejsPackaging;

/// The package behind each app of the catalogue, and how Apache is prepared.
pub trait AppFacts: Send + Sync {
    /// Name of the package that provides nginx. `nginx` on every family.
    fn nginx_package(&self) -> &'static str;

    /// Name of the package that provides Apache: `apache2` on the Debian and SUSE families,
    /// `httpd` on the RHEL family.
    fn apache_package(&self) -> &'static str;

    /// Further packages Apache needs before it can serve the panel's TLS vhosts, in install
    /// order. The RHEL family ships `mod_ssl` apart from `httpd`; the others carry TLS in the
    /// main package and answer an empty list.
    fn apache_extra_packages(&self) -> &'static [&'static str];

    /// Absolute path of the file that includes the agent's own Apache vhost directory from the
    /// packaged configuration, in a directory the packaged configuration already includes.
    ///
    /// Text, not a binary: the agent writes it through the config-write protocol and never
    /// executes it.
    fn apache_include_snippet_path(&self) -> &'static str;

    /// The Apache modules the panel's vhost templates use, which a family enables with
    /// [`Self::apache_module_enabler`]. Empty for a family whose package loads them already.
    fn apache_required_modules(&self) -> &'static [&'static str];

    /// Absolute path of the program that enables an Apache module (`a2enmod`), or `None` for a
    /// family that has none.
    fn apache_module_enabler(&self) -> Option<AdapterBinary>;

    /// How the family packages Node.js.
    fn nodejs_packaging(&self) -> NodejsPackaging;

    /// Name of the package that provides Node.js `major` (one of the catalogue's four).
    ///
    /// For a [`NodejsPackaging::SingleDistroPackage`] family this is the one `nodejs` package
    /// whatever `major` is; [`Self::nodejs_candidate_major`] says which major it is.
    fn nodejs_package(&self, major: &str) -> String;

    /// Which major the candidate of the single `nodejs` package is, read from the package
    /// manager's answer about it (`stdout` of the candidate query). `None` for a family with
    /// per-major packages, and for an answer that names no candidate.
    fn nodejs_candidate_major(&self, candidate_query_stdout: &str) -> Option<String>;

    /// Absolute path of the Node.js interpreter, run with `--version` to learn which major is
    /// installed. The interpreter a site's unit runs is
    /// [`WebServerFacts::node_executable`](crate::WebServerFacts::node_executable), the same file.
    fn node_binary(&self) -> AdapterBinary;
}
