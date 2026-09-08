//! The [`ObjectStoreHost`] an S3-compatible bucket is reached through.

use core::fmt;
use std::fs::{File, remove_file};
use std::io::{Read as _, Write as _};
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;
use std::sync::Arc;

use maran_agent_core::secret_string::SecretString;
use maran_agent_core::validation::web::s3_bucket::S3Bucket;
use maran_agent_core::validation::web::s3_region::S3Region;
use object_store::aws::{AmazonS3, AmazonS3Builder};
use object_store::path::Path as ObjectPath;
use object_store::{
    Error as ObjectStoreError, ObjectStore as _, ObjectStoreExt as _, PutPayload, RetryConfig,
};
use tokio::runtime::Handle;

use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_stage::BackupStage;
use crate::backup::model::object_summary::ObjectSummary;
use crate::backup::model::progress_sink::ProgressSink;
use crate::backup::model::public_read_verdict::PublicReadVerdict;
use crate::backup::object_store_host::ObjectStoreHost;

/// The only scheme a destination endpoint may carry.
///
/// A backup crossing the network is every file in a customer's home and a full
/// dump of every database they own, and the request that carries it is signed
/// with a key that is good for every other object in the bucket. Over `http://`
/// both are readable by anything between here and the provider
/// (rules/security.md item 10: "TLS everywhere external"). A custom `https://`
/// endpoint is welcome — that is how every S3-compatible provider that is not
/// AWS is reached — but the scheme is not negotiable.
const REQUIRED_SCHEME: &str = "https://";

/// How much is uploaded per part, and therefore how often progress moves.
///
/// Sixteen mebibytes. The floor is the providers': a multipart part other than
/// the last must be at least five mebibytes, and several providers additionally
/// require every part except the last to be the same size. The ceiling is
/// memory: this many bytes of a customer's archive are in this process at once,
/// and an artifact may be sixty-four gibibytes (rules/rust.md "Streams stay
/// bounded"). Sixteen also keeps the part count under the ten-thousand-part
/// limit for an artifact of that size.
const UPLOAD_PART_BYTES: usize = 16 * 1024 * 1024;

/// How much is downloaded per ranged request.
const DOWNLOAD_CHUNK_BYTES: u64 = 16 * 1024 * 1024;

/// The mode a downloaded object is created with.
const OBJECT_MODE: u32 = 0o600;

/// How many times a request is retried before it is reported.
///
/// **Zero, deliberately.** `object_store` defaults to ten with exponential
/// backoff, which is right for a data-processing job and wrong here on two
/// counts. A destination with wrong credentials would be hammered ten times per
/// operation, which is how a provider's lockout is tripped by a panel trying to
/// be helpful (Decision 3: "never a retry storm"). And a probe that retried for
/// three minutes before answering `Unproven` would hold an operator's Save
/// button for three minutes to tell them nothing. The panel retries at its own
/// layer, where a failure is a destination marked `Unhealthy` and an alert, and
/// where a human can see it happening.
const MAXIMUM_RETRIES: usize = 0;

/// The name a signed URL's signature parameter carries, in every SigV4
/// implementation.
const SIGNATURE_PARAMETER: &str = "X-Amz-Signature=";

/// What a redacted credential is replaced by.
const REDACTION: &str = "«redacted»";

/// The shortest access key id worth searching an error message for.
///
/// A credential shorter than this is not a credential — providers issue ids of
/// sixteen characters and up — and searching for a one- or two-character string
/// would blank out most of an error message and tell an operator nothing. The
/// guard is on the redactor's input rather than on its behaviour so that the
/// degenerate case is a *readable* message rather than a message that is all
/// redaction.
const SHORTEST_CREDENTIAL: usize = 8;

/// A backup destination that is an S3-compatible bucket.
///
/// # Why a crate and not a signer of our own
///
/// The request signing this needs is SigV4: an HMAC-SHA256 chain over a
/// canonical request whose every byte — the header order, the payload hash, the
/// URI encoding of each path segment — has to be exactly right. Getting it
/// wrong fails loudly, which is the good case; getting the *canonicalisation*
/// wrong in a way that still verifies is the bad one. rules/security.md item 9
/// forbids home-grown crypto outright, and this is what that rule is for. The
/// crate is `object_store`, `aws` feature only, Apache-2.0/MIT.
///
/// # Credentials
///
/// They arrive as [`SecretString`] and are handed to the client at
/// construction. Nothing writes them to disk: the agent is stateless by rule,
/// so a destination's keys travel per call over the root-only unix socket and
/// live in this struct for as long as an operation takes (Decision 3).
///
/// Two things keep them out of the operator's log, and they are separate
/// because they fail separately:
///
/// - **This type's `Debug` is hand-written.** `#[derive(Debug)]` on a struct holding a
///   credential is not a hypothetical — it is how credentials actually reach
///   logs, because nobody prints the key, they print the struct that holds it.
///   The derived implementation would also print the `AmazonS3` client, whose
///   own `Debug` this code does not control and whose contents include a
///   credential provider. So the implementation below names the fields it
///   prints, and adding a field does not silently add it to a log line.
/// - **`redact` runs over every message crossing back.** The provider's own
///   error text quotes the request, and a signed request carries both the
///   access key id (inside the `Credential=` scope) and `X-Amz-Signature=`. An
///   unredacted provider error is a credential in an operator's log, and the
///   operator did nothing wrong.
///
/// # The async bridge
///
/// `object_store`'s API is async and this area's is not, so every call goes
/// through `Handle::block_on` on the runtime handle this host was built with.
/// That is legal off a runtime worker thread and illegal on one, which is why
/// [`ObjectStoreHost`]'s contract says every method is called from
/// `tokio::task::spawn_blocking`. The handle is a constructor parameter rather
/// than a `Handle::current()` inside, so the obligation is visible at the call
/// site and so a test can drive this from an ordinary thread.
pub struct S3ObjectStoreHost {
    /// The runtime the async client is driven on.
    runtime: Handle,

    /// The signed client: every operation this product performs.
    store: Arc<AmazonS3>,

    /// The unsigned client, and the entire content of the public-read probe.
    ///
    /// A second client rather than a flag on the first, because "send no
    /// credentials" must not be a property that can be forgotten on one call:
    /// this one has none to send. A probe made with the signed client would
    /// succeed against a perfectly private bucket and report it readable by the
    /// world.
    anonymous: Arc<AmazonS3>,

    /// The access key id, kept for one purpose: `redact` searches error
    /// messages for it.
    access_key_id: SecretString,

    /// The bucket, for this type's hand-written `Debug`.
    bucket: String,

    /// The region, for this type's hand-written `Debug`.
    region: String,

    /// The endpoint, for this type's hand-written `Debug`. Empty when the
    /// provider's default for the region is used.
    endpoint: String,
}

impl S3ObjectStoreHost {
    /// Opens the bucket `bucket` in `region` at `endpoint`.
    ///
    /// `endpoint` is the base URL of an S3-compatible provider, and **must
    /// begin `https://`**; an empty string means "the provider's own endpoint
    /// for this region", which is AWS and is HTTPS by construction. Anything
    /// else — an `http://` URL above all — is
    /// [`BackupError::DestinationInsecure`], refused here at construction so
    /// that no operation exists that could use it.
    ///
    /// # Errors
    ///
    /// [`BackupError::DestinationInsecure`] for an endpoint that is not HTTPS,
    /// and [`BackupError::ObjectStoreFailed`] when the client could not be
    /// built from these values.
    pub fn new(
        runtime: Handle,
        endpoint: &str,
        bucket: &S3Bucket,
        region: &S3Region,
        access_key_id: SecretString,
        secret_access_key: &SecretString,
    ) -> Result<Self, BackupError> {
        if !endpoint.is_empty() && !endpoint.to_ascii_lowercase().starts_with(REQUIRED_SCHEME) {
            return Err(BackupError::DestinationInsecure);
        }

        Self::over_endpoint(
            runtime,
            endpoint,
            bucket,
            region,
            access_key_id,
            secret_access_key,
        )
    }

    /// The body of [`Self::new`], with the scheme decision already made.
    ///
    /// Split for the reason `backup_root`'s owner check is split, and it is the
    /// same rule: a gate that has only ever been handed input it must REFUSE
    /// passes just as well when it has been mutated to refuse everything
    /// (rules/testing.md, "a refusing gate needs an inverse control"). The
    /// scheme check has an inverse control because [`Self::new`] is tested with
    /// an `https://` endpoint it must accept — and the transport tests, which
    /// have to speak to a local stub over plain HTTP, enter here instead of
    /// weakening the check they are not testing.
    ///
    /// `with_allow_http` is set from the endpoint's own scheme rather than
    /// passed in, so this function cannot be asked to allow HTTP for an
    /// endpoint that is HTTPS or the reverse.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] when the client could not be built.
    fn over_endpoint(
        runtime: Handle,
        endpoint: &str,
        bucket: &S3Bucket,
        region: &S3Region,
        access_key_id: SecretString,
        secret_access_key: &SecretString,
    ) -> Result<Self, BackupError> {
        let plaintext = endpoint.to_ascii_lowercase().starts_with("http://");
        let retries = RetryConfig {
            max_retries: MAXIMUM_RETRIES,
            ..RetryConfig::default()
        };

        let base = || {
            let mut builder = AmazonS3Builder::new()
                .with_bucket_name(bucket.as_str())
                .with_region(region.as_str())
                .with_allow_http(plaintext)
                .with_retry(retries.clone());
            if !endpoint.is_empty() {
                builder = builder.with_endpoint(endpoint);
            }
            builder
        };

        let store = base()
            .with_access_key_id(access_key_id.expose())
            .with_secret_access_key(secret_access_key.expose())
            .build()
            .map_err(|error| failed(&access_key_id, &error))?;

        // No credentials of any kind, and none to forget: this is the probe's
        // client, and `skip_signature` is what makes its requests the same
        // requests anyone on the internet can make.
        let anonymous = base()
            .with_skip_signature(true)
            .build()
            .map_err(|error| failed(&access_key_id, &error))?;

        Ok(Self {
            runtime,
            store: Arc::new(store),
            anonymous: Arc::new(anonymous),
            access_key_id,
            bucket: bucket.as_str().to_owned(),
            region: region.as_str().to_owned(),
            endpoint: endpoint.to_owned(),
        })
    }

    /// Turns a transport failure into this area's error, redacted.
    ///
    /// Every `Err` from the client leaves this type through here or through
    /// [`failed`]; there is no other conversion, which is what makes "the
    /// message is redacted" a property of the type rather than of six call
    /// sites remembering.
    fn failure(&self, error: &ObjectStoreError) -> BackupError {
        failed(&self.access_key_id, error)
    }
}

impl fmt::Debug for S3ObjectStoreHost {
    /// Prints the destination and never the credentials.
    ///
    /// Hand-written, and this is the whole reason: a derived `Debug` prints
    /// every field, and two of this struct's fields are a credential and a
    /// client holding one. `SecretString`'s own `Debug` would redact the first
    /// even under a derive — but the second is `AmazonS3`, whose `Debug` is
    /// another crate's decision and prints a credential provider. A hand-written
    /// implementation is the only one that stays correct when somebody adds a
    /// field.
    ///
    /// The fields it does print are the ones an operator needs in order to know
    /// WHICH destination a log line is about: bucket, region, endpoint. None of
    /// them is a secret — an operator configured all three in the panel's UI.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter
            .debug_struct("S3ObjectStoreHost")
            .field("bucket", &self.bucket)
            .field("region", &self.region)
            .field("endpoint", &self.endpoint)
            .finish_non_exhaustive()
    }
}

impl ObjectStoreHost for S3ObjectStoreHost {
    /// Uploads `from` to `key`, in parts, reporting progress per part.
    ///
    /// A file that fits in one part is sent as a single `PUT`; anything larger
    /// goes through a multipart upload, and a multipart upload that fails is
    /// ABORTED rather than abandoned. An abandoned upload leaves its parts in
    /// the bucket, invisible to a listing and billed monthly, until a lifecycle
    /// rule the panel did not write removes them.
    ///
    /// The percentage is interpolated from bytes actually sent
    /// ([`BackupStage::percent_through`]) and is never written down as a
    /// literal.
    ///
    /// The object carries no ACL header. A bucket's default is private, and
    /// modern buckets have object ACLs disabled outright and reject
    /// `x-amz-acl` with a 400 — sending "private" to be explicit would refuse
    /// every correctly configured bucket in order to restate its default. What
    /// covers the case a header could not anyway is the probe: a bucket whose
    /// POLICY serves objects to the world is refused before it is ever saved.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::put`].
    fn put(&self, key: &str, from: &Path, sink: &mut dyn ProgressSink) -> Result<u64, BackupError> {
        let path = ObjectPath::from(key);
        let mut source = File::open(from).map_err(|_| BackupError::ChecksumUnreadable)?;
        let total = source
            .metadata()
            .map_err(|_| BackupError::ChecksumUnreadable)?
            .len();

        let mut first = vec![0_u8; UPLOAD_PART_BYTES];
        let filled = read_fully(&mut source, &mut first)?;
        first.truncate(filled);

        if (filled as u64) == total {
            let payload = PutPayload::from(first);
            self.runtime
                .block_on(self.store.put(&path, payload))
                .map_err(|error| self.failure(&error))?;
            sink.report(
                BackupStage::Uploading,
                BackupStage::Uploading.percent_through(total, total),
            );
            return Ok(total);
        }

        let mut upload = self
            .runtime
            .block_on(self.store.put_multipart(&path))
            .map_err(|error| self.failure(&error))?;

        let mut sent: u64 = 0;
        let mut part = first;
        loop {
            let bytes = part.len() as u64;
            let outcome = self
                .runtime
                .block_on(upload.put_part(PutPayload::from(part)));
            if let Err(error) = outcome {
                let _ = self.runtime.block_on(upload.abort());
                return Err(self.failure(&error));
            }
            sent = sent.saturating_add(bytes);
            sink.report(
                BackupStage::Uploading,
                BackupStage::Uploading.percent_through(sent, total),
            );

            let mut next = vec![0_u8; UPLOAD_PART_BYTES];
            let filled = match read_fully(&mut source, &mut next) {
                Ok(filled) => filled,
                Err(error) => {
                    let _ = self.runtime.block_on(upload.abort());
                    return Err(error);
                }
            };
            if filled == 0 {
                break;
            }
            next.truncate(filled);
            part = next;
        }

        if let Err(error) = self.runtime.block_on(upload.complete()) {
            let _ = self.runtime.block_on(upload.abort());
            return Err(self.failure(&error));
        }

        Ok(sent)
    }

    /// Downloads `key` into `into`, one range at a time.
    ///
    /// Ranged requests rather than one streamed body: the size is asked for
    /// first, so the loop knows what it is fetching, and a chunk that fails
    /// fails a request rather than a sixty-gigabyte transfer. Nothing is held
    /// in memory beyond one chunk.
    ///
    /// A download that does not complete removes the file it was writing. The
    /// caller asked for an artifact and a truncated artifact is not a smaller
    /// one — it is a file the next step would checksum, reject, and have to
    /// explain.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::get`].
    fn get(&self, key: &str, into: &Path) -> Result<u64, BackupError> {
        let path = ObjectPath::from(key);
        let meta = self
            .runtime
            .block_on(self.store.head(&path))
            .map_err(|error| match error {
                ObjectStoreError::NotFound { .. } => BackupError::ObjectNotFound,
                other => self.failure(&other),
            })?;

        match self.download(&path, meta.size, into) {
            Ok(received) => Ok(received),
            Err(error) => {
                let _ = remove_file(into);
                Err(error)
            }
        }
    }

    /// Removes `key`, and reports success when the bucket did not hold it.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::delete`].
    fn delete(&self, key: &str) -> Result<(), BackupError> {
        let path = ObjectPath::from(key);
        match self.runtime.block_on(self.store.delete(&path)) {
            Ok(()) | Err(ObjectStoreError::NotFound { .. }) => Ok(()),
            Err(error) => Err(self.failure(&error)),
        }
    }

    /// Answers every object directly under `prefix`.
    ///
    /// `list_with_delimiter`, so the answer is one "directory" of the bucket's
    /// namespace and not everything beneath it. One account's backups are one
    /// such directory, and a recursive listing of a prefix an operator chose
    /// could walk a bucket this product does not own alone.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::list`].
    fn list(&self, prefix: &str) -> Result<Vec<ObjectSummary>, BackupError> {
        let path = ObjectPath::from(prefix.trim_end_matches('/'));
        let listing = self
            .runtime
            .block_on(self.store.list_with_delimiter(Some(&path)))
            .map_err(|error| self.failure(&error))?;

        let mut summaries: Vec<ObjectSummary> = listing
            .objects
            .into_iter()
            .map(|object| ObjectSummary {
                key: object.location.to_string(),
                bytes: object.size,
            })
            .collect();
        summaries.sort_by(|left, right| left.key.cmp(&right.key));

        Ok(summaries)
    }

    /// Fetches `key` with no credentials and says what came back.
    ///
    /// This is the whole check, and its three outcomes are three different
    /// facts:
    ///
    /// - the object came back → [`PublicReadVerdict::PubliclyReadable`]. The
    ///   bucket serves this product's backups to anyone with the URL, and the
    ///   evidence is the bytes in hand.
    /// - the destination refused — 404, 403 or 401 →
    ///   [`PublicReadVerdict::Private`]. A refusal is an answer, and the answer
    ///   is no. 404 counts because a bucket that will not admit the object
    ///   exists is not serving it; several providers answer a private object
    ///   that way rather than with 403, and treating that as unproven would
    ///   refuse every such provider.
    /// - anything else — a connection that could not be made, a timeout, a
    ///   redirect, a 500, a proxy's error page →
    ///   [`PublicReadVerdict::Unproven`], carrying the redacted reason.
    ///
    /// **The third arm is the reason this function returns a verdict rather
    /// than a boolean.** "The anonymous GET could not be made" is not "the
    /// object is private", and a check that reported it as private would be
    /// reporting on something it never observed (rules/testing.md). The panel
    /// refuses the destination on `Unproven` exactly as it does on
    /// `PubliclyReadable`, so the honest answer costs an operator a retry and
    /// never costs a customer a public archive of their database.
    ///
    /// # Errors
    ///
    /// None from the fetch: a failed fetch is a verdict, not an error. The
    /// signature keeps the seam's `Result` because another implementation may
    /// need it.
    fn probe_public_read(&self, key: &str) -> Result<PublicReadVerdict, BackupError> {
        let path = ObjectPath::from(key);
        let verdict = match self.runtime.block_on(self.anonymous.get(&path)) {
            Ok(_) => PublicReadVerdict::PubliclyReadable,
            Err(
                ObjectStoreError::NotFound { .. }
                | ObjectStoreError::PermissionDenied { .. }
                | ObjectStoreError::Unauthenticated { .. },
            ) => PublicReadVerdict::Private,
            Err(other) => PublicReadVerdict::Unproven {
                reason: redact(&self.access_key_id, &other.to_string()),
            },
        };

        Ok(verdict)
    }
}

impl S3ObjectStoreHost {
    /// Writes `size` bytes of `path` into a fresh root-only file at `into`.
    ///
    /// Separated from [`ObjectStoreHost::get`] so the cleanup of a partial file
    /// is written once, outside, rather than at each place this body returns
    /// early.
    ///
    /// # Errors
    ///
    /// [`BackupError::ArtifactUnpublishable`] when the local file could not be
    /// created or written, [`BackupError::ObjectStoreFailed`] when a range
    /// could not be fetched.
    fn download(&self, path: &ObjectPath, size: u64, into: &Path) -> Result<u64, BackupError> {
        let mut target = File::options()
            .write(true)
            .create(true)
            .truncate(true)
            .mode(OBJECT_MODE)
            .open(into)
            .map_err(|_| BackupError::ArtifactUnpublishable)?;

        let mut received: u64 = 0;
        while received < size {
            let end = size.min(received.saturating_add(DOWNLOAD_CHUNK_BYTES));
            let chunk = self
                .runtime
                .block_on(self.store.get_range(path, received..end))
                .map_err(|error| self.failure(&error))?;
            target
                .write_all(&chunk)
                .map_err(|_| BackupError::ArtifactUnpublishable)?;
            received = received.saturating_add(chunk.len() as u64);
            if chunk.is_empty() {
                break;
            }
        }

        target
            .sync_all()
            .map_err(|_| BackupError::ArtifactUnpublishable)?;

        Ok(received)
    }
}

/// Fills as much of `buffer` as the file has left, and answers how much.
///
/// `Read::read` is allowed to return fewer bytes than asked for at any time, so
/// a single call per part would produce short parts — which a multipart upload
/// rejects on every part but the last.
///
/// # Errors
///
/// [`BackupError::ChecksumUnreadable`] when the file could not be read.
fn read_fully(source: &mut File, buffer: &mut [u8]) -> Result<usize, BackupError> {
    let mut filled = 0;
    while filled < buffer.len() {
        let read = source
            .read(&mut buffer[filled..])
            .map_err(|_| BackupError::ChecksumUnreadable)?;
        if read == 0 {
            break;
        }
        filled += read;
    }

    Ok(filled)
}

/// Builds a redacted [`BackupError::ObjectStoreFailed`] from a transport error.
///
/// A free function rather than a method, because the constructor needs it
/// before there is a `self` to call it on.
fn failed(access_key_id: &SecretString, error: &ObjectStoreError) -> BackupError {
    BackupError::ObjectStoreFailed {
        message: redact(access_key_id, &error.to_string()),
    }
}

/// Removes the access key id and any SigV4 signature from `message`.
///
/// # Why an error message is a credential leak
///
/// The provider's error text quotes the request that failed. A signed S3
/// request carries the access key id inside its `Credential=` scope, and a
/// presigned URL — which several providers echo back verbatim in an error —
/// carries `X-Amz-Signature=` as well. The message then travels: into the
/// agent's `tracing` output, into the RPC's error, into the panel's audit
/// record, into whatever the operator pastes into a support ticket. The
/// operator did nothing wrong, and by then the credential is in four places.
///
/// So both are removed, and they are removed separately because they leak
/// separately:
///
/// - **the access key id** is a literal string this host was configured with,
///   so it is searched for and replaced wherever it appears, including inside
///   a `Credential=AKIA…/20260905/eu-central-1/s3/aws4_request` scope where it
///   is not a whole word;
/// - **the signature** is a value this code has never seen, so it cannot be
///   searched for. What is known is its parameter name, so the value is
///   replaced from `X-Amz-Signature=` up to the next `&`, the next whitespace,
///   or the end of the message. The parameter name is left in place on
///   purpose: an operator reading `X-Amz-Signature=«redacted»` can see that a
///   presigned URL was involved, which is diagnostically useful, while the
///   secret part is gone.
///
/// The secret access key is not searched for, and that is deliberate rather
/// than an omission: it never appears in a request or in a provider's error —
/// it is a key that signs, not a value that travels — so searching for it
/// would mean holding a second copy of the strongest secret this host has in
/// order to look for something that is not there.
///
/// **What this does not do.** It cannot redact a credential the provider
/// mangled, re-encoded, or split across the message, and it does not attempt
/// to. It is the last line and not the only one: the errors this area returns
/// carry no paths and no tool output, `SecretString` keeps the values out of
/// every formatter, and the message a customer sees is mapped from a typed
/// variant rather than passed through.
fn redact(access_key_id: &SecretString, message: &str) -> String {
    let key = access_key_id.expose();
    let without_key = if key.len() >= SHORTEST_CREDENTIAL {
        message.replace(key, REDACTION)
    } else {
        message.to_owned()
    };

    let mut redacted = String::with_capacity(without_key.len());
    let mut rest = without_key.as_str();
    while let Some(at) = rest.find(SIGNATURE_PARAMETER) {
        let (before, from_parameter) = rest.split_at(at);
        redacted.push_str(before);
        redacted.push_str(SIGNATURE_PARAMETER);

        let value = &from_parameter[SIGNATURE_PARAMETER.len()..];
        let end = value
            .find(|character: char| character == '&' || character.is_whitespace())
            .unwrap_or(value.len());
        redacted.push_str(REDACTION);
        rest = &value[end..];
    }
    redacted.push_str(rest);

    redacted
}

#[cfg(test)]
#[path = "../tests/backup/s3_object_store_host_tests.rs"]
mod tests;
