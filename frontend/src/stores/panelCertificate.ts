import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { usePanelCertificateApi } from '../composables/apis/usePanelCertificateApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { PanelCertificate, PanelCertificateStatus } from '../types/panelCertificate'

/** The code the panel answers with while it serves its self-signed certificate. */
const NOT_ISSUED_CODE = 'PanelCertificateNotIssued'

/** The code the panel answers with when no usable host name is configured. */
const NO_HOST_NAME_CODE = 'PanelHostNameNotConfigured'

/**
 * Owns what the panel's own interface is served with, for the panel certificate
 * screen (issue #132).
 *
 * The read's two refusals are states, not failures: "not issued" is the
 * installer's self-signed certificate, and "no host name" is a configuration the
 * operator has to set. The codes decide the case; the sentence shown is always
 * the panel's own (rules/vue.md "Errors from the server").
 */
export const usePanelCertificateStore = defineStore('panelCertificate', () => {
  const api = usePanelCertificateApi()

  /** What the panel is served with, or `null` until the read has settled. */
  const status: Ref<PanelCertificateStatus | null> = ref(null)

  /** Where the read stands; an order never moves it. */
  const loadState: Ref<LoadingState> = ref('loading')

  /** True while an order is in flight. */
  const issuing: Ref<boolean> = ref(false)

  /** True once the last order installed a certificate, so the screen can confirm it. */
  const issued: Ref<boolean> = ref(false)

  /** Backend-localized message from the last failure, rendered verbatim. */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Turns a recorded certificate into the trusted state.
   * @param certificate The certificate the panel reported.
   * @returns The trusted status.
   */
  const trusted = (certificate: PanelCertificate): PanelCertificateStatus => {
    return { kind: 'trusted', certificate }
  }

  /**
   * Reads what the panel is served with.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    errorMessage.value = null
    try {
      status.value = trusted(await api.get())
      loadState.value = 'loaded'
    } catch (error) {
      // The two refusals that describe a state are the screen's content, not its failure.
      if (error instanceof ApiError && error.code === NOT_ISSUED_CODE) {
        status.value = { kind: 'selfSigned' }
        loadState.value = 'loaded'
      } else if (error instanceof ApiError && error.code === NO_HOST_NAME_CODE) {
        status.value = { kind: 'noHostName', message: error.message }
        loadState.value = 'loaded'
      } else {
        errorMessage.value = error instanceof ApiError ? error.message : null
        loadState.value = 'error'
      }
    }
  }

  /**
   * Orders a certificate for the panel's host name and installs it. A refusal
   * leaves the certificate the panel was serving in place, and its sentence on
   * the screen.
   * @returns True when a certificate was installed.
   */
  const issue = async (): Promise<boolean> => {
    issuing.value = true
    issued.value = false
    errorMessage.value = null
    try {
      status.value = trusted(await api.issue())
      issued.value = true
      return true
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      issuing.value = false
    }
  }

  return { status, loadState, issuing, issued, errorMessage, load, issue }
})
