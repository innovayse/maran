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
            "Maran.Modules.Cron.Common.CronRunDto",
            "Outcome",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.outcome.{member}",
            "A run started by hand succeeded, failed or was stopped: a closed set the history draws as a "
            + "badge and offers as its one filter (issue #158)."),
        new(
            "Maran.Modules.Cron.Common.CronRunDto",
            "Limit",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.limit.{member}",
            "The agent's two ceilings, runtime and output: a closed set the result dialog explains in one "
            + "sentence each (issue #158)."),
        new(
            "Maran.Modules.Cron.Common.CronRunSummaryDto",
            "Outcome",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.outcome.{member}",
            "A run started by hand succeeded, failed or was stopped: a closed set the history draws as a "
            + "badge and offers as its one filter (issue #158)."),
        new(
            "Maran.Modules.Cron.Common.CronRunSummaryDto",
            "Limit",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "cron.runs.limit.{member}",
            "The agent's two ceilings, runtime and output: a closed set the result dialog explains in one "
            + "sentence each (issue #158)."),
        new(
            "Maran.Modules.Accounts.Common.AccountDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.status.{member}",
            "An account is active or suspended and the list draws the two as badges. The set is "
            + "closed by the domain rather than by what has been implemented, so a bundle can hold "
            + "a word for every value it will ever receive."),
        new(
            "Maran.Modules.Accounts.Common.AccountDetailDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.status.{member}",
            "The detail screen draws the same badge from the same closed set as the list."),
        new(
            "Maran.Modules.Accounts.Common.MyAccountDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.status.{member}",
            "The customer's own account screen draws the same badge from the same closed set the "
            + "administrator's list and detail screens already use; the bundle already words every "
            + "value to offer those two."),
        new(
            "Maran.Modules.Accounts.Common.AccountResourceUsageDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "accounts.resources.kinds.{member}",
            "The resource kinds are a closed Sdk enum, one per counting module, and the summary card "
            + "labels each row with the same words the customer's limits screen already uses."),
        new(
            "Maran.Modules.Accounts.Common.AccountResourceUsageDto",
            "State",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/accountResourceSummary.ts",
            "Whether a count was taken decides how the row is drawn, a meter or the word "
            + "'unavailable'; the state itself is never printed as a name."),
        new(
            "Maran.Modules.Accounts.Common.AccountResourceUsageDto",
            "LimitKind",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/accountResourceSummary.ts",
            "Whether the plan caps the resource decides whether a meter or a bare count is drawn; "
            + "the value itself is never printed as a name."),
        new(
            "Maran.Modules.Accounts.Common.ResourceCeilingDto",
            "Kind",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/plan.ts",
            "Whether a plan caps CPU, memory or processes decides whether the form shows a figure or a "
            + "ticked 'No limit'; the value itself is never printed as a name."),
        new(
            "Maran.Modules.Backups.Common.BackupDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "backups.status.{member}",
            "Running, completed and failed are the three states a run can be in, drawn as a badge "
            + "whose tone carries half the meaning; the words belong with the tone."),
        new(
            "Maran.Modules.Backups.Common.BackupDto",
            "Kind",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "backups.kind.{member}",
            "Why a backup was taken is a closed set the panel itself decides between, and the "
            + "column shows it as one word."),
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
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "backups.schedule.frequency.{member}",
            "Daily and weekly are the options of a form control the SPA renders; the same words "
            + "have to exist in the bundle for the picker regardless."),
        new(
            "Maran.Modules.Backups.Common.BackupScheduleDto",
            "DayOfWeekUtc",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/pages/backups/BackupSchedulePage.vue",
            "A weekday is named by the platform's own locale data, not by anything this product "
            + "authored: the picker labels it through Intl in the interface language. A display "
            + "name on the wire would be a second, worse translation of a word every operating "
            + "system already knows."),
        new(
            "Maran.Modules.Backups.Common.RestoreOutcomeDto",
            "FailureCode",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/types/backup.ts",
            "This body is answered only for a restore that replaced everything it set out to, and "
            + "the code is empty on every such response. A restore that failed is a problem "
            + "document carrying the backend's own localized sentence, not this DTO."),
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
            "Maran.Modules.Notifications.Common.SmtpSettingsDto",
            "Security",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "app.smtp.security.{member}",
            "The options of the settings form's own picker, which the bundle has to word anyway to "
            + "offer the choice."),
        new(
            "Maran.Modules.Sites.Common.SiteDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.status.{member}",
            "Enabled or disabled, drawn as a badge from a set the domain closes."),
        new(
            "Maran.Modules.Sites.Common.SiteDto",
            "BackendType",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.backendType.{member}",
            "What serves the site is a closed choice this panel offers when the site is created, "
            + "so the bundle already words all three to offer it."),
        new(
            "Maran.Modules.Sites.Common.SiteDetailDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.status.{member}",
            "The detail screen draws the list's badge from the same closed set."),
        new(
            "Maran.Modules.Sites.Common.SiteDetailDto",
            "BackendType",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "sites.backendType.{member}",
            "The same closed choice the create form offers, shown back on the detail screen."),
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
            "Agreement",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/ftp/FtpsServerPanel.vue",
            "A verdict the server panel turns into an explanatory sentence, or into nothing when "
            + "intention and observation agree; it is never printed as the name of anything."),
        new(
            "Maran.Modules.Tasks.Common.PanelTaskDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "tasks.statuses.{member}",
            "Running, completed, failed — the badge on the tasks table, from a set the panel "
            + "closes."),
        new(
            "Maran.Modules.Tasks.Common.TaskFailureDto",
            "Code",
            DisplayNameExemptionKind.NotAnOperatorFacingName,
            "frontend/src/components/tasks/TaskLivePane.vue",
            "Shown framed as an identifier — \"Code: X\" — beneath its sibling Message, the "
            + "sentence the backend resolves for the reader's culture (issue #272). The defect this "
            + "law is about is a code printed AS the name of the thing; a code labelled as a code "
            + "beside its own sentence is the cure for it, not an instance of it."),
        new(
            "Maran.Modules.Tasks.Common.TaskStreamEndDto",
            "Status",
            DisplayNameExemptionKind.SpaOwnedVocabulary,
            "tasks.statuses.{member}",
            "The final frame of the stream carries the same badge value as the row it closes."),
    ];
}
