//! What `--generate-node-identity` was asked to make.

use std::path::PathBuf;

use maran_agent_core::validation::fleet::node_address::NodeAddress;

use super::absolute_directory::absolute_directory;
use super::options_error::OptionsError;

/// The flag that selects the generation mode and carries the directory. First position only.
pub(super) const GENERATE_NODE_IDENTITY_FLAG: &str = "--generate-node-identity";

/// Flag giving the address the certificate names.
const NODE_ADDRESS_FLAG: &str = "--node-address";

/// Flag allowing an identity already in the directory to be replaced.
const REPLACE_FLAG: &str = "--replace";

/// A request to generate this node's fleet identity (issue #180).
///
/// Built by [`super::invocation::Invocation::parse`]; the generation itself is
/// `fleet::generate_node_identity`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NodeIdentityRequest {
    /// The directory to write `node.key` and `node.pem` into. Absolute.
    pub directory: PathBuf,
    /// The address the panel will dial this node on, which the certificate names.
    pub address: NodeAddress,
    /// Whether an identity already in the directory may be replaced.
    ///
    /// `false` unless `--replace` was given: replacing a node's identity cuts it off from the
    /// panel that was approved against the old one, so it is something an operator asks for.
    pub replace: bool,
}

impl NodeIdentityRequest {
    /// Parses what follows `--generate-node-identity`: the directory, then `--node-address <a>`
    /// (required) and `--replace` (optional), each at most once, in any order.
    ///
    /// # Errors
    ///
    /// - [`OptionsError::MissingValue`] when the directory or a flag's value is missing, or
    ///   `--node-address` was never given. There is no default address: a certificate that
    ///   names a guess is a certificate the panel refuses at enrolment.
    /// - [`OptionsError::RelativePath`] when the directory is not absolute.
    /// - [`OptionsError::InvalidNodeAddress`] when the address is not one a node can be dialled
    ///   on.
    /// - [`OptionsError::RepeatedFlag`] when `--node-address` or `--replace` is given twice.
    /// - [`OptionsError::UnknownFlag`] for anything else, so the daemon's own flags
    ///   (`--socket`, `--fleet-listen`) cannot ride along on a generation run.
    pub(super) fn parse(arguments: &[String]) -> Result<Self, OptionsError> {
        let mut remaining = arguments.iter();

        let directory = remaining.next().ok_or(OptionsError::MissingValue {
            flag: GENERATE_NODE_IDENTITY_FLAG,
        })?;
        let directory = absolute_directory(GENERATE_NODE_IDENTITY_FLAG, directory)?;

        let mut address = None;
        let mut replace = false;

        while let Some(argument) = remaining.next() {
            match argument.as_str() {
                NODE_ADDRESS_FLAG => {
                    let value = remaining.next().ok_or(OptionsError::MissingValue {
                        flag: NODE_ADDRESS_FLAG,
                    })?;
                    let parsed = NodeAddress::parse(value)
                        .map_err(|reason| OptionsError::InvalidNodeAddress { reason })?;
                    if address.replace(parsed).is_some() {
                        return Err(OptionsError::RepeatedFlag {
                            flag: NODE_ADDRESS_FLAG,
                        });
                    }
                }
                REPLACE_FLAG => {
                    if replace {
                        return Err(OptionsError::RepeatedFlag { flag: REPLACE_FLAG });
                    }
                    replace = true;
                }
                unknown => {
                    return Err(OptionsError::UnknownFlag {
                        flag: unknown.to_owned(),
                    });
                }
            }
        }

        Ok(Self {
            directory,
            address: address.ok_or(OptionsError::MissingValue {
                flag: NODE_ADDRESS_FLAG,
            })?,
            replace,
        })
    }
}

#[cfg(test)]
#[path = "../tests/config/node_identity_request_tests.rs"]
mod tests;
