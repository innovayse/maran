import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useMigrationJobsApi } from '../composables/apis/useMigrationJobsApi'
import { useLatestRead } from '../composables/useLatestRead'
import { ApiError } from '../composables/useApi'
import { saveDownload } from '../utils/saveDownload'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type {
  MigrationAccountAction,
  MigrationAccountReport,
  MigrationDnsAdvice,
  MigrationIssuedSecret,
  MigrationJob,
  MigrationJobAccount,
  MigrationJobAction,
  MigrationJobBusy,
  MigrationJobReport,
  MigrationJobRequest,
  MigrationMappingRequest,
} from '../types/migrationJob'

/**
 * Owns the whole-server migration jobs (issue #197, phase 2): one server page of the list, the open
 * job and one page of its accounts, the dry run, the controls, the comparison report, the DNS
 * advice and the credentials claimed once. Screens read state from here and call its actions; none
 * touches the API composable directly (rules/vue.md). Error text is never generated here: a
 * refusal's already-localized `detail` is stored verbatim.
 */
export const useMigrationJobsStore = defineStore('migrationJobs', () => {
  const api = useMigrationJobsApi()
  const latestJobRead = useLatestRead()
  const latestAccountRead = useLatestRead()

  /** The rows of the job list page last answered by the panel. */
  const jobs: Ref<MigrationJob[]> = ref([])

  /** The job the detail screen opened, or `null` before it arrived or after a failed read. */
  const current: Ref<MigrationJob | null> = ref(null)

  /** Where the read of {@link current} stands. */
  const currentState: Ref<LoadingState> = ref('loading')

  /** The rows of the account page last answered by the panel. */
  const accounts: Ref<MigrationJobAccount[]> = ref([])

  /** The action in flight, or `null`. */
  const busy: Ref<MigrationJobBusy | null> = ref(null)

  /** The panel's message for the last refused action, or `null`. */
  const actionErrorMessage: Ref<string | null> = ref(null)

  /** The dry run last read, or `null` before one ran. */
  const dryRunReport: Ref<MigrationJobReport | null> = ref(null)

  /** The DNS advice last read, or `null`. */
  const dnsAdvice: Ref<MigrationDnsAdvice | null> = ref(null)

  /** The comparison report last read, or `null`. */
  const accountReport: Ref<MigrationAccountReport | null> = ref(null)

  /**
   * Turns a failure into the message the screen shows.
   * @param error What was thrown.
   * @returns The panel's own message, or `null` for no answer (the caller shows the unreachable text).
   */
  const messageOf = (error: unknown): string | null => {
    return error instanceof ApiError ? error.message : null
  }

  /**
   * Runs one action, with its busy flag and its refusal.
   * @param kind Which action.
   * @param run The call.
   * @returns The answer, or `null` when it was refused or nothing answered.
   */
  const act = async <T>(kind: MigrationJobBusy, run: () => Promise<T>): Promise<T | null> => {
    busy.value = kind
    actionErrorMessage.value = null
    try {
      return await run()
    } catch (error) {
      actionErrorMessage.value = messageOf(error)
      return null
    } finally {
      busy.value = null
    }
  }

  /**
   * Loads one page of the job list.
   * @param request The page, size, search, sort and filters.
   * @returns How the read ended.
   */
  const list = async (request: ListRequest): Promise<ListLoad<MigrationJob>> => {
    const isLatest = latestJobRead.begin()
    try {
      const page = await api.list(request)
      if (isLatest()) {
        jobs.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Reads one job into {@link current}.
   * @param id The job.
   * @returns Resolves once the read settled.
   */
  const load = async (id: string): Promise<void> => {
    try {
      current.value = await api.get(id)
      currentState.value = 'loaded'
    } catch (error) {
      current.value = null
      currentState.value = error instanceof ApiError && error.status === 404 ? 'loaded' : 'error'
    }
  }

  /**
   * Loads one page of the open job's accounts.
   * @param id The job.
   * @param request The page, size, search, sort and filters.
   * @returns How the read ended.
   */
  const listAccounts = async (id: string, request: ListRequest): Promise<ListLoad<MigrationJobAccount>> => {
    const isLatest = latestAccountRead.begin()
    try {
      const page = await api.listAccounts(id, request)
      if (isLatest()) {
        accounts.value = page.items
      }
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
  }

  /**
   * Creates a draft job.
   * @param request The server, the label and the accounts.
   * @returns The job, or `null` when it was refused.
   */
  const create = (request: MigrationJobRequest): Promise<MigrationJob | null> => {
    return act('mapping', () => {
      return api.create(request)
    })
  }

  /**
   * Maps accounts of the open job.
   * @param id The job.
   * @param request The mapping.
   * @returns How many accounts changed, or `null` when refused.
   */
  const map = (id: string, request: MigrationMappingRequest): Promise<number | null> => {
    return act('mapping', () => {
      return api.map(id, request)
    })
  }

  /**
   * Runs the dry run into {@link dryRunReport}.
   * @param id The job.
   * @returns Resolves once it settled.
   */
  const dryRun = async (id: string): Promise<void> => {
    const report = await act('dryRun', () => {
      return api.dryRun(id)
    })
    if (report !== null) {
      dryRunReport.value = report
    }
  }

  /**
   * Starts, pauses, resumes or cancels the open job.
   * @param id The job.
   * @param action What to do.
   * @returns Resolves once it settled.
   */
  const control = async (id: string, action: MigrationJobAction): Promise<void> => {
    const kind = action.toLowerCase() as MigrationJobBusy
    const changed = await act(kind, () => {
      return api.control(id, action)
    })
    if (changed !== null) {
      current.value = changed
    }
  }

  /**
   * Changes the open job's limits.
   * @param id The job.
   * @param concurrency How many accounts at once.
   * @param bandwidthLimitKib The limit, or `null` for none.
   * @returns Resolves once it settled.
   */
  const setLimits = async (id: string, concurrency: number, bandwidthLimitKib: number | null): Promise<void> => {
    const changed = await act('limits', () => {
      return api.setLimits(id, concurrency, bandwidthLimitKib)
    })
    if (changed !== null) {
      current.value = changed
    }
  }

  /**
   * Retries, cancels or cuts over one account.
   * @param id The job.
   * @param accountId The account row.
   * @param action What to do.
   * @returns Resolves once it settled.
   */
  const controlAccount = async (id: string, accountId: string, action: MigrationAccountAction): Promise<void> => {
    await act('account', () => {
      return api.controlAccount(id, accountId, action)
    })
  }

  /**
   * Reads one account's comparison report into {@link accountReport}.
   * @param id The job.
   * @param accountId The account row.
   * @returns Resolves once it settled.
   */
  const loadReport = async (id: string, accountId: string): Promise<void> => {
    accountReport.value = null
    const report = await act('account', () => {
      return api.report(id, accountId)
    })
    if (report !== null) {
      accountReport.value = report
    }
  }

  /**
   * Claims the generated credentials, once.
   * @param id The job.
   * @param accountId The account row.
   * @returns The credentials, or `null` when refused (already claimed).
   */
  const claimSecrets = (id: string, accountId: string): Promise<MigrationIssuedSecret[] | null> => {
    return act('account', () => {
      return api.claimSecrets(id, accountId)
    })
  }

  /**
   * Reads the DNS advice into {@link dnsAdvice}.
   * @param id The job.
   * @returns Resolves once it settled.
   */
  const loadDns = async (id: string): Promise<void> => {
    const advice = await act('dns', () => {
      return api.dns(id)
    })
    if (advice !== null) {
      dnsAdvice.value = advice
    }
  }

  /**
   * Downloads the summary of a job and hands it to the browser's save flow.
   * @param id The job.
   * @param asJson Whether the file is JSON instead of CSV.
   * @returns Resolves once it settled.
   */
  const saveSummary = async (id: string, asJson: boolean): Promise<void> => {
    const file = await act('dns', () => {
      return api.summary(id, asJson)
    })
    if (file !== null) {
      saveDownload(file)
    }
  }

  /**
   * Forgets the last refusal and the open job's derived reads.
   * @returns Nothing.
   */
  const clearErrors = (): void => {
    actionErrorMessage.value = null
  }

  return {
    jobs,
    current,
    currentState,
    accounts,
    busy,
    actionErrorMessage,
    dryRunReport,
    dnsAdvice,
    accountReport,
    list,
    load,
    listAccounts,
    create,
    map,
    dryRun,
    control,
    setLimits,
    controlAccount,
    loadReport,
    claimSecrets,
    loadDns,
    saveSummary,
    clearErrors,
  }
})
