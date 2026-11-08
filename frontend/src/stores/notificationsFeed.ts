import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useNotificationsApi } from '../composables/apis/useNotificationsApi'
import { ApiError } from '../composables/useApi'
import type { Paged } from '../types/list'
import type { Read, Submission } from '../types/loading'
import type { NotificationCount, PanelNotification } from '../types/notification'

/** How often the shell asks for the badge figure again, in milliseconds (the navigation badges' cadence). */
const COUNT_REFRESH_INTERVAL_MS = 60000

/** How many entries the bell lists; the badge says how many there are in all. */
const FEED_PAGE_SIZE = 25

/**
 * Owns the administrators' notification feed behind the app bar's bell: the badge figure, kept
 * fresh by polling, and the newest page of entries, read when the bell opens.
 *
 * **Polling, not a stream (plan decision D5).** The figure is read when polling starts, every
 * minute after, and whenever the window regains focus, so a person coming back to the tab sees
 * the current number at once. The list is read only when the bell opens, because nobody sees it
 * before that.
 *
 * **A failed read is "no figure", never a wrong one.** The badge draws nothing for a figure the
 * panel could not give, and a feed that cannot be read is a state the popover words itself.
 * Dismissals act on the caller's own sign-in on the panel, so one administrator clearing the bell
 * leaves it full for the others.
 */
export const useNotificationsFeedStore = defineStore('notificationsFeed', () => {
  const api = useNotificationsApi()

  /** The badge figure as last answered, or `null` before the first answer, after a failed read, and while not polling. */
  const count: Ref<NotificationCount | null> = ref(null)

  /** The newest page of entries; `idle` until the bell first opens. */
  const feed: Ref<Read<Paged<PanelNotification>>> = ref({ state: 'idle' })

  /** Where the last dismissal stands; the buttons ignore presses while it is `sending`. */
  const change: Ref<Submission> = ref({ state: 'idle' })

  /** The timer that asks for the figure again, or `null` while not polling. */
  const timer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The in-flight count read, so a newer read or stopping the store abandons it. */
  const countPending: Ref<AbortController | null> = ref(null)

  /** The in-flight feed read, abandoned the same way. */
  const feedPending: Ref<AbortController | null> = ref(null)

  /** The entries as last read, newest first; none until the panel has answered. */
  const items: ComputedRef<PanelNotification[]> = computed(() => {
    return feed.value.state === 'loaded' ? feed.value.value.items : []
  })

  /** How many entries the caller has in all, which can exceed what the list shows. */
  const total: ComputedRef<number> = computed(() => {
    return feed.value.state === 'loaded' ? feed.value.value.total : 0
  })

  /** Backend-localized message from the last refused read or dismissal, rendered verbatim, or `null`. */
  const errorMessage: ComputedRef<string | null> = computed(() => {
    if (feed.value.state === 'failed') {
      return feed.value.message
    }
    return change.value.state === 'failed' ? change.value.message : null
  })

  /** True while a dismissal is on its way. */
  const dismissing: ComputedRef<boolean> = computed(() => {
    return change.value.state === 'sending'
  })

  /**
   * Reads the badge figure once. A failure forgets the figure rather than throwing: a badge is
   * advice, and the shell must render without it.
   * @returns Resolves once the read settled, successfully or not.
   */
  const loadCount = async (): Promise<void> => {
    countPending.value?.abort()
    const controller = new AbortController()
    countPending.value = controller
    try {
      const answer = await api.count(controller.signal)
      if (!controller.signal.aborted) {
        count.value = answer
      }
    } catch {
      // Only a read that is still the current one may speak: an aborted one is the store stopping
      // or a newer read superseding this one, and must not wipe that read's answer.
      if (!controller.signal.aborted) {
        count.value = null
      }
    }
  }

  /**
   * Reads the newest page of entries.
   * @param quiet True to keep the list on screen while it is re-read (after a dismissal), so it
   * does not flash back to a loading state under the person's hand.
   * @returns Resolves once the read settled, successfully or not.
   */
  const loadFeed = async (quiet = false): Promise<void> => {
    feedPending.value?.abort()
    const controller = new AbortController()
    feedPending.value = controller
    if (!quiet || feed.value.state !== 'loaded') {
      feed.value = { state: 'loading' }
    }
    try {
      const page = await api.list(
        { page: 1, pageSize: FEED_PAGE_SIZE, search: '', sort: '', filters: {} },
        controller.signal,
      )
      if (!controller.signal.aborted) {
        feed.value = { state: 'loaded', value: page }
      }
    } catch (error) {
      if (!controller.signal.aborted) {
        feed.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Opens the bell's contents: forgets any earlier dismissal failure and reads the list.
   * @returns Resolves once the list read settled.
   */
  const open = async (): Promise<void> => {
    change.value = { state: 'idle' }
    await loadFeed()
  }

  /**
   * Runs one dismissal, then re-reads the figure and the list so both show what the panel holds.
   * @param send The call that dismisses.
   * @returns Resolves once the dismissal and the re-reads settled.
   */
  const dismissWith = async (send: () => Promise<void>): Promise<void> => {
    if (change.value.state === 'sending') {
      return
    }
    change.value = { state: 'sending' }
    try {
      await send()
      change.value = { state: 'done' }
    } catch (error) {
      change.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return
    }
    await Promise.all([loadCount(), loadFeed(true)])
  }

  /**
   * Dismisses one entry for the caller.
   * @param id The entry.
   * @returns Resolves once the dismissal and the re-reads settled.
   */
  const dismiss = async (id: string): Promise<void> => {
    await dismissWith((): Promise<void> => {
      return api.dismiss(id)
    })
  }

  /**
   * Dismisses every entry for the caller.
   * @returns Resolves once the dismissal and the re-reads settled.
   */
  const dismissAll = async (): Promise<void> => {
    await dismissWith((): Promise<void> => {
      return api.dismissAll()
    })
  }

  /**
   * Re-reads the figure when the window regains focus, so a person returning to the tab sees the
   * current number without waiting for the next tick.
   * @returns Nothing.
   */
  const onWindowFocus = (): void => {
    void loadCount()
  }

  /**
   * Starts polling: the figure once now, then every minute and on every window focus. Does
   * nothing when already polling.
   * @returns Nothing.
   */
  const start = (): void => {
    if (timer.value !== null) {
      return
    }
    void loadCount()
    timer.value = setInterval((): void => {
      void loadCount()
    }, COUNT_REFRESH_INTERVAL_MS)
    window.addEventListener('focus', onWindowFocus)
  }

  /**
   * Stops polling, abandons any read in flight and forgets everything held, so the next sign-in
   * does not begin with the previous person's entries.
   * @returns Nothing.
   */
  const stop = (): void => {
    if (timer.value !== null) {
      clearInterval(timer.value)
      timer.value = null
    }
    window.removeEventListener('focus', onWindowFocus)
    countPending.value?.abort()
    countPending.value = null
    feedPending.value?.abort()
    feedPending.value = null
    count.value = null
    feed.value = { state: 'idle' }
    change.value = { state: 'idle' }
  }

  return { count, feed, change, items, total, errorMessage, dismissing, loadCount, loadFeed, open, dismiss, dismissAll, start, stop }
})
