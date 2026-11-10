//! Fixed keys, hosts and builders the transfer tests share.
//!
//! Every key here was generated for the tests (`ssh-keygen`, no passphrase) or
//! is a public host key whose fingerprint its publisher states; none is real
//! material and none opens anything.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;

use crate::transfer::model::source_credential::SourceCredential;
use crate::transfer::model::source_endpoint::SourceEndpoint;

/// A throwaway ed25519 private key document, generated for these tests.
pub(crate) const PRIVATE_KEY: &str = "-----BEGIN OPENSSH PRIVATE KEY-----
b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
QyNTUxOQAAACBkpa+ARZIQEqCUa/1S58jAxIUD24qXoZl+lu2FAiwOVwAAAIjbBU6K2wVO
igAAAAtzc2gtZWQyNTUxOQAAACBkpa+ARZIQEqCUa/1S58jAxIUD24qXoZl+lu2FAiwOVw
AAAEDRqDsFY0l/dWREYDtYnfTYcs/9LQKxL7b9XgKNdOhMnmSlr4BFkhASoJRr/VLnyMDE
hQPbipehmX6W7YUCLA5XAAAAAAECAwQF
-----END OPENSSH PRIVATE KEY-----
";

/// A run of the private key's body that appears nowhere else: the planted
/// sentinel the secrecy tests hunt for.
pub(crate) const PRIVATE_KEY_SENTINEL: &str =
    "QyNTUxOQAAACBkpa+ARZIQEqCUa/1S58jAxIUD24qXoZl+lu2FAiwOVwAAAIjbBU6K2wVO";

/// The public half of [`PRIVATE_KEY`] as `ssh-keygen -C ''` writes it: with a
/// trailing space and newline.
pub(crate) const PUBLIC_KEY_FILE: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGSlr4BFkhASoJRr/VLnyMDEhQPbipehmX6W7YUCLA5X \n";

/// The key of [`PUBLIC_KEY_FILE`] without the padding of a comment.
pub(crate) const PUBLIC_KEY_LINE: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGSlr4BFkhASoJRr/VLnyMDEhQPbipehmX6W7YUCLA5X";

/// The `ssh-keygen -l` fingerprint of [`PUBLIC_KEY_LINE`].
pub(crate) const PUBLIC_KEY_FINGERPRINT: &str =
    "SHA256:k+MP3j/U7WRG2NrHBS5WCJvHRv1/bsT/xy7T/mcmaBU";

/// An ed25519 host key (the one github.com publishes).
pub(crate) const HOST_ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl";

/// The fingerprint its publisher states for [`HOST_ED25519`].
pub(crate) const HOST_ED25519_FINGERPRINT: &str =
    "SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU";

/// An ECDSA P-256 host key.
pub(crate) const HOST_ECDSA_256: &str = "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBHIgzR5bwie1lNiJC1HvYC8+5wgjRW/mnYBqrgRZeiZcvL9NMglS2rymGdYzwW24RrxYbeZ8AyJ3Kk/ZtPf0QWI=";

/// The fingerprint of [`HOST_ECDSA_256`].
pub(crate) const HOST_ECDSA_256_FINGERPRINT: &str =
    "SHA256:oz+B9CfMhv+p8xcb7T3JEKEvCxJUhhdMqNSd0bWkMR4";

/// An ECDSA P-384 host key.
pub(crate) const HOST_ECDSA_384: &str = "ecdsa-sha2-nistp384 AAAAE2VjZHNhLXNoYTItbmlzdHAzODQAAAAIbmlzdHAzODQAAABhBN23TU5VPg+KzfeJudEEpiTRbB//TFoNtCdArfv2AFh5GGgokrl7KGxO6XbBK2FXnfe6GUimr59P1/0xFuveWcYtlzixCNN/PUslGr+cYrDP6ycShWRtSaARQnLqVXUr6Q==";

/// An RSA host key.
pub(crate) const HOST_RSA: &str = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC2NY9PEL9ZxkMhP0Tlc1ImRsovfuC2EYfa0A8Mg3v0naw8QhVjyOVA+YK8nSuezLRcRewqapNvKlFdEdr7lSR5H+a78H+x/7eR1Mx8tqIY2BdUM10GNxgkSQKUCXOT5k6LnpyIjR5EwBVu43Jd/O6ld4z4snydF2vX5FgVKxytMNlqhk6bFs1v3SeUwnT3zOk3Yx9FYyhgpkdEar3vovKt3N+NSYCXmMHt9QSSMmZc1cMRBiush9w7LI7B9BoZNmEslPTM+Uc+8pF3wejmKhdJBNIikIIN+YzeOkRHSx8ZKpC1tikfnl/+c4S6TrgySItsl7hrdhYzhJtgQEPv8qZV";

/// The fingerprint of [`HOST_RSA`].
pub(crate) const HOST_RSA_FINGERPRINT: &str = "SHA256:jp77pmByoNdXO8+DexCw7Xta/rnN7s/mNDYAHV8QmuE";

/// An endpoint at `host`, port 22, login `root`.
pub(crate) fn endpoint_at(host: &str) -> SourceEndpoint {
    SourceEndpoint {
        host: RemoteHost::parse(host).unwrap(),
        port: Port::parse(22).unwrap(),
        user: SourceUserName::parse("root").unwrap(),
    }
}

/// The credential of the tests: [`PRIVATE_KEY`] and the pinned [`HOST_ED25519`].
pub(crate) fn credential() -> SourceCredential {
    SourceCredential {
        private_key: SourcePrivateKey::parse(PRIVATE_KEY.as_bytes()).unwrap(),
        host_key: HostKey::parse(HOST_ED25519).unwrap(),
    }
}
