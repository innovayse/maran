import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMonitoringApi } from '../composables/apis/useMonitoringApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { PendingUpdate, UpdateClassificationChoice } from '../types/monitoring'

/**
 * Owns the page of pending operating-system updates on the Updates screen. The page reads state
 * from here and calls its actions; it never touches the API layer (rules/vue.md).
 *
 * The list is a server page read through `useServerTable`, so the refusal travels back to the table
 * through {@link listPendingUpdates} as the panel's own localized message. A refusal is a display
 * state and never an empty page: an agent that could not read the package cache must not look like
 * a server with nothing to update.
 */
export const usePendingUpdatesStore = defineStore('pendingUpdates', () => {
  const api = useMonitoringApi()

  /** The rows of the page last answered by the panel; empty before the first answer. */
  const items: Ref<PendingUpdate[]> = ref([])

  /** The kinds of update as the panel last named them, in its order; empty before the first answer or when unreadable. */
  const classifications: Ref<UpdateClassificationChoice[]> = ref([])

  /** Whether the panel has answered the kinds at least once, so an empty list differs from "not asked yet". */
  const classificationsAnswered: Ref<boolean> = ref(false)

  /** The panel's message for the last failed read of the kinds, or `null` when it succeeded or none was made. */
  const classificationsError: Ref<string | null> = ref(null)

  /** The security count of the whole server, or `null` when it is unknown or could not be measured. */
  const securityCount: Ref<number | null> = ref(null)

  /** Whether a reboot is required; `null` is unknown or not measured, and the page then says nothing. */
  const rebootRequired: Ref<boolean | null> = ref(null)

  /**
   * Loads one page of pending updates into {@link items} and reports how the read ended.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listPendingUpdates = async (request: ListRequest): Promise<ListLoad<PendingUpdate>> => {
    try {
      const page = await api.pendingUpdates(request)
      items.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      // Rows from an earlier page must not stay on screen beside a failure message.
      items.value = []
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads the kinds of update the filter offers into {@link classifications}.
   *
   * A failure keeps what was held (a language switch whose read failed still has usable names) and
   * records the backend's own message. The names depend on the language, so the page calls this
   * again when the interface language changes.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadClassifications = async (): Promise<void> => {
    try {
      classifications.value = await api.updateClassifications()
      classificationsAnswered.value = true
      classificationsError.value = null
    } catch (error) {
      classificationsError.value = error instanceof ApiError ? error.message : null
    }
  }

  /**
   * Names a kind of update as the panel named it.
   * @param value A kind as the wire spells it (a `?classification=` value).
   * @returns The panel's name, or `null` when the kinds have not arrived or the panel did not list
   * it, so a caller draws nothing rather than a raw value or a locale key.
   */
  const classificationName = (value: string): string | null => {
    return (
      classifications.value.find((choice) => {
        return choice.value === value
      })?.name ?? null
    )
  }

  /**
   * Whether a `?classification=` value is worth sending to the list endpoint.
   * @param value The value from the URL.
   * @returns True while the kinds have not arrived (the server decides), otherwise whether the panel listed it.
   */
  const acceptsClassification = (value: string): boolean => {
    if (!classificationsAnswered.value) {
      return true
    }
    return classifications.value.some((choice) => {
      return choice.value === value
    })
  }

  /**
   * Reads the security count and the reboot requirement for the notices above the list. Each read
   * stands alone and a failure forgets its own figure: an unread figure is advice that is missing,
   * never a zero or a "no reboot needed" the panel did not say.
   * @returns Resolves once both reads settled, successfully or not.
   */
  const loadSummary = async (): Promise<void> => {
    const [count, reboot] = await Promise.allSettled([api.pendingUpdateCount(), api.rebootRequirement()])
    securityCount.value = count.status === 'fulfilled' ? count.value.securityCount : null
    rebootRequired.value = reboot.status === 'fulfilled' ? reboot.value.required : null
  }

  return {
    items,
    classifications,
    classificationsError,
    securityCount,
    rebootRequired,
    listPendingUpdates,
    loadClassifications,
    classificationName,
    acceptsClassification,
    loadSummary,
  }
})
