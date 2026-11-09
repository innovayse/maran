import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useFirstRunApi } from '../composables/apis/useFirstRunApi'
import type { Read } from '../types/loading'
import type { FirstRunChecklist } from '../types/firstRun'

/**
 * Owns the administrator's first-run checklist: what a new server still needs (a plan, an account,
 * a site, a backup destination, a certificate), as the panel works it out.
 *
 * **Derived, not remembered.** The panel recomputes the answer from what exists on every read, so
 * there is nothing to dismiss or to go stale here: when the last step is done the panel says
 * `complete` and the card goes away on its own.
 *
 * **A failed read is no card.** The checklist is advice; a read that fails leaves `checklist`
 * empty and the dashboard draws nothing in its place, never an error shouted at a screen whose
 * other sections are fine.
 */
export const useFirstRunStore = defineStore('firstRun', () => {
  const api = useFirstRunApi()

  /** The checklist as last read; `idle` until the dashboard first asks. */
  const read: Ref<Read<FirstRunChecklist>> = ref({ state: 'idle' })

  /** The in-flight read, so a newer read abandons it instead of applying a stale answer. */
  const pending: Ref<AbortController | null> = ref(null)

  /** The checklist once the panel has answered, otherwise `null`. */
  const checklist: ComputedRef<FirstRunChecklist | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /**
   * Reads the checklist. Failures are captured into the read rather than thrown.
   * @returns Resolves once the read settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    pending.value?.abort()
    const controller = new AbortController()
    pending.value = controller
    read.value = { state: 'loading' }
    try {
      const answer = await api.get(controller.signal)
      if (!controller.signal.aborted) {
        read.value = { state: 'loaded', value: answer }
      }
    } catch {
      // The card is advice: a failure, or a read a newer one superseded, leaves it undrawn.
      if (!controller.signal.aborted) {
        read.value = { state: 'failed', message: null }
      }
    }
  }

  /**
   * Abandons any read in flight and forgets the checklist, for a sign-out.
   * @returns Nothing.
   */
  const clear = (): void => {
    pending.value?.abort()
    pending.value = null
    read.value = { state: 'idle' }
  }

  return { read, checklist, load, clear }
})
