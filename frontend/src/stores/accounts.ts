import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountsApi } from '../composables/apis/useAccountsApi'
import { ApiError } from '../composables/useApi'
import type { Account, CreateAccountRequest, Plan } from '../types/account'

/**
 * Owns the hosting accounts list and the create-account workflow. The list and form pages read
 * state from here and call its actions — neither touches the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useAccountsStore = defineStore('accounts', () => {
  const api = useAccountsApi()

  /** The accounts as last reported by the panel; empty before the first successful load. */
  const accounts: Ref<Account[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed list load, or `null` when the last
   * load succeeded or has not been attempted yet.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /** The plans an account can be created against, as last loaded. */
  const plans: Ref<Plan[]> = ref([])

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null` when the
   * last attempt succeeded or has not been made yet. Rendered verbatim by the form page.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads the account list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      accounts.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Loads the plans the create form offers.
   * @returns Resolves once the request has settled.
   */
  const loadPlans = async (): Promise<void> => {
    try {
      plans.value = await api.listPlans()
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Creates a new hosting account and, on success, adds it to the held list so the list page
   * reflects it without a full reload.
   * @param request The account's name, primary domain, and plan.
   * @returns The created account on success, or `null` when the request failed — the caller reads
   * `createErrorMessage` for the reason.
   */
  const create = async (request: CreateAccountRequest): Promise<Account | null> => {
    creating.value = true
    try {
      const created = await api.create(request)
      accounts.value = [...accounts.value, created]
      createErrorMessage.value = null
      return created
    } catch (error) {
      // Server validation/conflict errors arrive already localized; store them verbatim rather
      // than inventing frontend copy.
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      creating.value = false
    }
  }

  return {
    accounts,
    plans,
    loading,
    errorMessage,
    isLoaded,
    creating,
    createErrorMessage,
    load,
    loadPlans,
    create,
  }
})
