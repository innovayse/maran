//! The PHP-extension side of a family: which package provides an extension,
//! how it is switched, and where extra ini files go.
//!
//! A supertrait of [`DistroAdapter`](crate::adapter::DistroAdapter), a concern
//! of its own beside [`PhpFacts`](crate::php_facts::PhpFacts) so neither grows
//! past what one reader can hold (rules/rust.md "Distro adapter").
//!
//! # Not checked against a live repository
//!
//! Every mapping a family answers here is the author's reading of the
//! distribution's package lists, NOT a measurement against a live repository
//! or a container: the session that wrote it had neither. The open
//! measurements are listed in
//! `docs/superpowers/notes/2026-11-07-php-extensions-threat-note.md`
//! ("Not verified by the author"), and no fixtures were recorded, so none are
//! invented. A reviewer checks the tables against the repositories' own
//! package lists.

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php_extension_switch::PhpExtensionSwitch;

/// The PHP-extension side of a family: package, switch and ini drop-in directory.
pub trait PhpExtensionFacts: Send + Sync {
    /// The OS package that provides `extension` for `version`.
    ///
    /// `None` means this family has no separate package for it: the extension
    /// is built into another package, or is not offered. The caller refuses
    /// the install rather than guessing a name.
    ///
    /// Takes the validated [`PhpVersion`] and [`PhpExtensionName`], so the
    /// answer is built from two closed grammars and cannot carry a separator,
    /// an option or a control character. The mapping is the author's reading
    /// of the repositories, NOT checked against a live one (see the module
    /// documentation).
    fn php_extension_package(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<String>;

    /// How to switch `extension` on and off for the fpm SAPI of `version`.
    ///
    /// `None` means the family has no clean switch: the extension can be
    /// switched on by installing its package but not off again, and the
    /// agent answers a typed refusal rather than editing the distribution's
    /// ini files itself. The mapping is the author's reading, NOT checked
    /// against a live host (see the module documentation).
    fn php_extension_switch(
        &self,
        version: &PhpVersion,
        extension: PhpExtensionName,
    ) -> Option<PhpExtensionSwitch>;

    /// Directory of the additional ini files php-fpm reads for `version` —
    /// `/etc/php/8.3/fpm/conf.d` on the Debian family,
    /// `/etc/opt/remi/php83/php.d` on the RHEL family, where the dot is
    /// dropped.
    ///
    /// The path shape is the author's reading of each family's layout, NOT
    /// checked against a live host (see the module documentation).
    fn php_ini_drop_in_directory(&self, version: &PhpVersion) -> String;
}
