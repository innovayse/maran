import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { usePanelUpdateApi } from '../composables/apis/usePanelUpdateApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState, Read, Submission } from '../types/loading'
import type { PanelUpdateState } from '../types/panelUpdate'
import { loadingStateOf } from '../utils/loadingStateOf'

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

  return { state, loadState, loadMessage, starting, started, startMessage, load, start }
})
