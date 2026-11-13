namespace Maran.Host.Demo.Guard.Enums;

/// <summary>What the demo's text screen found in one piece of visitor-typed text.</summary>
public enum DemoTextVerdict
{
    /// <summary>Nothing the screen refuses: the text may be stored and shown to the next visitor.</summary>
    Clean,

    /// <summary>The text contains a term from the blocked-term lists (<c>Resources/DemoBlockedTerms.*.txt</c>).</summary>
    BlockedTerm,

    /// <summary>The text contains a link to a host that is not on the allow-list, or a script-like scheme.</summary>
    ForeignUrl,

    /// <summary>
    /// The screen could not judge the text: it is not well-formed Unicode, or judging it outlived its time limit.
    /// Refused like a finding, because a screen that fails open is one a crafted input switches off.
    /// </summary>
    Unreadable,
}
