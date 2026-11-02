namespace Maran.Agent.Client.Services.DbService;

/// <summary>
/// A census of this server's database-level grant table read by a pass that changed nothing: what it
/// would rewrite, and what it would leave.
/// </summary>
/// <param name="ExaminedGrants">
/// How many rows were read and classified. Every row lands in exactly one bucket, so
/// <paramref name="AlreadyCorrect"/> plus the two list counts equals this number — which is what
/// makes the report a census rather than a list of what happened to be interesting.
/// </param>
/// <param name="AlreadyCorrect">
/// Rows that already held the escaped form, or whose name cannot hold a wildcard at all. Nothing
/// would be sent for these. A clean installation reports every row here.
/// </param>
/// <param name="WouldRepair">Rows the repair would rewrite.</param>
/// <param name="Refused">Rows the repair would leave exactly as found, each with the reason.</param>
/// <remarks>
/// The sibling of <see cref="GrantRepairReportDto"/>, which reports a pass that DID rewrite. They are
/// two types because the wire sends both lists in one message and leaves one of them empty, and a
/// caller who must remember which one to read is a caller who can read the wrong one: "would repair"
/// and "repaired" must never be mistaken for each other.
/// </remarks>
public sealed record GrantInspectionReportDto(
    uint ExaminedGrants,
    uint AlreadyCorrect,
    IReadOnlyList<RepairedGrantDto> WouldRepair,
    IReadOnlyList<RefusedGrantDto> Refused);
