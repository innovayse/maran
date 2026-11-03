namespace Maran.ArchitectureTests;

/// <summary>
/// The one place a vocabulary member may be excused from carrying a localized display name, and the
/// evidence for each excuse.
/// </summary>
/// <remarks>
/// <para>
/// A law with silent exceptions decays into a list of things that used to be true, so nothing here
/// is taken on trust: <see cref="DisplayNameLawTests"/> resolves every locale key template in
/// English, Russian and Armenian, opens every SPA file named as evidence and requires it to still
/// mention the member, and asserts that every entry — the pending ones most of all — still names a
/// member the law would otherwise flag. An entry that has become unnecessary fails the suite until
/// it is deleted.
/// </para>
/// <para>
/// Adding an entry therefore costs a sentence and a piece of evidence, which is the price this
/// register is for. If neither can be written, the member needs a display name, not an entry.
/// </para>
/// </remarks>
public static class DisplayNameExemptions
{
    /// <summary>Every declared exemption, grouped by the module the DTO belongs to.</summary>
    public static readonly IReadOnlyList<DisplayNameExemption> Declared =
    [
        new(
            "Maran.Modules.Cron.Common.CronRunStoppedDto",
            "Limit",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.limit.{member}",
            "The agent's two ceilings, carried only by a run that one of them stopped, runtime and output: a closed set the result dialog explains in one "
            + "sentence each (issue #158)."),
        new(
            "Maran.Modules.Accounts.Common.AccountResourceUsageDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.resources.kinds.{member}",
            "The resource kinds are a closed Sdk enum, one per counting module, and the summary card "
            + "labels each row with the same words the customer's limits screen already uses."),
        new(
            "Maran.Modules.Backups.Common.BackupDestinationDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "backups.destinations.kinds.{member}.label",
            "The kind of storage is named and explained by the SPA, which needs both a label and a "
            + "sentence for it; a display name on the wire would carry the label and leave the "
            + "sentence homeless. The row's own English label is a different problem and is solved "
            + "on the wire by DisplayName."),
        new(
            "Maran.Modules.Backups.Common.BackupScheduleDto",
            "Frequency",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/pages/backups/BackupSchedulePage.vue",
            "The cadence is only echoed back into the schedule form's own picker, whose choices and "
            + "their names are the backend's (GET /api/v1/backup-options, issue #330), not a label "
            + "drawn from this field; no screen prints the field as a name."),
        new(
            "Maran.Modules.Backups.Common.BackupScheduleDto",
            "DayOfWeekUtc",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/pages/backups/BackupSchedulePage.vue",
            "A weekday is only echoed back into the schedule form's own picker, whose choices and "
            + "their names are the backend's (GET /api/v1/backup-options, issue #330): the "
            + "picker never prints this field as a name, so a display name beside it on the wire "
            + "would be a second, redundant translation of a word the options list already names."),
        new(
            "Maran.Modules.Firewall.Common.BanDto",
            "Reason",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "firewall.bans.reasons.{member}",
            "Manual or brute force: two values the panel itself writes, shown as a column of one "
            + "word each."),
        new(
            "Maran.Modules.Firewall.Common.FirewallRuleDto",
            "Protocol",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "firewall.protocols.{member}",
            "TCP and UDP are proper nouns that read the same in every language; the bundle carries "
            + "them so the rule table can label a column without asking the panel what TCP is "
            + "called in Armenian."),
        new(
            "Maran.Modules.Identity.Common.AuthenticatedUserDto",
            "Role",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "app.auth.role.{member}",
            "Administrator or customer, decided by this product and shown in the shell's own "
            + "chrome beside the user's name."),
        new(
            "Maran.Modules.Identity.Common.ProfileDto",
            "Role",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "app.auth.role.{member}",
            "The same administrator-or-customer vocabulary as AuthenticatedUserDto.Role, shown on "
            + "the profile page through the same keys."),
        new(
            "Maran.Modules.Monitoring.Common.MetricsChartDto",
            "Range",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/monitoring.ts",
            "The range is the value the SPA asked for, echoed back so a late answer can be matched "
            + "to the control that requested it. The screen prints its own control's label and "
            + "never this value."),
        new(
            "Maran.Modules.Monitoring.Common.ServiceStatusDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "monitoring.services.{member}",
            "Up, down and not-known are three badge tones as much as three words, and the badge "
            + "must carry text so the tone is never the only thing saying which state it is. The "
            + "service's identity beside it is the opposite case and is named by the backend."),
        new(
            "Maran.Modules.Monitoring.Common.ClockSyncDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "monitoring.clock.{member}",
            "The clock card's badge, a tone as much as a word, like ServiceStatusDto.State. The "
            + "sentence saying what the state means is the backend's, in Description (issue #150)."),
        new(
            "Maran.Modules.Sites.Common.SiteLogEndDto",
            "Reason",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.detail.logs.endReason.{member}",
            "Why a log tail stopped is the SPA's own chrome around a stream it is holding open, "
            + "and it is shown while no request is outstanding to ask the panel for wording."),
        new(
            "Maran.Modules.Ssl.Common.CertificateDto",
            "Source",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "certificates.source.{member}",
            "Issued here or uploaded: two values the panel writes itself, shown as a column."),
        new(
            "Maran.Modules.Ssl.Common.CertificateDto",
            "LastRenewalErrorCode",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/certificate.ts",
            "Carried for diagnosis beside the failure count; no screen prints it. It is on this "
            + "list rather than off the wire because a support session reads it from the response, "
            + "and the day a screen does show it this entry stops being true — which is what the "
            + "evidence check is for."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "DesiredState",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "ftp.server.desiredStates.{member}",
            "Never configured, on and off are the three things the panel can want of its one FTPS "
            + "daemon, drawn as the panel-setting badge; the set is closed by the domain."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "TlsEnforcement",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "ftp.server.tlsEnforcement.{member}",
            "Enforced, not enforced and not observed are three sentences the server panel draws under "
            + "the encryption line; the set is closed by the agent's own three-valued answer, and "
            + "not-observed is worded as an admission rather than as off."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "Certificate",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/ftp/FtpsServerPanel.vue",
            "A decision input and never a printed name: the panel only compares it with the self-signed "
            + "case to show its own warning sentence, and the certificate line prints the file path."),
        new(
            "Maran.Modules.Ftp.Common.FtpsStatusDto",
            "Agreement",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/ftp/FtpsServerPanel.vue",
            "A verdict the server panel turns into an explanatory sentence, or into nothing when "
            + "intention and observation agree; it is never printed as the name of anything."),
        new(
            "Maran.Host.Modules.ModuleDto",
            "State",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/module.ts",
            "Decides whether a module's entries are drawn open or locked and is never printed as a name: "
            + "the locked badge's text is the shell's own chrome, and the module's own name travels "
            + "beside it as DisplayName."),
        new(
            "Maran.Modules.Identity.Common.SubUserDto",
            "State",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "subUsers.states.{member}",
            "Active, invited, invited while the account is suspended, and suspended: a closed set the "
            + "team table draws as its status column, worded by the bundle beside the column's header."),
        new(
            "Maran.Modules.Sites.Common.PhpVersionDto",
            "DefaultStatus",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "php.table.defaultStatus.{member}",
            "Default, not default and not established are the three answers of the versions table's "
            + "default column, which the page words as yes, no and not established beside the badge tone."),
        new(
            "Maran.Modules.Tasks.Common.TaskFailureDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/tasks/TaskLivePane.vue",
            "Shown framed as an identifier — \"Code: X\" — beneath its sibling Message, the "
            + "sentence the backend resolves for the reader's culture (issue #272). The defect this "
            + "law is about is a code printed AS the name of the thing; a code labelled as a code "
            + "beside its own sentence is the cure for it, not an instance of it."),
    ];
}
