//! Reading a group's numeric id out of `getent group <name>`.

/// The position of the numeric id in a group database row:
/// `name:password:gid:members`.
const GROUP_GID_FIELD: usize = 2;

/// Reads the id of `group` from the first line of `getent group <group>`.
///
/// The NAME field is compared too, and not only the id's position: a `getent`
/// run with no key lists every group on the host, and a reading that took the
/// first row's id would hand the credential directory to whichever group sorts
/// first. A row for another name, a short row and an id that is not a number
/// all answer `None`.
pub(crate) fn parse_group_id(group: &str, getent_output: &str) -> Option<u32> {
    let mut fields = getent_output.lines().next()?.split(':');
    let name = fields.next()?;
    if name != group {
        return None;
    }
    // `next()` consumed the name, so the id is the second field left.
    fields.nth(GROUP_GID_FIELD - 1)?.parse().ok()
}

#[cfg(test)]
#[path = "../tests/sites/parse_group_id_tests.rs"]
mod tests;
