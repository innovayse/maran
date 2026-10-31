import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAuthApi } from '../composables/apis/useAuthApi'
import { ApiError } from '../composables/useApi'
import type { CompleteSetupRequest } from '../types/auth'

/**
 * Owns the panel's first-run state: whether an administrator exists yet, and the action that
 * creates the first one. The router asks it before anything else, because a panel with no
 * administrator has exactly one meaningful screen.
 */
export const useSetupStore = defineStore('setup', () => {
  const api = useAuthApi()

  /** Whether the panel already has an administrator; `null` until asked. */
  const isSetupComplete: Ref<boolean | null> = ref(null)

  /** True while the first administrator is being created. */
  const loading: Ref<boolean> = ref(false)

  /** Backend-localized message from the last refused setup, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Forgets the last failure's message. A message belongs to the screen that produced it, so the
   * root component calls this on every navigation between screens (`App.vue`).
   * @returns Nothing; `errorMessage` is cleared synchronously.
   */
  const dismissError = (): void => {
    errorMessage.value = null
  }

  /**
   * Asks whether the panel already has an administrator, once per page load.
   * @returns Resolves once the answer is known.
   */
  const loadSetupState = async (): Promise<void> => {
    if (isSetupComplete.value !== null) {
      return
    }

    try {
      isSetupComplete.value = (await api.setupState()).isComplete
    } catch {
      // An unreachable panel is not an un-set-up one. Assuming "complete" keeps a
      // visitor on the login screen rather than offering to claim a server that
      // may well already have an owner.
      isSetupComplete.value = true
    }
  }

  /**
   * Creates the panel's first administrator.
   * @param request The token and the administrator's details.
   * @returns True when the administrator was created.
   */
  const completeSetup = async (request: CompleteSetupRequest): Promise<boolean> => {
    loading.value = true
    errorMessage.value = null
    try {
      await api.completeSetup(request)
      isSetupComplete.value = true
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      loading.value = false
    }
  }

  return {
    isSetupComplete,
    loading,
    errorMessage,
    dismissError,
    loadSetupState,
    completeSetup,
  }
})
