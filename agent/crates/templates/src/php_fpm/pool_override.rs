//! One entry of the whitelisted php.ini overrides a pool may carry.

/// A single `php_value[name] = value` line.
///
/// The caller restricts `name` to a fixed whitelist (spec §11) before this
/// type is ever constructed — the disable-list and `open_basedir` above it in
/// the rendered pool are set with `php_admin_value`, which a `php_value`
/// override cannot shadow, so this type carries only what the whitelist
/// already allows.
#[derive(Debug, Clone, Copy)]
pub struct PoolOverride<'a> {
    /// The php.ini setting name, from the whitelist.
    pub name: &'a str,
    /// The value to set it to.
    pub value: &'a str,
}
