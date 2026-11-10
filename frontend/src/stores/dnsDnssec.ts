import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsDnssecApi } from '../composables/apis/useDnsDnssecApi'
import { ApiError } from '../composables/useApi'
import type { DnsDnssecLoad, ZoneDnssec } from '../types/dnsDnssec'

/**
 * Owns what the zone page on screen knows about the zone's DNSSEC signing and the three
 * transitions: enable, withdraw and finish the withdrawal. The page reads state from here and calls
 * its actions; none touches the API composable directly (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim, including its refusal of a withdrawal finished too early (rules/vue.md: "the backend
 * owns their text").
 */
export const useDnsDnssecStore = defineStore('dnsDnssec', () => {
  const api = useDnsDnssecApi()

  /** What the page last learned about the zone's signing. */
  const detail: Ref<DnsDnssecLoad> = ref({ state: 'loading' })

  /** True while an enable, a withdraw or a finish is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** The panel's reason the last transition was refused, already localized, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the zone's signing into {@link detail}. A failure is a display state, not a thrown error:
   * the zone page renders without the tab when the signing cannot be read.
   * @param zoneId The zone.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (zoneId: string): Promise<void> => {
    detail.value = { state: 'loading' }
    try {
      detail.value = { state: 'loaded', dnssec: await api.get(zoneId) }
    } catch (error) {
      detail.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
    }
  }

  /**
   * Runs one transition and keeps the answer the panel gives, or its reason for refusing.
   * @param run The call to make.
   * @returns True when the panel accepted the transition.
   */
  const transition = async (run: () => Promise<ZoneDnssec>): Promise<boolean> => {
    acting.value = true
    try {
      detail.value = { state: 'loaded', dnssec: await run() }
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Starts signing the zone.
   * @param zoneId The zone.
   * @returns True when the panel accepted; otherwise {@link actionErrorMessage} holds the reason.
   */
  const enable = (zoneId: string): Promise<boolean> => {
    return transition(() => { return api.enable(zoneId) })
  }

  /**
   * Begins withdrawing the signing.
   * @param zoneId The zone.
   * @returns True when the panel accepted; otherwise {@link actionErrorMessage} holds the reason.
   */
  const withdraw = (zoneId: string): Promise<boolean> => {
    return transition(() => { return api.withdraw(zoneId) })
  }

  /**
   * Ends the withdrawal, stating that the DS record was removed at the registrar. The panel owns
   * the 48-hour rule; a refusal arrives as its own sentence.
   * @param zoneId The zone.
   * @returns True when the panel accepted; otherwise {@link actionErrorMessage} holds the reason.
   */
  const finishWithdrawal = (zoneId: string): Promise<boolean> => {
    return transition(() => { return api.finishWithdrawal(zoneId, { dsRecordRemoved: true }) })
  }

  /**
   * Forgets the last refusal, so a new confirmation does not open beside a reason that belonged to
   * the previous attempt.
   * @returns Nothing.
   */
  const clearActionError = (): void => {
    actionErrorMessage.value = null
  }

  /**
   * Forgets the last refusal and read, so a zone that opens does not greet the person with a
   * reason that belonged to an earlier visit or another zone.
   * @returns Nothing.
   */
  const reset = (): void => {
    detail.value = { state: 'loading' }
    actionErrorMessage.value = null
  }

  return { detail, acting, actionErrorMessage, load, enable, withdraw, finishWithdrawal, clearActionError, reset }
})
