import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useFtpsServerApi } from '../composables/apis/useFtpsServerApi'
import { ApiError } from '../composables/useApi'
import type { EnableFtpsRequest, FtpsStatus } from '../types/ftpsStatus'

/** The HTTP status the panel answers a caller who may not read the FTPS server's state. */
const FORBIDDEN = 403

/**
 * Owns the FTPS daemon's measured state and the administrator's switch.
 *
 * **A 403 here is not an error and is not stored as one.** `GET /api/v1/ftps-server` is
 * administrator-only, so a customer's load is refused by design — the status carries a certificate
 * path on the host, which is operator-facing text a customer must never be shown. The refusal lands
 * in {@link isDisclosed} as `false`, and the screens read that as "this panel will not tell me",
 * which is a different sentence from "FTPS is off" and from "the server is broken". Folding it into
 * {@link errorMessage} would put a red alert on every customer's file transfer screen for a request
 * the panel was right to refuse.
 *
 * Error text is never generated here: a real failure's already-localized `detail` is stored verbatim
 * (rules/vue.md).
 */
export const useFtpsServerStore = defineStore('ftpsServer', () => {
  const api = useFtpsServerApi()

  /**
   * The daemon's state as last measured, or `null` when it has not been read — either because
   * nothing has asked yet or because this caller may not be told.
   */
  const status: Ref<FtpsStatus | null> = ref(null)

  /**
   * Whether the panel disclosed the status to this caller.
   *
   * `false` until a load succeeds, and `false` again after a 403. A screen must read this rather
   * than test `status === null`, because the two are the same value for "not asked yet" and
   * "refused", and only one of them is worth a sentence to the reader.
   */
  const isDisclosed: Ref<boolean> = ref(false)

  /** True while the status request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while an enable or disable request is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent genuine failure, or `null`. A 403 never lands
   * here — see this store's own note about why.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Reads the daemon's state.
   *
   * A 403 is recorded as non-disclosure and NOT as a failure, so a customer's screen shows the
   * facts it does have instead of an alert about a refusal that was correct.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    try {
      status.value = await api.status()
      isDisclosed.value = true
      errorMessage.value = null
    } catch (error) {
      if (error instanceof ApiError && error.status === FORBIDDEN) {
        status.value = null
        isDisclosed.value = false
        errorMessage.value = null
        return
      }
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      loading.value = false
    }
  }

  /**
   * Turns the daemon on for a hostname this panel serves, and holds the state measured afterwards.
   * @param request The hostname, and the passive address for a host behind NAT.
   * @returns True when the panel brought the daemon up.
   */
  const enable = async (request: EnableFtpsRequest): Promise<boolean> => {
    acting.value = true
    try {
      // The response is the state measured AFTER the change, so it replaces what is held rather
      // than being merged into it: a status assembled from what was asked for would report a
      // healthy daemon on a host where the unit failed to start.
      status.value = await api.enable(request)
      isDisclosed.value = true
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
   * Turns the daemon off. The firewall is deliberately left alone — the server panel's own warning
   * is what makes the leftover open ports visible.
   * @returns True when the panel stopped the daemon.
   */
  const disable = async (): Promise<boolean> => {
    acting.value = true
    try {
      status.value = await api.disable()
      isDisclosed.value = true
      errorMessage.value = null
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      acting.value = false
    }
  }

  return { status, isDisclosed, loading, acting, errorMessage, load, enable, disable }
})
