namespace Maran.Host.Demo.Guard.Enums;

/// <summary>
/// The area of the panel a refused demo action belongs to. The area is what the SPA keys the reason
/// shown beside a blocked control on; its wire spelling is <see cref="DemoRefusalAreaIds.IdOf"/>.
/// </summary>
/// <remarks>
/// The first seven members are the areas the SPA knows by name. The rest exist because some refusals
/// fit none of them (a password, an invitation, an outside service); the SPA shows the generic
/// sentence for an area it has no special wording for.
/// </remarks>
public enum DemoRefusalArea
{
    /// <summary>Security policy, audit retention, panel log level and the address whitelist.</summary>
    SecurityPolicy,

    /// <summary>Outgoing mail: server settings, test mail, alert recipients, password-reset mail.</summary>
    Smtp,

    /// <summary>The panel's own certificate.</summary>
    PanelCertificate,

    /// <summary>Updating the panel itself.</summary>
    PanelUpdate,

    /// <summary>Enrolling, confirming or disabling two-factor sign-in.</summary>
    TwoFactor,

    /// <summary>API keys, an administrator's and a reseller's.</summary>
    ApiKeys,

    /// <summary>Installing a licence.</summary>
    Licence,

    /// <summary>Passwords and sessions, which every visitor shares.</summary>
    Credentials,

    /// <summary>Invitations, team members and membership offers: new ways to sign in.</summary>
    AccessPaths,

    /// <summary>Anything that connects to a service outside the demo.</summary>
    ExternalServices,

    /// <summary>An account, reseller or plan that holds a demo login or gives it its screens.</summary>
    ProtectedResource,

    /// <summary>An endpoint the demo has not classified; refused until someone decides.</summary>
    Unclassified,
}
