import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useDnsZonesApi } from '../composables/apis/useDnsZonesApi'
import { ApiError } from '../composables/useApi'
import type { DnsRecordCatalog } from '../types/dns'

/**
 * Owns the DNS reference data: the record types and the fields each takes, the TTL and length
 * bounds, and the CAA tags. Every DNS screen selects from it, and none of it is written into the
 * SPA (rules/vue.md: the SPA holds no domain constants).
 *
 * The read is repeated every time a screen opens, because its descriptions are localized per
 * request and a language switch remounts the routed page.
 */
export const useDnsCatalogStore = defineStore('dnsCatalog', () => {
  const api = useDnsZonesApi()

  /** The catalogue, or `null` until the panel has answered; forms stay permissive until then. */
  const catalog: Ref<DnsRecordCatalog | null> = ref(null)

  /** The panel's reason the last read failed, already localized, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the catalogue. A failure leaves the previous data in place and records the reason: the
   * data is advice for the forms, and the server's refusal is what a person reads on submit.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    try {
      catalog.value = await api.catalog()
      errorMessage.value = null
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    }
  }

  return { catalog, errorMessage, load }
})
