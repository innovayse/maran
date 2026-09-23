/**
 * How far one backup got, and therefore whether its artifact may be relied on. Mirrors the
 * backend's `BackupStatus`, which crosses the wire as a camelCase NAME rather than a number — the
 * panel registers `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` for every enum it sends.
 *
 * Three values and no fourth. There is deliberately no `partial`: an archive missing a database is
 * an archive nothing may be restored from, so anything short of a complete artifact is `failed`.
 */
export type BackupStatus = 'running' | 'completed' | 'failed'

/**
 * Why a backup was taken, mirroring the backend's `BackupKind` and likewise a camelCase name on
 * the wire.
 *
 * All four values are declared because the backend declares all four and may send any of them at
 * any time. Only `manual` is produced today — schedules, the final backup before an account is
 * deleted, and the safety copy taken before a restore are all later work — but a screen that met
 * one of the others and rendered nothing would be worse than one that names it.
 */
export type BackupKind = 'manual' | 'scheduled' | 'preDeletion' | 'preRestore'

/**
 * Outward view of one backup, mirroring the backend's `BackupDto` field for field.
 *
 * It carries no path, no bucket and no object key, because the backend's DTO carries none: where
 * the bytes live is not a customer's business. It does carry the digest, which is not a secret —
 * a SHA-256 of an archive discloses nothing about its contents and cannot be inverted — and is the
 * one value an operator checking a file on the server needs.
 */
export interface Backup {
  /** The backup's identity, which is also the name its artifact is stored under. */
  id: string
  /** The account this is a backup of. */
  accountId: string
  /**
   * The account's username when that account no longer exists, and an empty string while it does.
   *
   * A pre-deletion backup outlives its account deliberately, so for precisely those rows the
   * accounts list can name nobody — and those are the rows an operator reaches for after a
   * deletion they regret.
   */
  orphanedAccountUsername: string
  /** How far the run got. */
  status: BackupStatus
  /** Why the backup was taken. */
  kind: BackupKind
  /** The artifact's size in bytes, or zero when there is no artifact. */
  sizeBytes: number
  /** The artifact's digest, hex and lowercase, or empty when there is no artifact. */
  sha256: string
  /** How many database dumps the archive contains. */
  databaseCount: number
  /** When the run began, as an ISO-8601 string. */
  startedAt: string
  /** When the run ended, or `null` while it is still running. */
  finishedAt: string | null
  /**
   * The machine-stable code of the failure, or empty when nothing failed.
   *
   * Never looked up in a translation table: the panel owns no text for a server outcome
   * (rules/vue.md). It is kept on the wire, and on the screen, because it is the value an operator
   * quotes in a support ticket and greps a log for — it does not change with the caller's language,
   * and `failureDisplayName` does.
   */
  failureCode: string
  /**
   * The same failure named in the caller's language, or empty when nothing failed.
   *
   * It travels BESIDE {@link Backup.failureCode} rather than replacing it, and the screen renders
   * both for that reason: an operator reading a row needs a sentence, and the same operator writing
   * the incident up needs the code. A row shown with the code alone is what this field was added to
   * end — a Russian interface printed `Не удалась AgentSystemFailure`.
   *
   * It can be empty on a failed row when the panel answering is older than the field. The screen
   * falls back to the code in that case rather than rendering an empty cell.
   */
  failureDisplayName: string
}

/**
 * Request body for `POST /api/v1/backups`, binding the backend's `CreateBackupCommand`.
 *
 * One field, and the two a caller might expect are absent because the backend refuses them: there
 * is no destination, since where the bytes go is the operator's configuration rather than a
 * per-request choice, and no kind, since everything asked for over HTTP is a manual backup.
 */
export interface CreateBackupRequest {
  /** The account to back up. Another customer's answers not-found, never forbidden. */
  accountId: string
}

/**
 * Request body for `POST /api/v1/backups/{id}/restore`, binding the backend's
 * `RestoreBackupCommand`.
 *
 * One field, and it is the whole of the confirmation. The command declares three more —
 * `backupId`, `ipAddress`, `userAgent` — and every one of them is `[BindNever]` and `JsonIgnore`
 * on the backend, stamped by the controller from the route and the connection. Sending any of them
 * would be sending a value the server discards, and a `backupId` in the body is precisely the
 * attack the server's stamping exists to refuse: it would let a caller type one account's name and
 * have another account's backup restored.
 */
export interface RestoreBackupRequest {
  /**
   * The target account's system user name, typed by the operator. Compared for an exact,
   * case-sensitive match against the account being replaced — never against the caller's own name,
   * so an administrator restoring on a customer's behalf types the customer's name.
   */
  confirmAccountUsername: string
}

/**
 * What a restore did, mirroring the backend's `RestoreOutcomeDto` field for field.
 *
 * The panel answers 200 with this shape **only** when `whole` is true; a partial restore is a
 * failure response carrying a code, not a body. So every value here describes a restore that
 * replaced everything it set out to, and the counts are here to be SHOWN rather than re-judged —
 * the verdict is the server's, and recomputing it in the SPA would be a second statement of the
 * rule in a place the backend cannot test.
 *
 * There is deliberately no list of rolled-back databases, because the wire carries none: the
 * agent's `not_rolled_back` field is documented as always empty and those lists live only on its
 * failing arms, inside diagnostics the panel logs at its own boundary and never sends outward.
 */
export interface RestoreOutcome {
  /** The backup that was restored from. */
  backupId: string
  /** The account that was replaced. */
  accountId: string
  /** Whether the account was fully replaced. `true` on every body the SPA can receive. */
  whole: boolean
  /** Whether the account's home is now the archive's home. */
  filesRestored: boolean
  /** How many databases were dropped, re-created and loaded. */
  databasesRestored: number
  /** How many the restore set out to replace. */
  databasesTotal: number
  /** The machine-stable code naming what went wrong, empty whenever `whole`. */
  failureCode: string
}

/**
 * What a restore that stopped partway had already replaced, mirroring the backend's
 * `RestorePartialDto` field for field.
 *
 * It arrives on the FAILURE response — the `restore` extension member of the problem JSON, decoded
 * onto `ApiError.extensions` — because a partial restore is a rejection and a rejection has no
 * body of its own. Absent entirely when the server measured nothing (a refusal that touched
 * nothing, a truncated stream) and from an older panel that does not send counts, so a screen must
 * render cleanly without it and must never invent a `0 of 0`.
 */
export interface RestorePartial {
  /** Whether the account's home was already the archive's home when the run stopped. */
  filesRestored: boolean
  /** How many databases had been dropped, re-created and loaded when it stopped. */
  databasesRestored: number
  /** How many the restore set out to replace. */
  databasesTotal: number
}

/**
 * Typed access to the backups endpoints.
 *
 * Five calls, matching the five routes `BackupsController` publishes. `restore` is one of them:
 * the panel's `POST /api/v1/backups/{id}/restore` landed with Task 10, and the note that used to
 * stand here — that no panel endpoint reached the agent's restore — is no longer true of this tree.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface BackupsApi {
  /**
   * Lists the backups the caller may see, newest first.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backups, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<Backup[]>

  /**
   * Reads one backup. Another customer's backup answers 404, never 403.
   * @param id The backup to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backup as the panel holds it now.
   */
  get: (id: string, signal?: AbortSignal) => Promise<Backup>

  /**
   * Takes a backup of an account now, answering once the run has finished.
   * @param request The account to back up.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The finished record — completed or failed, which its status says.
   */
  create: (request: CreateBackupRequest, signal?: AbortSignal) => Promise<Backup>

  /**
   * Deletes a backup's archive and its record. The copy is gone and is not recoverable.
   * @param id The backup to delete.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel deleted it.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>

  /**
   * Replaces an account from one of its backups: its home directory is swapped for the archive's
   * and each of its databases is dropped, re-created and reloaded.
   *
   * It resolves ONLY for a restore that replaced everything it set out to. Every other ending —
   * including a restore that changed the account and left it half-replaced — rejects with an
   * `ApiError` whose `code` says which, and the two codes that mean the account was CHANGED are
   * the ones a caller must not treat as a retryable failure.
   * @param id The backup to restore from. Another customer's answers 404, never 403.
   * @param request The typed confirmation.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns What the restore replaced.
   */
  restore: (
    id: string,
    request: RestoreBackupRequest,
    signal?: AbortSignal,
  ) => Promise<RestoreOutcome>
}
