/**
 * The panel-wide security policy, from the three angles the settings screen needs
 * it: what the panel is enforcing, what a save sends, and the endpoints that carry
 * both (rules/vue.md "Types" — one domain per file).
 */

/**
 * The policy the panel is enforcing, exactly as `GET /api/v1/security-policy`
 * reports it (`SecurityPolicyDto`).
 */
export interface SecurityPolicy {
  /** The shortest password the panel accepts. */
  minimumPasswordLength: number
  /** Whether an administrator without a second factor is steered into enrolment. */
  forceTwoFactorForAdmins: boolean
  /** Consecutive failed sign-ins that lock an account. */
  maxFailedLoginAttempts: number
  /** How long a locked account stays locked, in minutes. */
  lockoutMinutes: number
}

/**
 * The body of `PUT /api/v1/security-policy`. Identical in shape to
 * {@link SecurityPolicy} because the endpoint is a whole-document replace: there is
 * one policy on a panel and the request carries all of it.
 */
export type SaveSecurityPolicyRequest = SecurityPolicy

/** Public surface of the security-policy API composable. */
export interface SecurityPolicyApi {
  /** Reads the policy the panel is enforcing. */
  get: (signal?: AbortSignal) => Promise<SecurityPolicy>
  /** Replaces the policy wholesale. */
  save: (request: SaveSecurityPolicyRequest, signal?: AbortSignal) => Promise<boolean>
}
