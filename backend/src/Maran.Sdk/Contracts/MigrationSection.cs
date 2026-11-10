namespace Maran.Sdk.Contracts;

/// <summary>
/// What one module says about one account for a move to another server: the module's id, the version of the
/// shape it wrote, and the payload as JSON (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list of named fields, never a serialised entity.</b> Each module's exporter builds its payload
/// from a record of its own that lists the fields that travel, so a secret column added to an entity later is
/// not exported by accident (the rule <see cref="Interfaces.IPersonalDataProvider"/> already follows). A
/// password, hash, token or private key is never a field of any section.
/// </para>
/// <para>
/// The payload is opaque to everything but the module that wrote it: the importer of the same module on the
/// target reads it back, and a version it does not know is refused rather than guessed at.
/// </para>
/// </remarks>
/// <param name="ModuleId">The id of the module that wrote the section, as in its manifest.</param>
/// <param name="Version">The version of the payload's shape, starting at 1.</param>
/// <param name="Json">The payload, as a JSON document the owning module understands.</param>
/// <param name="ResourceCount">
/// How many resources of the module's kind the section holds (sites, databases, logins, entries), so the
/// account's plan limits can be judged without reading another module's payload. Zero for a section that is not
/// a count of resources.
/// </param>
public sealed record MigrationSection(string ModuleId, int Version, string Json, int ResourceCount = 0);
