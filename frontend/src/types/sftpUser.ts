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
  /** The name the customer asked for, WITHOUT the account prefix. */
  name: string
  /**
   * The system login the host holds — `<account>_<name>`, as it appears in `/etc/passwd`.
   *
   * This is what the customer types into an SFTP client. Somebody who reads the unprefixed
   * {@link name} and types that instead simply cannot log in.
   */
  fullName: string
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/sftp-users`, mirroring the backend's `CreateSftpUserRequest`
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
 * Typed access to the SFTP users endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface SftpApi {
  /**
   * Lists the SFTP logins the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The logins, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<SftpUser[]>

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
   * @returns Whether the panel removed the login.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>
}
