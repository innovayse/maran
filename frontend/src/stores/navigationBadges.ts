import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useDashboardApi } from '../composables/apis/useDashboardApi'
import { useMailQueueApi } from '../composables/apis/useMailQueueApi'
import { useMonitoringApi } from '../composables/apis/useMonitoringApi'
import type { DashboardSection } from '../types/dashboard'
import type { NavigationBadgeKind } from '../types/navigation'

/** How often the shell asks the panel for the needs-attention figure again, in milliseconds. */
const ATTENTION_REFRESH_INTERVAL_MS = 60000

/**
 * How often the shell asks for the pending-updates figure again, in milliseconds. Slower than the
 * attention figure on purpose: the panel answers it by running the package manager against its
 * local cache, and the cache itself only changes when the system refreshes it.
 */
const UPDATES_REFRESH_INTERVAL_MS = 600000

/**
 * How often the shell asks for the mail-queue figure again, in milliseconds. The queue moves faster
 * than a package cache, and the panel answers with a single-entry read of it, so it is asked as often
 * as the attention figure.
 */
const MAIL_QUEUE_REFRESH_INTERVAL_MS = 60000

/**
 * Owns the live counts the navigation draws beside its items that no other store already holds:
 * the needs-attention figure, the number of pending package updates and the number of messages in
 * the mail queue. The failed-task count stays with the tasks store, which the tasks screen also
 * reads.
 *
 * **A failed read is "no figure", never a wrong one.** Each figure is kept only when the panel
 * answered with it, and {@link counts} offers it to the navigation only then: an unreadable package
 * cache or mail queue draws no badge, never a zero.
 */
export const useNavigationBadgesStore = defineStore('navigationBadges', () => {
  const api = useDashboardApi()
  const monitoringApi = useMonitoringApi()
  const mailQueueApi = useMailQueueApi()

  /**
   * The needs-attention figure as last answered, or `null` before the first answer and while the
   * shell is not asking (a role the figure is not for).
   */
  const attention: Ref<DashboardSection<number> | null> = ref(null)

  /**
   * The number of pending package updates as last answered, or `null` before the first answer,
   * after a failed read, and while the shell is not asking.
   */
  const updates: Ref<number | null> = ref(null)

  /**
   * The number of messages in the mail queue as last answered, or `null` before the first answer,
   * after a failed read, and while the shell is not asking.
   */
  const mailQueue: Ref<number | null> = ref(null)

  /** The timer that asks for the needs-attention figure again, or `null` while not polling. */
  const attentionTimer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The timer that asks for the pending-updates figure again, or `null` while not polling. */
  const updatesTimer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The timer that asks for the mail-queue figure again, or `null` while not polling. */
  const mailQueueTimer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The in-flight attention read, so stopping the store abandons it instead of applying a stale answer. */
  const attentionPending: Ref<AbortController | null> = ref(null)

  /** The in-flight updates read, abandoned the same way. */
  const updatesPending: Ref<AbortController | null> = ref(null)

  /** The in-flight mail-queue read, abandoned the same way. */
  const mailQueuePending: Ref<AbortController | null> = ref(null)

  /**
   * The counts the navigation may draw: a kind is present only when the panel answered with a real
   * count for it. Absent means not asked, not answered yet, or could not be read.
   */
  const counts: ComputedRef<Partial<Record<NavigationBadgeKind, number>>> = computed(() => {
    return {
      ...(attention.value?.state === 'loaded' ? { needsAttention: attention.value.data } : {}),
      ...(updates.value !== null ? { availableUpdates: updates.value } : {}),
      ...(mailQueue.value !== null ? { mailQueue: mailQueue.value } : {}),
    }
  })

  /**
   * Reads the needs-attention figure once. Failures are captured as "unavailable" rather than
   * thrown: a badge is advice, and the shell must render without it.
   * @returns Resolves once the read settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    attentionPending.value?.abort()
    const controller = new AbortController()
    attentionPending.value = controller
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
   * Reads the pending-updates figure once. A failure, including an unreadable package cache the
   * panel reports as an error, forgets the figure: no badge is drawn, and a zero is never invented.
   * @returns Resolves once the read settled, successfully or not.
   */
  const loadUpdates = async (): Promise<void> => {
    updatesPending.value?.abort()
    const controller = new AbortController()
    updatesPending.value = controller
    try {
      const answer = await monitoringApi.pendingUpdateCount(controller.signal)
      if (!controller.signal.aborted) {
        updates.value = answer.count
      }
    } catch {
      // Same rule as the attention read: only a read that is still the current one may speak.
      if (!controller.signal.aborted) {
        updates.value = null
      }
    }
  }

  /**
   * Reads the mail-queue figure once. A failure, including a queue the panel could not read, forgets
   * the figure: no badge is drawn, and a zero is never invented.
   * @returns Resolves once the read settled, successfully or not.
   */
  const loadMailQueue = async (): Promise<void> => {
    mailQueuePending.value?.abort()
    const controller = new AbortController()
    mailQueuePending.value = controller
    try {
      const answer = await mailQueueApi.count(controller.signal)
      if (!controller.signal.aborted) {
        mailQueue.value = answer.count
      }
    } catch {
      // Same rule as the other reads: only a read that is still the current one may speak.
      if (!controller.signal.aborted) {
        mailQueue.value = null
      }
    }
  }

  /**
   * Starts reading the figures: once now, then each on its own interval, so a long-lived session
   * does not keep showing a number that has since changed. Does nothing when already polling.
   * @returns Nothing.
   */
  const start = (): void => {
    if (attentionTimer.value !== null) {
      return
    }
    void load()
    attentionTimer.value = setInterval((): void => {
      void load()
    }, ATTENTION_REFRESH_INTERVAL_MS)
    void loadUpdates()
    updatesTimer.value = setInterval((): void => {
      void loadUpdates()
    }, UPDATES_REFRESH_INTERVAL_MS)
    void loadMailQueue()
    mailQueueTimer.value = setInterval((): void => {
      void loadMailQueue()
    }, MAIL_QUEUE_REFRESH_INTERVAL_MS)
  }

  /**
   * Stops polling, abandons any read in flight and forgets the figures, so the next sign-in does
   * not begin with the previous person's numbers.
   * @returns Nothing.
   */
  const stop = (): void => {
    if (attentionTimer.value !== null) {
      clearInterval(attentionTimer.value)
      attentionTimer.value = null
    }
    if (updatesTimer.value !== null) {
      clearInterval(updatesTimer.value)
      updatesTimer.value = null
    }
    if (mailQueueTimer.value !== null) {
      clearInterval(mailQueueTimer.value)
      mailQueueTimer.value = null
    }
    attentionPending.value?.abort()
    attentionPending.value = null
    updatesPending.value?.abort()
    updatesPending.value = null
    mailQueuePending.value?.abort()
    mailQueuePending.value = null
    attention.value = null
    updates.value = null
    mailQueue.value = null
  }

  return { attention, updates, mailQueue, counts, load, loadUpdates, loadMailQueue, start, stop }
})
