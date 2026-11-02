using System.Text.Json.Serialization;

namespace Maran.Host.Dashboard;

/// <summary>
/// Everything the panel's landing screen shows, for the caller who asked. Exactly one of two shapes,
/// told apart by <c>audience</c>, so a section exists only on the shape that has it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who gets which shape is decided on the server</b>, by <c>ICurrentUser.IsAdmin</c>, which fails
/// closed: an administrator receives <see cref="AdministratorDashboardDto"/> with every section, and
/// everyone else receives <see cref="CustomerDashboardDto"/>, which has no section at all. The shape
/// replaced one record carrying an <c>isAdministrator</c> flag beside five nullable sections, which
/// allowed combinations no code path produced (the flag true with null sections, false with data) and
/// made the SPA read two members to learn one fact.
/// </para>
/// <para>
/// <b>Whether a section could be read is its <see cref="DashboardSection{T}"/> state</b>, never an
/// empty list or a null (issue #248). An empty list is a real answer — no services watched, no
/// audit entries yet — and a different one from a module that did not answer, so the two are
/// carried as different values rather than drawn the same way.
/// </para>
/// <para>
/// The endpoint must serialize it as this declared type for <c>audience</c> to be written, which is
/// why it returns <c>Results.Ok&lt;DashboardDto&gt;</c>: a value serialized as its own derived type
/// carries no discriminator.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "audience")]
[JsonDerivedType(typeof(AdministratorDashboardDto), "administrator")]
[JsonDerivedType(typeof(CustomerDashboardDto), "customer")]
public abstract record DashboardDto;
