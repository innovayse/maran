namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>The two dates that bound the support life of one PHP version, one row of the lifecycle table.</summary>
/// <param name="ActiveSupportEnds">The last day the version receives bug fixes as well as security fixes.</param>
/// <param name="SecuritySupportEnds">The last day the version receives security fixes; the day it reaches end of life.</param>
public sealed record PhpLifecycleDates(DateOnly ActiveSupportEnds, DateOnly SecuritySupportEnds);
