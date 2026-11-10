import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useMailServerApi } from '../composables/apis/useMailServerApi'
import { ApiError } from '../composables/useApi'
import type { MailServerStatus } from '../types/mailServer'

/**
 * Owns what the administrator's mail screens know about the mail server: whether it is on, and the
 * action that turns it on. The mail pages read state from here and call its actions — they never
 * touch the API composable directly (rules/vue.md).
 *
 * The status endpoint is the administrator's. A caller who is not one never loads it, and a status
 * that could not be read leaves the server "not known to be off", so the mail forms stay offered:
 * the panel refuses what cannot work with its own sentence, and a failed read must not take a working
 * screen away.
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is kept verbatim.
 */
export const useMailServerStore = defineStore('mailServer', () => {
  const api = useMailServerApi()

  /** The status last read, or `null` before the first successful read or after a failed one. */
  const status: Ref<MailServerStatus | null> = ref(null)

  /** True while the turn-on request is in flight. */
  const enabling: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed turn-on, or `null`. */
  const enableErrorMessage: Ref<string | null> = ref(null)

  /**
   * Whether the server is known to be off: a status arrived and it holds no settings that are on.
   * `false` while the status is unread, so an unread status never hides a working form.
   */
  const isOff: ComputedRef<boolean> = computed(() => {
    return status.value !== null && (status.value.settings === null || !status.value.settings.enabled)
  })

  /**
   * Reads the mail server's status.
   * @returns Resolves once the request settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      status.value = await api.status()
    } catch {
      // Advisory: the forms stay offered and the panel's own refusal says what cannot work.
      status.value = null
    }
  }

  /**
   * Turns the mail server on with the starting values the panel stated.
   * @param hostname The host name the server presents.
   * @returns True when the panel turned mail on — the caller reads {@link enableErrorMessage} when it did not.
   */
  const enable = async (hostname: string): Promise<boolean> => {
    if (status.value === null) {
      return false
    }
    enabling.value = true
    try {
      status.value = await api.enable({ hostname, ...status.value.startingValues })
      enableErrorMessage.value = null
      return true
    } catch (error) {
      enableErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      enabling.value = false
    }
  }

  return { status, enabling, enableErrorMessage, isOff, load, enable }
})
