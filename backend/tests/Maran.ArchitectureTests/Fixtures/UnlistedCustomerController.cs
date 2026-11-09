using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests.Fixtures;

/// <summary>
/// The planted violation of the customer-operation census: an action a customer reaches that no matrix lists.
/// </summary>
/// <remarks>
/// Never composed by the panel. It exists so that
/// <see cref="CustomerOperationMatrixTests.The_census_names_a_customer_operation_the_matrix_omits"/> has
/// something real to refuse; without it the census is satisfied by an empty answer
/// (rules/README.md "Mechanical enforcement").
/// </remarks>
[Route("api/v1/architecture-tests/customer-unlisted")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
public sealed class UnlistedCustomerController : ControllerBase
{
    /// <summary>A customer deletes it, and nothing says it was considered.</summary>
    /// <returns>Nothing meaningful; the body is never executed.</returns>
    [HttpDelete("{id:guid}")]
    public IActionResult DeleteAsync()
    {
        return Ok();
    }
}
