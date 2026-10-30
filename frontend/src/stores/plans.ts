import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { usePlansApi } from '../composables/apis/usePlansApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type { ManagedPlan, PlanRequest } from '../types/plan'

/**
 * Owns the plans an administrator manages: one server page of the list at a time, the one plan the
 * edit screen opened, the create and edit workflow, and the delete.
 * The plans screens read state from here and call its actions — neither touches the API composable
 * directly (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: when the backend rejects a request, its already-localized
 * `title`/`detail` is stored verbatim (rules/vue.md: "the backend owns their text"). That matters
 * more here than elsewhere, because the refusals are the interesting answers — a plan still in use,
 * a quota an account already exceeds, a name another plan has — and each carries a remedy only the
 * server can word correctly.
 *
 * Separate from `useAccountsStore`, whose `plans` are the picker's read-only choices for the
 * create-account form: a plan being managed and a plan being chosen are different jobs.
 */
export const usePlansStore = defineStore('plans', () => {
  const api = usePlansApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const plans: Ref<ManagedPlan[]> = ref([])

  /**
   * The plan the edit screen opened, as last read; `null` before it arrived, after the read failed,
   * and when the panel has no plan with that id.
   */
  const selected: Ref<ManagedPlan | null> = ref(null)

  /** Where the read of {@link selected} stands; every failure sets `errorMessage`, whatever its cause. */
  const loadState: Ref<LoadingState> = ref('loading')

  /**
   * Backend-localized message from the most recent failed read of one plan, or `null` when it
   * succeeded, found no such plan, or the failure carried no message (the panel was unreachable).
   */
  const errorMessage: Ref<string | null> = ref(null)

  /** True while a create or update request is in flight. */
  const saving: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed create or update, or `null`. Kept apart
   * from {@link errorMessage} so a rejected form does not blank the list it was opened from.
   */
  const saveErrorMessage: Ref<string | null> = ref(null)

  /** True while a delete request is in flight. */
  const deleting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed delete, or `null`. The refusal for a
   * plan that accounts are still on arrives here and is shown unchanged.
   */
  const deleteErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the plan list into {@link plans} and reports how the read ended, for the list
   * page's `useServerTable`.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<ManagedPlan>> => {
    try {
      const page = await api.list(request)
      plans.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads one plan into {@link selected}, fresh every time: the account count the edit notice states
   * is a live fact, and a stale one would tell an administrator nobody is on a plan somebody has
   * just been put on.
   * @param id The plan's identity.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    loadState.value = 'loading'
    selected.value = null
    try {
      selected.value = await api.get(id)
      errorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      // No such plan (deleted since the link was followed, or a mistyped id) is an answer, not a
      // failure: the read settled, and the screen says the plan was not found with a way back.
      if (error instanceof ApiError && error.status === 404) {
        errorMessage.value = null
        loadState.value = 'loaded'
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  /**
   * Forgets the last save and delete refusals, so a form opened a second time does not greet the
   * administrator with the previous attempt's error.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    saveErrorMessage.value = null
    deleteErrorMessage.value = null
  }

  /**
   * Defines a plan. The list page is not patched: where the new row falls depends on the server's
   * sort and search, so the list page reads its page again when it opens.
   * @param request The plan's name and limits.
   * @returns The created plan, or `null` when the panel refused — the reason is in
   * {@link saveErrorMessage}.
   */
  const create = async (request: PlanRequest): Promise<ManagedPlan | null> => {
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
   * Replaces a plan's name and limits and swaps the held row and the opened plan for the server's
   * answer.
   * @param id The plan to edit.
   * @param request The plan's new name and limits.
   * @returns The plan as it now stands, or `null` when the panel refused (a quota an account
   * already exceeds, a name another plan has) — the reason is in {@link saveErrorMessage}, and the
   * held row is untouched because the server changed nothing.
   */
  const update = async (id: string, request: PlanRequest): Promise<ManagedPlan | null> => {
    saving.value = true
    try {
      const updated = await api.update(id, request)
      plans.value = plans.value.map((plan) => {
        return plan.id === id ? updated : plan
      })
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
   * Deletes a plan and removes it from the held page. The list page then reads its page again, so
   * the total and the next plan moving up from the following page are the server's.
   * @param id The plan to delete.
   * @returns True when the panel deleted it; false when it refused (accounts are still on it) —
   * the reason is in {@link deleteErrorMessage} and the row stays.
   */
  const remove = async (id: string): Promise<boolean> => {
    deleting.value = true
    try {
      await api.remove(id)
      plans.value = plans.value.filter((plan) => {
        return plan.id !== id
      })
      deleteErrorMessage.value = null
      return true
    } catch (error) {
      deleteErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      deleting.value = false
    }
  }

  return {
    plans,
    selected,
    loadState,
    errorMessage,
    saving,
    saveErrorMessage,
    deleting,
    deleteErrorMessage,
    list,
    loadOne,
    clearErrors,
    create,
    update,
    remove,
  }
})
