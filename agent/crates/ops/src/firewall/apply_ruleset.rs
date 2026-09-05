//! The one sequence by which a rendered nftables file becomes the live one.

use std::path::Path;

use maran_distro::DistroAdapter;

use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;

/// Asks `nft` to parse and type-check a file without loading any of it.
const CHECK: &str = "--check";

/// Tells `nft` to read its input from the file named next.
const FILE: &str = "-f";

/// Writes `contents` to `target` and loads it into the kernel, in the one
/// order that cannot leave the host worse off than it was.
///
/// # The order, and why it is this one
///
/// 1. **Stage.** The rendered text is written to a temporary file in
///    `target`'s own directory. Nothing at `target` has changed.
/// 2. **Check.** `nft --check -f <staged>` parses and type-checks the staged
///    file and loads nothing. A refusal returns
///    [`FirewallError::RuleRefusedByNft`] with `nft`'s own standard error, and
///    the LIVE ruleset is untouched — the real path still holds the previous
///    content, and the kernel still holds the previous rules.
/// 3. **Flush the file.** Only now is the staged file `fsync`ed — after the
///    check rather than before, which saves a flush on a ruleset that is about
///    to be thrown away. Doing it before the rename is what stops a crash from
///    leaving a directory entry pointing at data that never reached the disk.
/// 4. **Rename.** The staged file is renamed over `target`, atomically. The
///    file either changes completely or not at all; nothing can read a
///    half-written ruleset.
/// 5. **Flush the directory.** `fsync` on the directory holding `target`,
///    **after** the rename and not before. This is the step that makes the
///    RENAME durable, and it is a different job from step 3: step 3 makes the
///    file's contents survive a crash, this makes the directory entry that
///    publishes them survive one. Flushing the directory before the rename
///    would flush a state that predates the new entry and would do nothing at
///    all — and the window it leaves open is not academic here, because a
///    crash inside it resolves the path back to the OLD inode, so
///    `nftables.service` re-reads the previous ruleset at boot and a
///    `deny_port` that reported success silently re-opens its port.
/// 6. **Load.** `nft -f <target>` applies the file that is now really there,
///    so what runs is what a reboot would re-read.
///
/// This is deliberately NOT `ops::safe_write`'s protocol, and the difference
/// is the position of the check. That protocol renames BEFORE it validates,
/// because its validators read the real configuration tree by path — `nginx
/// -t` parses `nginx.conf` and everything its includes glob in, and a
/// temporary file matches no glob. `nft --check -f <file>` is the opposite: it
/// reads exactly the file it is given and nothing else, so checking the staged
/// file is not merely possible but strictly better — a refusal never touches
/// the live path at all, and there is nothing to roll back afterwards.
///
/// # The replace idiom is what makes this converge
///
/// Loading a file with `nft -f` is ADDITIVE. Measured on nftables v1.0.9:
/// re-applying a re-rendered ruleset with the 3306 rule removed leaves
/// `3306 rules: 0, loopback rules: 1` when the rendered file carries the
/// create-delete-redeclare idiom, and `3306 rules: 1, loopback rules: 2` when
/// it does not — the removed rule stays LIVE and every other rule is
/// duplicated. So a deny only really removes because the file this function
/// loads deletes its own table first. That property lives in the template and
/// is guarded on the way in by
/// [`RulesetState::parse`](crate::firewall::model::ruleset_state::RulesetState::parse),
/// which refuses to read back a ruleset without it.
///
/// # Errors
///
/// - [`FirewallError::StagingFailed`] when `target` cannot be named to `nft`,
///   or the temporary file cannot be written, flushed or renamed. The live
///   ruleset is untouched for every one of those, because all of them happen
///   before the rename. The one failure that can arrive after it is the
///   directory flush at step 5: the file at `target` is then the complete new
///   one and the kernel still holds the old rules, which the same operation
///   retried converges on.
/// - [`FirewallError::RuleRefusedByNft`] when `nft --check` rejects the
///   rendered file, carrying its standard error. The live ruleset is
///   untouched.
/// - [`FirewallError::NftFailed`] when `nft` cannot be started, or when the
///   load of an already-checked file fails anyway. The file at `target` is
///   the new one in that case — it passed the check and the rename happened —
///   so a retry of the same operation converges rather than needing a
///   cleanup.
pub(crate) fn apply_ruleset(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    target: &Path,
    contents: &str,
) -> Result<(), FirewallError> {
    // Read before anything is staged, so that a target this agent cannot name
    // to `nft` is refused while the live ruleset is still untouched. Checking
    // it after the rename would return a "nothing was touched" error at a
    // point where the file had already been replaced.
    let Some(target_path) = target.to_str() else {
        return Err(FirewallError::StagingFailed);
    };

    let staged = host.stage_file(target, contents)?;

    let Some(staged_path) = staged.to_str() else {
        host.discard_file(&staged);

        return Err(FirewallError::StagingFailed);
    };

    let checked = match host.run(distro.nft_binary(), &[CHECK, FILE, staged_path]) {
        Ok(outcome) => outcome,
        Err(error) => {
            host.discard_file(&staged);

            return Err(error);
        }
    };
    if checked.status != 0 {
        host.discard_file(&staged);

        return Err(FirewallError::RuleRefusedByNft {
            stderr: checked.stderr,
        });
    }

    if let Err(error) = host.sync_file(&staged) {
        host.discard_file(&staged);

        return Err(error);
    }

    if let Err(error) = host.commit_file(&staged, target) {
        host.discard_file(&staged);

        return Err(error);
    }

    // AFTER the rename, and that is the whole point of it being its own step:
    // flushing the directory beforehand would flush a state that predates the
    // new entry and would do nothing for the rename's durability.
    host.sync_directory(target)?;

    let loaded = host.run(distro.nft_binary(), &[FILE, target_path])?;
    if loaded.status != 0 {
        return Err(FirewallError::NftFailed {
            stderr: loaded.stderr,
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/firewall/apply_ruleset_tests.rs"]
mod tests;
