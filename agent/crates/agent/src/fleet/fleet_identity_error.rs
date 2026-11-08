//! Why a fleet identity could not be generated, read or checked.

use std::path::PathBuf;

/// Errors of the fleet identity: the directory it lives in, the files in it, and the
/// certificates they hold.
///
/// The text is for the operator and the log. It names paths and the reason a file was refused,
/// and never the contents of a file: a private key must not reach a log line through an error.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum FleetIdentityError {
    /// The identity directory does not exist (a read, which never creates it).
    #[error("the fleet directory {} does not exist", path.display())]
    DirectoryMissing {
        /// The directory that was looked for.
        path: PathBuf,
    },
    /// The path is not a directory, or is a symbolic link to one.
    ///
    /// A link is refused because the directory's ownership and mode are what protect the key,
    /// and a link makes those the properties of wherever it points.
    #[error("{} is not a plain directory", path.display())]
    NotADirectory {
        /// The path that was refused.
        path: PathBuf,
    },
    /// The directory belongs to someone other than the user the agent runs as.
    #[error("{} is owned by uid {owner}, not uid {expected}", path.display())]
    DirectoryOwnedByAnother {
        /// The directory that was refused.
        path: PathBuf,
        /// Its owner.
        owner: u32,
        /// The uid the agent runs as.
        expected: u32,
    },
    /// The directory grants some access to its group or to everyone.
    #[error("{} has mode {mode:04o}; it must be readable by its owner alone (0700)", path.display())]
    DirectoryTooPermissive {
        /// The directory that was refused.
        path: PathBuf,
        /// Its permission bits.
        mode: u32,
    },
    /// An identity already exists in the directory and `--replace` was not given.
    #[error("{} already exists; pass --replace to replace this node's identity", path.display())]
    IdentityAlreadyExists {
        /// The first file found.
        path: PathBuf,
    },
    /// A file the identity needs is not there.
    #[error("{} does not exist", path.display())]
    FileMissing {
        /// The file that was looked for.
        path: PathBuf,
    },
    /// A file in the identity directory is not a plain file (a link, a directory, a device).
    #[error("{} is not a plain file", path.display())]
    FileNotPlain {
        /// The file that was refused.
        path: PathBuf,
    },
    /// A file in the identity directory is larger than any key or certificate can be.
    #[error("{} is larger than {limit} bytes", path.display())]
    FileTooLarge {
        /// The file that was refused.
        path: PathBuf,
        /// The most the agent reads.
        limit: u64,
    },
    /// A certificate file is not exactly one well-formed PEM certificate.
    #[error("{} is not a single PEM certificate: {reason}", path.display())]
    CertificateMalformed {
        /// The file that was refused.
        path: PathBuf,
        /// What was wrong with it.
        reason: &'static str,
    },
    /// A key file is not valid UTF-8 text.
    #[error("{} is not a text key file", path.display())]
    KeyMalformed {
        /// The file that was refused.
        path: PathBuf,
    },
    /// The system clock cannot be expressed as a certificate validity.
    #[error("the system clock is outside the range a certificate can state")]
    ClockOutOfRange,
    /// The keypair or the certificate could not be generated.
    ///
    /// Carries the generator's message as text; it never contains key material.
    #[error("generating the node certificate failed: {reason}")]
    GenerationFailed {
        /// What the certificate generator said.
        reason: String,
    },
    /// A file operation on the identity directory failed.
    #[error("{} could not be accessed: {source}", path.display())]
    Io {
        /// The path the operation was about.
        path: PathBuf,
        /// The operating system's answer.
        #[source]
        source: std::io::Error,
    },
}
