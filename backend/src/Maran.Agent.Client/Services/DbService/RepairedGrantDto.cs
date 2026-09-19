namespace Maran.Agent.Client.Services.DbService;

/// <summary>
/// One grant-table row whose stored pattern was — or would be — narrowed back to the single database
/// it was always meant to name.
/// </summary>
/// <param name="DatabaseName">
/// The fully-qualified database name the grant was always meant to name, as the agent decoded it
/// from the row. Never a pattern: the metacharacters are what the repair removes.
/// </param>
/// <param name="DbUsername">The fully-qualified user the grant belongs to.</param>
/// <param name="AlsoMatchedDatabases">
/// Other databases on the same server that the OLD pattern also matched.
/// <para>
/// <b>Evidence of EXPOSURE and not of use, and no caller may read it as either less or more.</b> A
/// name here is a database the repaired credential could have read and written for as long as the row
/// stood. An empty list does NOT clear the row: a matching database may have been created and dropped
/// in between, and a pattern is matched at connection time rather than at repair time. Whether the
/// reach was exercised is answered only by the server's query log, which is off on a default install
/// (docs/superpowers/notes/2026-09-13-grant-repair-threat-note.md).
/// </para>
/// </param>
public sealed record RepairedGrantDto(
    string DatabaseName,
    string DbUsername,
    IReadOnlyList<string> AlsoMatchedDatabases);
