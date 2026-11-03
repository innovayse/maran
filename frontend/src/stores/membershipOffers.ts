import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMembershipOffersApi } from '../composables/apis/useMembershipOffersApi'
import { ApiError } from '../composables/useApi'
import type { MembershipOffer } from '../types/membershipOffer'

/**
 * Owns the offers to join another person's hosting account that are addressed to the signed-in
 * person, and the accept and decline workflows. The account screen reads state from here and calls
 * its actions; it never touches the API composable (rules/vue.md).
 *
 * **Reading is advice, the server is the boundary.** Which offers exist, who they are addressed to
 * and whether one may still be accepted are decided by the panel; a list this store held wrongly
 * could only mislead the screen, because an accept names the offer and the panel checks it belongs
 * to the caller. After an accept the account switcher must be read again, and that is the page's
 * job: one store does not import another.
 *
 * Error text is never generated here: the panel's already-localized `title`/`detail` is stored verbatim.
 */
export const useMembershipOffersStore = defineStore('membershipOffers', () => {
  const api = useMembershipOffersApi()

  /** The open offers last reported by the panel; empty before the first answer. */
  const offers: Ref<MembershipOffer[]> = ref([])

  /** The offer an accept or a decline is in flight for, or `null` when none is. */
  const actingId: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed read, accept or decline, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the open offers addressed to the caller.
   *
   * A failure is captured into {@link errorMessage}, never thrown: the account screen renders
   * without the card, and the offers are advice about what may be accepted.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      offers.value = await api.list()
      errorMessage.value = null
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Sends one answer for one offer. An offer the panel no longer knows (answered elsewhere, expired,
   * withdrawn) is refused, and the list is read again so a stale row does not stay on screen.
   * @param id The offer being answered.
   * @param send The API call that carries the answer.
   * @returns True when the panel accepted the answer.
   */
  const answer = async (id: string, send: (id: string) => Promise<void>): Promise<boolean> => {
    actingId.value = id
    try {
      await send(id)
      offers.value = offers.value.filter((offer) => {
        return offer.id !== id
      })
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      // The refusal is shown first; the re-read then drops what the panel says is gone.
      const reason = errorMessage.value
      await load()
      errorMessage.value = reason ?? errorMessage.value
      return false
    } finally {
      actingId.value = null
    }
  }

  /**
   * Accepts an offer and drops it from the held list.
   * @param id The offer to accept.
   * @returns True when the panel added the caller to the account; the reason is in {@link errorMessage} when not.
   */
  const accept = async (id: string): Promise<boolean> => {
    return answer(id, api.accept)
  }

  /**
   * Declines an offer and drops it from the held list.
   * @param id The offer to decline.
   * @returns True when the panel closed the offer; the reason is in {@link errorMessage} when not.
   */
  const decline = async (id: string): Promise<boolean> => {
    return answer(id, api.decline)
  }

  return { offers, actingId, errorMessage, load, accept, decline }
})
