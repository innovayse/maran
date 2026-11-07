import type { ApiDownload } from './api'

/**
 * The personal-data export's shapes (rules/vue.md "Types": one domain per file). The document
 * itself is a file the panel streams and the browser saves; the SPA never parses or renders it, so
 * the only contract here is the endpoint that produces it.
 */

/** The privacy endpoints this SPA calls. Administrators only. */
export interface PrivacyApi {
  /**
   * Downloads everything the panel holds about one account, as a JSON document: every module's
   * records about it, its team, and the journal entries that name it. Secrets are never included.
   * @param accountId The account to export.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The file and the name the panel proposed for it.
   */
  exportAccount: (accountId: string, signal?: AbortSignal) => Promise<ApiDownload>
}
