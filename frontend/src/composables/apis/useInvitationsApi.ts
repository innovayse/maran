import { useApi } from '../useApi'
import type { InvitationLink, InvitationsApi, InvitationStatus } from '../../types/invitation'

/** The endpoint an account owner's invitation is read, replaced and resent through. */
const INVITATIONS_PATH = '/api/v1/invitations'

/**
 * Builds the invitations API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 * @returns The {@link InvitationsApi} bound to the panel's invitation endpoints.
 */
export const useInvitationsApi = (): InvitationsApi => {
  const api = useApi()

  /**
   * Reads where an account owner's invitation stands.
   * @param accountId The hosting account being asked about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The invitation's state and, while it is pending, when it lapses.
   */
  const getStatus = (accountId: string, signal?: AbortSignal): Promise<InvitationStatus> => {
    return api.get<InvitationStatus>(`${INVITATIONS_PATH}/${accountId}`, signal)
  }

  /**
   * Issues a fresh invitation and returns its one-time link.
   * @param accountId The hosting account whose owner is being invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The link, shown once, and when it expires.
   */
  const issueLink = (accountId: string, signal?: AbortSignal): Promise<InvitationLink> => {
    return api.post<InvitationLink>(`${INVITATIONS_PATH}/${accountId}/link`, undefined, signal)
  }

  /**
   * Mails the account's owner a fresh invitation.
   * @param accountId The hosting account whose owner is being (re)invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns True once a new invitation was sent.
   */
  const resend = (accountId: string, signal?: AbortSignal): Promise<boolean> => {
    return api.post<boolean>(`${INVITATIONS_PATH}/${accountId}/resend`, undefined, signal)
  }

  return { getStatus, issueLink, resend }
}
