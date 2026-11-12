//! A Node.js module stream of the RHEL family, safe to hand to the package manager and to a unit.

use crate::validation::web::node_major::NodeMajor;

/// What a module spec starts with: the module's name and the separator before the stream.
const MODULE_PREFIX: &str = "nodejs:";

/// What a module spec ends with: the profile the panel always installs. `common` carries the
/// interpreter and `npm`, which is what a site's unit runs.
const PROFILE_SUFFIX: &str = "/common";

/// One stream of the `nodejs` module (`nodejs:20`) with the `common` profile, carried as a type.
///
/// On the RHEL family a Node.js major is not a package but a stream of an AppStream module,
/// installed as `dnf module install nodejs:20/common`. The spec has a `:` and a `/`, neither of
/// which is valid in a systemd unit instance name, so the unit the agent starts is given the
/// DIGITS ([`Self::instance`]) and spells the spec itself; the spec ([`Self::spec`]) is what the
/// agent hands to the dry run of a removal. Both are built from a [`NodeMajor`], a member of the
/// closed catalogue, so no caller-supplied text can reach either: there is no constructor from a
/// string.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NodejsModuleStream(NodeMajor);

impl NodejsModuleStream {
    /// The stream that carries Node.js `major`.
    #[must_use]
    pub fn from_major(major: NodeMajor) -> Self {
        Self(major)
    }

    /// The Node.js major the stream carries.
    #[must_use]
    pub fn major(&self) -> NodeMajor {
        self.0
    }

    /// The digits of the stream — `20` — which are the whole instance of the
    /// `maran-app-nodejs-install@` and `maran-app-nodejs-remove@` units.
    #[must_use]
    pub fn instance(&self) -> &'static str {
        self.0.as_str()
    }

    /// The module spec the package manager reads — `nodejs:20/common`.
    #[must_use]
    pub fn spec(&self) -> String {
        format!("{MODULE_PREFIX}{}{PROFILE_SUFFIX}", self.0.as_str())
    }
}

#[cfg(test)]
#[path = "../../tests/validation/apps/nodejs_module_stream_tests.rs"]
mod tests;
