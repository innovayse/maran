//! What the creation of one archive carries.

use maran_agent_core::validation::fs::browse_path::BrowsePath;
use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::system::name::AccountName;

use super::archive_format::ArchiveFormat;
use super::create_archive_input_error::CreateArchiveInputError;

/// Most sources one archive takes.
const MAXIMUM_SOURCES: usize = 1000;

/// One validated request to create an archive.
///
/// Constructed only by [`CreateArchiveInput::new`], which refuses the
/// combinations that cannot be made safely whatever the filesystem holds: no
/// source, the home as a source or destination, two sources where one contains
/// the other, and a destination inside a source (an archive that would be
/// asked to contain itself). These are decided on the components of the paths,
/// which is safe here because they only ever REFUSE; nothing is allowed because
/// of them, and the descriptor walk is what contains the work.
#[derive(Debug, Clone)]
pub struct CreateArchiveInput {
    /// The account whose home holds the sources and receives the archive.
    account: AccountName,
    /// What goes into the archive, relative to the home.
    sources: Vec<BrowsePath>,
    /// The archive file to create, relative to the home.
    destination: BrowsePath,
    /// The container format to write.
    format: ArchiveFormat,
    /// The allowances the creation may use up.
    bounds: CreateBounds,
}

impl CreateArchiveInput {
    /// Builds a request.
    ///
    /// # Errors
    ///
    /// Returns [`CreateArchiveInputError::NoSources`],
    /// [`CreateArchiveInputError::TooManySources`] (over 1000),
    /// [`CreateArchiveInputError::SourceIsHome`],
    /// [`CreateArchiveInputError::DestinationIsHome`],
    /// [`CreateArchiveInputError::OverlappingSources`] and
    /// [`CreateArchiveInputError::DestinationInsideSource`].
    pub fn new(
        account: AccountName,
        sources: Vec<BrowsePath>,
        destination: BrowsePath,
        format: ArchiveFormat,
        bounds: CreateBounds,
    ) -> Result<Self, CreateArchiveInputError> {
        if sources.is_empty() {
            return Err(CreateArchiveInputError::NoSources);
        }
        if sources.len() > MAXIMUM_SOURCES {
            return Err(CreateArchiveInputError::TooManySources);
        }
        if sources.iter().any(BrowsePath::is_home) {
            return Err(CreateArchiveInputError::SourceIsHome);
        }
        if destination.is_home() {
            return Err(CreateArchiveInputError::DestinationIsHome);
        }
        for (index, source) in sources.iter().enumerate() {
            if destination.components().starts_with(source.components()) {
                return Err(CreateArchiveInputError::DestinationInsideSource);
            }
            for other in &sources[index + 1..] {
                if source.components().starts_with(other.components())
                    || other.components().starts_with(source.components())
                {
                    return Err(CreateArchiveInputError::OverlappingSources);
                }
            }
        }

        Ok(Self {
            account,
            sources,
            destination,
            format,
            bounds,
        })
    }

    /// The account whose home holds the sources.
    #[must_use]
    pub fn account(&self) -> &AccountName {
        &self.account
    }

    /// What goes into the archive.
    #[must_use]
    pub fn sources(&self) -> &[BrowsePath] {
        &self.sources
    }

    /// The archive file to create.
    #[must_use]
    pub fn destination(&self) -> &BrowsePath {
        &self.destination
    }

    /// The container format to write.
    #[must_use]
    pub fn format(&self) -> ArchiveFormat {
        self.format
    }

    /// The allowances the creation may use up.
    #[must_use]
    pub fn bounds(&self) -> &CreateBounds {
        &self.bounds
    }
}

#[cfg(test)]
#[path = "../../tests/files/model/create_archive_input_tests.rs"]
mod tests;
