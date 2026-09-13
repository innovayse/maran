import { useApi } from '../useApi'
import type {
  Certificate,
  CertificatesApi,
  InstallCustomCertificateRequest,
  IssueCertificateRequest,
} from '../../types/certificate'

/** The endpoint certificates are listed, issued, installed and removed through. */
const CERTIFICATES_PATH = '/api/v1/certificates'

/**
 * Builds the certificates API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry
 * in the returned object: the name is what appears in a stack trace, and the doc block sits next
 * to the call it describes (rules/vue.md).
 *
 * This file was a declared SEAM until the Ssl module's endpoints existed — every call rejected —
 * and it stayed a seam after they did. The result an operator saw was a site whose Overview tab
 * said "Certificate: Installed", whose nginx was serving that certificate, and whose SSL tab said
 * the panel had no certificates. That is what a seam left in place costs, and it is why the tests
 * that asserted the seam's own message were rewritten rather than removed.
 * @returns The {@link CertificatesApi} bound to the panel's certificate endpoints.
 */
export const useCertificatesApi = (): CertificatesApi => {
  const api = useApi()

  /**
   * Lists the certificates installed for one site.
   *
   * The panel's collection endpoint answers with everything the CALLER may see, already scoped to
   * their tenancy, and this narrows that to the one site whose tab is open. The narrowing is here
   * rather than in the store because it is a property of the endpoint, not of any screen.
   * @param siteId The site to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The certificates installed for that site.
   */
  const list = async (siteId: string, signal?: AbortSignal): Promise<Certificate[]> => {
    const all = await api.get<Certificate[]>(CERTIFICATES_PATH, signal)
    return all.filter((certificate) => {
      return certificate.siteId === siteId
    })
  }

  /**
   * Issues a certificate over ACME for the site's domain.
   * @param request The domain to certify.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The issued certificate.
   */
  const issue = (
    request: IssueCertificateRequest,
    signal?: AbortSignal,
  ): Promise<Certificate> => {
    return api.post<Certificate>(CERTIFICATES_PATH, request, signal)
  }

  /**
   * Installs certificate material the customer supplied.
   * @param request The domain and its PEM-encoded chain and key.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The installed certificate.
   */
  const installCustom = (
    request: InstallCustomCertificateRequest,
    signal?: AbortSignal,
  ): Promise<Certificate> => {
    return api.post<Certificate>(`${CERTIFICATES_PATH}/custom`, request, signal)
  }

  /**
   * Removes an installed certificate; the site returns to serving plain HTTP.
   * @param id The certificate to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed it.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${CERTIFICATES_PATH}/${id}`, signal)
  }

  return { list, issue, installCustom, remove }
}
