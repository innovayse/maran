//! Reading the opening of an import stream into a request, or refusing it.

use maran_ops::backup::ImportRequest;
use tonic::Streaming;

use crate::proto::{AgentError, ImportBackupRequest};
use crate::services::transfer::import_collector::ImportCollector;
use crate::services::wire::invalid_input::invalid_input;

/// Reads the first message of `stream`, which must be the header.
///
/// Answers the request the header describes and the collector that goes on
/// judging the rest of the stream. Nothing in the request is trusted yet: the
/// operation verifies the signature and the fingerprint before it touches anything.
///
/// # Errors
///
/// `INVALID_INPUT` for a stream that ends or fails before a message, and for a
/// first message that is not the header.
pub async fn validated_import(
    stream: &mut Streaming<ImportBackupRequest>,
) -> Result<(ImportRequest, ImportCollector), AgentError> {
    let mut collector = ImportCollector::new();

    let first = match stream.message().await {
        Ok(Some(message)) => message,
        Ok(None) => {
            return Err(invalid_input(
                "the first message of an import stream carries the header".to_owned(),
            ));
        }
        Err(status) => {
            return Err(invalid_input(format!(
                "the import stream failed: {}",
                status.code()
            )));
        }
    };

    let accepted = collector.accept(first)?;
    let Some(request) = accepted.header else {
        return Err(invalid_input(
            "the first message of an import stream carries the header".to_owned(),
        ));
    };

    Ok((request, collector))
}
