namespace Maran.SharedKernel.Enums;

/// <summary>The direction a list is ordered in by its sort key.</summary>
public enum SortDirection
{
    /// <summary>Smallest first; written as a bare field name (<c>name</c>).</summary>
    Ascending = 0,

    /// <summary>Largest first; written with a leading minus (<c>-name</c>).</summary>
    Descending = 1,
}
