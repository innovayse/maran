import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useModulesApi } from '../composables/apis/useModulesApi'
import { ApiError } from '../composables/useApi'
import type { PanelModule } from '../types/module'

/**
 * Owns the module catalogue: which modules the panel composed and which the licence permits.
 * The navigation and the router guard read from here, so the shell asks the backend once rather
 * than every screen asking for itself.
 */
export const useModulesStore = defineStore('modules', () => {
  const api = useModulesApi()

  /** The catalogue as last reported by the panel; empty before the first successful load. */
  const modules: Ref<PanelModule[]> = ref([])

  /** True while a catalogue request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed load, or `null` when the last load
   * succeeded or never reached the server. Server error text is rendered verbatim — the frontend
   * owns no error copy (rules/vue.md).
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** True when the catalogue has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Loads the catalogue, replacing what is held. Safe to call repeatedly; failures leave the
   * previous catalogue in place so a transient error does not blank the navigation.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      modules.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      // The catalogue is advisory for rendering: a failure must not throw into the router guard,
      // which would leave the user on a dead screen instead of a degraded but usable panel.
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Whether a module is present and permitted by the licence.
   * @param name Machine name of the module (`sites`, `backups`…).
   * @returns True when the module exists in the catalogue and is enabled.
   */
  const isEnabled = (name: string): boolean => {
    return modules.value.some((module) => {
      return module.name === name && module.isEnabled
    })
  }

  /**
   * Whether a module exists but the licence does not permit it — the case that deserves an
   * upgrade prompt rather than a "not found".
   * @param name Machine name of the module.
   * @returns True when the module is known but disabled.
   */
  const isLocked = (name: string): boolean => {
    return modules.value.some((module) => {
      return module.name === name && !module.isEnabled
    })
  }

  return { modules, loading, errorMessage, isLoaded, load, isEnabled, isLocked }
})
