//! The app store's platform facts: the package behind each app of the closed catalogue and how
//! a family prepares Apache for the panel's vhosts.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter); split out by concern
//! so the one adapter seam stays under the file-size trigger (rules/rust.md "Distro adapter").
//! The service names, the nginx and Apache binaries and the Node.js launcher paths are asked of
//! [`WebServerFacts`](crate::WebServerFacts), and the PHP package of
//! [`PhpFacts`](crate::PhpFacts): they are facts of those subjects before they are facts of the
//! app store.

use maran_agent_core::validation::web::node_major::NodeMajor;

use crate::adapter_binary::AdapterBinary;
use crate::nodejs_packaging::NodejsPackaging;
use crate::nodejs_stream_listing::NodejsStreamListing;
use crate::package_query_failure::PackageQueryFailure;

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

    /// Packages the package manager installs WITH Apache and removes with it, that the agent
    /// never names itself: the module that does the serving (SUSE's `apache2` needs one of the
    /// `apache2-prefork`, `-worker` or `-event` packages, and a dry run of its removal lists the
    /// one that is installed). The removal check accepts these beside the app's own packages
    /// and nothing else. Empty where the main package carries everything.
    fn apache_companion_packages(&self) -> &'static [&'static str];

    /// Whether installing the Apache package starts the service by itself, through the
    /// package's own maintainer script (the Debian family's `postinst`). The agent then keeps
    /// the service from starting until port 80 is free for it (issue #529); a family whose
    /// package leaves the service stopped answers `false`.
    fn apache_install_starts_service(&self) -> bool;

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

    /// Further packages Node.js `major` needs before a site's unit can run its commands, in
    /// install order. The SUSE family ships `npm<major>` apart from `nodejs<major>` and does not
    /// require it; the other families carry what a unit runs in the main package or answer an
    /// empty list.
    fn nodejs_extra_packages(&self, major: &str) -> Vec<String>;

    /// Which major the candidate of the single `nodejs` package is, read from the package
    /// manager's answer about it (`stdout` of the candidate query). `None` for a family with
    /// per-major packages, and for an answer that names no candidate.
    fn nodejs_candidate_major(&self, candidate_query_stdout: &str) -> Option<String>;

    /// Absolute path of the Node.js interpreter of `major`, run with `--version` to learn
    /// whether that major is installed. The interpreter a site's unit runs is
    /// [`WebServerFacts::node_executable`](crate::WebServerFacts::node_executable), the same
    /// file.
    ///
    /// A [`NodejsPackaging::PerMajorPackages`] family installs each major beside the others, so
    /// every major has a path of its own; a [`NodejsPackaging::SingleDistroPackage`] family has
    /// one interpreter whatever `major` is.
    fn node_binary(&self, major: &NodeMajor) -> AdapterBinary;

    /// The arguments of the read-only, cache-only run that lists the streams of the `nodejs`
    /// module, run with [`PackageFacts::package_query_binary`](crate::PackageFacts::package_query_binary)
    /// (issue #532). `Some` for [`NodejsPackaging::ModuleStreams`] only; every other family, and
    /// the RHEL 10 generation, answers `None` ("there is no module question to ask"), so a caller
    /// can never run the package manager with an empty argument list.
    fn nodejs_stream_list_arguments(&self) -> Option<Vec<String>>;

    /// Reads the run of [`Self::nodejs_stream_list_arguments`]: its exit `status`, `stdout` and
    /// `stderr`, as the streams of the catalogue's majors the sources list with the `common`
    /// profile (in the order they are listed, once each) and the streams the host has enabled.
    ///
    /// A row the reader does not understand fails the whole read, and so does a status that is
    /// not a listing's: "the sources list no stream" and "the sources could not be asked" are
    /// different facts, and so is "the sources carry no modules at all"
    /// ([`PackageQueryFailure::NoModules`]), which only `stderr` tells from the others. A family
    /// without module streams answers an empty listing.
    ///
    /// # Errors
    ///
    /// Returns a [`PackageQueryFailure`] for an unsuccessful exit, for sources without modules,
    /// and for a `nodejs` row outside the grammar of the family's listing.
    fn read_nodejs_streams(
        &self,
        status: i32,
        stdout: &str,
        stderr: &str,
    ) -> Result<NodejsStreamListing, PackageQueryFailure>;

    /// The command an operator runs to take a Node.js off the host that was installed outside the
    /// panel (no module stream enabled), for the refusal that tells them how to go on. Text for
    /// the operator, never run by the agent. Empty for a family without module streams.
    fn nodejs_unmanaged_removal_command(&self) -> &'static str;

    /// The command an operator runs to give up a Node.js module stream that is enabled but has
    /// nothing installed (`dnf module reset nodejs`), for the refusal that tells them the panel
    /// will not reset a choice it did not make. Text for the operator, never run by the agent.
    /// Empty for a family without module streams.
    fn nodejs_stream_release_command(&self) -> &'static str;

    /// The package names a removal of a Node.js module stream may take off the host: what the
    /// stream's `common` profile installs. The dry run of the removal is accepted only when
    /// every package it removes because the stream goes is one of these. Empty for a family
    /// without module streams.
    fn nodejs_module_packages(&self) -> &'static [&'static str];
}
