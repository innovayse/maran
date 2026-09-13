/**
 * The customer FTPS login domain: the row, the request that creates one, and the two responses that
 * carry a password exactly once.
 *
 * It mirrors the backend's `Ftp` module DTOs field-for-field, the way `sftpUser.ts` mirrors the
 * `Sftp` module's. The two stay separate types for the same reason the two modules stay separate on
 * the server; what merges them is `types/fileTransferLogin.ts`, in the SPA, where the merge belongs.
 */

/**
 * Outward view of one customer FTPS login, mirroring the backend's `FtpUserDto`.
 *
 * There is no password here and there is nothing for one to come from: the `Ftp` module holds no
 * password column of any kind, plaintext or hashed. The value exists in exactly two responses — the
 * one that created the login and the one that reset it — and in the host's shadow file.
 */
export interface FtpUser {
  /** The login's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this login. */
  accountId: string
  /** The name the customer asked for, WITHOUT the account prefix. */
  name: string
  /**
   * The system login the host holds — `<account>_<name>`, as it appears in `/etc/passwd`.
   *
   * This is what the customer types into their FTPS client.
   */
  fullName: string
  /**
   * Which daemon the backend says accepts this login, as the server spells it.
   *
   * It is on the wire rather than assumed by the SPA because the merged screen renders a
   * backend-supplied fact, not one derived from which URL was called. The value is narrowed to
   * `types/fileTransferLogin.ts`'s union at the store boundary; a token this bundle does not know is
   * refused there rather than rendered.
   */
  protocol: string
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/ftp-users`, binding the backend's `CreateFtpUserCommand`.
 *
 * No password field and no chroot path: the panel mints the credential, and the jail is derived from
 * the account by the agent — so there is no directory for a request to name and none to be trusted
 * with. `ftp.proto` reserves the field name for exactly this reason.
 */
export interface CreateFtpUserRequest {
  /** The account that will own the login. */
  accountId: string
  /** The login name, without the account prefix; lowercase letters and digits only. */
  name: string
}

/**
 * What `POST /api/v1/ftp-users` answered, mirroring the backend's `CreatedFtpUserDto` — one of the
 * only two responses that ever carry an FTPS password.
 */
export interface CreatedFtpUser {
  /** The new login's identity. */
  id: string
  /** The account that owns it. */
  accountId: string
  /** The name the customer asked for, without the account prefix. */
  name: string
  /** The system login the host holds — what the customer signs in with. */
  fullName: string
  /** Which daemon accepts it, as the server spells it. */
  protocol: string
  /**
   * The generated password. Nothing keeps a copy — not this SPA, not the panel, not the agent — so
   * it is shown once and recovered only by setting a new one.
   */
  password: string
  /** The instant the login was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What `POST /api/v1/ftp-users/{id}/password` answered, mirroring `FtpUserPasswordDto`. The only
 * recovery path a lost password has.
 */
export interface FtpUserPassword {
  /** The login that was re-credentialled. */
  id: string
  /** The system login the new password belongs to. */
  fullName: string
  /** The new password, shown once and stored nowhere. */
  password: string
}

/**
 * Typed access to the FTPS users endpoints. Called from Pinia stores only (rules/vue.md).
 */
export interface FtpUsersApi {
  /**
   * Lists the FTPS logins the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The logins, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<FtpUser[]>

  /**
   * Creates an FTPS login.
   * @param request The owning account and the name the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login as created, including the password shown once.
   */
  create: (request: CreateFtpUserRequest, signal?: AbortSignal) => Promise<CreatedFtpUser>

  /**
   * Gives the login a new password. A login belonging to somebody else answers 404, never 403.
   * @param id The login to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  resetPassword: (id: string, signal?: AbortSignal) => Promise<FtpUserPassword>

  /**
   * Removes the login, and only the login: the account's files stay where they are.
   * @param id The login to remove.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel removed the login.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>
}

/**
 * An FTPS credential the panel is showing for the only time it ever will.
 *
 * It carries the login and the password and NOT the connection facts. The host name and the control
 * port live on `FtpsStatus`, which `GET /api/v1/ftps-server` serves to administrators only, so a
 * customer's dialog has no way to learn them — and the one thing it must never do is compose a host
 * name of its own from `window.location`, because the server's certificate is issued for the
 * panel's own hostname and any other name produces a mismatch warning. The dialog therefore renders
 * the facts when the status is readable and says plainly that it cannot when it is not.
 */
export interface RevealedFtpCredential {
  /** The system login the password belongs to, prefixed exactly as the host holds it. */
  fullName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
}
