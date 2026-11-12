//! What a `login.defs` file says about one id floor.

/// What the text of one `login.defs` file says about `UID_MIN` or `GID_MIN`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LoginDefsFloorReading {
    /// The file does not set it.
    Unset,
    /// The file sets it to this value.
    Set(u32),
    /// The file sets it to something that is not a usable floor: not a number, or zero (which
    /// would put root in the customer range, and makes `id < floor` never true).
    Unusable,
}
