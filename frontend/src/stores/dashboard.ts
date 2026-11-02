import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useDashboardApi } from '../composables/apis/useDashboardApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { AdministratorDashboard, Dashboard } from '../types/dashboard'

/**
 * Owns what the landing screen is made of: the answer the panel gave for this caller, which is the
 * administrator's sections or a customer's nothing. The page reads state from here and calls `load`;
 * it never touches the API layer (rules/vue.md: "API composables are called from Pinia stores ONLY").
 *
 * **The answer is kept as the union the panel sent, and the sections as sections, deliberately.** A
 * section the panel could not read arrives as `unavailable` and is stored as that, never unwrapped
 * into an empty list, so the page can say "could not be read" rather than "nothing here"
 * (issue #248).
 *
 * Error text is never generated here: the panel's already-localized message is stored verbatim
 * (rules/vue.md: "the backend owns their text").
 */
export const useDashboardStore = defineStore('dashboard', () => {
  const api = useDashboardApi()

  /** The panel's answer as last read, or `null` before the first one. */
  const contents: Ref<Dashboard | null> = ref(null)

  /** Where the read of the screen stands: on its way, arrived, or refused. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the most recent failed read, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * The administrator's sections, or `null` when the answer was a customer's or has not arrived. A
   * computed view of {@link contents}, so the sections are non-null wherever it is non-null.
   */
  const administrator: ComputedRef<AdministratorDashboard | null> = computed(() => {
    const answer = contents.value
    return answer !== null && answer.audience === 'administrator' ? answer : null
  })

  /**
   * Reads the whole screen in one request.
   *
   * One request and not five, which is the point of the endpoint: five would show an operator five
   * panels describing five different moments, and each would need its own failure handling here.
   * The panel already isolates a failing section on its own side.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    try {
      const answer = await api.get()
      contents.value = answer
      errorMessage.value = null
      loadState.value = 'loaded'
    } catch (error) {
      // A refusal arrives here exactly like a failure and is rendered the same way: as the panel's
      // own message. The state is `error`, never `loaded` — the screen has no contents to show,
      // and claiming otherwise would draw zeros that nobody measured.
      errorMessage.value = error instanceof ApiError ? error.message : null
      loadState.value = 'error'
    }
  }

  return {
    contents,
    administrator,
    loadState,
    errorMessage,
    load,
  }
})
