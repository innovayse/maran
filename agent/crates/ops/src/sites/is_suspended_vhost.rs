//! The one question every writer of an enabled vhost has to ask first.

use crate::sites::SitesOpError;
use crate::sites::model::create_site_input::CreateSiteInput;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::render_suspended_vhost::render_suspended_vhost;

/// Whether `contents` is the suspended rendering of this site.
///
/// The rendered file IS this area's record of whether a site is suspended —
/// `disable_site` writes the suspended template over the site's own vhost and
/// `enable_site` writes the site's own back, and there is no marker beside
/// them, on the argument `enable_site` states: a marker can outlive the config
/// it claims to describe. So the question "is this site suspended?" is
/// answered the way `disable_site` decides it has nothing to do — by rendering
/// the suspended text for this site and comparing byte for byte.
///
/// # Why every writer of an ENABLED vhost must ask
///
/// A suspended site's vhost is rewritten by any operation that re-renders the
/// site's own configuration, because those operations render from the input
/// the panel handed them and that input describes the site, not its
/// suspension. Three of them did exactly that:
/// `update_site_php_version`, `ssl::install_certificate` and
/// `ssl::remove_certificate`. A certificate renewal landing on a just-suspended
/// site put the customer's site back on the air, and the panel went on
/// recording it as suspended — a suspension undone by an operation that never
/// mentions suspension, which is why the question is asked by every writer of an
/// enabled vhost rather than remembered by those three.
///
/// The complete set of callers that must ask is derivable rather than
/// remembered: it is the callers of `write_vhost` that render through
/// `render_vhost`, since `render_vhost` is the ONLY producer of an enabled
/// vhost. There are five, and each is accounted for —
/// `create_site` refuses a domain whose vhost already exists, so it can never
/// overwrite a suspended one; `enable_site` un-suspends deliberately, which is
/// what it is for; and the other three call this function.
///
/// # Errors
///
/// Returns [`SitesOpError::Render`] when the suspended text cannot be rendered
/// at all. That is deliberately not folded into a `false`: "I could not tell"
/// answered as "not suspended" is answered as a licence to overwrite the
/// suspension, which is the defect this function exists to close.
pub(crate) fn is_suspended_vhost(
    contents: &str,
    input: &CreateSiteInput,
    paths: &SitePaths,
) -> Result<bool, SitesOpError> {
    let aliases: Vec<String> = input
        .aliases
        .iter()
        .map(|alias| alias.as_str().to_owned())
        .collect();

    let suspended = render_suspended_vhost(input.domain.as_str(), &aliases, paths)?;

    Ok(contents == suspended)
}

#[cfg(test)]
#[path = "../tests/sites/is_suspended_vhost_tests.rs"]
mod tests;
