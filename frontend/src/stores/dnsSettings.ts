import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsSettingsApi } from '../composables/apis/useDnsSettingsApi'
import { ApiError } from '../composables/useApi'
import type { DnsServerStatus, DnsSettings, SaveDnsSettingsRequest } from '../types/dnsSettings'

/**
 * Owns the DNS module's server-wide settings and the name server's state (administrators only).
 * The nameservers page reads state from here and calls its actions (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md).
 */
export const useDnsSettingsStore = defineStore('dnsSettings', () => {
  const api = useDnsSettingsApi()

  /** The settings as last read or saved, or `null` before the first answer. */
  const settings: Ref<DnsSettings | null> = ref(null)

  /** The name server's state as last read or changed, or `null` before the first answer. */
  const server: Ref<DnsServerStatus | null> = ref(null)

  /** True while the first read of the settings and the name server is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a save is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** True while turning the name server on or off is in flight. */
  const switching: Ref<boolean> = ref(false)

  /** The panel's reason a read failed, already localized, or `null`. */
  const loadErrorMessage: Ref<string | null> = ref(null)

  /** The panel's reason the last save failed, already localized, or `null`. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** The panel's reason the last switch failed, already localized, or `null`. */
  const switchErrorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the settings and the name server's state together.
   * @returns Resolves once both requests have settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    // A refusal from an earlier visit must not greet a fresh one.
    saveErrorMessage.value = null
    switchErrorMessage.value = null
    try {
      const [read, state] = await Promise.all([api.get(), api.server()])
      settings.value = read
      server.value = state
      loadErrorMessage.value = null
    } catch (error) {
      loadErrorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Saves the settings.
   * @param request The complete settings.
   * @returns True when the panel accepted them; otherwise {@link saveErrorMessage} holds the reason.
   */
  const save = async (request: SaveDnsSettingsRequest): Promise<boolean> => {
    saving.value = true
    try {
      settings.value = await api.save(request)
      saveErrorMessage.value = null
      return true
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      saving.value = false
    }
  }

  /**
   * Turns the name server on or off.
   * @param on True to turn it on, false to turn it off.
   * @returns True when the panel changed it; otherwise {@link switchErrorMessage} holds the reason.
   */
  const setServerEnabled = async (on: boolean): Promise<boolean> => {
    switching.value = true
    try {
      server.value = on ? await api.enableServer() : await api.disableServer()
      switchErrorMessage.value = null
      // The answer to a switch carries no zone count (the panel did not ask the agent for its list), so the
      // state is read again; a failed read keeps the answer already held.
      try {
        server.value = await api.server()
      } catch {
        // The switch itself succeeded; the next full load corrects the count.
      }
      return true
    } catch (error) {
      switchErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      switching.value = false
    }
  }

  return {
    settings,
    server,
    loading,
    saving,
    switching,
    loadErrorMessage,
    saveErrorMessage,
    switchErrorMessage,
    load,
    save,
    setServerEnabled,
  }
})
