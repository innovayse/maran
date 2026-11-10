import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { usePanelUpdateApi } from '../composables/apis/usePanelUpdateApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { PanelUpdateCheckOutcome, PanelUpdateState } from '../types/panelUpdate'
import { loadingStateOf } from '../utils/loadingStateOf'

/** How often the state is re-read while a release check runs, in milliseconds. */
const CHECK_REFRESH_INTERVAL_MS = 2000

/**
 * Owns the panel's release state and the request to update it, for the "Panel updates" screen
 * (issue #382).
 *
 * The store only displays facts and forwards one closed request. Progress and the outcome of an
 * update (including an automatic rollback) are the task list's, written by the backend; the
 * sentence shown for a refusal is always the panel's own (rules/vue.md "Errors from the server").
 */
export const usePanelUpdateStore = defineStore('panelUpdate', () => {
  const api = usePanelUpdateApi()

  /** The read of the release state: on its way, arrived, or refused. */
  const read: Ref<Read<PanelUpdateState>> = ref({ state: 'loading' })

  /** Where the last update request stands; `done` lets the screen point at the task list. */
  const startSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** Where the last check request stands; the button ignores presses while it is `sending`. */
  const checkSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** How the last followed check ended; `idle` until one has been followed to its end. */
  const checkOutcome: Ref<PanelUpdateCheckOutcome> = ref('idle')

  /** The timer that re-reads the state while a check runs, or `null` while none is followed. */
  const checkTimer: Ref<ReturnType<typeof setInterval> | null> = ref(null)

  /** The time of the last check when the followed one began, to tell whether it recorded anything. */
  const checkBaseline: Ref<string | null> = ref(null)

  /** True while a refresh for the followed check is in flight, so ticks never overlap. */
  const refreshing: Ref<boolean> = ref(false)

  /** The release state, or `null` until the read has settled. */
  const state: ComputedRef<PanelUpdateState | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  /** Where the read stands, in the words the loading region understands. */
  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  /** The panel's reason the read failed, rendered verbatim; `null` otherwise. */
  const loadMessage: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  /** True while the update request is in flight. */
  const starting: ComputedRef<boolean> = computed(() => {
    return startSubmission.value.state === 'sending'
  })

  /** True once the last request was accepted, so the screen can say where progress appears. */
  const started: ComputedRef<boolean> = computed(() => {
    return startSubmission.value.state === 'done'
  })

  /** The panel's reason the last request was refused, rendered verbatim; `null` otherwise. */
  const startMessage: ComputedRef<string | null> = computed(() => {
    return startSubmission.value.state === 'failed' ? startSubmission.value.message : null
  })

  /** True from the moment a check is requested until it has ended, however it was started. */
  const checking: ComputedRef<boolean> = computed(() => {
    return checkSubmission.value.state === 'sending' || state.value?.checkInProgress === true
  })

  /** The panel's reason the last check request was refused, rendered verbatim; `null` otherwise. */
  const checkMessage: ComputedRef<string | null> = computed(() => {
    return checkSubmission.value.state === 'failed' ? checkSubmission.value.message : null
  })

  /**
   * Reads the release state.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: await api.get() }
    } catch (error) {
      read.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
    }
  }

  /**
   * Requests an update, then re-reads the state so the screen shows an update in progress.
   * @returns True when the panel accepted the request.
   */
  const start = async (): Promise<boolean> => {
    startSubmission.value = { state: 'sending' }
    try {
      await api.start()
      startSubmission.value = { state: 'done' }
    } catch (error) {
      startSubmission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
    await load()
    return true
  }

  /**
   * Re-reads the state without returning the screen to its loading placeholder. A failed re-read
   * keeps what is shown: the next tick asks again.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const refresh = async (): Promise<void> => {
    try {
      read.value = { state: 'loaded', value: await api.get() }
    } catch {
      // A refresh is advisory: the screen keeps the facts it has and the next tick asks again.
    }
  }

  /**
   * Stops following a check. Safe to call when none is followed.
   * @returns Nothing.
   */
  const stopFollowing = (): void => {
    if (checkTimer.value !== null) {
      clearInterval(checkTimer.value)
      checkTimer.value = null
    }
  }

  /**
   * Ends the followed check: stops the timer and judges the check by the facts. A check that
   * recorded something moved `lastCheckedAt`; one that did not (the release host was unreachable, or
   * an update held the lock) left it where it was.
   * @returns Nothing.
   */
  const finishFollowing = (): void => {
    stopFollowing()
    checkOutcome.value = (state.value?.lastCheckedAt ?? null) === checkBaseline.value ? 'notRecorded' : 'recorded'
  }

  /**
   * Re-reads the state once and ends the followed check when the server no longer reports one.
   * @returns Resolves once the tick has settled.
   */
  const tick = async (): Promise<void> => {
    if (refreshing.value) {
      return
    }
    refreshing.value = true
    try {
      await refresh()
    } finally {
      refreshing.value = false
    }
    if (checkTimer.value !== null && state.value?.checkInProgress !== true) {
      finishFollowing()
    }
  }

  /**
   * Re-reads the state on a timer until the server no longer reports a check in progress, then
   * judges the check against `baseline`. Does nothing when one is already followed.
   * @param baseline The time of the last check before the followed one began, `null` for none.
   * @returns Nothing.
   */
  const followFrom = (baseline: string | null): void => {
    if (checkTimer.value !== null) {
      return
    }
    checkBaseline.value = baseline
    checkOutcome.value = 'idle'
    checkTimer.value = setInterval((): void => {
      void tick()
    }, CHECK_REFRESH_INTERVAL_MS)
  }

  /**
   * Follows a check that was already running when the screen opened (started by someone else, or
   * before a reload). Does nothing when the state shows none.
   * @returns Nothing.
   */
  const follow = (): void => {
    if (state.value?.checkInProgress === true) {
      followFrom(state.value.lastCheckedAt)
    }
  }

  /**
   * Asks the server to check for updates, then follows the check until the server says it has ended.
   * @returns True when the panel accepted the request.
   */
  const check = async (): Promise<boolean> => {
    checkSubmission.value = { state: 'sending' }
    checkOutcome.value = 'idle'
    const before = state.value?.lastCheckedAt ?? null
    try {
      await api.check()
    } catch (error) {
      checkSubmission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
    checkSubmission.value = { state: 'done' }
    await refresh()
    if (state.value?.checkInProgress === true) {
      followFrom(before)
    } else {
      // The check ended before the first read: judge it now, against the time before the request.
      checkBaseline.value = before
      finishFollowing()
    }
    return true
  }

  return {
    state,
    loadState,
    loadMessage,
    starting,
    started,
    startMessage,
    checking,
    checkOutcome,
    checkMessage,
    load,
    start,
    check,
    follow,
    stopFollowing,
  }
})
