/**
 * Outward view of one customer MySQL database, mirroring the backend's `DatabaseDto`
 * field-for-field.
 *
 * There is no password here, and there is nothing for one to come from: no column on the server
 * holds it. A database password exists in exactly two responses — the one that created the
 * database and the one that reset it — and never again.
 */
export interface Database {
  /** The database's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns this database. */
  accountId: string
  /** The name the customer asked for, WITHOUT the account prefix. */
  name: string
  /**
   * The fully-qualified name MySQL actually holds — `<account>_<name>`.
   *
   * This is what a screen shows and what a connection string needs. An operator who reads the
   * unprefixed {@link name} and types it into a mysql client gets "unknown database".
   */
  fullName: string
  /** The fully-qualified name of the dedicated MySQL user, likewise `<account>_<name>`. */
  dbUserName: string
  /** The instant the database was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * Request body for `POST /api/v1/databases`, mirroring the backend's `CreateDatabaseRequest`
 * field-for-field.
 *
 * It has no password field, and none may be added: the panel mints the credential, so a
 * customer-chosen value would be one more secret travelling inbound for no gain.
 */
export interface CreateDatabaseRequest {
  /** The account that will own the database. */
  accountId: string
  /** The database name, without the account prefix; lowercase letters and digits only. */
  name: string
  /** The dedicated user's name, without the account prefix; likewise. */
  dbUserName: string
}

/**
 * What `POST /api/v1/databases` answered, mirroring the backend's `CreatedDatabaseDto` — the one
 * and only response that carries a new database's password.
 */
export interface CreatedDatabase {
  /** The new database's identity. */
  id: string
  /** The account that owns it. */
  accountId: string
  /** The name the customer asked for, without the account prefix. */
  name: string
  /** The fully-qualified name MySQL holds. */
  fullName: string
  /** The fully-qualified dedicated user name. */
  dbUserName: string
  /**
   * The generated password. Nothing keeps a copy — not this SPA, not the panel, not the agent —
   * so it is shown once and recovered only by setting a new one.
   */
  password: string
  /** The instant the database was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What `POST /api/v1/databases/{id}/password` answered, mirroring the backend's
 * `DatabasePasswordDto`. The only recovery path a lost password has.
 */
export interface DatabasePassword {
  /** The database whose user was re-credentialled. */
  id: string
  /** The fully-qualified MySQL user the new password belongs to. */
  dbUserName: string
  /** The new password, shown once and stored nowhere. */
  password: string
}

/**
 * A database credential the panel is showing for the only time it ever will.
 *
 * The two responses above are folded into this one shape by the store, because the screen's
 * problem is the same for both: put the value in front of the operator, let them copy it, and say
 * plainly that closing the dialog ends the only chance they have. It is held in memory only — no
 * storage, no query string, no history entry — so a reload loses it exactly as the server did.
 */
export interface RevealedDatabaseCredential {
  /**
   * The fully-qualified database the login opens, or `null` when a reset revealed a login whose
   * database is not in the held list. Never guessed from the account and the name: the prefixed
   * form is the server's answer, not this SPA's.
   */
  databaseFullName: string | null
  /** The fully-qualified MySQL user the password belongs to. */
  dbUserName: string
  /** The generated password, exactly as the panel sent it. */
  password: string
}

/**
 * Typed access to the databases endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface DatabasesApi {
  /**
   * Lists the databases the caller may see.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The databases, in the order the panel reports them.
   */
  list: (signal?: AbortSignal) => Promise<Database[]>

  /**
   * Creates a database and its dedicated user.
   * @param request The owning account and the two names the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The database as created, including the password shown once.
   */
  create: (request: CreateDatabaseRequest, signal?: AbortSignal) => Promise<CreatedDatabase>

  /**
   * Gives the database's user a new password. Another customer's database answers 404, not 403.
   * @param id The database whose user to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  resetPassword: (id: string, signal?: AbortSignal) => Promise<DatabasePassword>

  /**
   * Drops the database and its dedicated user. The customer's data goes with it.
   * @param id The database to drop.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel dropped the database.
   */
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>
}
