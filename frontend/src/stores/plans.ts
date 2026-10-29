import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { usePlansApi } from '../composables/apis/usePlansApi'
import { ApiError } from '../composables/useApi'
import type { ManagedPlan, PlanRequest } from '../types/plan'

/**
 * Owns the plans an administrator manages: the list, the create and edit workflow, and the delete.
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

  /** The plans as last reported by the panel; empty before the first successful load. */
  const plans: Ref<ManagedPlan[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True once the list has been loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failed list load, or `null`. For a customer who
   * opens the screen this is the server's refusal, shown in place of the list.
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
   * Loads the plan list, replacing what is held.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      plans.value = await api.list()
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Finds a held plan by identity.
   * @param id The plan's identity.
   * @returns The plan, or `undefined` when the list holds none with that id (not loaded yet, or deleted).
   */
  const find = (id: string): ManagedPlan | undefined => {
    return plans.value.find((plan) => {
      return plan.id === id
    })
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
   * Defines a plan and adds it to the held list.
   * @param request The plan's name and limits.
   * @returns The created plan, or `null` when the panel refused — the reason is in
   * {@link saveErrorMessage}.
   */
  const create = async (request: PlanRequest): Promise<ManagedPlan | null> => {
    saving.value = true
    try {
      const created = await api.create(request)
      plans.value = [...plans.value, created]
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
   * Replaces a plan's name and limits and swaps the held row for the server's answer.
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
   * Deletes a plan and removes it from the held list.
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
    loading,
    isLoaded,
    errorMessage,
    saving,
    saveErrorMessage,
    deleting,
    deleteErrorMessage,
    load,
    find,
    clearErrors,
    create,
    update,
    remove,
  }
})
