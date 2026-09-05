import { useApi } from '../useApi'
import type { SaveSecurityPolicyRequest, SecurityPolicy, SecurityPolicyApi } from '../../types/securityPolicy'

/** The endpoint the panel's security policy is read from and written to. */
const SECURITY_POLICY_PATH = '/api/v1/security-policy'

/**
 * Builds the security-policy API on top of the shared low-level client.
 *
 * Two calls on one path, because the panel has exactly one policy: a GET that
 * reads it and a PUT that replaces all of it. There is no PATCH, and adding one
 * would invite a screen to send half a policy and leave the other half to whatever
 * was already stored.
 * @returns The {@link SecurityPolicyApi} bound to the panel's security-policy endpoint.
 */
export const useSecurityPolicyApi = (): SecurityPolicyApi => {
  const api = useApi()

  /**
   * Reads the policy the panel is enforcing.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The stored policy.
   */
  const get = (signal?: AbortSignal): Promise<SecurityPolicy> => {
    return api.get<SecurityPolicy>(SECURITY_POLICY_PATH, signal)
  }

  /**
   * Replaces the policy wholesale.
   * @param request The complete policy to store.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once it has been stored.
   */
  const save = (request: SaveSecurityPolicyRequest, signal?: AbortSignal): Promise<boolean> => {
    return api.put<boolean>(SECURITY_POLICY_PATH, request, signal)
  }

  return { get, save }
}
