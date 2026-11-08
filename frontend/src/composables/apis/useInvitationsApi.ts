import { useApi } from '../useApi'
import type { InvitationLink, InvitationsApi, InvitationStatus, InvitationSubject } from '../../types/invitation'

/**
 * The endpoint an invitation is read, replaced and resent through. An account's invitation sits
 * directly under it and a reseller's under `resellers/`, so the two id spaces never share a route.
 * Each call below states both paths in full instead of building one, so `maran api` can read the
 * route every request goes to.
 */
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
   * Reads where an invitation stands.
   * @param subject Whose invitation is asked about.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The invitation's state and, while it is pending, when it lapses.
   */
  const getStatus = (subject: InvitationSubject, signal?: AbortSignal): Promise<InvitationStatus> => {
    return subject.kind === 'reseller'
      ? api.get<InvitationStatus>(`${INVITATIONS_PATH}/resellers/${subject.id}`, signal)
      : api.get<InvitationStatus>(`${INVITATIONS_PATH}/${subject.id}`, signal)
  }

  /**
   * Issues a fresh invitation and returns its one-time link.
   * @param subject Whose login is being invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The link, shown once, and when it expires.
   */
  const issueLink = (subject: InvitationSubject, signal?: AbortSignal): Promise<InvitationLink> => {
    return subject.kind === 'reseller'
      ? api.post<InvitationLink>(`${INVITATIONS_PATH}/resellers/${subject.id}/link`, undefined, signal)
      : api.post<InvitationLink>(`${INVITATIONS_PATH}/${subject.id}/link`, undefined, signal)
  }

  /**
   * Mails the login's contact a fresh invitation.
   * @param subject Whose login is being (re)invited.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once a new invitation was sent; the answer is an empty 204.
   */
  const resend = (subject: InvitationSubject, signal?: AbortSignal): Promise<void> => {
    return subject.kind === 'reseller'
      ? api.post<void>(`${INVITATIONS_PATH}/resellers/${subject.id}/resend`, undefined, signal)
      : api.post<void>(`${INVITATIONS_PATH}/${subject.id}/resend`, undefined, signal)
  }

  return { getStatus, issueLink, resend }
}
