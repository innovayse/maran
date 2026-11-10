import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type {
  AccountExport,
  AccountImport,
  AccountImportReport,
  AccountMoveApi,
  ExportGrant,
  ExportGrantSummary,
  IssueExportGrantRequest,
  IssuedSecret,
  StartAccountImportRequest,
} from '../../types/accountMove'
import type { ApiDownload } from '../../types/api'
import type { ListRequest, Paged } from '../../types/list'

/** The endpoint an account is exported through (administrator-only). */
const EXPORTS_PATH = '/api/v1/account-exports'

/** The endpoint an exported account is imported through (administrator-only). */
const IMPORTS_PATH = '/api/v1/account-imports'

/** The grants list's parameter for each filter its page reads from the URL. */
const GRANT_FILTER_PARAMS: Readonly<Record<string, string>> = { state: 'state' }

/** The imports list's parameter for each filter its page reads from the URL. */
const IMPORT_FILTER_PARAMS: Readonly<Record<string, string>> = { state: 'state' }

/** The exports list takes no filter. */
const EXPORT_FILTER_PARAMS: Readonly<Record<string, string>> = {}

/**
 * Builds the account-move API on top of the shared low-level client.
 * @returns The {@link AccountMoveApi} bound to the panel's export and import endpoints.
 */
export const useAccountMoveApi = (): AccountMoveApi => {
  const api = useApi()

  /**
   * Exports one account.
   * @param accountId The account to export.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The export as made.
   */
  const createExport = (accountId: string, signal?: AbortSignal): Promise<AccountExport> => {
    return api.post<AccountExport>(EXPORTS_PATH, { accountId }, signal)
  }

  /**
   * Lists one page of exports.
   * @param request The page, size, search and sort.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  const listExports = (request: ListRequest, signal?: AbortSignal): Promise<Paged<AccountExport>> => {
    return api.get<Paged<AccountExport>>(`${EXPORTS_PATH}${listQuery(request, EXPORT_FILTER_PARAMS)}`, signal)
  }

  /**
   * Lists one page of pull grants.
   * @param request The page, size, search, sort and the `state` filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page; no row carries a token.
   */
  const listGrants = (request: ListRequest, signal?: AbortSignal): Promise<Paged<ExportGrantSummary>> => {
    return api.get<Paged<ExportGrantSummary>>(`${EXPORTS_PATH}/grants${listQuery(request, GRANT_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one export.
   * @param id The export's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The export.
   */
  const getExport = (id: string, signal?: AbortSignal): Promise<AccountExport> => {
    return api.get<AccountExport>(`${EXPORTS_PATH}/${id}`, signal)
  }

  /**
   * Fetches the package of an export.
   * @param id The export's identity.
   * @param signal Optional abort signal to cancel the download.
   * @returns The file and the name the panel proposed.
   */
  const downloadPackage = (id: string, signal?: AbortSignal): Promise<ApiDownload> => {
    return api.download(`${EXPORTS_PATH}/${id}/package`, signal)
  }

  /**
   * Issues a single-use pull grant.
   * @param id The export's identity.
   * @param request The grant's address range and lifetime.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The grant, with its token this once.
   */
  const issueGrant = (id: string, request: IssueExportGrantRequest, signal?: AbortSignal): Promise<ExportGrant> => {
    return api.post<ExportGrant>(`${EXPORTS_PATH}/${id}/grants`, request, signal)
  }

  /**
   * Revokes a grant.
   * @param grantId The grant's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel revoked it.
   */
  const revokeGrant = async (grantId: string, signal?: AbortSignal): Promise<void> => {
    await api.delete<void>(`${EXPORTS_PATH}/grants/${grantId}`, signal)
  }

  /**
   * Uploads a package for verification.
   * @param file The package file, sent as its raw bytes.
   * @param fingerprint The sending server's key fingerprint, as read on that server.
   * @param signal Optional abort signal to cancel the upload.
   * @returns The staged import.
   */
  const stageImport = (file: Blob, fingerprint: string, signal?: AbortSignal): Promise<AccountImport> => {
    return api.upload<AccountImport>(`${IMPORTS_PATH}?fingerprint=${encodeURIComponent(fingerprint)}`, file, signal)
  }

  /**
   * Lists one page of imports.
   * @param request The page, size, search, sort and the `state` filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page.
   */
  const listImports = (request: ListRequest, signal?: AbortSignal): Promise<Paged<AccountImport>> => {
    return api.get<Paged<AccountImport>>(`${IMPORTS_PATH}${listQuery(request, IMPORT_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one import.
   * @param id The import's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The import.
   */
  const getImport = (id: string, signal?: AbortSignal): Promise<AccountImport> => {
    return api.get<AccountImport>(`${IMPORTS_PATH}/${id}`, signal)
  }

  /**
   * Runs the dry run of a staged import.
   * @param id The import's identity.
   * @param planId The plan the account would be created against.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The report.
   */
  const getReport = (id: string, planId: string, signal?: AbortSignal): Promise<AccountImportReport> => {
    return api.get<AccountImportReport>(`${IMPORTS_PATH}/${id}/report?planId=${encodeURIComponent(planId)}`, signal)
  }

  /**
   * Starts the import job.
   * @param id The import's identity.
   * @param request The plan, the optional reseller and the two confirmations.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The import as it now stands.
   */
  const startImport = (id: string, request: StartAccountImportRequest, signal?: AbortSignal): Promise<AccountImport> => {
    return api.post<AccountImport>(`${IMPORTS_PATH}/${id}/start`, request, signal)
  }

  /**
   * Discards a staged import.
   * @param id The import's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The import as it now stands.
   */
  const discardImport = (id: string, signal?: AbortSignal): Promise<AccountImport> => {
    return api.delete<AccountImport>(`${IMPORTS_PATH}/${id}`, signal)
  }

  /**
   * Claims the credentials the import generated.
   * @param id The import's identity.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The credentials, once.
   */
  const claimSecrets = (id: string, signal?: AbortSignal): Promise<IssuedSecret[]> => {
    return api.post<IssuedSecret[]>(`${IMPORTS_PATH}/${id}/secrets`, undefined, signal)
  }

  return {
    createExport,
    listExports,
    listGrants,
    getExport,
    downloadPackage,
    issueGrant,
    revokeGrant,
    stageImport,
    listImports,
    getImport,
    getReport,
    startImport,
    discardImport,
    claimSecrets,
  }
}
