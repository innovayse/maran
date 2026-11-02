import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useDashboardApi } from '../composables/apis/useDashboardApi'
import type { DashboardSection } from '../types/dashboard'
import type { NavigationBadgeKind } from '../types/navigation'

/** How often the shell asks the panel for the needs-attention figure again, in milliseconds. */
const REFRESH_INTERVAL_MS = 60000

/**
 * Owns the live counts the navigation draws beside its items that no other store already holds.
 * Today that is the needs-attention figure; the failed-task count stays with the tasks store, which
 * the tasks screen also reads.
 *
 * **Only badges with a real source are fed here.** The mail queue (issue #177) and the available
 * updates have a place in the tree but no data behind them yet, so this store never requests them
 * and never invents a figure: their badges stay absent until a source exists.
 *
 * **A failed read is "no figure", never a wrong one.** The figure is kept as the panel answered it
 * (loaded or unavailable), and {@link counts} offers it to the navigation only when it was loaded.
 */
export const useNavigationBadgesStore = defineStore('navigationBadges', () => {
  const api = useDashboardApi()

  /**
   * The needs-attention figure as last answered, or `null` before the first answer and while the
   * shell is not asking (a role the figure is not for).
   */
  const attention: Ref<DashboardSection<number> | null> = ref(null)

  /** The timer that asks again, or `null` while the store is not polling. */
  const timer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The in-flight read, so stopping the store abandons it instead of applying a stale answer. */
  const pending: Ref<AbortController | null> = ref(null)

  /**
   * The counts the navigation may draw: a kind is present only when the panel answered with a real
   * count for it. Absent means not asked, not answered yet, or could not be read.
   */
  const counts: ComputedRef<Partial<Record<NavigationBadgeKind, number>>> = computed(() => {
    return attention.value?.state === 'loaded' ? { needsAttention: attention.value.data } : {}
  })

  /**
   * Reads the needs-attention figure once. Failures are captured as "unavailable" rather than
   * thrown: a badge is advice, and the shell must render without it.
   * @returns Resolves once the read settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    pending.value?.abort()
    const controller = new AbortController()
    pending.value = controller
    try {
      const answer = await api.attentionCount(controller.signal)
      if (!controller.signal.aborted) {
        attention.value = answer
      }
    } catch {
      // A refused or failed read leaves no badge. An aborted one is the store stopping or a newer
      // read superseding this one, and must not overwrite that read's answer.
      if (!controller.signal.aborted) {
        attention.value = { state: 'unavailable' }
      }
    }
  }

  /**
   * Starts reading the figure: once now, then on an interval, so a long-lived session does not keep
   * showing a number that has since changed. Does nothing when already polling.
   * @returns Nothing.
   */
  const start = (): void => {
    if (timer.value !== null) {
      return
    }
    void load()
    timer.value = setInterval((): void => {
      void load()
    }, REFRESH_INTERVAL_MS)
  }

  /**
   * Stops polling, abandons any read in flight and forgets the figure, so the next sign-in does not
   * begin with the previous person's number.
   * @returns Nothing.
   */
  const stop = (): void => {
    if (timer.value !== null) {
      clearInterval(timer.value)
      timer.value = null
    }
    pending.value?.abort()
    pending.value = null
    attention.value = null
  }

  return { attention, counts, load, start, stop }
})
