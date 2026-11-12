using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// Decides the request body limit a site's vhost carries: the figure the owner chose, or the plan's
/// upload bound when the owner chose none (issue #556).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the plan's bound is the default.</b> A PHP plan bounds <c>post_max_size</c> (and
/// <c>upload_max_filesize</c>) at <see cref="PhpLimitsSnapshot.UploadMaxMb"/>. A web server that still
/// accepted 512 MiB bodies in front of a pool that refuses anything above that bound would carry every
/// oversized upload to PHP before refusing it, so the vhost's limit follows the plan unless the owner
/// states a figure.
/// </para>
/// <para>
/// <b>The plan is a ceiling for a stated figure too.</b> A figure above the bound is refused when it is
/// stated (<see cref="ExceedsPlan"/>); a stored one that a later plan edit left above the bound is cut to
/// it when the vhost is rendered, never deleted, so raising the bound gives the figure back. The same
/// rule <see cref="PhpSettingsPlanClamp"/> applies to the pool.
/// </para>
/// <para>
/// Only a PHP site is governed: the upload bound is a PHP bound, and a static site refuses a stated figure
/// altogether while a proxied or Node site is not the plan's PHP. For those the stated figure travels
/// unchanged and an absent one stays absent, which the agent renders as its own default.
/// </para>
/// </remarks>
public static class SiteBodySizePolicy
{
    /// <summary>The body limit a stored site's vhost is rendered with, decided once for every mapper.</summary>
    /// <param name="site">The stored site, with its options.</param>
    /// <param name="limits">The plan's PHP bounds of the site's account.</param>
    /// <returns>See <see cref="Effective(SiteBackendType, int?, PhpLimitsSnapshot)"/>.</returns>
    public static int? Effective(Site site, PhpLimitsSnapshot limits)
    {
        return Effective(site.BackendType, site.Options?.ClientMaxBodySizeMib, limits);
    }

    /// <summary>The body limit a vhost is rendered with, from a backend and the figure stated for it.</summary>
    /// <param name="backendType">The site's backend.</param>
    /// <param name="stated">The figure the owner stored in MiB, or null when the owner chose none.</param>
    /// <param name="limits">The plan's PHP bounds of the site's account.</param>
    /// <returns>
    /// For a PHP site the stated figure cut to the plan's bound, or the bound itself when none is stated;
    /// for any other site the stated figure unchanged (null when none).
    /// </returns>
    public static int? Effective(SiteBackendType backendType, int? stated, PhpLimitsSnapshot limits)
    {
        if (backendType != SiteBackendType.Php)
        {
            return stated;
        }

        var ceiling = Ceiling(limits);

        return stated is { } figure ? Math.Min(figure, ceiling) : ceiling;
    }

    /// <summary>Whether a figure the owner states is above what the plan allows the site.</summary>
    /// <param name="backendType">The site's backend.</param>
    /// <param name="stated">The figure in MiB, or null when none is stated.</param>
    /// <param name="limits">The plan's PHP bounds of the site's account.</param>
    /// <returns>True for a PHP site whose stated figure is above the plan's bound; false for any other site or none.</returns>
    public static bool ExceedsPlan(SiteBackendType backendType, int? stated, PhpLimitsSnapshot limits)
    {
        return backendType == SiteBackendType.Php && stated is { } figure && figure > Ceiling(limits);
    }

    /// <summary>The largest figure the owner may state for a site under the plan, for the screen that asks for it.</summary>
    /// <param name="backendType">The site's backend.</param>
    /// <param name="limits">The plan's PHP bounds of the site's account.</param>
    /// <returns>The plan's ceiling in MiB for a PHP site; null for any other site, which the plan does not cap.</returns>
    public static int? PlanCeiling(SiteBackendType backendType, PhpLimitsSnapshot limits)
    {
        return backendType == SiteBackendType.Php ? Ceiling(limits) : null;
    }

    /// <summary>The largest limit a PHP site's vhost may carry under the plan, in MiB.</summary>
    /// <param name="limits">The plan's PHP bounds.</param>
    /// <returns>
    /// The plan's bound for <c>post_max_size</c>, kept inside the range a site's option may take: a plan
    /// bound is at or below the agent's ceiling today, and the clamp keeps it so should that ever change.
    /// </returns>
    private static int Ceiling(PhpLimitsSnapshot limits)
    {
        var bound = PhpSettingCatalogue.PlanMaximum(PhpSettingNames.PostMaxSize, limits)
            ?? throw new InvalidOperationException("post_max_size has no plan bound.");

        return Math.Clamp(bound, SiteOptionsLimits.MinimumBodySizeMib, SiteOptionsLimits.MaximumBodySizeMib);
    }
}
