//! What writing a credential file needs to know about the machine.

use maran_distro::AdapterBinary;

/// The web server's group and the program that resolves it.
///
/// The credential directory is `root:<group>` so the web server's workers can
/// read a file the agent writes and nobody else can list it. Both facts come
/// from the distribution adapter (`web_server_group`, `getent_binary`); this
/// operation names no group and no path of its own (rules/rust.md "Distro
/// adapter"). `getent` is an [`AdapterBinary`], a type only the adapter can
/// construct.
pub struct AuthFilePlacement<'a> {
    /// The web server's group, as the adapter names it.
    pub group: &'a str,
    /// The adapter's `getent`, which maps the group's name to its numeric id.
    pub getent: &'a AdapterBinary,
}
