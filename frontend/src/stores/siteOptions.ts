import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSiteOptionsApi } from '../composables/apis/useSiteOptionsApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { ReplaceSiteOptionsRequest, SiteOptions } from '../types/siteOptions'

/**
 * Owns the options of the one site whose Options tab is open. The tab reads state from here and
 * calls its actions — it never touches the API composable directly (rules/vue.md).
 *
 * Every failure is reported in the panel's own words and nothing of this store's: the backend owns
 * every sentence a user reads. A failure that carried no server message leaves {@link errorMessage}
 * `null` and the screen says nothing rather than inventing one.
 */
export const useSiteOptionsStore = defineStore('siteOptions', () => {
  const api = useSiteOptionsApi()

  /** The options last read or saved, or `null` before the first successful read. */
  const options: Ref<SiteOptions | null> = ref(null)

  /**
   * Where the read stands: on its way, arrived, or refused. A save never moves it, so a refused
   * save leaves the form on screen with its message.
   */
  const loadState: Ref<LoadingState> = ref('loading')

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True once the last save succeeded, so the screen can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /** The panel's localized message from the last failed read or save, or `null`. Rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Machine-stable code of the last failure, or `null`. Read for behaviour only — which field a
   * refusal is shown under — and never looked up in a translation table.
   */
  const errorCode: Ref<string | null> = ref(null)

  /**
   * Records a failure in the panel's own words.
   * @param error The caught error.
   * @returns Nothing; the error state is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
    errorCode.value = error instanceof ApiError ? error.code : null
  }

  /**
   * Reads one site's options, replacing whatever was held.
   *
   * The held options are cleared BEFORE the request: the form fills itself from them, and leaving the
   * previous site's set in place while the next loads would show one site's redirects under another's name.
   * @param siteId The site to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (siteId: string): Promise<void> => {
    loadState.value = 'loading'
    options.value = null
    errorMessage.value = null
    errorCode.value = null
    saved.value = false
    try {
      options.value = await api.get(siteId)
      loadState.value = 'loaded'
    } catch (error) {
      remember(error)
      loadState.value = 'error'
    }
  }

  /**
   * Replaces all of a site's options and holds what the panel stored.
   * @param siteId The site to change.
   * @param request The complete new set.
   * @returns True when the panel stored it.
   */
  const save = async (siteId: string, request: ReplaceSiteOptionsRequest): Promise<boolean> => {
    saving.value = true
    saved.value = false
    errorMessage.value = null
    errorCode.value = null
    try {
      options.value = await api.replace(siteId, request)
      saved.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      saving.value = false
    }
  }

  return { options, loadState, saving, saved, errorMessage, errorCode, load, save }
})
