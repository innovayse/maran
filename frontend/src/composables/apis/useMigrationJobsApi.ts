import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ApiDownload } from '../../types/api'
import type { ListRequest, Paged } from '../../types/list'
import type {
  MigrationAccountAction,
  MigrationAccountReport,
  MigrationDnsAdvice,
  MigrationIssuedSecret,
  MigrationJob,
  MigrationJobAccount,
  MigrationJobAction,
  MigrationJobReport,
  MigrationJobRequest,
  MigrationJobsApi,
  MigrationMappingRequest,
} from '../../types/migrationJob'

/** The endpoint whole-server migration jobs are managed through (administrator-only). */
const JOBS_PATH = '/api/v1/migration-jobs'

/** The job list endpoint's parameter for each filter its page reads from the URL. */
const JOB_FILTER_PARAMS: Readonly<Record<string, string>> = { state: 'state' }

/** The account list endpoint's parameter for each filter its table reads. */
const ACCOUNT_FILTER_PARAMS: Readonly<Record<string, string>> = { state: 'state' }

/**
 * Builds the migration-jobs API on top of the shared low-level client.
 * @returns The {@link MigrationJobsApi} bound to the panel's migration-job endpoints.
 */
export const useMigrationJobsApi = (): MigrationJobsApi => {
  const api = useApi()

  /**
   * Lists one page of jobs.
   * @param request The page, size, search, sort and `state` filter.
   * @param signal Optional abort signal.
   * @returns The page.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<MigrationJob>> => {
    return api.get<Paged<MigrationJob>>(`${JOBS_PATH}${listQuery(request, JOB_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one job.
   * @param id The job.
   * @param signal Optional abort signal.
   * @returns The job.
   */
  const get = (id: string, signal?: AbortSignal): Promise<MigrationJob> => {
    return api.get<MigrationJob>(`${JOBS_PATH}/${id}`, signal)
  }

  /**
   * Creates a draft job.
   * @param request The server, the label and the accounts.
   * @param signal Optional abort signal.
   * @returns The job as stored.
   */
  const create = (request: MigrationJobRequest, signal?: AbortSignal): Promise<MigrationJob> => {
    return api.post<MigrationJob>(JOBS_PATH, request, signal)
  }

  /**
   * Lists one page of a job's accounts.
   * @param id The job.
   * @param request The page, size, search, sort and `state` filter.
   * @param signal Optional abort signal.
   * @returns The page.
   */
  const listAccounts = (id: string, request: ListRequest, signal?: AbortSignal): Promise<Paged<MigrationJobAccount>> => {
    return api.get<Paged<MigrationJobAccount>>(`${JOBS_PATH}/${id}/accounts${listQuery(request, ACCOUNT_FILTER_PARAMS)}`, signal)
  }

  /**
   * Maps accounts of a draft job.
   * @param id The job.
   * @param request The mapping.
   * @param signal Optional abort signal.
   * @returns How many accounts changed.
   */
  const map = (id: string, request: MigrationMappingRequest, signal?: AbortSignal): Promise<number> => {
    return api.put<number>(`${JOBS_PATH}/${id}/mapping`, request, signal)
  }

  /**
   * Runs the dry run.
   * @param id The job.
   * @param signal Optional abort signal.
   * @returns The findings.
   */
  const dryRun = (id: string, signal?: AbortSignal): Promise<MigrationJobReport> => {
    return api.post<MigrationJobReport>(`${JOBS_PATH}/${id}/dry-run`, undefined, signal)
  }

  /**
   * Starts, pauses, resumes or cancels the job.
   * @param id The job.
   * @param action What to do.
   * @param signal Optional abort signal.
   * @returns The job after the change.
   */
  const control = (id: string, action: MigrationJobAction, signal?: AbortSignal): Promise<MigrationJob> => {
    return api.post<MigrationJob>(`${JOBS_PATH}/${id}/control`, { action }, signal)
  }

  /**
   * Changes the concurrency and the bandwidth limit.
   * @param id The job.
   * @param concurrency How many accounts at once.
   * @param bandwidthLimitKib The limit in KiB/s, or `null` for none.
   * @param signal Optional abort signal.
   * @returns The job after the change.
   */
  const setLimits = (id: string, concurrency: number, bandwidthLimitKib: number | null, signal?: AbortSignal): Promise<MigrationJob> => {
    return api.put<MigrationJob>(`${JOBS_PATH}/${id}/limits`, { concurrency, bandwidthLimitKib }, signal)
  }

  /**
   * Retries, cancels or cuts over one account.
   * @param id The job.
   * @param accountId The account row.
   * @param action What to do.
   * @param signal Optional abort signal.
   * @returns The account after the change.
   */
  const controlAccount = (
    id: string,
    accountId: string,
    action: MigrationAccountAction,
    signal?: AbortSignal,
  ): Promise<MigrationJobAccount> => {
    return api.post<MigrationJobAccount>(`${JOBS_PATH}/${id}/accounts/${accountId}/control`, { action }, signal)
  }

  /**
   * Reads the comparison report of one account.
   * @param id The job.
   * @param accountId The account row.
   * @param signal Optional abort signal.
   * @returns The report.
   */
  const report = (id: string, accountId: string, signal?: AbortSignal): Promise<MigrationAccountReport> => {
    return api.get<MigrationAccountReport>(`${JOBS_PATH}/${id}/accounts/${accountId}/report`, signal)
  }

  /**
   * Claims the generated credentials, once.
   * @param id The job.
   * @param accountId The account row.
   * @param signal Optional abort signal.
   * @returns The credentials.
   */
  const claimSecrets = (id: string, accountId: string, signal?: AbortSignal): Promise<MigrationIssuedSecret[]> => {
    return api.post<MigrationIssuedSecret[]>(`${JOBS_PATH}/${id}/accounts/${accountId}/secrets`, undefined, signal)
  }

  /**
   * Reads where the job's domains resolve now.
   * @param id The job.
   * @param signal Optional abort signal.
   * @returns The DNS advice.
   */
  const dns = (id: string, signal?: AbortSignal): Promise<MigrationDnsAdvice> => {
    return api.get<MigrationDnsAdvice>(`${JOBS_PATH}/${id}/dns`, signal)
  }

  /**
   * Downloads the summary of a job as a file.
   * @param id The job.
   * @param asJson Whether the file is JSON instead of CSV.
   * @param signal Optional abort signal.
   * @returns The file and the name the panel proposed for it.
   */
  const summary = (id: string, asJson: boolean, signal?: AbortSignal): Promise<ApiDownload> => {
    return api.download(`${JOBS_PATH}/${id}/summary?json=${asJson ? 'true' : 'false'}`, signal)
  }

  return { list, get, create, listAccounts, map, dryRun, control, setLimits, controlAccount, report, claimSecrets, dns, summary }
}
