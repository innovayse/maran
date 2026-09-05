//! Why a render type failed to produce its configuration text.

use thiserror::Error;

/// Reasons rendering a template can fail.
///
/// Every field reaching a render type has already been validated by the
/// caller — a config never fails to render because of unsafe input, only
/// because the template and its render type have drifted apart.
#[derive(Debug, Error)]
#[non_exhaustive]
pub enum RenderError {
    /// Askama itself failed to render the template.
    #[error("failed to render the template: {0}")]
    Askama(#[from] askama::Error),
}
