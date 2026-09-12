//! The one mapping from a login's daemon onto the wire enum.

use maran_ops::logins::LoginProtocol;

use crate::proto::TransferProtocol;

/// Converts the daemon a login belongs to into the enum the contract carries on
/// `SftpLoginSuspensionFact`.
///
/// It lives beside the service rather than inside it so that one protocol maps
/// to one wire value in exactly one place (rules/rust.md "Service anatomy").
///
/// [`TransferProtocol::Unspecified`] is deliberately not produced here: on the
/// wire it means "the agent predates this field", and an agent that enumerated
/// the login knows which jail its home was in, which is the only way either
/// answer is ever reached. A caller that receives it is talking to an older
/// agent and reads it as SFTP — correctly, because before FTPS existed every
/// fact in that list was an SFTP login by construction.
///
/// **The message this field lands on is still called `SftpLoginSuspensionFact`,
/// and a fact in it may now be an FTPS login.** That is the additive law
/// (rules/proto.md) rather than an oversight: renaming the message would break
/// every released caller, and the alternative — a second repeated field for the
/// second protocol — would let a caller read one and miss the other, which is
/// the exact defect `ops::logins` was extracted to close. The name is
/// historical; this field is what the list actually means.
#[must_use]
pub fn to_transfer_protocol(protocol: LoginProtocol) -> TransferProtocol {
    match protocol {
        LoginProtocol::Sftp => TransferProtocol::Sftp,
        LoginProtocol::Ftps => TransferProtocol::Ftps,
    }
}

#[cfg(test)]
#[path = "../../tests/services/accounts/to_transfer_protocol_tests.rs"]
mod tests;
