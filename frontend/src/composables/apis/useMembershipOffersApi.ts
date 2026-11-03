import { useApi } from '../useApi'
import type { MembershipOffer, MembershipOffersApi } from '../../types/membershipOffer'

/** The endpoint the caller's own membership offers are listed, accepted and declined at. */
const OFFERS_PATH = '/api/v1/me/membership-offers'

/**
 * Builds the membership-offers API on top of the shared low-level client. Every call is scoped to the
 * caller's own token, so none names a user or an account: an offer id that is not the caller's answers 404.
 * @returns The {@link MembershipOffersApi} bound to the `me` offer endpoints.
 */
export const useMembershipOffersApi = (): MembershipOffersApi => {
  const api = useApi()

  /**
   * Lists the open offers addressed to the caller.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every open offer.
   */
  const list = (signal?: AbortSignal): Promise<MembershipOffer[]> => {
    return api.get<MembershipOffer[]>(OFFERS_PATH, signal)
  }

  /**
   * Accepts one offer.
   * @param id The offer to accept.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel added the caller; the answer is an empty 204.
   */
  const accept = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.post<void>(`${OFFERS_PATH}/${id}/accept`, undefined, signal)
  }

  /**
   * Declines one offer.
   * @param id The offer to decline.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel closed the offer; the answer is an empty 204.
   */
  const decline = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.post<void>(`${OFFERS_PATH}/${id}/decline`, undefined, signal)
  }

  return { list, accept, decline }
}
