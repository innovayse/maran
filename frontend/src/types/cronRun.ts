import type { ListRequest, Paged } from './list'

/**
 * How a run started by hand ended, mirroring the backend's `CronRunOutcome` as a camelCase name.
 * `stopped` means one of the agent's ceilings ended it before the program did.
 */
export type CronRunOutcome = 'succeeded' | 'failed' | 'stopped'

/** Every {@link CronRunOutcome}, in the order the history's filter offers them. */
export const CRON_RUN_OUTCOMES: readonly CronRunOutcome[] = ['succeeded', 'failed', 'stopped']

/** The agent's ceiling that stopped a run, mirroring the backend's `CronRunLimit`. */
export type CronRunLimit = 'runtime' | 'output'

/** The program ended on its own and returned a status. */
export interface CronRunExited {
  /** Discriminator. */
  kind: 'exited'
  /** The program's exit status; 0 is the status of a successful run. */
  exitCode: number
}

/** The program ended on its own but reported no status of its own, because it died of a signal. */
export interface CronRunWithoutStatus {
  /** Discriminator. */
  kind: 'withoutStatus'
}

/** One of the agent's ceilings stopped the run, so it has no status. */
export interface CronRunStopped {
  /** Discriminator. */
  kind: 'stopped'
  /** The ceiling that stopped it. */
  limit: CronRunLimit
}

/**
 * How a run ended in detail, mirroring the backend's `CronRunEndDto`: exactly one of three shapes,
 * told apart by `kind`, so a status never sits beside a ceiling.
 */
export type CronRunEnd = CronRunExited | CronRunWithoutStatus | CronRunStopped

/** One row of an entry's run history: everything about a run except what it printed. */
export interface CronRunSummary {
  /** The run's identity. */
  id: string
  /** The agent's identifier for the entry that ran. */
  entryId: string
  /** When the panel started the run, ISO 8601. */
  startedAt: string
  /** How long the run took, in milliseconds, measured by the agent. */
  durationMilliseconds: number
  /** How the run ended in detail. */
  end: CronRunEnd
  /** The verdict on it, for the badge and the filter. */
  outcome: CronRunOutcome
}

/** One run with what it printed, as the run-now answer and the single-run read return it. */
export interface CronRun extends CronRunSummary {
  /** Standard output, at most 64 KiB; empty when the output ceiling stopped the run. */
  stdout: string
  /** Standard error, at most 64 KiB; empty when the output ceiling stopped the run. */
  stderr: string
}

/**
 * Typed access to the run-now and run-history endpoints. Called from Pinia stores only, never from a
 * component (rules/vue.md). Every call names the account, which is the module's tenant boundary.
 */
export interface CronRunsApi {
  /**
   * Runs the entry now, as its account, and waits for it to end.
   * @param entryId The entry to run.
   * @param accountId The account whose crontab holds it.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The recorded run; a job that failed is a run, not an error.
   */
  run: (entryId: string, accountId: string, signal?: AbortSignal) => Promise<CronRun>

  /**
   * Lists one server page of the entry's runs, newest first by default.
   * @param entryId The entry whose runs to list.
   * @param accountId The account whose crontab holds it.
   * @param request The page, size, search, sort and the outcome filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page and the total across every page.
   */
  list: (entryId: string, accountId: string, request: ListRequest, signal?: AbortSignal) => Promise<Paged<CronRunSummary>>

  /**
   * Reads one run with what it printed.
   * @param entryId The entry the run belongs to.
   * @param accountId The account whose crontab holds it.
   * @param runId The run.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The run.
   */
  get: (entryId: string, accountId: string, runId: string, signal?: AbortSignal) => Promise<CronRun>
}
