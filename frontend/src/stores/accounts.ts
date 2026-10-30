import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountsApi } from '../composables/apis/useAccountsApi'
import { ApiError } from '../composables/useApi'
import type { Account, AccountDetail, CreateAccountRequest, MyAccount, Plan } from '../types/account'
import type { AccountResourceSummary, AccountResourceSummaryRead } from '../types/accountResourceSummary'

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

  /** The account the detail page is showing, or `null` before one is loaded. */
  const selected: Ref<AccountDetail | null> = ref(null)

  /** True while a suspend, reactivate or delete request is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed create attempt, or `null` when the
   * last attempt succeeded or has not been made yet. Rendered verbatim by the form page.
   */
  const createErrorMessage: Ref<string | null> = ref(null)

  /** True while a plan or owner-address change is in flight. */
  const editing: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent refused plan or owner-address change, or
   * `null` when the last one succeeded or none has been made. Rendered by the edit dialogs.
   */
  const editErrorMessage: Ref<string | null> = ref(null)

  /** The caller's own account, its status, and its plan's limits — `null` before it is loaded. */
  const mine: Ref<MyAccount | null> = ref(null)

  /** True while the "my account" request is in flight. */
  const mineLoading: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed "my account" load, or `null` when the
   * last load succeeded or has not been attempted yet.
   */
  const mineErrorMessage: Ref<string | null> = ref(null)

  /**
   * Where the read of the detail page's account summary stands. Kept apart from {@link selected} so
   * a failed count never blanks the account itself.
   */
  const resourceSummary: Ref<AccountResourceSummaryRead> = ref({ state: 'loading' })

  /** Where the read of the caller's own account summary stands. */
  const mineResourceSummary: Ref<AccountResourceSummaryRead> = ref({ state: 'loading' })

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

  /**
   * Loads one account into {@link selected}, replacing whatever was held.
   * @param id The account to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    loading.value = true
    try {
      selected.value = await api.get(id)
      errorMessage.value = null
    } catch (error) {
      selected.value = null
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Moves the account to another plan and stores it as the panel reports it back.
   * @param id The account to move.
   * @param planId The plan to move it to.
   * @returns True when the panel accepted the change; otherwise `editErrorMessage` says why.
   */
  const changePlan = async (id: string, planId: string): Promise<boolean> => {
    return await edit(() => {
      return api.changePlan(id, { planId })
    })
  }

  /**
   * Changes the account's owner address and stores the account as the panel reports it back.
   * @param id The account to change.
   * @param ownerEmail The new address.
   * @returns True when the panel accepted the change; otherwise `editErrorMessage` says why.
   */
  const changeOwnerEmail = async (id: string, ownerEmail: string): Promise<boolean> => {
    return await edit(() => {
      return api.changeOwnerEmail(id, { ownerEmail })
    })
  }

  /**
   * Forgets the last edit refusal, so a dialog opened again starts clean.
   * @returns Nothing.
   */
  const clearEditError = (): void => {
    editErrorMessage.value = null
  }

  /**
   * Suspends the account and stores the state the panel reports back.
   * @param id The account to suspend.
   * @returns True when the panel accepted the change.
   */
  const suspend = async (id: string): Promise<boolean> => {
    return await changeState(() => {
      return api.suspend(id)
    })
  }

  /**
   * Lifts a suspension and stores the state the panel reports back.
   * @param id The account to reactivate.
   * @returns True when the panel accepted the change.
   */
  const reactivate = async (id: string): Promise<boolean> => {
    return await changeState(() => {
      return api.reactivate(id)
    })
  }

  /**
   * Deletes the account, dropping it from the held list.
   * @param id The account to delete.
   * @returns True when the panel accepted the deletion.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      accounts.value = accounts.value.filter((account) => {
        return account.id !== id
      })
      selected.value = null
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Loads the account the caller owns, replacing what is held.
   *
   * A 404 is not a genuine failure here: it is the expected answer for an
   * administrator, who owns no account by design (rules server-side,
   * `GetMyAccountQueryHandler`). `mineErrorMessage` stays `null` for it, so the
   * page's empty state — written for exactly this case — renders instead of a
   * red alert repeating a "not found" sentence that was never a malfunction.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadMine = async (): Promise<void> => {
    mineLoading.value = true
    try {
      mine.value = await api.getMine()
      mineErrorMessage.value = null
    } catch (error) {
      mine.value = null
      mineErrorMessage.value = error instanceof ApiError && error.status !== 404 ? error.message : null
    } finally {
      mineLoading.value = false
    }
  }

  /**
   * Reads one summary and states how the read ended, never throwing.
   * @param read The request to make.
   * @returns The outcome: the summary, the server's refusal, or an unreachable API.
   */
  const readSummary = async (read: () => Promise<AccountResourceSummary>): Promise<AccountResourceSummaryRead> => {
    try {
      return { state: 'loaded', summary: await read() }
    } catch (error) {
      return error instanceof ApiError ? { state: 'refused', message: error.message } : { state: 'unreachable' }
    }
  }

  /**
   * Reads what one account holds against its plan into {@link resourceSummary}. The previous
   * account's summary is dropped first, so the card never shows one account's counts under
   * another's name while the request is in flight.
   * @param id The account to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadResourceSummary = async (id: string): Promise<void> => {
    resourceSummary.value = { state: 'loading' }
    resourceSummary.value = await readSummary(() => {
      return api.getResourceSummary(id)
    })
  }

  /**
   * Reads what the caller's own account holds against its plan into {@link mineResourceSummary}.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadMineResourceSummary = async (): Promise<void> => {
    mineResourceSummary.value = { state: 'loading' }
    mineResourceSummary.value = await readSummary(() => {
      return api.getMyResourceSummary()
    })
  }

  /**
   * Runs one lifecycle call and folds the account it returns back into both the held list and
   * {@link selected}, so a list opened behind a detail page does not go stale.
   * @param call The lifecycle call to make.
   * @returns True when the panel accepted the change.
   */
  const changeState = async (call: () => Promise<Account>): Promise<boolean> => {
    acting.value = true
    try {
      const changed = await call()
      // A lifecycle call answers with the list shape, which carries no plan name or owner
      // address; those did not change, so the detail already held keeps them.
      selected.value = selected.value === null ? null : { ...selected.value, ...changed }
      accounts.value = accounts.value.map((account) => {
        return account.id === changed.id ? changed : account
      })
      errorMessage.value = null
      return true
    } catch (error) {
      // The panel's message, verbatim: an agent that refused says why, and inventing frontend
      // copy here would hide the reason the operator needs.
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Runs one edit and stores the account it returns as {@link selected}, folding the list fields
   * back into the held list too.
   * @param call The edit to make.
   * @returns True when the panel accepted the change.
   */
  const edit = async (call: () => Promise<AccountDetail>): Promise<boolean> => {
    editing.value = true
    try {
      const changed = await call()
      selected.value = changed
      accounts.value = accounts.value.map((account) => {
        return account.id === changed.id ? changed : account
      })
      editErrorMessage.value = null
      return true
    } catch (error) {
      editErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      editing.value = false
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
    selected,
    acting,
    editing,
    editErrorMessage,
    mine,
    mineLoading,
    mineErrorMessage,
    load,
    loadPlans,
    create,
    loadOne,
    changePlan,
    changeOwnerEmail,
    clearEditError,
    suspend,
    reactivate,
    remove,
    loadMine,
    resourceSummary,
    mineResourceSummary,
    loadResourceSummary,
    loadMineResourceSummary,
  }
})
