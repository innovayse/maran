namespace Maran.Modules.Databases.Common;

/// <summary>
/// One grant this panel issued whose stored name had become a wildcard pattern, as an administrator
/// reads it — either rewritten, or listed by a report as one that would be.
/// </summary>
/// <param name="DatabaseName">The fully-qualified database the grant was always meant to name.</param>
/// <param name="DbUsername">The fully-qualified user the grant belongs to.</param>
/// <param name="AlsoMatchedDatabases">
/// Other databases on this server that the old pattern also matched.
/// <para>
/// <b>Exposure, not use.</b> A name here is a database this credential COULD have read and written
/// while the row stood. An empty list does not clear the row: a matching database may have been
/// created and dropped in between, and a pattern is matched when a client connects rather than when
/// the panel looks. The screen that renders this says both halves, because a list is read as a
/// finding and an empty list is read as an all-clear, and only one of those readings is wrong in a
/// direction that matters.
/// </para>
/// </param>
public sealed record RepairedGrantDto(
    string DatabaseName,
    string DbUsername,
    IReadOnlyList<string> AlsoMatchedDatabases);
