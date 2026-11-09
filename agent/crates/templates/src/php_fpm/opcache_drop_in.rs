//! The OPcache drop-in of one PHP version's php-fpm.

use askama::Template;

use crate::render_error::RenderError;

/// Renders the ini drop-in that hardens and sizes the OPcache shared by every
/// account's pool of one PHP version.
///
/// Only the three sizes are fields. The isolation lines (`validate_permission`,
/// `validate_root`, `restrict_api`) are fixed text in the template, deliberately
/// not fields: a caller cannot render a drop-in without them, and the golden
/// pins them on every render.
#[derive(Template)]
// A config file is not a document: `escape = "none"`, as for every render type
// here. The only values reaching the template are three integers, which cannot
// carry a newline or a control character.
#[template(path = "php-fpm/opcache.ini.j2", escape = "none")]
pub struct OpcacheDropIn {
    /// `opcache.memory_consumption`: the size of the shared cache, in MiB.
    pub memory_consumption_mib: u32,
    /// `opcache.interned_strings_buffer`: the memory for interned strings, in MiB.
    pub interned_strings_buffer_mib: u32,
    /// `opcache.max_accelerated_files`: how many scripts the cache can hold.
    pub max_accelerated_files: u32,
}

impl OpcacheDropIn {
    /// Renders the drop-in text.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which
    /// can only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}

#[cfg(test)]
#[path = "../tests/php_fpm/opcache_drop_in_tests.rs"]
mod tests;
