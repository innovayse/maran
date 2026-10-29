import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDashboardApi } from '../composables/apis/useDashboardApi'
import { ApiError } from '../composables/useApi'
import type { AuditEvent } from '../types/audit'
import type { DashboardAttention, DashboardCounts, DashboardResources } from '../types/dashboard'
import type { ServiceStatus } from '../types/monitoring'

/**
 * Owns what the landing screen is made of: the host's live resource reading, the services the agent
 * watches, what this server holds, what needs attention, and the newest audit entries. The page
 * reads state from here and calls `load`; it never touches the API layer (rules/vue.md: "API
 * composables are called from Pinia stores ONLY").
 *
 * **The nulls are kept as nulls, deliberately.** The panel answers with a null section to mean "not
 * for this caller", and collapsing that into an empty object here would make a withheld section
 * indistinguishable from an empty one — which is the difference the page needs in order to say
 * "nothing to show you" instead of drawing four blank panels at a customer.
 *
 * Error text is never generated here: the panel's already-localized message is stored verbatim
 * (rules/vue.md: "the backend owns their text").
 */
export const useDashboardStore = defineStore('dashboard', () => {
  const api = useDashboardApi()

  /** Whether the panel filled the administrator sections for this caller. */
  const isAdministrator: Ref<boolean> = ref(false)

  /** The host's live resource reading, or `null` when withheld, unreadable, or not yet read. */
  const resources: Ref<DashboardResources | null> = ref(null)

  /** The watched services as last reported. */
  const services: Ref<ServiceStatus[]> = ref([])

  /** What this server holds, or `null` when withheld, unreadable, or not yet read. */
  const counts: Ref<DashboardCounts | null> = ref(null)

  /** What needs looking at, or `null` when withheld, unreadable, or not yet read. */
  const attention: Ref<DashboardAttention | null> = ref(null)

  /** The newest audit entries as last reported. */
  const recentAudit: Ref<AuditEvent[]> = ref([])

  /** True while a read is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True once the screen has been read at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed read, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the whole screen in one request.
   *
   * One request and not five, which is the point of the endpoint: five would show an operator five
   * panels describing five different moments, and each would need its own failure handling here.
   * The panel already isolates a failing section on its own side.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      const answer = await api.get()
      isAdministrator.value = answer.isAdministrator
      resources.value = answer.resources
      services.value = answer.services
      counts.value = answer.counts
      attention.value = answer.attention
      recentAudit.value = answer.recentAudit
      errorMessage.value = null
      isLoaded.value = true
    } catch (error) {
      // A refusal arrives here exactly like a failure and is rendered the same way: as the panel's
      // own message. `isLoaded` is deliberately NOT set — the screen has no contents to show, and
      // claiming otherwise would draw zeros that nobody measured.
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  return {
    isAdministrator,
    resources,
    services,
    counts,
    attention,
    recentAudit,
    loading,
    isLoaded,
    errorMessage,
    load,
  }
})
