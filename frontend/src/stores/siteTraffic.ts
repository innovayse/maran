import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { RetentionOutcome, SavedLogRetention, SiteTrafficLoad } from '../types/siteTraffic'

/**
 * Owns what the site traffic tab shows and the per-site log retention change: the traffic summary
 * of the window last asked for, and the outcome of the last retention save. Both belong to the
 * site detail page's tabs, and neither reaches the API composable from a component
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useSiteTrafficStore = defineStore('siteTraffic', () => {
  const api = useSitesApi()

  /** What the traffic tab last learned: on its way, the summary, or the panel's refusal. */
  const traffic: Ref<SiteTrafficLoad> = ref({ state: 'loading' })

  /** True while a retention change is in flight. */
  const retentionSaving: Ref<boolean> = ref(false)

  /**
   * The panel's already-localized reason the last retention change was refused, or `null` when the
   * last one was accepted or none has been attempted. Rendered verbatim.
   */
  const retentionMessage: Ref<string | null> = ref(null)

  /**
   * How the last retention change ended: not attempted, accepted, or failed. `failed` with a `null`
   * {@link retentionMessage} means the panel did not answer at all.
   */
  const retentionOutcome: Ref<RetentionOutcome> = ref('none')

  /**
   * The retention the panel last accepted, with the site it belongs to, or `null` before any save.
   * The sites store's detail is not re-read after a save, so the control prefers this over the
   * detail's stale figure for the same site.
   */
  const savedRetention: Ref<SavedLogRetention | null> = ref(null)

  /**
   * Counts traffic reads so a slow answer for a window the operator has already left cannot
   * overwrite the answer for the window now on screen.
   */
  let latestRead = 0

  /**
   * Reads the site's traffic over a window into {@link traffic}, replacing whatever was held.
   * A summary with `hasData: false` is an ordinary answer and is stored as loaded.
   * @param siteId The site to read.
   * @param days The window in days; one the panel's choices offered.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadTraffic = async (siteId: string, days: number): Promise<void> => {
    latestRead += 1
    const thisRead = latestRead
    traffic.value = { state: 'loading' }
    try {
      const summary = await api.getTraffic(siteId, days)
      if (thisRead === latestRead) {
        traffic.value = { state: 'loaded', traffic: summary }
      }
    } catch (error) {
      if (thisRead === latestRead) {
        traffic.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Changes how many days of rotated logs the site keeps.
   * @param siteId The site to change.
   * @param days The retention in days; one the panel's choices offered.
   * @returns True when the panel accepted the change; otherwise {@link retentionMessage} holds why.
   */
  const changeLogRetention = async (siteId: string, days: number): Promise<boolean> => {
    retentionSaving.value = true
    retentionOutcome.value = 'none'
    try {
      const changed = await api.changeLogRetention(siteId, days)
      savedRetention.value = { siteId, days: changed.logRetentionDays }
      retentionMessage.value = null
      retentionOutcome.value = 'saved'
      return true
    } catch (error) {
      retentionMessage.value = error instanceof ApiError ? error.message : null
      retentionOutcome.value = 'failed'
      return false
    } finally {
      retentionSaving.value = false
    }
  }

  /**
   * Forgets the last retention outcome, so a message about another visit is not shown on a fresh one.
   * @returns Nothing.
   */
  const clearRetentionOutcome = (): void => {
    retentionMessage.value = null
    retentionOutcome.value = 'none'
  }

  return {
    traffic,
    retentionSaving,
    retentionMessage,
    retentionOutcome,
    savedRetention,
    loadTraffic,
    changeLogRetention,
    clearRetentionOutcome,
  }
})
