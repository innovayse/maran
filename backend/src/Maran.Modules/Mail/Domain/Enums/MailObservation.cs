namespace Maran.Modules.Mail.Domain.Enums;

/// <summary>Whether the host was seen to do a thing, with "could not tell" kept apart from "no".</summary>
public enum MailObservation
{
    /// <summary>The host was not asked or could not be read; this is not an answer of "no".</summary>
    NotObserved,

    /// <summary>The host was seen to do it.</summary>
    Yes,

    /// <summary>The host was seen NOT to do it.</summary>
    No,
}
