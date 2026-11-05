import type { ListRequest, Paged } from './list'
/**
 * Outward view of one customer SFTP login, mirroring the backend's `SftpUserDto` field-for-field.
 *
 * There is no password here, and there is nothing for one to come from: no column on the server
 * holds it. An SFTP password exists in exactly two responses — the one that created the login and
 * the one that reset it — and never again.
 */
export interface SftpUser {
  /** The login's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this login. */
  accountId: string
  /**
   * The owning account's current short name, resolved by the panel from its account directory so
   * the list never joins accounts itself. An empty string when the panel does not name that account
   * for this caller.
   */
  ownerName: string
  /** The name the customer asked for, WITHOUT the account prefix. */
  name: string
  /**
   * The system login the host holds — `<account>_<name>`, as it appears in `/etc/passwd`.
   *
   * This is what the customer types into an SFTP client. Somebody who reads the unprefixed
   * {@link name} and types that instead simply cannot log in.
   */
  fullName: string
  /**
   * Which daemon accepts this login, as the panel spells it (`Sftp`). Mirrored for completeness; the
   * SFTP logins page lists this module's logins only, so it has no column to render it in.
   */
  protocol: string
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/sftp-users`, binding the backend's `CreateSftpUserCommand`
 * field-for-field.
 *
 * It has no password field and no chroot path. The panel mints the credential, and the jail is
 * derived from the account by the agent — so the customer has no directory to name and this
 * request has no path to be trusted with.
 */
export interface CreateSftpUserRequest {
  /** The account that will own the login. */
  accountId: string
  /** The login name, without the account prefix; lowercase letters and digits only. */
  name: string
}

/**
 * What `POST /api/v1/sftp-users` answered, mirroring the backend's `CreatedSftpUserDto` — the one
 * and only response that carries a new login's password.
 */
export interface CreatedSftpUser {
  /** The new login's identity. */
  id: string
  /** The account that owns it. */
  accountId: string
  /** The name the customer asked for, without the account prefix. */
  name: string
  /** The system login the host holds — what the customer signs in with. */
  fullName: string
  /**
   * The generated password. Nothing keeps a copy — not this SPA, not the panel, not the agent —
   * so it is shown once and recovered only by setting a new one.
   */
  password: string
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What `POST /api/v1/sftp-users/{id}/password` answered, mirroring the backend's
 * `SftpUserPasswordDto`. The only recovery path a lost password has.
 */
export interface SftpUserPassword {
  /** The login that was re-credentialled. */
  id: string
  /** The system login the new password belongs to. */
  fullName: string
  /** The new password, shown once and stored nowhere. */
  password: string
}

/**
 * An SFTP credential the panel is showing for the only time it ever will.
 *
 * The two responses above are folded into this one shape by the store, because the screen's
 * problem is the same for both: put the value in front of the operator, let them copy it, and say
 * plainly that closing the dialog ends the only chance they have. It is held in memory only — no
 * storage, no query string, no history entry — so a reload loses it exactly as the server did.
 */
export interface RevealedSftpCredential {
  /** The system login the password belongs to, prefixed exactly as the host holds it. */
  fullName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
}

/**
 * One public key that may sign in as an SFTP login, mirroring the backend's `SftpUserKeyDto`
 * field-for-field. The key body is never sent back: the panel keeps it, and the list shows only
 * what lets a person recognise the key.
 */
export interface SftpUserKey {
  /** The key's identity, and the only identifier a removal may name. */
  id: string
  /** The name the customer gave it ("Work laptop"), 1 to 64 characters. */
  label: string
  /** The key type as the panel read it from the key (`ssh-ed25519`, `ecdsa-sha2-nistp256`). */
  algorithm: string
  /** The SHA-256 fingerprint the panel computed, `SHA256:` then unpadded base64, as `ssh-keygen -l` prints it. */
  fingerprint: string
  /** The instant the key was added, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What the keys list asks the panel for: one page, optionally narrowed by a search on the label.
 * Sent as query parameters; the server validates every field.
 */
export interface SftpUserKeysQuery {
  /** The 1-based page asked for. */
  page: number
  /** Rows per page; one of the sizes the panel accepts. */
  pageSize: number
  /** Free-text search on the label; empty for none. */
  search: string
}

/**
 * Request body for `POST /api/v1/sftp-users/{id}/keys`, binding the backend's
 * `AddSftpUserKeyCommand` field-for-field. The login is named by the route, never by the body.
 */
export interface AddSftpUserKeyRequest {
  /** The name to give the key, 1 to 64 characters. */
  label: string
  /** The public key as one `<algorithm> <base64> [comment]` line; the comment is dropped by the server. */
  publicKey: string
}

/**
 * Typed access to the SFTP users endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface SftpApi {
  /**
   * Lists one page of the SFTP logins the caller may see.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<SftpUser>>

  /**
   * Creates an SFTP login.
   * @param request The owning account and the name the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login as created, including the password shown once.
   */
  create: (request: CreateSftpUserRequest, signal?: AbortSignal) => Promise<CreatedSftpUser>

  /**
   * Gives the login a new password. Another customer's login answers 404, not 403.
   * @param id The login to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  resetPassword: (id: string, signal?: AbortSignal) => Promise<SftpUserPassword>

  /**
   * Removes the login, and only the login: the account's files stay where they are.
   * @param id The login to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>

  /**
   * Lists one page of a login's public keys. Another customer's login answers 404, not 403.
   * @param loginId The login whose keys are listed.
   * @param query The page, size and label search.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered, with the total across every page.
   */
  listKeys: (loginId: string, query: SftpUserKeysQuery, signal?: AbortSignal) => Promise<Paged<SftpUserKey>>

  /**
   * Adds a public key to a login. Adding never ends an open session.
   * @param loginId The login that gains the key.
   * @param request The label and the key line.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The key as stored.
   */
  addKey: (loginId: string, request: AddSftpUserKeyRequest, signal?: AbortSignal) => Promise<SftpUserKey>

  /**
   * Removes a public key from a login; the login's open sessions are ended.
   * @param loginId The login that loses the key.
   * @param keyId The key to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   */
  removeKey: (loginId: string, keyId: string, signal?: AbortSignal) => Promise<void>
}

/** An action the panel offers on an SFTP login's row, confirmed by the operator before it is sent. */
export type SftpUserAction = 'resetPassword' | 'remove'

/** A login and the action the operator started on it; the two are only ever known together. */
export interface PendingSftpUserAction {
  /** The login the action is aimed at. */
  login: SftpUser
  /** Which action that login is waiting on. */
  action: SftpUserAction
}
