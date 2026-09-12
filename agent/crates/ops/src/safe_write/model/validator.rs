//! The command that checks a rendered configuration before it is swapped in.

/// The command [`super::super::render_validate_swap::write_config`] runs
/// against the freshly written temporary file to decide whether it is safe
/// to put in place — `nginx -t`, `php-fpm -t`, `crontab -T`, as the area
/// requires.
///
/// Holds only the argv, never a path: the program and its arguments are
/// facts of the distribution, supplied by the caller from its
/// `DistroAdapter`, and this crate must not guess at either
/// (rules/rust.md "Distro adapter").
/// # A validator MUST be a check, and four of this workspace's twelve are not
///
/// `rules/rust.md` names step 5 "validate", and the examples it gives are all
/// pure: `nginx -t`, `php-fpm -t`, `crontab -T`. A command that CHANGES the
/// running system is not a check, and passing one here has three consequences
/// that the type cannot prevent and so states instead. It makes the protocol's
/// rollback fire on an action that has already happened rather than on a verdict
/// about bytes. It makes the write non-idempotent in the daemon's terms — the
/// FTPS one restarts the daemon and drops every live session — where a check
/// may be repeated freely. And it makes the tree unobservable afterwards: a
/// startup pass that re-ran the validator to ask "is what is on disk valid?"
/// would restart a production daemon to ask a question, which is why
/// `reconcile_web_tree` and `reconcile_php_pool_trees` in the daemon cover the
/// nginx and php-fpm trees and nothing covers the other three.
///
/// Four construction sites in this crate pass `distro.service_manager()` as the
/// `program`, which is the mechanical signature of an action: the SFTP jail
/// mount unit and the FTPS jail mount unit pass `systemctl daemon-reload`,
/// because a systemd unit file has no check-without-load, and `vsftpd.conf`
/// passes `systemctl restart` at two sites, because `vsftpd` has no `-t` mode at
/// all. `validator_tests.rs` DISCOVERS every site from this crate's own source
/// and asserts the list, so the count above cannot go stale and a fifth cannot
/// appear unremarked. Closing it needs a pure validator per tree, which does not
/// exist behind today's `DistroAdapter`.
///
/// The ruling on those three, so a reader does not have to re-derive it: they are
/// NOT re-validated at a start, and the reason is that they have no observation
/// available rather than that nobody wrote one. Running the only command there is
/// in order to learn whether the file parses would drop every live FTPS session
/// on the host, at every agent restart, whether or not anything was ever
/// interrupted — paying a certain, universal cost for an uncertain, rare benefit,
/// and turning a startup reconciler into a startup actor. So the gap is declared
/// where a reader hits it instead: here, in
/// [`super::super::render_validate_swap::write_config`]'s midpoint section, and
/// in `agent::server::reconcile_web_tree`'s doc comment AND log line, so an
/// operator reconciling a host after a hard kill reads which trees were checked
/// and which were not. What would close it properly is a pure check per tree:
/// `systemd-analyze verify <unit>`, which does parse a unit without loading it
/// and would be one new `DistroAdapter` method, and for `vsftpd` nothing at all —
/// it would need a parser written here.
pub struct Validator<'a> {
    /// Absolute path of the validating binary.
    pub program: &'a str,
    /// Arguments passed to `program`, as an argv array — never a shell string.
    pub arguments: &'a [&'a str],
}

#[cfg(test)]
#[path = "../../tests/safe_write/validator_tests.rs"]
mod tests;
