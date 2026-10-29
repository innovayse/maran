//! Parsing a `/proc/sysvipc` table into the objects it lists.

use crate::accounts::AccountError;
use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;
use crate::accounts::model::sysv_ipc_object::SysvIpcObject;

/// The header name of the identifier column in `kind`'s table.
const fn id_column(kind: SysvIpcKind) -> &'static str {
    match kind {
        SysvIpcKind::Shm => "shmid",
        SysvIpcKind::Msg => "msqid",
        SysvIpcKind::Sem => "semid",
    }
}

/// The error every malformed table ends in, naming the table and not its bytes.
fn unreadable(kind: SysvIpcKind) -> AccountError {
    AccountError::UnreadableOutput {
        program: format!("/proc/sysvipc/{}", kind.table_name()),
    }
}

/// The position of the column headed `name`, if the header has one.
fn column(header: &[&str], name: &str) -> Option<usize> {
    header.iter().position(|field| *field == name)
}

/// Parses the text of one `/proc/sysvipc` table into its objects.
///
/// Header-driven: the first non-blank line names the columns and the
/// identifier, `uid` and `cuid` columns are found by name, so a kernel that adds
/// or reorders columns does not shift the values read. Columns are separated by
/// runs of whitespace. Text with no non-blank line at all is an empty table (a
/// kernel without SysV IPC answers that), not an error.
///
/// # Errors
///
/// Returns [`AccountError::UnreadableOutput`] when the header lacks the
/// identifier, `uid` or `cuid` column, when a row has fewer fields than the
/// header needs, or when one of the three values is not a non-negative integer.
pub(crate) fn parse_sysv_ipc_table(
    kind: SysvIpcKind,
    text: &str,
) -> Result<Vec<SysvIpcObject>, AccountError> {
    let mut lines = text.lines().filter(|line| !line.trim().is_empty());
    let Some(header_line) = lines.next() else {
        return Ok(Vec::new());
    };
    let header: Vec<&str> = header_line.split_whitespace().collect();
    let (Some(id_at), Some(uid_at), Some(cuid_at)) = (
        column(&header, id_column(kind)),
        column(&header, "uid"),
        column(&header, "cuid"),
    ) else {
        return Err(unreadable(kind));
    };

    let mut objects = Vec::new();
    for line in lines {
        let fields: Vec<&str> = line.split_whitespace().collect();
        let value = |at: usize| -> Result<u32, AccountError> {
            fields
                .get(at)
                .and_then(|field| field.parse::<u32>().ok())
                .ok_or_else(|| unreadable(kind))
        };
        objects.push(SysvIpcObject {
            kind,
            id: value(id_at)?,
            uid: value(uid_at)?,
            cuid: value(cuid_at)?,
        });
    }
    Ok(objects)
}

#[cfg(test)]
#[path = "../tests/accounts/parse_sysv_ipc_table_tests.rs"]
mod tests;
