import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useLogLevelApi } from '../composables/apis/useLogLevelApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { LogLevel, PanelLogLevel } from '../types/logLevel'

/**
 * Owns the panel's runtime log level for the monitoring screen (issue #131). The screen reads state
 * from here and calls its actions; it never touches the API composable (rules/vue.md).
 *
 * No level is invented here: until the panel has answered, `logLevel` is `null` and the card draws a
 * placeholder rather than a plausible-looking choice.
 */
export const useLogLevelStore = defineStore('logLevel', () => {
  const api = useLogLevelApi()

  /** The running level and the choices, or `null` until they have been read. */
  const logLevel: Ref<LogLevel | null> = ref(null)

  /** Where the read stands. A change never moves it, so a refused change keeps the card on screen. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** True while a change is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True once the last change succeeded, so the card can confirm it. */
  const saved: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failure, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads a failure's backend-localized text, or clears it for anything that is not an API error.
   * @param error The caught error.
   * @returns Nothing; `errorMessage` is updated.
   */
  const remember = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the running level and the choices.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    errorMessage.value = null
    try {
      logLevel.value = await api.get()
      loadState.value = 'loaded'
    } catch (error) {
      remember(error)
      loadState.value = 'error'
    }
  }

  /**
   * Applies a level, then reads the running level back.
   *
   * Read back rather than assumed: the level shown is the panel's observation of its logger, so the
   * screen confirms what the logger is doing, not what was asked of it.
   * @param level The level to apply, one of the choices the panel offered.
   * @returns True when the panel applied it.
   */
  const change = async (level: PanelLogLevel): Promise<boolean> => {
    saving.value = true
    saved.value = false
    errorMessage.value = null
    try {
      await api.change({ level })
      logLevel.value = await api.get()
      saved.value = true
      return true
    } catch (error) {
      remember(error)
      return false
    } finally {
      saving.value = false
    }
  }

  return { logLevel, loadState, saving, saved, errorMessage, load, change }
})
