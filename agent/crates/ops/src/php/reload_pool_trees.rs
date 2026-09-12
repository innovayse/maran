//! Validating every installed version's php-fpm pool tree on disk, and
//! reloading the services that accept theirs — writing nothing.

use maran_distro::DistroAdapter;

use crate::php::model::pool_tree_decision::PoolTreeDecision;
use crate::php::model::pool_tree_outcome::PoolTreeOutcome;
use crate::php::write_pool::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::php::{PhpHost, PhpOpError, list_php_versions};
use crate::safe_write::model::{Reload, Validator};

/// Validates the pool tree of every installed PHP version and reloads the
/// services whose trees pass.
///
/// This is the write protocol of [`super::write_pool`] with the write taken
/// out: the same `php-fpm<version> -t` and the same `systemctl reload
/// php<version>-fpm`, both from the [`DistroAdapter`], so a pass over the tree
/// cannot check a different binary or poke a different service than the one
/// every individual pool write already used.
///
/// # Why it exists
///
/// `ops::safe_write` renames a new pool file over the live target BEFORE
/// validating it, deliberately: `php-fpm -t` reads the pool directory by path
/// and a `.tmpXXXXXX` file matches no `include` glob there any more than it
/// does under nginx. The cost is an on-disk midpoint. A process killed between
/// that rename and the commit leaves a pool file no validator has accepted at
/// the real path, with the `RollbackGuard` that would have undone it gone with
/// the process — and the running php-fpm unaffected, because it has not been
/// asked to reload. Nothing on the host looked until this existed: the next
/// reload from any cause was the first to find out, and at a restart that is
/// every site on the host that uses that version failing at once, because
/// php-fpm refuses to start on a pool file it cannot parse.
///
/// Reloading a tree that PASSES is the deliberate half rather than an extra.
/// It is the only thing that closes the case where the killed write left VALID
/// content the daemon never loaded — a pool whose worker budget, `open_basedir`
/// or session directory the panel believes it changed and php-fpm is still
/// serving the old value for.
///
/// # Why one outcome per version
///
/// The trees are separate: each version's binary reads only its own pool
/// directory. A single verdict for the host would report one version's broken
/// file as if it said something about the others, and would not name the
/// version an operator has to go and fix.
///
/// # Enumeration, and what an empty answer means
///
/// The installed versions come from [`list_php_versions`], which is the same
/// question — and the same host method — every pool write asks, so a version
/// this pass skips is a version nothing else believes is installed either. A
/// host with no PHP installed yields an empty vector, which is the honest
/// answer and not a failure: there is no pool tree to observe.
///
/// # Errors
///
/// None. Every refusal is reported per version in the returned
/// [`PoolTreeOutcome`]s rather than as a failure of the pass, because one
/// version's broken tree must not stop the others being observed — the same
/// reason the nightly backup sweep encloses each account. The enumeration's own
/// fallible signature is folded the same way: a host that cannot be asked which
/// versions it has yields no outcomes.
#[must_use]
pub fn reload_pool_trees(host: &dyn PhpHost, distro: &dyn DistroAdapter) -> Vec<PoolTreeOutcome> {
    let Ok(installed) = list_php_versions(host, distro) else {
        return Vec::new();
    };

    installed
        .into_iter()
        .map(|version| reload_one(host, distro, version.version))
        .collect()
}

/// Validates one version's pool tree and reloads its service if it passes.
///
/// Split out so the loop above reads as the enumeration it is, and so the
/// argv-building lives beside the single version it describes.
fn reload_one(host: &dyn PhpHost, distro: &dyn DistroAdapter, version: String) -> PoolTreeOutcome {
    let validator_program = distro.php_fpm_binary(&version);
    let validator = Validator {
        program: &validator_program,
        arguments: &[VALIDATE_ARGUMENT],
    };
    let service = distro.php_fpm_service(&version);
    let reload_arguments = [RELOAD_SUBCOMMAND, service.as_str()];
    let reload = Reload {
        // The absolute path of the service manager, from the adapter: `ops`
        // names no binary path of its own, and a bare `"systemctl"` would be a
        // program a root process resolves through `PATH`.
        program: distro.service_manager(),
        arguments: &reload_arguments,
    };

    match host.validate_and_reload(&validator, &reload) {
        Ok(()) => PoolTreeOutcome {
            version,
            decision: PoolTreeDecision::Applied,
            stderr: String::new(),
        },
        Err(PhpOpError::PoolValidation { stderr }) => PoolTreeOutcome {
            version,
            decision: PoolTreeDecision::Refused,
            stderr,
        },
        Err(other) => PoolTreeOutcome {
            version,
            decision: PoolTreeDecision::NotReloaded,
            stderr: other.to_string(),
        },
    }
}

#[cfg(test)]
#[path = "../tests/php/reload_pool_trees_tests.rs"]
mod tests;
