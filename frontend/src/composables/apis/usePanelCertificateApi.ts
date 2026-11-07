import { useApi } from '../useApi'
import type {
  PanelCertificate,
  PanelCertificateApi,
  PanelCertificateReading,
  PanelSelfSigned,
} from '../../types/panelCertificate'

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
   * Reads the trusted certificate recorded for the panel's configured host name, or that none is configured.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The recorded certificate, or the flagged no-host-name reading.
   */
  const get = (signal?: AbortSignal): Promise<PanelCertificateReading> => {
    return api.get<PanelCertificateReading>(PANEL_CERTIFICATE_PATH, signal)
  }

  /**
   * Orders a certificate for the panel's host name and installs it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The certificate now served.
   */
  const issue = (signal?: AbortSignal): Promise<PanelCertificate> => {
    return api.post<PanelCertificate>(PANEL_CERTIFICATE_PATH, undefined, signal)
  }

  /**
   * Returns the panel to a self-signed certificate and forgets the recorded trusted one.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The self-signed certificate now served.
   */
  const restore = (signal?: AbortSignal): Promise<PanelSelfSigned> => {
    return api.delete<PanelSelfSigned>(PANEL_CERTIFICATE_PATH, signal)
  }

  return { get, issue, restore }
}
