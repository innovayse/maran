import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsZonesApi } from '../composables/apis/useDnsZonesApi'
import { ApiError } from '../composables/useApi'
import type { CreateDnsZoneRequest, DnsZone, DnsZoneLoad } from '../types/dns'
import type { ListLoad, ListRequest } from '../types/list'

/**
 * Owns the page of the zones list on screen, one zone's detail, and the zone mutations. The DNS
 * pages read state from here and call its actions; none touches the API composable directly
 * (rules/vue.md).
 *
 * Error text is never generated here: the backend's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text").
 */
export const useDnsZonesStore = defineStore('dnsZones', () => {
  const api = useDnsZonesApi()

  /** The rows of the list page last answered by the panel; empty before the first answer. */
  const zones: Ref<DnsZone[]> = ref([])

  /** What the detail page last learned about the zone it asked for. */
  const detail: Ref<DnsZoneLoad> = ref({ state: 'loading' })

  /** True while a create request is in flight. */
  const creating: Ref<boolean> = ref(false)

  /** True while a delete is in flight. */
  const acting: Ref<boolean> = ref(false)

  /** The panel's reason the last create failed, already localized, or `null`. */
  const createErrorMessage: Ref<string | null> = ref(null)

  /** The panel's reason the last delete failed, already localized, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /**
   * Loads one page of the zone list into {@link zones} and reports how the read ended, for the
   * page's `useServerTable`.
   * @param request The page, size, search, sort and filters, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const list = async (request: ListRequest): Promise<ListLoad<DnsZone>> => {
    try {
      const page = await api.list(request)
      zones.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Loads one zone into {@link detail}. A 404 is an answer, not a failure: the zone does not exist
   * for the caller, which is also what another account's zone looks like.
   * @param id The zone to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadOne = async (id: string): Promise<void> => {
    detail.value = { state: 'loading' }
    try {
      detail.value = { state: 'found', zone: await api.get(id) }
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        detail.value = { state: 'notFound' }
      } else {
        detail.value = { state: 'failed', message: error instanceof ApiError ? error.message : null }
      }
    }
  }

  /**
   * Reads the zone on screen again without blanking it: a records change moves the record count
   * and the serial, and the overview must show the new figures without the page flashing to its
   * loading state. A failed read keeps what is held.
   * @param id The zone to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const refresh = async (id: string): Promise<void> => {
    try {
      detail.value = { state: 'found', zone: await api.get(id) }
    } catch {
      // The figures held are still the last the panel stated; the next full load corrects them.
    }
  }

  /**
   * Creates a zone. The list is not patched: where the new row falls is the server's to say, so
   * the list reads its page when it opens.
   * @param request The owning account, the apex name and the template.
   * @returns The created zone, or `null` when the request failed; the caller reads
   * {@link createErrorMessage} for the reason.
   */
  const create = async (request: CreateDnsZoneRequest): Promise<DnsZone | null> => {
    creating.value = true
    try {
      const created = await api.create(request)
      createErrorMessage.value = null
      return created
    } catch (error) {
      createErrorMessage.value = error instanceof ApiError ? error.message : null
      return null
    } finally {
      creating.value = false
    }
  }

  /**
   * Deletes a zone together with its records.
   * @param id The zone to delete.
   * @returns True when the panel deleted it; otherwise {@link actionErrorMessage} holds the reason.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      actionErrorMessage.value = null
      return true
    } catch (error) {
      actionErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Forgets the last refusals, so a screen that opens does not greet the person with a reason that
   * belonged to an earlier visit.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    createErrorMessage.value = null
    actionErrorMessage.value = null
  }

  return {
    zones,
    detail,
    creating,
    acting,
    createErrorMessage,
    actionErrorMessage,
    list,
    loadOne,
    refresh,
    create,
    remove,
    clearErrors,
  }
})
