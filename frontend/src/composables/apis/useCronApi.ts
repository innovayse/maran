import { useApi } from '../useApi'
import type {
  CreateCronEntryRequest,
  CronApi,
  CronEntry,
  CronEntryOutput,
  SetCronEntryEnabledRequest,
  UpdateCronEntryRequest,
} from '../../types/cronEntry'
import type {
  CronEnvironmentVariable,
  SetCronEnvironmentRequest,
} from '../../types/cronEnvironmentVariable'

/** The endpoint cron entries are listed, created, rewritten, switched and removed through. */
const CRON_ENTRIES_PATH = '/api/v1/cron-entries'

/** The endpoint the crontab's managed environment assignments are read and replaced through. */
const CRON_ENVIRONMENT_PATH = '/api/v1/cron-environment'

/**
 * Builds the cron API on top of the shared low-level client.
 *
 * One composable for both of the module's controllers, because they are one module and one screen:
 * the environment is a property of the crontab rather than of any entry, which is why the backend
 * gives it its own controller, but a caller that has one has the other.
 *
 * **Every call names the account explicitly, and that is the module's contract rather than a habit
 * of this file.** Cron keeps no rows, so an entry id means nothing until it is asked of one
 * account's crontab — the account travels as a query parameter on the reads and in the body on the
 * writes, and the handler resolving it is the whole tenant boundary. Omitting it does not widen
 * access, it simply asks an unanswerable question.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 * @returns The {@link CronApi} bound to the panel's cron endpoints.
 */
export const useCronApi = (): CronApi => {
  const api = useApi()

  /**
   * Builds the `?accountId=` a read carries, escaped rather than concatenated.
   * @param accountId The account the read is scoped to.
   * @returns The query string, including its leading `?`.
   */
  const accountQuery = (accountId: string): string => {
    return `?${new URLSearchParams({ accountId }).toString()}`
  }

  /**
   * Lists one account's cron entries.
   * @param accountId The account whose crontab to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The entries, in the order the agent reported them.
   */
  const list = (accountId: string, signal?: AbortSignal): Promise<CronEntry[]> => {
    return api.get<CronEntry[]>(`${CRON_ENTRIES_PATH}${accountQuery(accountId)}`, signal)
  }

  /**
   * Installs a new entry.
   * @param request The owning account, the schedule and the command.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The entry as installed, carrying the identifier the agent minted for it.
   */
  const create = (request: CreateCronEntryRequest, signal?: AbortSignal): Promise<CronEntry> => {
    return api.post<CronEntry>(CRON_ENTRIES_PATH, request, signal)
  }

  /**
   * Replaces an entry's schedule and command, leaving its enablement alone.
   * @param entryId The entry to rewrite.
   * @param request The owning account, the new schedule and the new command.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel rewrote the entry.
   */
  const update = (
    entryId: string,
    request: UpdateCronEntryRequest,
    signal?: AbortSignal,
  ): Promise<boolean> => {
    return api.put<boolean>(`${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}`, request, signal)
  }

  /**
   * Switches an entry on or off without touching what it runs.
   * @param entryId The entry to switch.
   * @param request The owning account and the state to put the entry in.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel switched it.
   */
  const setEnabled = (
    entryId: string,
    request: SetCronEntryEnabledRequest,
    signal?: AbortSignal,
  ): Promise<boolean> => {
    return api.post<boolean>(
      `${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}/enabled`,
      request,
      signal,
    )
  }

  /**
   * Reads what the entry's last run left behind.
   *
   * The module answers `200` with a `null` BODY for an entry that has never run, and that null is
   * passed straight through rather than flattened into an empty reading: every field of the reading
   * has a meaningful default — an empty string is a run that printed nothing, zero is a successful
   * exit, zero seconds is the epoch — so any invented value would tell a customer their job ran
   * when it never has, which is exactly the question somebody debugging a job that never fires is
   * asking.
   * @param entryId The entry to read.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The reading, or `null` when the entry has never run.
   */
  const getOutput = async (
    entryId: string,
    accountId: string,
    signal?: AbortSignal,
  ): Promise<CronEntryOutput | null> => {
    const reading = await api.get<CronEntryOutput | null>(
      `${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}/output${accountQuery(accountId)}`,
      signal,
    )
    // `readJson` also answers `undefined` for a body-less 200; both spellings of "nothing" become
    // the one the screen knows how to render.
    return reading ?? null
  }

  /**
   * Removes the entry, with the files that held its command and its last run.
   * @param entryId The entry to remove.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed it.
   */
  const remove = (
    entryId: string,
    accountId: string,
    signal?: AbortSignal,
  ): Promise<boolean> => {
    return api.delete<boolean>(
      `${CRON_ENTRIES_PATH}/${encodeURIComponent(entryId)}${accountQuery(accountId)}`,
      signal,
    )
  }

  /**
   * Reads one account's managed environment assignments.
   * @param accountId The account whose crontab to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The assignments the agent manages.
   */
  const listEnvironment = (
    accountId: string,
    signal?: AbortSignal,
  ): Promise<CronEnvironmentVariable[]> => {
    return api.get<CronEnvironmentVariable[]>(
      `${CRON_ENVIRONMENT_PATH}${accountQuery(accountId)}`,
      signal,
    )
  }

  /**
   * Replaces the managed assignments with exactly the set sent.
   * @param request The owning account and the complete new set.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel rewrote them.
   */
  const setEnvironment = (
    request: SetCronEnvironmentRequest,
    signal?: AbortSignal,
  ): Promise<boolean> => {
    return api.put<boolean>(CRON_ENVIRONMENT_PATH, request, signal)
  }

  return {
    list,
    create,
    update,
    setEnabled,
    getOutput,
    remove,
    listEnvironment,
    setEnvironment,
  }
}
