//! Rendering one account's php-fpm pool file.

use maran_distro::DistroAdapter;
use maran_templates::php_fpm::pool::Pool;
use maran_templates::php_fpm::pool_override::PoolOverride;

use crate::php::PhpOpError;
use crate::php::model::php_override::{DEFAULT_SIZE, PhpOverride};
use crate::php::model::pool_input::PoolInput;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::write_pool::MINIMUM_WORKERS;

/// The execution limit assumed when the customer has set none.
///
/// The same 300 seconds the whitelist bounds `max_execution_time` by, since
/// that is the effective limit for a customer who never touched the setting.
const DEFAULT_EXECUTION_LIMIT: u32 = 300;

/// Seconds php-fpm's own timer is given ON TOP of the execution limit.
///
/// A margin, not a rounding. At equal values the two timers fire at the same
/// instant for an ordinary CPU-bound request that legitimately runs to its
/// limit, and php-fpm wins the race: the worker is killed, so PHP never raises
/// its fatal error and the customer gets no stack trace and nothing in their
/// error log to explain the blank page. With a margin, PHP always reports
/// first and fpm's timer only ever fires for the case it was added for — a
/// request blocked in a syscall, which PHP's timer does not count at all.
const REQUEST_TIMEOUT_MARGIN: u32 = 30;

/// The whitelisted setting this pool takes its hard request timeout from.
const EXECUTION_TIME_SETTING: &str = "max_execution_time";

/// Divisor turning the worker budget into the idle floor.
const IDLE_FLOOR_DIVISOR: u32 = 8;

/// Divisor turning the worker budget into the idle ceiling.
const IDLE_CEILING_DIVISOR: u32 = 3;

/// Renders the pool file for `input`.
///
/// The ONE renderer of a pool: `write_pool` writes its result and
/// `sites::converge_php_site` compares its result with the file on disk, so the
/// bytes that are compared are by construction the bytes that would be written.
/// Pure: it touches no file and spawns nothing.
///
/// The overrides reach the template as [`PoolOverride`]s built from
/// [`crate::php::model::php_override::PhpOverride`]s, which is the only way
/// one can be made — so every name here came from the whitelist and every
/// value has been through its bound and the control-character check. The
/// template escapes nothing by design, and this is the seam where that is
/// safe or is not (rules/rust.md "Validation first").
///
/// The three hardening lines the template writes above these — `open_basedir`,
/// `disable_functions` and `cgi.fix_pathinfo` — are `php_admin_value`, which
/// `php_value` cannot countermand at any position in the file. That is what
/// makes rendering customer settings after them safe rather than merely
/// conventional.
///
/// # Errors
///
/// Returns [`PhpOpError::Render`] when the template fails, which can only
/// happen if the template and its render type have drifted apart.
pub(crate) fn render_pool(
    distro: &dyn DistroAdapter,
    input: &PoolInput,
    paths: &PoolPaths,
) -> Result<String, PhpOpError> {
    // The panel's copy of this check is advice; this is the one that decides,
    // and it sits here because `PoolInput` is built by several callers.
    PhpOverride::ensure_consistent(&input.overrides)?;

    let socket_path = paths.socket_path.display().to_string();
    let home_directory = paths.home_directory.display().to_string();
    let session_directory = paths.session_directory.display().to_string();
    let upload_temporary_directory = paths.upload_temporary_directory.display().to_string();
    let overrides: Vec<PoolOverride<'_>> = input
        .overrides
        .iter()
        .map(|item| PoolOverride {
            name: item.name(),
            value: item.value(),
        })
        .collect();

    let max_children = input.max_children;
    let min_spare_servers = (max_children / IDLE_FLOOR_DIVISOR).max(MINIMUM_WORKERS);
    let max_spare_servers = (max_children / IDLE_CEILING_DIVISOR).max(min_spare_servers);

    Pool {
        pool_name: &paths.pool_name,
        account: input.account.as_str(),
        socket_path: &socket_path,
        web_server_user: distro.web_server_user(),
        max_children,
        // Halfway between the idle bounds: starting at the floor makes every
        // morning's first visitors wait for workers to fork, and starting at
        // the ceiling has php-fpm immediately kill what it just started.
        start_servers: min_spare_servers + (max_spare_servers - min_spare_servers) / 2,
        min_spare_servers,
        max_spare_servers,
        home_directory: &home_directory,
        session_directory: &session_directory,
        upload_temporary_directory: &upload_temporary_directory,
        request_terminate_timeout: request_timeout(input),
        default_size: DEFAULT_SIZE,
        overrides: &overrides,
    }
    .render_config()
    .map_err(|error| PhpOpError::Render {
        reason: error.to_string(),
    })
}

/// The hard request timeout for this pool.
///
/// The customer's own `max_execution_time` when they set one — it is already
/// bounded to 1..=300 by the whitelist, so it can only ever lower this — and
/// the default otherwise, plus [`REQUEST_TIMEOUT_MARGIN`]. A customer who
/// asked for short requests gets a worker reclaimed on their own schedule
/// rather than five minutes later, and always after PHP has had its say.
fn request_timeout(input: &PoolInput) -> u32 {
    let execution_limit = input
        .overrides
        .iter()
        .find(|item| item.name() == EXECUTION_TIME_SETTING)
        // The value is digits only and at most 300, proven by
        // `PhpOverride::parse`; the fallback is unreachable and is the default
        // rather than a panic, because a pool that renders is better than a
        // daemon that does not.
        .and_then(|item| item.value().parse().ok())
        .unwrap_or(DEFAULT_EXECUTION_LIMIT);

    execution_limit.saturating_add(REQUEST_TIMEOUT_MARGIN)
}

#[cfg(test)]
#[path = "../tests/php/render_pool_tests.rs"]
mod tests;
