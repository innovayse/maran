import { useApi } from '../useApi'
import type { PanelCertificate, PanelCertificateApi } from '../../types/panelCertificate'

/** The endpoint the panel's own certificate is read from and ordered through. */
const PANEL_CERTIFICATE_PATH = '/api/v1/panel-certificate'

/**
 * Builds the panel-certificate API on top of the shared low-level client.
 *
 * Neither call carries a host name: the name is the operator's configuration,
 * read by the server, never something the browser chooses.
 * @returns The {@link PanelCertificateApi} bound to the panel-certificate endpoint.
 */
export const usePanelCertificateApi = (): PanelCertificateApi => {
  const api = useApi()

  /**
   * Reads the trusted certificate recorded for the panel's configured host name.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The recorded certificate.
   */
  const get = (signal?: AbortSignal): Promise<PanelCertificate> => {
    return api.get<PanelCertificate>(PANEL_CERTIFICATE_PATH, signal)
  }

  /**
   * Orders a certificate for the panel's host name and installs it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The certificate now served.
   */
  const issue = (signal?: AbortSignal): Promise<PanelCertificate> => {
    return api.post<PanelCertificate>(PANEL_CERTIFICATE_PATH, undefined, signal)
  }

  return { get, issue }
}
