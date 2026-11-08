import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useBrandingApi } from '../composables/apis/useBrandingApi'
import { ApiError } from '../composables/useApi'
import type { ResellerBranding, SaveResellerBrandingRequest } from '../types/branding'
import type { LoadingState, Read, Submission } from '../types/loading'
import { loadingStateOf } from '../utils/loadingStateOf'

/**
 * Owns the reseller's own branding for the settings screen: what its customers' shell shows.
 * The shell itself reads it through {@link useBrandingStore}, which asks a different endpoint.
 */
export const useResellerBrandingStore = defineStore('resellerBranding', () => {
  const api = useBrandingApi()

  /** The read of the branding: on its way, arrived, or refused. A save never moves it. */
  const read: Ref<Read<ResellerBranding>> = ref({ state: 'loading' })

  /** Where the last save stands; `done` lets the screen confirm it. */
  const saveSubmission: Ref<Submission> = ref({ state: 'idle' })

  /** The branding the panel holds, or `null` until it has been read. */
  const branding: ComputedRef<ResellerBranding | null> = computed(() => {
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

  /** The panel's reason the last save was refused, rendered verbatim; `null` otherwise. */
  const submitMessage: ComputedRef<string | null> = computed(() => {
    return saveSubmission.value.state === 'failed' ? saveSubmission.value.message : null
  })

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: ComputedRef<boolean> = computed(() => {
    return saveSubmission.value.state === 'done'
  })

  /** True while a read or a save is in flight. */
  const loading: ComputedRef<boolean> = computed(() => {
    return read.value.state === 'loading' || saveSubmission.value.state === 'sending'
  })

  /**
   * Reads a failure's backend-localized text, or nothing for anything that is not an API error.
   * @param error The caught error.
   * @returns The text, or `null`.
   */
  const messageOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the reseller's branding.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    read.value = { state: 'loading' }
    try {
      read.value = { state: 'loaded', value: await api.getOwn() }
    } catch (error) {
      read.value = { state: 'failed', message: messageOf(error) }
    }
  }

  /**
   * Replaces the reseller's branding, and holds the answer as stored: the panel normalises the name
   * and the colour, so the form shows what is really kept and not what was typed.
   * @param request All three values.
   * @returns True when they were stored.
   */
  const save = async (request: SaveResellerBrandingRequest): Promise<boolean> => {
    saveSubmission.value = { state: 'sending' }
    try {
      read.value = { state: 'loaded', value: await api.save(request) }
      saveSubmission.value = { state: 'done' }
      return true
    } catch (error) {
      saveSubmission.value = { state: 'failed', message: messageOf(error) }
      return false
    }
  }

  return { branding, loading, loadState, loadMessage, saved, submitMessage, load, save }
})
