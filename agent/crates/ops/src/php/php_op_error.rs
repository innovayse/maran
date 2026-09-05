//! Failures of the multi-PHP operations.

use crate::safe_write::SafeWriteError;

/// What can go wrong while listing, installing, or configuring a PHP version.
///
/// One exhaustive list for the whole area, so a caller reads what PHP work can
/// fail on without following a chain of `#[from]`s into other domains
/// (rules/rust.md "Errors"). The tool output carried by several variants is
/// for an operator's log only — the C# side decides what a hosting customer is
/// told (rules/security.md, role-aware errors).
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum PhpOpError {
    /// The version parses as a PHP version but is not one this panel runs.
    ///
    /// The supported set is closed (spec §11): what the agent installs is not
    /// the caller's choice, so `9.9` is refused HERE and never reaches a
    /// package manager. Handing an unknown name to `apt-get install` would
    /// make the caller the author of the agent's package list.
    #[error("PHP {version} is not a supported version")]
    UnsupportedVersion {
        /// The version that was asked for.
        version: String,
    },

    /// The version is supported but is not installed on this host.
    ///
    /// Reported rather than installed on the spot: installing takes minutes
    /// and streams progress, so it is its own operation with its own rpc, and
    /// a site cannot be pointed at a pool that does not exist yet.
    #[error("PHP {version} is not installed on this host")]
    PhpVersionNotInstalled {
        /// The version that was asked for.
        version: String,
    },

    /// A php.ini setting the customer asked to change is not on the whitelist.
    ///
    /// Refused, never dropped: silently discarding an override means a
    /// customer sets a value, sees no error, and gets behaviour they did not
    /// ask for — and would never learn why.
    #[error("`{name}` is not a setting a customer may change")]
    OverrideNotAllowed {
        /// The setting name that was offered.
        name: String,
    },

    /// The value is not the shape the setting takes — `12x` for a byte size,
    /// a non-number for a count, a path for a timezone.
    #[error("`{value}` is not a valid value for `{name}`")]
    OverrideMalformed {
        /// The setting being set.
        name: String,
        /// The value that was offered.
        value: String,
    },

    /// The value is well-formed but exceeds the bound the whitelist sets.
    ///
    /// The bounds are the point of the whitelist: an unbounded
    /// `memory_limit` on a shared host is one account taking the machine
    /// down for every other account on it.
    #[error("`{value}` exceeds the maximum of {maximum} allowed for `{name}`")]
    OverrideOutOfRange {
        /// The setting being set.
        name: String,
        /// The value that was offered.
        value: String,
        /// The largest value the whitelist permits, in the setting's own
        /// unit.
        maximum: u64,
    },

    /// The value contains a newline, a carriage return, or another control
    /// character.
    ///
    /// The config-injection rule (rules/security.md §4) in a second file
    /// format: `pool.conf` is line-oriented exactly as an nginx vhost is, and
    /// one embedded newline turns `php_value[x] = y` into that line plus a
    /// directive of the customer's choosing — including a `php_admin_value`
    /// undoing the pool's own hardening.
    #[error("the value of `{name}` cannot contain a control character")]
    OverrideControlCharacter {
        /// The setting whose value was refused.
        name: String,
    },

    /// The worker budget the panel supplied is outside the range the agent
    /// will render.
    ///
    /// Refused rather than clamped. Clamping would stop the denial of service
    /// and then silently write a pool that does not match the plan the
    /// customer is paying for — a caller sets a value, sees success, and gets
    /// behaviour it did not ask for, which is the failure
    /// [`Self::OverrideNotAllowed`] exists to prevent for customer settings
    /// and which is worse here, because nobody chose this number by hand.
    #[error("a worker budget of {requested} is outside {minimum}..={maximum}")]
    WorkerBudgetOutOfRange {
        /// What the panel asked for.
        requested: u32,
        /// The smallest budget the agent will render.
        minimum: u32,
        /// The largest budget the agent will render.
        maximum: u32,
    },

    /// The pool file could not be rendered from its template.
    #[error("failed to render the php-fpm pool: {reason}")]
    Render {
        /// What the renderer reported.
        reason: String,
    },

    /// The rendered pool failed `php-fpm -t`; the previous pool was restored.
    #[error("php-fpm validation failed: {stderr}")]
    PoolValidation {
        /// The validator's standard error, for an operator's log.
        stderr: String,
    },

    /// The pool was valid but php-fpm refused to reload it; the previous pool
    /// was restored.
    #[error("php-fpm reload failed: {stderr}")]
    ReloadFailed {
        /// The reload command's standard error, for an operator's log.
        stderr: String,
    },

    /// The pool file could not be written or swapped in — every failure of the
    /// write protocol that is not a validation or reload refusal.
    #[error("failed to write the php-fpm pool: {reason}")]
    ConfigWrite {
        /// What the write protocol reported, including whether the previous
        /// content was restored.
        reason: String,
    },

    /// The package manager could not be run, or refused the installation.
    #[error("the package manager failed: {stderr}")]
    PackageManager {
        /// The package manager's standard error, for an operator's log.
        stderr: String,
    },

    /// The php-fpm service for this version could not be enabled or started.
    #[error("could not enable the php-fpm service: {stderr}")]
    ServiceEnable {
        /// The service manager's standard error, for an operator's log.
        stderr: String,
    },
}

impl From<SafeWriteError> for PhpOpError {
    /// Folds the config-write protocol's failures into this area's list.
    ///
    /// The two failures an operator acts on differently — php-fpm refusing the
    /// content, and php-fpm refusing to reload valid content — keep their own
    /// variants and their tool output; everything else collapses into
    /// [`PhpOpError::ConfigWrite`], whose `Display` still carries the original
    /// chain, including a rollback that did not happen.
    fn from(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::PoolValidation { stderr },
            SafeWriteError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}
