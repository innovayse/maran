using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// The answer to creating an API key: the key's record and the plaintext key, which exists nowhere
/// else and is never shown again.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONLY type that carries a key's plaintext. The create handler returns it in memory
/// through the message bus and the controller writes it into the one response; it is never stored,
/// logged, journalled or sent on a durable queue.
/// </para>
/// <para>
/// <see cref="Secret"/> is a <see cref="SensitiveString"/> for the reason every secret-carrying record
/// in this panel is: a record's generated <c>ToString()</c> prints its properties, and a plain string
/// would be printed by the first log call that mentioned the answer. The panel-wide JSON converter
/// writes the real value, as a plain string, on this one response.
/// </para>
/// </remarks>
/// <param name="Key">The key's record, as the list will show it from now on.</param>
/// <param name="Secret">The complete key to hand to the integration. Shown once.</param>
public sealed record ApiKeyCreatedDto(ApiKeyDto Key, SensitiveString Secret);
