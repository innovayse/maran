import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { usePhpSettingsApi } from '../composables/apis/usePhpSettingsApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { PhpSettingItem, PhpSettingsDto, SavePhpSettingsRequest } from '../types/phpSetting'

/**
 * Owns the signed-in customer's PHP settings. The settings page reads state from here and calls its
 * actions; it never touches the API composable (rules/vue.md: API composables are called from
 * Pinia stores only).
 *
 * There is no account to switch: the settings are the caller's account's, so a read and a write
 * take none. Every failure is kept in the backend's own words in {@link error} and rendered as-is.
 */
export const usePhpSettingsStore = defineStore('phpSettings', () => {
  const api = usePhpSettingsApi()

  /** Every setting the customer may change, as the panel last reported it. */
  const items: Ref<PhpSettingItem[]> = ref([])

  /** How many of the account's PHP sites the settings reach. */
  const sitesAffected: Ref<number> = ref(0)

  /** Where the read stands: on its way, arrived, or refused. A save never moves it. */
  const loadState: Ref<LoadingState> = ref('loading')


  /** True while a read is in flight; derived from {@link loadState} so the two cannot disagree. */
  const loading: ComputedRef<boolean> = computed(() => {
    return loadState.value === 'loading'
  })

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failed read or save, or `null`. Rendered verbatim. */
  const error: Ref<string | null> = ref(null)

  /**
   * Holds an answer of the panel.
   * @param settings The settings the panel reported.
   * @returns Nothing; the state is updated.
   */
  const adopt = (settings: PhpSettingsDto): void => {
    items.value = settings.items
    sitesAffected.value = settings.sitesAffected
  }

  /**
   * Records a failure in the backend's own words.
   * @param cause The caught error.
   * @returns Nothing; the error state is updated.
   */
  const remember = (cause: unknown): void => {
    error.value = cause instanceof ApiError ? cause.message : null
  }

  /**
   * Reads the account's PHP settings, replacing whatever was held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    error.value = null
    try {
      adopt(await api.get())
      loadState.value = 'loaded'
    } catch (cause) {
      remember(cause)
      loadState.value = 'error'
    }
  }

  /**
   * Replaces the account's stored settings and holds what the panel answers.
   * @param request The settings to keep; any other is removed (back to PHP's default).
   * @returns True when the panel stored and applied them.
   */
  const save = async (request: SavePhpSettingsRequest): Promise<boolean> => {
    saving.value = true
    error.value = null
    try {
      adopt(await api.save(request))
      return true
    } catch (cause) {
      remember(cause)
      return false
    } finally {
      saving.value = false
    }
  }

  return { items, sitesAffected, loadState, loading, saving, error, load, save }
})
