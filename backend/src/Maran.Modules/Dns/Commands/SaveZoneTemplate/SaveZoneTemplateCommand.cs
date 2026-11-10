using System.Text.Json.Serialization;
using Maran.Modules.Dns.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Dns.Commands.SaveZoneTemplate;

/// <summary>
/// Creates a zone template, or replaces an existing one whole when the route names it. Administrator only.
/// </summary>
/// <param name="Name">The template's name; unique, compared without regard to case.</param>
/// <param name="Description">What the template is for, or an empty string.</param>
/// <param name="Records">The complete record set. Each record's value may name the closed template variables.</param>
/// <param name="TemplateId">
/// The template to replace, from the route, or empty to create one. Established by the server; never bound from
/// the body, so a body cannot aim a creation at an existing template.
/// </param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Established by the server.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Established by the server.</param>
public sealed record SaveZoneTemplateCommand(
    string Name,
    string Description,
    IReadOnlyList<TemplateRecordDraft> Records,
    [property: JsonIgnore][property: BindNever][BindNever] Guid TemplateId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
