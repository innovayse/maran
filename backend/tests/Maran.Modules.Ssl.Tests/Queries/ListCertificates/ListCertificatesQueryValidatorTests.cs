using Maran.Modules.Ssl.Domain.Enums;
using Maran.Modules.Ssl.Queries.ListCertificates;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Ssl.Tests.Queries.ListCertificates;

/// <summary>Behavioral contract of <see cref="ListCertificatesQueryValidator"/>.</summary>
public sealed class ListCertificatesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListCertificatesQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListCertificatesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListCertificatesQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("shop"),
            Sort: SortRequest.Parse("-domain"),
            SiteId: Guid.NewGuid(),
            Status: CertificateStatus.RenewalFailing,
            Expiring: true)).IsValid);
    }

    /// <summary>A status that names no member of the enum is refused with its own code.</summary>
    [Fact]
    public void A_status_that_names_no_member_is_refused_with_its_own_code()
    {
        var result = _validator.Validate(new ListCertificatesQuery(Status: (CertificateStatus)7));

        Assert.Equal("CertificateStatusUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_list_does_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListCertificatesQuery(Sort: SortRequest.Parse("issuedAt")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListCertificatesQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
