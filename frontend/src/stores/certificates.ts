import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useCertificatesApi } from '../composables/apis/useCertificatesApi'
import { ApiError } from '../composables/useApi'
import type {
  Certificate,
  InstallCustomCertificateRequest,
  IssueCertificateRequest,
} from '../types/certificate'

/**
 * Owns a site's TLS certificates: what is installed, and the three things an operator can do
 * about it.
 *
 * Every action reports failure with the panel's own message and nothing of its own, because the
 * backend owns every word a user reads (rules/vue.md). A failure that carried no server message
 * — a dropped connection, say — leaves `errorMessage` null and the screen says nothing rather
 * than inventing a sentence.
 */
export const useCertificatesStore = defineStore('certificates', () => {
  const api = useCertificatesApi()

  /** The certificates installed for the site last loaded; empty before a successful load. */
  const certificates: Ref<Certificate[]> = ref([])

  /** True while the list request is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while an issue, install or remove request is in flight. */
  const acting: Ref<boolean> = ref(false)

  /**
   * Backend-localized message from the most recent failure, or `null` when the last operation
   * succeeded, none has been attempted, or the failure carried no server message.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Records what a failed call is to be reported as.
   * @param error The rejection a call produced.
   * @returns Nothing.
   */
  const noteFailure = (error: unknown): void => {
    errorMessage.value = error instanceof ApiError ? error.message : null
  }

  /**
   * Loads the certificates installed for one site.
   * @param siteId The site to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (siteId: string): Promise<void> => {
    loading.value = true
    try {
      certificates.value = await api.list(siteId)
      errorMessage.value = null
    } catch (error) {
      // Not cleared to an empty list on failure: showing "no certificates" for a request that
      // never answered would tell a customer their site is unprotected when nobody knows.
      noteFailure(error)
    } finally {
      loading.value = false
    }
  }

  /**
   * Runs one certificate mutation and folds its result into the held list.
   * @param call The mutation to make.
   * @returns True when the panel accepted the change.
   */
  const mutate = async (call: () => Promise<Certificate>): Promise<boolean> => {
    acting.value = true
    try {
      const changed = await call()
      const known = certificates.value.some((certificate) => {
        return certificate.id === changed.id
      })
      certificates.value = known
        ? certificates.value.map((certificate) => {
            return certificate.id === changed.id ? changed : certificate
          })
        : [...certificates.value, changed]
      errorMessage.value = null
      return true
    } catch (error) {
      noteFailure(error)
      return false
    } finally {
      acting.value = false
    }
  }

  /**
   * Issues a certificate for the site's domain over ACME.
   * @param request The site to certify.
   * @returns True when the panel issued and installed it.
   */
  const issue = async (request: IssueCertificateRequest): Promise<boolean> => {
    return await mutate(() => {
      return api.issue(request)
    })
  }

  /**
   * Installs certificate material the customer supplied.
   *
   * The private key is passed straight through to the request and never stored in this state:
   * nothing in the panel displays one, and a key held in a store outlives the form that sent it.
   * @param request The site and its PEM-encoded chain and key.
   * @returns True when the panel installed it.
   */
  const installCustom = async (request: InstallCustomCertificateRequest): Promise<boolean> => {
    return await mutate(() => {
      return api.installCustom(request)
    })
  }

  /**
   * Removes an installed certificate; the site returns to serving plain HTTP.
   * @param id The certificate to remove.
   * @returns True when the panel removed it.
   */
  const remove = async (id: string): Promise<boolean> => {
    acting.value = true
    try {
      await api.remove(id)
      certificates.value = certificates.value.filter((certificate) => {
        return certificate.id !== id
      })
      errorMessage.value = null
      return true
    } catch (error) {
      noteFailure(error)
      return false
    } finally {
      acting.value = false
    }
  }

  return { certificates, loading, acting, errorMessage, load, issue, installCustom, remove }
})
