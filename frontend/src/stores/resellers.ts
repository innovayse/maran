import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useResellersApi } from '../composables/apis/useResellersApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type {
  ChangeResellerEnvelopeRequest,
  CreateResellerRequest,
  Reseller,
  ResellerChoicesLoad,
  ResellerDetail,
  UpdateResellerRequest,
} from '../types/reseller'

/**
 * How many choices one picker read asks for. A UI constant: enough to scan by eye, few enough that
 * the picker says "type to narrow" rather than scrolling through hundreds.
 */
const CHOICES_PAGE_SIZE = 25

/**
 * Owns the resellers an administrator manages (one server page of the list at a time, the one
 * reseller a detail or edit screen opened, the create and envelope workflow, the lifecycle actions),
 * the reseller the caller is (a reseller's own overview and its plan-form ceilings), and the page of
 * choices the open reseller picker offers. The screens read state from here and call its actions;
 * none touches the API composable directly (rules/vue.md: "API composables are called from Pinia
 * stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text").
 */
export const useResellersStore = defineStore('resellers', () => {
  const api = useResellersApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const resellers: Ref<Reseller[]> = ref([])

  /**
   * The reseller the detail or edit screen opened, as last read; `null` before it arrived, after the
   * read failed, and when the panel has no reseller with that id.
   */
  const selected: Ref<ResellerDetail | null> = ref(null)

  /**
   * Where the read of {@link selected} stands. A reseller the panel does not have (the server's 404)
   * is `loaded` with a `null` {@link selected}: an answer, not a failure.
   */
  const selectedState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link selected}, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** The reseller the caller is, with its envelope and usage; `null` before it arrived or after a failure. */
  const mine: Ref<ResellerDetail | null> = ref(null)

  /** Where the read of {@link mine} stands. */
  const mineState: Ref<LoadingState> = ref('loading')

  /** The panel's message for the last failed read of {@link mine}, or `null`. */
  const mineErrorMessage: Ref<string | null> = ref(null)

  /** True while a create, an update or an envelope change is in flight. */
  const saving: Ref<boolean> = ref(false)

  /** The panel's message for the last refused create, update or envelope change, or `null`. */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** True while a suspend, reactivate or delete is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** The panel's message for the last refused suspend, reactivate or delete, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The open picker's latest page of `GET /api/v1/resellers/choices`, or where that read stands. */
  const choicePage: Ref<ResellerChoicesLoad> = ref({ kind: 'loading' })

  /**
   * Reseller names read one at a time by id, for text that names a reseller the open page of
   * choices may not hold (the accounts list's reseller chip). An id is absent while its read is in
   * flight or after it failed.
   */
  const resellerNames: Ref<Readonly<Record<string, string>>> = ref({})

  /** Numbers each choices read, so only the latest search's answer is kept. Not reactive. */
  let choicesTicket = 0

  /**
   * Loads one page of the reseller list into {@link resellers} and reports how the read ended, for
   * the list page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<Reseller>> => {
    try {
      const page = await api.list(request)
      resellers.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the first page of resellers whose name matches what was typed into the open picker into
   * {@link choicePage}. An answer to an older search arriving after a newer one was sent is dropped.
   * @param search Narrows by name; empty for the first resellers the panel lists.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const searchChoices = async (search: string): Promise<void> => {
    choicesTicket += 1
    const ticket = choicesTicket
    choicePage.value = { kind: 'loading' }
    let outcome: ResellerChoicesLoad
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
   * Reads one reseller's name by id into {@link resellerNames}, once. A failure stores nothing, so
   * the caller keeps showing the id, which is the truth the SPA has.
   * @param id The reseller to name.
   * @returns Resolves once the read has settled, successfully or not.
   */
  const loadName = async (id: string): Promise<void> => {
    if (id in resellerNames.value) {
      return
    }
    try {
      const reseller = await api.get(id)
      resellerNames.value = { ...resellerNames.value, [id]: reseller.name }
    } catch {
      // Nothing to store: an unnamed reseller is shown by its id.
    }
  }

  /**
   * Reads one reseller into {@link selected}, fresh every time: its usage is a live fact.
   * @param id The reseller's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    selectedState.value = 'loading'
    selected.value = null
    try {
      selected.value = await api.get(id)
      errorMessage.value = null
      selectedState.value = 'loaded'
    } catch (error) {
      // No such reseller (deleted since the link was followed) is an answer, not a failure.
      if (error instanceof ApiError && error.status === 404) {
        errorMessage.value = null
        selectedState.value = 'loaded'
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
      selectedState.value = 'error'
    }
  }

  /**
   * Reads the reseller the caller is into {@link mine}, fresh every time.
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
      mineErrorMessage.value = error instanceof ApiError ? error.message : null
      mineState.value = 'error'
    }
  }

  /**
   * Forgets the last refusals, so a form or dialog opened a second time does not greet the
   * administrator with the previous attempt's error.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    saveErrorMessage.value = null
    actionErrorMessage.value = null
  }

  /**
   * Creates a reseller. The list page is not patched: where the new row falls depends on the
   * server's sort and search, so the list reads its page again when it opens.
   * @param request The reseller's name, contact address, language and envelope.
   * @returns The created reseller, or `null` when the panel refused; the reason is in {@link saveErrorMessage}.
   */
  const create = async (request: CreateResellerRequest): Promise<ResellerDetail | null> => {
    saving.value = true
    try {
      const created = await api.create(request)
      saveErrorMessage.value = null
      return created
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Renames a reseller and replaces its contact address and language, and swaps the opened reseller
   * for the server's answer. The cached name of the reseller is dropped so a chip naming it reads the
   * new one.
   * @param id The reseller's identity.
   * @param request The reseller's new name, contact address and language.
   * @returns The reseller as it now stands, or `null` when the panel refused; the reason is in {@link saveErrorMessage}.
   */
  const update = async (id: string, request: UpdateResellerRequest): Promise<ResellerDetail | null> => {
    saving.value = true
    try {
      const updated = await api.update(id, request)
      if (selected.value?.id === id) {
        selected.value = updated
      }
      if (id in resellerNames.value) {
        resellerNames.value = { ...resellerNames.value, [id]: updated.name }
      }
      saveErrorMessage.value = null
      return updated
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Replaces a reseller's envelope and swaps the opened reseller for the server's answer.
   * @param id The reseller's identity.
   * @param request The new envelope.
   * @returns The reseller as it now stands, or `null` when the panel refused; the reason is in {@link saveErrorMessage}.
   */
  const changeEnvelope = async (id: string, request: ChangeResellerEnvelopeRequest): Promise<ResellerDetail | null> => {
    saving.value = true
    try {
      const updated = await api.changeEnvelope(id, request)
      if (selected.value?.id === id) {
        selected.value = updated
      }
      saveErrorMessage.value = null
      return updated
    } catch (error) {
      saveErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      saving.value = false
    }
  }

  /**
   * Runs one lifecycle call that answers the list shape, then reads the opened reseller again
   * without blanking it: the answer of the call is the list shape, and the detail screen needs the
   * envelope and usage too.
   * @param id The reseller's identity.
   * @param call The suspend or reactivate call.
   * @returns True when the panel accepted the call.
   */
  const act = async (id: string, call: (id: string) => Promise<Reseller>): Promise<boolean> => {
    acting.value = true
    try {
      await call(id)
      actionErrorMessage.value = null
      if (selected.value?.id === id) {
        selected.value = await api.get(id)
      }
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Suspends a reseller.
   * @param id The reseller's identity.
   * @returns True when the panel suspended it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const suspend = (id: string): Promise<boolean> => {
    return act(id, api.suspend)
  }

  /**
   * Lifts a reseller's suspension.
   * @param id The reseller's identity.
   * @returns True when the panel reactivated it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const reactivate = (id: string): Promise<boolean> => {
    return act(id, api.reactivate)
  }

  /**
   * Deletes a reseller and forgets it. A refusal (it still owns accounts or plans) keeps the row.
   * @param id The reseller's identity.
   * @returns True when the panel deleted it; false when it refused (the reason is in {@link actionErrorMessage}).
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      resellers.value = resellers.value.filter((reseller) => {
        return reseller.id !== id
      })
      if (selected.value?.id === id) {
        selected.value = null
      }
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return {
    resellers,
    selected,
    selectedState,
    errorMessage,
    mine,
    mineState,
    mineErrorMessage,
    saving,
    saveErrorMessage,
    acting,
    actionErrorMessage,
    choicePage,
    resellerNames,
    list,
    searchChoices,
    loadName,
    loadOne,
    loadMine,
    clearErrors,
    create,
    update,
    changeEnvelope,
    suspend,
    reactivate,
    remove,
  }
})
