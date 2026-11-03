import type { GrantableFeature } from './subUser'

/**
 * One open offer to join another person's hosting account, mirroring the backend's
 * `MembershipOfferDto` field-for-field. The offer is addressed to the signed-in person and to nobody
 * else; nothing in it is a credential, so reading it grants nothing until the person accepts it.
 */
export interface MembershipOffer {
  /** The offer's id, and the only identifier an accept or a decline may name. */
  id: string
  /** The name of the account the person is invited to act for, as the panel sent it. */
  accountName: string
  /** The features the owner offers, each with its name in the interface language already localized. */
  features: GrantableFeature[]
  /** When the offer stops being acceptable, as an ISO-8601 string. */
  expiresAt: string
}

/**
 * Typed access to the membership-offer endpoints of the signed-in person.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface MembershipOffersApi {
  /**
   * Lists the open offers addressed to the caller.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Every open offer; empty when there is none.
   */
  list: (signal?: AbortSignal) => Promise<MembershipOffer[]>

  /**
   * Accepts an offer, which adds the caller to the account with the offered features. A foreign,
   * expired or already answered offer answers 404.
   * @param id The offer to accept.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  accept: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Declines an offer, which closes it without adding anyone. A foreign, expired or already answered
   * offer answers 404.
   * @param id The offer to decline.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  decline: (id: string, signal?: AbortSignal) => Promise<void>
}
