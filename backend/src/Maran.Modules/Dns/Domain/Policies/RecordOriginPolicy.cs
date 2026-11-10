using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The one statement of who may change or remove a record, by who wrote it. The zone and the screens both ask
/// it, so the rule is written once and a record the owner may not touch is never offered as editable.
/// </summary>
/// <remarks>
/// A customer's own record and a record a template created for them are the owner's to edit or delete; editing
/// a template record makes it the customer's. A record the panel, the mail facility or a certificate
/// validation keeps stays with that facility: those facilities rewrite their records, and an owner's edit would
/// be overwritten or, worse, would break what depends on them.
/// </remarks>
public static class RecordOriginPolicy
{
    /// <summary>Tells whether the zone's owner may change or remove a record written by <paramref name="origin"/>.</summary>
    /// <param name="origin">Who wrote the record.</param>
    /// <returns>True for <see cref="RecordOrigin.Customer"/> and <see cref="RecordOrigin.Template"/>.</returns>
    public static bool IsOwnerEditable(RecordOrigin origin)
    {
        return origin is RecordOrigin.Customer or RecordOrigin.Template;
    }

    /// <summary>Tells who owns a record once its owner has changed it.</summary>
    /// <param name="origin">Who wrote the record before the change.</param>
    /// <returns>Always <see cref="RecordOrigin.Customer"/>: a changed record is no longer what the template wrote.</returns>
    /// <exception cref="InvalidOperationException">The origin is not owner-editable, so no owner change can be made.</exception>
    public static RecordOrigin AfterOwnerEdit(RecordOrigin origin)
    {
        if (!IsOwnerEditable(origin))
        {
            throw new InvalidOperationException("Only a record the owner may edit changes owner by being edited.");
        }

        return RecordOrigin.Customer;
    }
}
