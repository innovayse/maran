import { useApi } from '../useApi'
import type { StartViewAsRequest, ViewAsApi, ViewAsSession, ViewAsState } from '../../types/viewAs'

/** The endpoint an operator views the panel as an account through (issue #317). */
const VIEW_AS_PATH = '/api/v1/view-as'

/**
 * Builds the view-as API on top of the shared low-level client. Every call speaks for the caller's
 * own token: no call names an operator or a session, so none can be pointed at somebody else.
 * @returns The {@link ViewAsApi} bound to the view-as endpoint.
 */
export const useViewAsApi = (): ViewAsApi => {
  const api = useApi()

  /**
   * Reads the view-as session the current token belongs to.
   *
   * Keeps the shared 401 retry on purpose: a view-as token that ended elsewhere (Back in another
   * tab, expiry, the customer revoking it) answers 401, the retry renews from the operator's own
   * refresh cookie, and the replayed read then truthfully answers "not viewing" for the operator.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The session, or a state with none.
   */
  const current = (signal?: AbortSignal): Promise<ViewAsState> => {
    return api.get<ViewAsState>(VIEW_AS_PATH, signal)
  }

  /**
   * Starts viewing as the login that owns an account. Administrators only; the panel answers 404
   * for an account outside the caller's scope.
   * @param request The account.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The view-as token and what the banner shows.
   */
  const start = (request: StartViewAsRequest, signal?: AbortSignal): Promise<ViewAsSession> => {
    return api.post<ViewAsSession>(VIEW_AS_PATH, request, signal)
  }

  /**
   * Ends the view-as session the current token belongs to. When it had already ended, the shared
   * 401 retry renews to the operator's own token and the replay answers 409 ("not viewing"); the
   * store treats both answers as "back", because in both the operator is no longer viewing.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the session has been ended; the answer is an empty 204.
   */
  const stop = (signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(VIEW_AS_PATH, signal)
  }

  return { current, start, stop }
}
