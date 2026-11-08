import type { ListRequest, Paged } from './list'

/**
 * One of the closed set of privileges a database user may hold on a single database, as the panel
 * names it on the wire: the camelCase member name of the backend's `DatabasePrivilege` enum.
 *
 * The union is the request contract and nothing more, so `maran api` can tell a renamed privilege
 * from a typo. It carries no label and no order, and an editor does not enumerate it: the set it
 * offers, its order and each name as a person reads it arrive from
 * `GET /api/v1/database-users/privilege-presets` as a {@link DatabasePrivilegeCatalogue}
 * (rules/vue.md "Data comes from the backend"), so a privilege the panel adds appears there without
 * a release of the SPA and is added to this union in the same change.
 */
export type DatabasePrivilegeKey =
  | 'alter'
  | 'alterRoutine'
  | 'create'
  | 'createRoutine'
  | 'createTemporaryTables'
  | 'createView'
  | 'delete'
  | 'drop'
  | 'event'
  | 'execute'
  | 'index'
  | 'insert'
  | 'lockTables'
  | 'references'
  | 'select'
  | 'showView'
  | 'trigger'
  | 'update'

/**
 * What one user may do on one database, mirroring the backend's `DatabaseUserGrantDto`. A grant is
 * always scoped to a single database (`ON db.*`): there is no global scope to represent.
 */
export interface DatabaseUserGrant {
  /** The database the grant is on. */
  databaseId: string
  /** The database's fully-qualified name, as MySQL holds it. */
  databaseFullName: string
  /** The privileges held on it, in the panel's order; never empty (an empty set removes the grant). */
  privileges: DatabasePrivilegeKey[]
}

/**
 * One local MySQL user of an account, mirroring the backend's `DatabaseUserDto` field-for-field.
 *
 * There is no password and no host list: a user is reachable from the server itself only, and the
 * password exists in exactly two responses (the one that created the user and the one that reset it).
 */
export interface DatabaseUser {
  /** The user's identity, and the only identifier a request may name. */
  id: string
  /** The account that owns the user. */
  accountId: string
  /** The owning account's current short name; empty when the panel does not name it for this caller. */
  ownerName: string
  /** The name chosen at creation, WITHOUT the account prefix. */
  nameSuffix: string
  /** The fully-qualified name MySQL holds, `<account>_<name>`; the login a client types. */
  fullName: string
  /**
   * Whether this is a database's dedicated user, created with that database and dropped with it.
   * Such a user can have its privileges edited and its password reset but cannot be dropped alone;
   * the backend refuses that with a 409.
   */
  isDedicated: boolean
  /** The grants the user holds, one per database it has any privilege on. */
  grants: DatabaseUserGrant[]
  /** The instant the user was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * A database of the owning account that the user holds no grant on yet, offered by the privileges
 * editor as a database to grant on.
 */
export interface GrantableDatabase {
  /** The database's identity. */
  id: string
  /** The database's fully-qualified name. */
  fullName: string
}

/**
 * A user with the databases the privileges editor may still add a grant on, mirroring the backend's
 * `GET /api/v1/database-users/{id}` answer: the {@link DatabaseUser} fields plus the choices.
 */
export interface DatabaseUserDetail extends DatabaseUser {
  /** The owning account's databases on which the user holds no grant. */
  grantableDatabases: GrantableDatabase[]
}

/**
 * Request body for `POST /api/v1/database-users`. It has no password field: the panel mints the
 * credential, and none may be added.
 */
export interface CreateDatabaseUserRequest {
  /** The account that will own the user. */
  accountId: string
  /** The user's name, without the account prefix; lowercase letters and digits only. */
  nameSuffix: string
}

/**
 * What `POST /api/v1/database-users` answered, mirroring the backend's `CreatedDatabaseUserDto`:
 * the one response that carries a new user's password.
 */
export interface CreatedDatabaseUser {
  /** The new user's identity. */
  id: string
  /** The account that owns it. */
  accountId: string
  /** The name chosen, without the account prefix. */
  nameSuffix: string
  /** The fully-qualified user name. */
  fullName: string
  /** The generated password, shown once and stored nowhere. */
  password: string
  /** The instant the user was created, as an ISO-8601 string. */
  createdAt: string
}

/**
 * What `POST /api/v1/database-users/{id}/password` answered, mirroring the backend's
 * `DatabaseUserPasswordDto`: the only recovery a lost password has.
 */
export interface DatabaseUserPassword {
  /** The user whose password was replaced. */
  id: string
  /** The fully-qualified user name the new password belongs to. */
  fullName: string
  /** The new password, shown once and stored nowhere. */
  password: string
}

/**
 * One privilege the editor offers, mirroring the backend's `DatabasePrivilegeDto`: the wire key and
 * the name the operator reads, already localized by the panel.
 */
export interface DatabasePrivilegeChoice {
  /** The privilege's wire key; what a grant sends back. */
  key: DatabasePrivilegeKey
  /** The privilege as the operator reads it (for example `SELECT`), localized by the panel. */
  name: string
}

/**
 * One quick fill the editor offers, mirroring the backend's `DatabasePrivilegePresetDto`. Selecting
 * a preset only ticks its privileges in the editor; saving sends the individual privileges like any
 * hand-ticked set, so the backend validates both identically.
 */
export interface DatabasePrivilegePreset {
  /** The preset's identity. */
  key: string
  /** The preset as the operator reads it, localized by the panel. */
  name: string
  /** The privileges the preset ticks. */
  privileges: DatabasePrivilegeKey[]
}

/**
 * What `GET /api/v1/database-users/privilege-presets` answered: the closed privilege set and the
 * presets over it. Both are the panel's; the SPA invents neither a privilege nor a preset.
 */
export interface DatabasePrivilegeCatalogue {
  /** Every privilege that can be granted, in the order the editor lists them. */
  privileges: DatabasePrivilegeChoice[]
  /** The quick fills over those privileges. */
  presets: DatabasePrivilegePreset[]
}

/** An action the panel offers on a user's row that waits for the operator's confirmation. */
export type DatabaseUserAction = 'resetPassword' | 'drop'

/** A user and the action started on it; the two are only ever known together. */
export interface PendingDatabaseUserAction {
  /** The user the action is aimed at. */
  user: DatabaseUser
  /** Which action that user is waiting on. */
  action: DatabaseUserAction
}

/**
 * Typed access to the database-users endpoints.
 *
 * Called from Pinia stores only — never from a component (rules/vue.md).
 */
export interface DatabaseUsersApi {
  /**
   * Lists one page of the users the caller may see.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  list: (request: ListRequest, signal?: AbortSignal) => Promise<Paged<DatabaseUser>>

  /**
   * Reads one user with the databases a grant may still be added on.
   * @param id The user to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The user and its grantable databases.
   */
  get: (id: string, signal?: AbortSignal) => Promise<DatabaseUserDetail>

  /**
   * Reads the privileges that can be granted and the presets over them.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The catalogue.
   */
  privilegeCatalogue: (signal?: AbortSignal) => Promise<DatabasePrivilegeCatalogue>

  /**
   * Creates a user with no privileges.
   * @param request The owning account and the name chosen.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The user as created, including the password shown once.
   */
  create: (request: CreateDatabaseUserRequest, signal?: AbortSignal) => Promise<CreatedDatabaseUser>

  /**
   * Gives a user a new password. Another customer's user answers 404, never 403.
   * @param id The user to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  resetPassword: (id: string, signal?: AbortSignal) => Promise<DatabaseUserPassword>

  /**
   * Replaces the user's privileges on one database; an empty set removes the grant.
   * @param id The user.
   * @param databaseId The database the privileges are on.
   * @param privileges The complete set the user should hold there.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel applied the change.
   */
  setPrivileges: (
    id: string,
    databaseId: string,
    privileges: DatabasePrivilegeKey[],
    signal?: AbortSignal,
  ) => Promise<void>

  /**
   * Drops a user. A database's dedicated user is refused with a 409.
   * @param id The user to drop.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel dropped the user (an empty 204 answer).
   */
  remove: (id: string, signal?: AbortSignal) => Promise<void>
}
