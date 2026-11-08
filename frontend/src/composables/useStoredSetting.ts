import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { ApiError } from './useApi'
import type { LoadingState, Read, StoredSettingApi, Submission } from '../types/loading'
import { loadingStateOf } from '../utils/loadingStateOf'

/** What {@link useStoredSetting} hands to the store that owns a setting. */
export interface StoredSetting<TValue, TRequest> {
  /** The setting the panel holds, or `null` until it has been read. */
  value: ComputedRef<TValue | null>
  /** Where the read stands, in the words the loading region understands. */
  loadState: ComputedRef<LoadingState>
  /** The panel's reason the read failed, rendered verbatim; `null` otherwise. */
  loadMessage: ComputedRef<string | null>
  /** The panel's reason the last save was refused, rendered verbatim; `null` otherwise. */
  submitMessage: ComputedRef<string | null>
  /** True once the last save succeeded. */
  saved: ComputedRef<boolean>
  /** True while a save is in flight. */
  saving: ComputedRef<boolean>
  /** Reads the setting; resolves once the request has settled, successfully or not. */
  load: () => Promise<void>
  /** Replaces the setting and re-reads it; resolves true when it was stored. */
  save: (request: TRequest) => Promise<boolean>
}

/**
 * The read and submission state of a singleton setting that is read whole and replaced whole. The
 * read and the save are kept apart: a save never moves the read, so a refused save leaves the form
 * showing what the panel still holds.
 *
 * @param api The setting's two calls.
 * @returns The state and actions the owning store re-exports.
 */
export const useStoredSetting = <TValue, TRequest>(
  api: StoredSettingApi<TValue, TRequest>,
): StoredSetting<TValue, TRequest> => {
  /** The read of the setting: on its way, arrived, or refused. A save never moves it. */
  const read: Ref<Read<TValue>> = ref({ state: 'loading' })

  /** Where the last save stands; `done` lets the screen confirm it. */
  const saveSubmission: Ref<Submission> = ref({ state: 'idle' })

  const value: ComputedRef<TValue | null> = computed(() => {
    return read.value.state === 'loaded' ? read.value.value : null
  })

  const loadState: ComputedRef<LoadingState> = computed(() => {
    return loadingStateOf(read.value)
  })

  const loadMessage: ComputedRef<string | null> = computed(() => {
    return read.value.state === 'failed' ? read.value.message : null
  })

  const submitMessage: ComputedRef<string | null> = computed(() => {
    return saveSubmission.value.state === 'failed' ? saveSubmission.value.message : null
  })

  const saved: ComputedRef<boolean> = computed(() => {
    return saveSubmission.value.state === 'done'
  })

  const saving: ComputedRef<boolean> = computed(() => {
    return saveSubmission.value.state === 'sending'
  })

  /**
   * Reads the setting.
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
   * Replaces the setting and re-reads it, because the stored form (and any bound the panel states
   * with it) is the server's to decide.
   * @param request The complete setting.
   * @returns True when it was stored.
   */
  const save = async (request: TRequest): Promise<boolean> => {
    saveSubmission.value = { state: 'sending' }
    try {
      await api.save(request)
      read.value = { state: 'loaded', value: await api.get() }
      saveSubmission.value = { state: 'done' }
      return true
    } catch (error) {
      saveSubmission.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      return false
    }
  }

  return { value, loadState, loadMessage, submitMessage, saved, saving, load, save }
}
