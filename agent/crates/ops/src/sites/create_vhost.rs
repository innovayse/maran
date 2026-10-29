//! Handing a NEW site's vhost to the creation half of the config-write
//! protocol, refusing any host name another vhost already serves.

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::DistroAdapter;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::write_vhost::{RELOAD_SUBCOMMAND, VALIDATE_ARGUMENT};
use crate::sites::{SiteHost, SitesOpError};
use maran_agent_core::end_of_options::END_OF_OPTIONS;

/// The words nginx prints, at `warn`, when a server block names a host that a
/// server block it read earlier on the same listen address already names —
/// immediately followed by the later name, in double quotes.
///
/// Measured on Ubuntu 24.04's packaged nginx 1.24.0, against a tree in which
/// two vhosts named `alice.test`: `[warn] conflicting server name "alice.test"
/// on 0.0.0.0:80, ignored` on standard error, then `test is successful` and
/// exit status 0 (issue #85). For the other versions the supported families
/// ship, the claim is READ, not measured: `ngx_http_server_names` logs this
/// text at `NGX_LOG_WARN` and carries on in every release from 1.18.0 to
/// 1.26.3, and during `-t` it is written by the startup log, whose level the
/// `error_log` directive does not govern, so a quieter `error_log` cannot hide
/// it. What precedes the fragment differs with where that log points — `nginx:
/// [warn] ` when it is a file, a timestamp and a process id when it is standard
/// error itself — and the fragment does not, which is why the match starts here
/// and not at the severity. It is nginx's own text rather than a fact of either
/// family, so it lives beside the `-t` argument it is read from and not in the
/// distro adapter.
const CONFLICTING_SERVER_NAME_WARNING: &str = "conflicting server name";

/// Creates the vhost at `target` for a site serving `served_names`, refusing
/// when another vhost on this host already serves any of them.
///
/// The validator and the reload are built exactly as
/// [`super::write_vhost::write_vhost`] builds them, from the same constants
/// and the same [`DistroAdapter`] methods. What differs is the protocol half:
/// a creation never replaces an existing file, and it refuses the `nginx -t`
/// warning that names one of `served_names` as served twice — the warning
/// nginx prints, and then exits 0, when it is about to answer that host from
/// whichever file the include glob sorts first.
///
/// That warning is the check, rather than a scan of the other vhosts before the
/// write, for two reasons. It is decided inside the config-write protocol's
/// critical section, against the tree that already holds the new file, so two
/// creations racing for one name are serialised and the second one sees the
/// first; a scan made before the protocol is entered would be a check-then-act
/// with the act out of reach of the lock, which is the race this refusal
/// exists to close. And it is nginx's own answer about every file it loads —
/// the panel's vhost, the distribution's default site, anything an operator
/// placed in `conf.d` — under nginx's own rules for which names are the same,
/// where a scan would cover only the agent's directory and would be a second
/// parser of a grammar nginx already owns.
///
/// Each warning is matched with the host in its quotes, so `shop.example`
/// does not match a warning about `shop.example.org`. A host name cannot
/// contain a double quote — `Domain` admits letters, digits, `-` and `.` and
/// lowercases them, and nginx lowercases server names too — so the quoted form
/// is exact. Each name is refused in a second form as well, with a leading dot:
/// nginx reads `server_name .shop.example` as `shop.example` plus every
/// subdomain, and when such a block is read AFTER this site's, the warning
/// quotes the later key — `".shop.example"` — and this site wins the bare name
/// from the file that held it. Read earlier, the same block makes the warning
/// quote this site's own name, which the first form matches. The agent never
/// renders a leading dot, so that second form can only come from a file
/// somebody else placed in the tree.
///
/// Only the site's own names refuse: a conflict elsewhere in the tree between
/// two names this site does not serve is somebody else's defect, and refusing
/// every creation on the host over it would turn one bad file into a host on
/// which no site can be created.
///
/// What nginx does not warn about, this does not refuse. A wildcard
/// (`*.shop.example`) or a regular expression held by another file never
/// conflicts with an exact name — nginx prefers the exact one by its
/// documented precedence, whatever the files' order — and neither does a name
/// another file serves on a different listen address, because names are
/// compared per address. Both are decided by rules rather than by which file
/// sorts first, so neither is the takeover this refusal exists for.
///
/// # Errors
///
/// Returns [`SitesOpError::ServerNameTaken`] when a vhost already exists at
/// `target` or when `nginx -t` reports one of `served_names` as served twice —
/// with nothing left at `target` in either case. Otherwise as
/// [`super::write_vhost::write_vhost`]: [`SitesOpError::NginxValidation`],
/// [`SitesOpError::ReloadFailed`], [`SitesOpError::Bound`] (a program killed at
/// its deadline, which is no verdict on the vhost) or [`SitesOpError::ConfigWrite`],
/// with the new vhost removed again.
pub(crate) fn create_vhost(
    host: &dyn SiteHost,
    distro: &dyn DistroAdapter,
    target: &Path,
    contents: &str,
    served_names: &[&str],
) -> Result<(), SitesOpError> {
    let nginx = distro.nginx_binary();
    let service_manager = distro.service_manager();
    // A pure check, so the short class: `nginx -t` reads the tree and changes nothing.
    let validator = Validator {
        program: &nginx,
        arguments: &[VALIDATE_ARGUMENT],
        class: CallClass::Validate,
    };
    // `--` before the unit: `systemctl` reads its options with `getopt_long`, which ends
    // option parsing at `--`. The program is the adapter's absolute path, and a bare
    // `"systemctl"` would be worse than a literal, because a root process would then
    // resolve it through `PATH`.
    let reload_arguments = [RELOAD_SUBCOMMAND, END_OF_OPTIONS, distro.nginx_service()];
    let reload = Reload {
        program: &service_manager,
        arguments: &reload_arguments,
    };
    let refused_warnings: Vec<String> = served_names
        .iter()
        .flat_map(|name| {
            [
                format!("{CONFLICTING_SERVER_NAME_WARNING} \"{name}\""),
                format!("{CONFLICTING_SERVER_NAME_WARNING} \".{name}\""),
            ]
        })
        .collect();

    host.create_config(target, contents, &validator, &reload, &refused_warnings)
}

#[cfg(test)]
#[path = "../tests/sites/create_vhost_tests.rs"]
mod tests;
