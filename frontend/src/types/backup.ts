import type { AccountListFilter } from './accountListFilter'
import type { ListRequest, Paged } from './list'
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
 * Whether a backup's artifact has been re-read after the run and found sound, mirroring the
 * backend's `BackupVerificationState`, which crosses the wire as a camelCase NAME.
 *
 * `notVerified` is not a failure: it is a backup nobody has checked yet, and the screen must not
 * draw it as one. Verification is its own fact beside {@link BackupStatus} and never rewrites it.
 */
export type BackupVerificationState = 'notVerified' | 'verified' | 'failed'

/**
 * Why a backup's verification failed, mirroring the backend's verification failure object: the
 * machine-stable code and the sentence already in the caller's language. The panel owns no text for
 * it (rules/vue.md): the message is rendered as given and the code is printed beneath it.
 */
export interface BackupVerificationFailure {
  /** The machine-stable code an operator quotes in a support ticket; never looked up for text. */
  code: string
  /** The failure as the operator reads it, in the request's language. */
  message: string
}

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
   * The owning account's CURRENT short name, resolved by the panel from its account directory. An
   * empty string when the panel does not name that account for this caller — including once the
   * account is deleted, which {@link orphanedAccountUsername} answers instead: this is the live
   * account's name, that one the name it had when it was removed.
   */
  ownerName: string
  /**
   * The account's username when that account no longer exists, and `null` while it does.
   *
   * A pre-deletion backup outlives its account deliberately, so for precisely those rows the
   * accounts list can name nobody — and those are the rows an operator reaches for after a
   * deletion they regret.
   */
  orphanedAccountUsername: string | null
  /** How far the run got. */
  status: BackupStatus
  /** The status as an operator reads it, in the request's language. */
  statusName: string
  /** Why the backup was taken. */
  kind: BackupKind
  /** The reason as an operator reads it, in the request's language. */
  kindName: string
  /** The artifact's size in bytes, or zero when there is no artifact. */
  sizeBytes: number
  /** The artifact's digest, hex and lowercase, or `null` when there is no artifact. */
  sha256: string | null
  /** How many database dumps the archive contains. */
  databaseCount: number
  /** When the run began, as an ISO-8601 string. */
  startedAt: string
  /** When the run ended, or `null` while it is still running. */
  finishedAt: string | null
  /**
   * The machine-stable code of the failure, or `null` when nothing failed.
   *
   * Never looked up in a translation table: the panel owns no text for a server outcome
   * (rules/vue.md). It is kept on the wire, and on the screen, because it is the value an operator
   * quotes in a support ticket and greps a log for — it does not change with the caller's language,
   * and `failureDisplayName` does.
   */
  failureCode: string | null
  /**
   * The same failure named in the caller's language, or `null` when nothing failed.
   *
   * It travels BESIDE {@link Backup.failureCode} rather than replacing it, and the screen renders
   * both for that reason: an operator reading a row needs a sentence, and the same operator writing
   * the incident up needs the code. A row shown with the code alone is what this field was added to
   * end — a Russian interface printed `Не удалась AgentSystemFailure`.
   *
   * It can be `null` on a failed row when the panel answering is older than the field. The screen
   * falls back to the code in that case rather than rendering an empty cell.
   */
  failureDisplayName: string | null
  /** Whether the artifact was re-read after the run and found sound. */
  verificationState: BackupVerificationState
  /** The verification state as an operator reads it, in the request's language. */
  verificationStateName: string
  /** When the artifact was last verified, as an ISO-8601 string, or `null` when it never was. */
  verifiedAt: string | null
  /** Why the last verification failed, or `null` when it did not fail. */
  verificationFailure: BackupVerificationFailure | null
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
 * The panel answers 200 with this shape **only** for a restore that replaced everything it set out
 * to; a partial restore is a failure response carrying a code, not a body. So the shape holds
 * nothing a success makes constant — no verdict, no files flag, no failure code, no total that
 * equals the restored count — and the one figure that varies is the number of databases replaced.
 * The verdict is the server's, and recomputing it in the SPA would be a second statement of the
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
  /** How many databases were dropped, re-created and loaded. */
  databasesRestored: number
}

/**
 * Where the one restore the backups dialog reports on stands: not started, running, replaced the
 * whole account, refused before anything was touched, or stopped after the account was changed.
 *
 * A union rather than a flag, a nullable outcome, a nullable message and a code held in separate
 * refs, so the dialog never has to re-derive which of five situations it is in, and cannot show a
 * "restored" panel over a leftover error. `unanswered` is its own arm because "no answer reached
 * the browser" says nothing about the account, and the dialog must neither claim the account was
 * left alone nor that it was changed.
 */
export type RestoreRun =
  | {
      /** No restore has been attempted since the dialog opened. */
      state: 'idle'
    }
  | {
      /** The request is in flight; the panel answers only once the account has been replaced. */
      state: 'running'
    }
  | {
      /** The account was wholly replaced. */
      state: 'restored'
      /** What the panel reported replacing. */
      outcome: RestoreOutcome
    }
  | {
      /** The browser got no answer at all: the panel was unreachable. */
      state: 'unanswered'
    }
  | {
      /** The panel refused, and the account is exactly as it was. */
      state: 'untouched'
      /** The backend's own localized message, rendered verbatim. */
      message: string
      /** The machine-stable problem code, kept for behaviour (which hint to show), never for text. */
      code: string
    }
  | {
      /** The restore stopped after the account had been changed: it must not be restored over blindly. */
      state: 'changed'
      /** The backend's own localized message, rendered verbatim. */
      message: string
      /** What the server had already replaced when it stopped, or `null` when it stated none. */
      partial: RestorePartial | null
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
 * The history's one attention item, mirroring the panel's `BackupAttentionDto`: how many accounts
 * have no good copy from their latest finished attempt, and the newest such failure. Asked of the
 * server because the history is paged and a page cannot answer a question about every row.
 */
export interface BackupAttention {
  /** Accounts whose most recent finished backup failed; a failure a later success superseded is not counted. */
  unrecoveredAccounts: number
  /** The newest of those failures, or `null` exactly when there are none. */
  newest: Backup | null
}

/**
 * Where the attention question stands on the backups page: not answered yet, answered, or not
 * answerable (refused or unreachable). A union rather than a nullable answer, so "nothing needs
 * attention" and "the panel could not say" are never the same value.
 */
export type BackupAttentionLoad =
  | {
      /** The answer has not arrived yet. */
      state: 'loading'
    }
  | {
      /** The panel answered. */
      state: 'answered'
      /** What it answered. */
      attention: BackupAttention
    }
  | {
      /** The panel refused or did not answer. */
      state: 'unavailable'
    }

/**
 * Typed access to the backups endpoints.
 *
 * Seven calls, matching the seven routes `BackupsController` publishes. `verify` and `restore` are two of them,
 * backed by the panel's `POST /api/v1/backups/{id}/verify` and `POST /api/v1/backups/{id}/restore`.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface BackupsApi {
  /**
   * Reads one page of the backups the caller may see, newest first unless a sort is named.
   * @param request The page, size, search, sort and the account, status, kind, from and to filters.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<Backup>>

  /**
   * Asks how many accounts' latest finished backup failed, and which failure is the newest.
   * @param filter The account the question is narrowed to; empty for every account the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The panel's answer.
   */
  attention: (filter: AccountListFilter, signal?: AbortSignal) => Promise<BackupAttention>

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
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Re-reads a backup's artifact on the server and records whether it is sound.
   * @param id The backup to verify.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The backup with its verification state as the panel now holds it.
   */
  verify: (id: string, signal?: AbortSignal) => Promise<Backup>

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
