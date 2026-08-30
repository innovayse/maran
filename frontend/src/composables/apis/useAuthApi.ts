import { useApi } from '../useApi'
import type {
  AuthApi,
  AuthenticatedUser,
  CompleteSetupRequest,
  LoginRequest,
  LoginResult,
  RecoveryCodes,
  Session,
  SetupState,
  TotpEnrolment,
  VerifyTwoFactorRequest,
} from '../../types/auth'

/** The endpoints sign-in, sign-out and two-factor management live under. */
const AUTH_PATH = '/api/v1/auth'

/** The endpoint the caller's own signed-in devices are listed and revoked through. */
const SESSIONS_PATH = '/api/v1/sessions'

/** The endpoint first-run setup is driven through. */
const SETUP_PATH = '/api/v1/setup'

/**
 * Builds the authentication API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an
 * anonymous entry in the returned object: the name is what appears in a stack
 * trace, and the doc block is where the non-obvious details of each endpoint —
 * why the username is repeated in a query string, why a call opts out of the
 * retry — are recorded next to the call they govern (rules/vue.md).
 * @returns The {@link AuthApi} bound to the panel's authentication endpoints.
 */
export const useAuthApi = (): AuthApi => {
  const api = useApi()

  /**
   * Signs in with a username and password.
   *
   * The username is repeated in the query string on purpose: the backend's rate
   * limiter partitions by (address, username) and its partition resolver runs
   * before the request body can be read. The credential actually checked is
   * always the one in the body, so a caller who omits or forges the query value
   * only shares a coarser bucket — never a larger budget.
   *
   * Opts out of the 401 retry: a wrong password is not a stale token, and
   * retrying would spend a refresh and hide the real answer.
   * @param request The credentials.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The sign-in result, which may report that a second factor is owed.
   */
  const login = (request: LoginRequest, signal?: AbortSignal): Promise<LoginResult> => {
    // The username travels in the body only. It used to be repeated in the query string, for a
    // rate limiter that keyed on it — a key the caller could change at will, so it bounded
    // nothing and has been removed. A login name in a URL is written to every access log and
    // proxy along the way; in the body it is not.
    return api.post<LoginResult>(`${AUTH_PATH}/login`, request, signal, false)
  }

  /**
   * Completes a sign-in that stopped for a second factor.
   * @param request The credentials from the first step plus the code.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The completed sign-in result.
   */
  const verifyTwoFactor = (request: VerifyTwoFactorRequest, signal?: AbortSignal): Promise<LoginResult> => {
    return api.post<LoginResult>(`${AUTH_PATH}/two-factor`, request, signal, false)
  }

  /**
   * Exchanges the refresh cookie for a new access token, rotating the cookie.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The new token and the user it belongs to.
   */
  const refresh = (signal?: AbortSignal): Promise<LoginResult> => {
    return api.post<LoginResult>(`${AUTH_PATH}/refresh`, undefined, signal, false)
  }

  /**
   * Signs out of this device.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once the session has been ended.
   */
  const logout = (signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(`${AUTH_PATH}/logout`, undefined, signal, false)
  }

  /**
   * Signs out of every device. Requires a valid access token, so it keeps the retry.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once every session has been ended.
   */
  const logoutEverywhere = (signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(`${AUTH_PATH}/logout-all`, undefined, signal)
  }

  /**
   * Reports whether the panel already has an administrator.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The setup state.
   */
  const setupState = (signal?: AbortSignal): Promise<SetupState> => {
    return api.get<SetupState>(`${SETUP_PATH}/state`, signal)
  }

  /**
   * Creates the panel's first administrator.
   * @param request The one-time token and the administrator's details.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The administrator that was created.
   */
  const completeSetup = (
    request: CompleteSetupRequest,
    signal?: AbortSignal,
  ): Promise<AuthenticatedUser> => {
    return api.post<AuthenticatedUser>(SETUP_PATH, request, signal, false)
  }

  /**
   * Lists the caller's live sessions. Takes no user parameter: the endpoint is
   * scoped to the caller's own token, so there is nothing to point elsewhere.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The caller's signed-in devices.
   */
  const listSessions = (signal?: AbortSignal): Promise<Session[]> => {
    return api.get<Session[]>(SESSIONS_PATH, signal)
  }

  /**
   * Ends one of the caller's sessions.
   * @param id The session to end.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once it has been ended.
   */
  const revokeSession = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${SESSIONS_PATH}/${encodeURIComponent(id)}`, signal)
  }

  /**
   * Starts a two-factor enrolment. Nothing is enabled until it is confirmed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The secret and its provisioning URI.
   */
  const beginTwoFactorEnrolment = (signal?: AbortSignal): Promise<TotpEnrolment> => {
    return api.post<TotpEnrolment>(`${AUTH_PATH}/two-factor/enrol`, undefined, signal)
  }

  /**
   * Completes an enrolment by proving the secret works.
   * @param secret The secret handed out by the enrolment step.
   * @param code A code the user's authenticator produced from it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The recovery codes, readable this once and never again.
   */
  const confirmTwoFactorEnrolment = (
    secret: string,
    code: string,
    signal?: AbortSignal,
  ): Promise<RecoveryCodes> => {
    return api.post<RecoveryCodes>(`${AUTH_PATH}/two-factor/confirm`, { secret, code }, signal)
  }

  /**
   * Turns the second factor off, for a caller who can still satisfy it.
   * @param code A current code or one of the recovery codes.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once it has been turned off.
   */
  const disableTwoFactor = (code: string, signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(`${AUTH_PATH}/two-factor/disable`, { code }, signal)
  }

  return {
    login,
    verifyTwoFactor,
    refresh,
    logout,
    logoutEverywhere,
    setupState,
    completeSetup,
    listSessions,
    revokeSession,
    beginTwoFactorEnrolment,
    confirmTwoFactorEnrolment,
    disableTwoFactor,
  }
}
