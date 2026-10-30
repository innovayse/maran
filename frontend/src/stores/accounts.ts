import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useAccountsApi } from '../composables/apis/useAccountsApi'
import { ApiError } from '../composables/useApi'
import type {
  Account,
  AccountChoicesLoad,
  AccountDetail,
  CreateAccountRequest,
  MyAccount,
  Plan,
} from '../types/account'
import type { LoadingState } from '../types/loading'
import type { AccountResourceSummary, AccountResourceSummaryRead } from '../types/accountResourceSummary'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * How many choices one picker read asks for. A UI constant: enough to scan by eye, few enough that
 * the picker says "type to narrow" rather than scrolling through hundreds.
 */
const CHOICES_PAGE_SIZE = 25

/**
 * Owns the hosting accounts list (one server page at a time), the page of account choices the open
 * picker offers, the names read by id for text that names one account,
 * and the create-account workflow. The list and form pages read
 * state from here and call its actions — neither touches the API composable directly
 * (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useAccountsStore = defineStore('accounts', () => {
  const api = useAccountsApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const accounts: Ref<Account[]> = ref([])

  /**
   * The open picker's latest page of `GET /api/v1/accounts/choices` for what was typed into it, or
   * where that read stands. Only an administrator's picker reads it; a customer picks nothing.
   */
  const choicePage: Ref<AccountChoicesLoad> = ref({ kind: 'loading' })

  /**
   * Account names read one at a time by id, for text that names an account the open page of
   * choices may not hold (the "Only alpha" filter strip). An id is absent while its read is in
   * flight or after it failed.
   */
  const accountNames: Ref<Readonly<Record<string, string>>> = ref({})

  /**
   * Numbers each choices read, so an answer that arrives after a newer search was sent is dropped:
   * the latest request wins. Not reactive, because nothing renders it.
   */
  let choicesTicket = 0

  /**
   * The name read in flight for each id. A second caller asking for the same id awaits the same
   * read rather than returning early, so it never decides on a name that is still on its way.
   */
  const pendingNames: Map<string, Promise<void>> = new Map()

  /**
   * Where the read of {@link selected} stands: on its way, arrived, or failed. An account the panel
   * does not have for the caller (the server's 404) is `loaded` with a `null` {@link selected}: an
   * answer, not a failure.
   */
  const selectedState: Ref<LoadingState> = ref('loading')

  /**
   * Backend-localized message from the most recent failed detail load or lifecycle call, or `null`
   * when the last one succeeded or none has been attempted yet.
   */
  const errorMessage: Ref<string | null> = ref(null)

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

  /**
   * Where the read of {@link mine} stands: on its way, arrived, or refused. A caller who owns no
   * account (the server's 404) is `loaded` with a `null` {@link mine}: an answer, not a failure.
   */
  const mineState: Ref<LoadingState> = ref('loading')

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
   * Loads one page of the account list into {@link accounts} and reports how the read ended, for
   * the list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Account>> => {
    try {
      const page = await api.list(request)
      accounts.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the first page of accounts whose name matches what was typed into the open picker into
   * {@link choicePage}. An answer to an older search arriving after a newer one was sent is dropped,
   * so a slow request can never overwrite the list for what the field now says.
   * @param search Narrows by name; empty for the first accounts the panel lists.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const searchChoices = async (search: string): Promise<void> => {
    choicesTicket += 1
    const ticket = choicesTicket
    choicePage.value = { kind: 'loading' }
    let outcome: AccountChoicesLoad
    try {
      outcome = { kind: 'loaded', page: await api.choices(search, CHOICES_PAGE_SIZE) }
    } catch (error) {
      outcome = error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
    if (ticket === choicesTicket) {
      choicePage.value = outcome
    }
  }

  /**
   * Reads one account's name by id into {@link accountNames}, once: a name already held is not asked
   * for again, and a caller asking while it is being read awaits that same read. A failure stores nothing, so the caller keeps showing the id.
   * @param id The account to name.
   * @returns Resolves once the read has settled, successfully or not.
   */
  const loadAccountName = async (id: string): Promise<void> => {
    if (id in accountNames.value) {
      return
    }
    const inFlight = pendingNames.get(id)
    if (inFlight !== undefined) {
      await inFlight
      return
    }
    const read = (async (): Promise<void> => {
      try {
        const account = await api.get(id)
        accountNames.value = { ...accountNames.value, [id]: account.name }
      } catch {
        // Nothing to store: an unnamed account is shown by its id, which is the truth the SPA has.
      } finally {
        pendingNames.delete(id)
      }
    })()
    pendingNames.set(id, read)
    await read
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
   * Creates a new hosting account. The list page is not patched: where the new row falls depends
   * on the server's sort and filters, so the list page reads its page again when it opens.
   * @param request The account's name, primary domain, and plan.
   * @returns The created account on success, or `null` when the request failed — the caller reads
   * `createErrorMessage` for the reason.
   */
  const create = async (request: CreateAccountRequest): Promise<Account | null> => {
    creating.value = true
    try {
      const created = await api.create(request)
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
    selectedState.value = 'loading'
    try {
      selected.value = await api.get(id)
      errorMessage.value = null
      selectedState.value = 'loaded'
    } catch (error) {
      selected.value = null
      const missing = error instanceof ApiError && error.status === 404
      errorMessage.value = error instanceof ApiError && !missing ? error.message : null
      selectedState.value = missing ? 'loaded' : 'error'
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
   * Deletes the account, dropping it from the held list page and the held page of choices.
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
      const held = choicePage.value
      if (held.kind === 'loaded') {
        const items = held.page.items.filter((choice) => {
          return choice.id !== id
        })
        const removed = held.page.items.length - items.length
        choicePage.value = { kind: 'loaded', page: { ...held.page, items, total: held.page.total - removed } }
      }
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
    mineState.value = 'loading'
    try {
      mine.value = await api.getMine()
      mineErrorMessage.value = null
      mineState.value = 'loaded'
    } catch (error) {
      mine.value = null
      const ownsNone = error instanceof ApiError && error.status === 404
      mineErrorMessage.value = error instanceof ApiError && !ownsNone ? error.message : null
      mineState.value = ownsNone ? 'loaded' : 'error'
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
    choicePage,
    accountNames,
    plans,
    selectedState,
    errorMessage,
    creating,
    createErrorMessage,
    selected,
    acting,
    editing,
    editErrorMessage,
    mine,
    mineState,
    mineErrorMessage,
    list,
    searchChoices,
    loadAccountName,
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
