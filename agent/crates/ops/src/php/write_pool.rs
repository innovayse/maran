//! WritePool: one php-fpm pool per account per version, through the one
//! config-write path.

use maran_distro::DistroAdapter;
use maran_templates::php_fpm::pool::Pool;
use maran_templates::php_fpm::pool_override::PoolOverride;

use crate::php::list_php_versions::is_installed;
use crate::php::model::pool_input::PoolInput;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::supported_versions::ensure_supported;
use crate::php::{PhpHost, PhpOpError};

/// The argument that makes php-fpm check its configuration instead of serving.
pub(crate) const VALIDATE_ARGUMENT: &str = "-t";

/// The subcommand that makes the service manager re-read a configuration.
pub(crate) const RELOAD_SUBCOMMAND: &str = "reload";

/// The smallest pool: one worker, and the spare bounds that still make sense
/// around it.
const MINIMUM_WORKERS: u32 = 1;

/// The largest worker budget the agent will render, whatever the panel asks
/// for.
///
/// `max_children` arrives from the caller, and the caller is re-validated here
/// like every other input (rules/security.md item 1). Without a ceiling a bug
/// in the panel renders `pm.max_children = 4294967295`, which php-fpm accepts
/// — it is a fork budget for the whole machine, taken by one account. Every
/// other number in this file is bounded; this is the one that can take the
/// host down, so it is bounded hardest.
///
/// A budget outside `MINIMUM_WORKERS..=MAXIMUM_WORKERS` is REFUSED, not
/// clamped. Clamping would stop the denial of service and then silently write
/// a pool that does not match the plan the customer is paying for — the exact
/// failure `PhpOverride`'s refuse-don't-drop rule exists to prevent, and worse
/// here, because nobody chose this number by hand. A plan carrying zero
/// workers or `u32::MAX` is a panel bug, and refusing surfaces it at the one
/// moment somebody is looking at it.
const MAXIMUM_WORKERS: u32 = 256;

/// Mode of the agent's socket directory: traversable by everyone, writable
/// only by root.
///
/// Explicit rather than inherited from the umask. World-writable and
/// non-sticky, which is what a umask of zero would produce, lets one account
/// unlink a neighbour's socket and bind its own in its place — every request
/// for the neighbour's sites then reaches the wrong customer's PHP.
const SOCKET_DIRECTORY_MODE: u32 = 0o755;

/// Mode of the account's session and upload directories: the account, and
/// nobody else at all.
///
/// Tighter than the socket directory's, and for a sharper reason. **A PHP
/// session filename IS the session ID** — `sess_<id>` — so a world-listable
/// session directory hands a live session to anyone who can run `ls` in it,
/// without a byte of file content being read. Left to the forked child's
/// inherited umask this is typically `0755`, and the cross-tenant hole closed
/// by moving sessions out of `/tmp` would simply reopen through the directory
/// listing. Upload staging gets the same mode: an in-flight upload is as much
/// the customer's as a session is.
const ACCOUNT_DIRECTORY_MODE: u32 = 0o700;

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

/// Writes the php-fpm pool for one account at one version.
///
/// The pool is a system configuration file like any other, so it takes the one
/// path a configuration may take: render → temporary file in the pool
/// directory → `fsync` → atomic rename → `php-fpm -t` → reload → restore the
/// previous pool if either refuses (rules/rust.md "Config writes"). Both the
/// validator binary and the service name are per-version and come from the
/// [`DistroAdapter`]: validating with 8.3's binary a pool that 8.1's master
/// will read proves nothing about the file that was written.
///
/// Idempotent by construction rather than by a check: the rendered text is a
/// function of the input, so writing the same pool twice writes the same
/// bytes. It is written and reloaded again anyway, deliberately — a retry
/// after a timeout may be a retry of a write that never landed, and the
/// alternative, comparing against what is on disk, would skip the reload a
/// half-finished first attempt still needs.
///
/// # Errors
///
/// Returns [`PhpOpError::UnsupportedVersion`] for a version outside the
/// supported set and [`PhpOpError::PhpVersionNotInstalled`] when the version
/// is supported but absent — writing into a pool directory that does not exist
/// would fail at the temporary file with a much less useful message. Returns
/// [`PhpOpError::Render`] when the template fails,
/// [`PhpOpError::PoolValidation`] when `php-fpm -t` rejects the result — with
/// the previous pool restored — [`PhpOpError::ReloadFailed`] when the reload
/// refuses it, and [`PhpOpError::ConfigWrite`] for any other failure of the
/// protocol, including the creation of the socket directory.
pub fn write_pool(
    host: &dyn PhpHost,
    distro: &dyn DistroAdapter,
    input: &PoolInput,
) -> Result<(), PhpOpError> {
    ensure_supported(&input.version)?;
    ensure_worker_budget(input.max_children)?;

    if !is_installed(host, distro, &input.version) {
        return Err(PhpOpError::PhpVersionNotInstalled {
            version: input.version.as_str().to_owned(),
        });
    }

    let paths = PoolPaths::for_pool(distro, &input.account, &input.version);

    // php-fpm cannot create the directory its socket lives in, only the socket
    // itself, and this one is under `/run` — so it is gone after every reboot
    // and has to be ensured on every write rather than once at install time.
    host.create_directory(&paths.socket_directory, SOCKET_DIRECTORY_MODE)?;

    // The session and upload directories the pool points PHP at, created AS
    // THE ACCOUNT and before the pool that names them. Both facts matter: a
    // root-owned session directory is unwritable by the pool's workers, which
    // is the precise condition that makes PHP fall back to the shared,
    // world-readable /tmp — the fallback this pool exists to close.
    host.create_directories_as_account(
        &input.account,
        &[&paths.session_directory, &paths.upload_temporary_directory],
        ACCOUNT_DIRECTORY_MODE,
    )?;

    let contents = render_pool(distro, input, &paths)?;

    let validator_program = distro.php_fpm_binary(input.version.as_str());
    let validator = crate::safe_write::model::Validator {
        program: &validator_program,
        arguments: &[VALIDATE_ARGUMENT],
    };
    let service = distro.php_fpm_service(input.version.as_str());
    let reload_arguments = [RELOAD_SUBCOMMAND, service.as_str()];
    let reload = crate::safe_write::model::Reload {
        // The absolute path of the service manager, from the adapter. `ops`
        // names no binary path of its own — and a bare `"systemctl"` written
        // to get past that rule would be a program a root process resolves
        // through `PATH`, which is worse than the literal it replaced.
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    host.write_config(&paths.config_path, &contents, &validator, &reload)
}

/// Renders the pool file for `input`.
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
fn render_pool(
    distro: &dyn DistroAdapter,
    input: &PoolInput,
    paths: &PoolPaths,
) -> Result<String, PhpOpError> {
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
        overrides: &overrides,
    }
    .render_config()
    .map_err(|error| PhpOpError::Render {
        reason: error.to_string(),
    })
}

/// Refuses a worker budget outside the range the agent will render.
///
/// # Errors
///
/// Returns [`PhpOpError::WorkerBudgetOutOfRange`], carrying both bounds so an
/// operator's log says what the range was and not only that it was missed.
fn ensure_worker_budget(requested: u32) -> Result<(), PhpOpError> {
    if (MINIMUM_WORKERS..=MAXIMUM_WORKERS).contains(&requested) {
        return Ok(());
    }

    Err(PhpOpError::WorkerBudgetOutOfRange {
        requested,
        minimum: MINIMUM_WORKERS,
        maximum: MAXIMUM_WORKERS,
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
#[path = "../tests/php/write_pool_tests.rs"]
mod tests;
