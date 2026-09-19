/**
 * The database server's grant-table census, as the administrator's maintenance screen reads it, plus
 * the request that performs the repair.
 *
 * Everything here is a fact about the SERVER, measured by the agent on the host — not something the
 * panel remembers. The panel keeps no copy of the grant table, which is why there is no "last
 * repaired" field and no cached figure: a screen showing one would be describing a host it had not
 * looked at.
 */

/**
 * One grant the panel issued whose stored name had become a wildcard pattern — either rewritten, or
 * listed by a report as one that would be.
 */
export interface RepairedGrant {
  /** The fully-qualified database the grant was always meant to name. Never a pattern. */
  databaseName: string
  /** The fully-qualified user the grant belongs to. */
  dbUsername: string
  /**
   * Other databases on this server that the OLD pattern also matched.
   *
   * **Exposure, not use, and the screen must say both halves.** A name here is a database this
   * credential could have read and written while the row stood. An empty list does NOT clear the
   * row: a matching database may have been created and dropped in between, and a pattern is matched
   * when a client connects rather than when the agent looks. Rendering an empty list as "nothing was
   * exposed" is the one sentence this screen may never produce.
   */
  alsoMatchedDatabases: string[]
}

/** One grant-table row the repair left exactly as it found it, and what to do about it. */
export interface RefusedGrant {
  /** The database server's `Host` column, verbatim. */
  grantHost: string
  /**
   * The server's `Db` column, verbatim, escapes included.
   *
   * Not tidied anywhere: a row is refused because it is not a value this panel could have written,
   * so the bytes the server holds are the thing the operator has to compare against their server.
   */
  databaseName: string
  /** The server's `User` column, verbatim. */
  dbUsername: string
  /**
   * The machine-stable refusal name the agent reported. Shown as machine text beside the sentence,
   * never instead of it — it is what a support conversation and a log line can both spell.
   */
  reason: string
  /** What the agent decided, already localized by the backend (rules/vue.md). */
  reasonDisplayName: string
  /** What the operator can do about this row, already localized by the backend. */
  reasonAdvice: string
}

/**
 * What `GET /api/v1/database-grants` answers, and what `POST /api/v1/database-grants/repair` answers
 * too, mirroring the backend's `GrantRepairReportDto` field for field.
 *
 * **Administrator-only.** {@link refused} carries the raw columns of rows this panel did not write,
 * so it names databases and users the reader does not own. A customer's request answers 403, and the
 * store reads that as "not disclosed to me" rather than as a failure.
 */
export interface GrantRepairReport {
  /**
   * True when nothing on the server was changed.
   *
   * The screen reads this rather than testing whether {@link repaired} is empty: a repair that found
   * nothing to do returns the same two empty lists an inspection of a clean host does, and a reader
   * who cannot tell an inspection from an action would not know whether their customers' access had
   * already been rewritten.
   */
  isReportOnly: boolean
  /** Rows read and classified. The four buckets sum to exactly this number. */
  examinedGrants: number
  /** Rows that were already right, or whose name cannot hold a wildcard at all. */
  alreadyCorrect: number
  /** Rows that were rewritten. Empty on a report. */
  repaired: RepairedGrant[]
  /** Rows a report says would be rewritten. Empty after a repair. */
  wouldRepair: RepairedGrant[]
  /** Rows left untouched, each with its reason and the action it needs. */
  refused: RefusedGrant[]
}

/**
 * Request body for `POST /api/v1/database-grants/repair`, binding the backend's
 * `RepairDatabaseGrantsCommand`.
 *
 * It carries only the figure the report gave the operator. The server re-classifies the host and
 * refuses with a 409 when the number no longer matches, so this is not a confirmation checkbox: it
 * is what makes the report the only way in.
 */
export interface RepairGrantsRequest {
  /** How many rows the report the operator read said would be rewritten. */
  expectedRepairCount: number
}

/** Typed access to the grant-repair endpoints. Called from Pinia stores only (rules/vue.md). */
export interface DatabaseGrantsApi {
  /**
   * Reads what a repair would change on this host, changing nothing.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The census, with `isReportOnly` true.
   */
  report: (signal?: AbortSignal) => Promise<GrantRepairReport>

  /**
   * Performs the repair, provided the host still matches the report that was read.
   * @param request The figure the report gave the operator.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The census of what was actually changed, with `isReportOnly` false.
   */
  repair: (request: RepairGrantsRequest, signal?: AbortSignal) => Promise<GrantRepairReport>
}
