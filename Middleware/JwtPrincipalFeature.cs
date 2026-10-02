using System.Security.Claims;

namespace fn_lot_scanner.Middleware;

// Carries the resolved employee identity through FunctionContext.Features, same
// pattern as fn-mystore's JwtPrincipalFeature (ported so fn-lot-scanner accepts the
// exact same employee tokens mystore issues -- FR-011/plan.md Technical Context).
public class JwtPrincipalFeature(ClaimsPrincipal principal, string employeeId, int? companyId)
{
    public ClaimsPrincipal Principal { get; } = principal;
    public string EmployeeId { get; } = employeeId;
    public int? CompanyId { get; } = companyId;
}
