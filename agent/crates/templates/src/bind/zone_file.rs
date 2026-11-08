//! The whole zone file the agent writes for one zone.

use askama::Template;

use crate::bind::zone_record_line::ZoneRecordLine;
use crate::render_error::RenderError;

/// Renders one complete zone file: the origin, the default TTL, the SOA, the apex
/// NS set and the records.
///
/// **Whole, never merged into.** A zone is replaced by its complete record set on
/// every change, through `ops::safe_write`; nothing appends a record to an
/// existing file. The template has no `$INCLUDE`, `$GENERATE` or other directive
/// besides the `$ORIGIN` and `$TTL` it writes itself, and no field can introduce
/// one: every value is validated by the caller, never escaped, and a quoted
/// string is the only place free text appears.
///
/// Every name written absolute (with a trailing dot) is built by the caller from
/// a validated name; owners are relative to `$ORIGIN`.
///
/// The render ends with a newline: the last template line is a loop whose body
/// carries its own, and the apex NS loop precedes it, so an empty record list
/// still ends on the last NS line.
#[derive(Template)]
// A zone file is not a document: HTML-escaping a name or a text would corrupt it
// silently. Values reaching the template are validated (rules/rust.md "Validation
// first"), which is what makes an escaper needless rather than merely
// inconvenient.
#[template(path = "bind/zone.db.j2", escape = "none")]
pub struct ZoneFile<'a> {
    /// The zone apex as an absolute name, with the trailing dot.
    pub origin: &'a str,
    /// The `$TTL` of records that state none; the caller states every TTL.
    pub default_ttl: u32,
    /// The SOA primary name server, absolute.
    pub primary_nameserver: &'a str,
    /// The SOA hostmaster mailbox in name form, absolute.
    pub hostmaster: &'a str,
    /// The SOA serial.
    pub serial: u32,
    /// The SOA refresh interval, seconds.
    pub refresh: u32,
    /// The SOA retry interval, seconds.
    pub retry: u32,
    /// The SOA expire interval, seconds.
    pub expire: u32,
    /// The SOA negative-caching TTL, seconds.
    pub negative_ttl: u32,
    /// The apex name servers, absolute, in the order written.
    pub nameservers: &'a [String],
    /// The records, in the order written.
    pub records: &'a [ZoneRecordLine<'a>],
}

impl ZoneFile<'_> {
    /// Renders the zone file.
    ///
    /// # Errors
    ///
    /// Returns [`RenderError::Askama`] when the template itself fails, which can
    /// only happen if the template and this type have drifted apart.
    pub fn render_config(&self) -> Result<String, RenderError> {
        self.render().map_err(RenderError::Askama)
    }
}
