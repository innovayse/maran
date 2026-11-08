import { useApi } from '../useApi'
import { listQuery } from '../../utils/listQuery'
import type { ListRequest, Paged } from '../../types/list'
import type {
  CreateDatabaseUserRequest,
  CreatedDatabaseUser,
  DatabasePrivilegeCatalogue,
  DatabasePrivilegeKey,
  DatabaseUser,
  DatabaseUserDetail,
  DatabaseUserPassword,
  DatabaseUsersApi,
} from '../../types/databaseUser'

/** The endpoint database users are listed and created through. */
const DATABASE_USERS_PATH = '/api/v1/database-users'

/** The list endpoint's parameter for each filter the users page reads from its URL. */
const LIST_FILTER_PARAMS: Readonly<Record<string, string>> = {
  account: 'accountId',
}

/**
 * Builds the database-users API on top of the shared low-level client.
 *
 * Two calls answer with a password, and both are named for it — `create` and `resetPassword`.
 * Nothing else in this file returns a credential and nothing else may be made to.
 * @returns The {@link DatabaseUsersApi} bound to the panel's database-user endpoints.
 */
export const useDatabaseUsersApi = (): DatabaseUsersApi => {
  const api = useApi()

  /**
   * Lists one page of the users the caller may see. Another customer's rows are not in the answer.
   * @param request The page, size, search, sort and the account filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The page the panel answered.
   */
  const list = (request: ListRequest, signal?: AbortSignal): Promise<Paged<DatabaseUser>> => {
    return api.get<Paged<DatabaseUser>>(`${DATABASE_USERS_PATH}${listQuery(request, LIST_FILTER_PARAMS)}`, signal)
  }

  /**
   * Reads one user with the databases a grant may still be added on.
   * @param id The user to read.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The user and its grantable databases.
   */
  const get = (id: string, signal?: AbortSignal): Promise<DatabaseUserDetail> => {
    return api.get<DatabaseUserDetail>(`${DATABASE_USERS_PATH}/${id}`, signal)
  }

  /**
   * Reads the grantable privileges and the presets over them.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The catalogue.
   */
  const privilegeCatalogue = (signal?: AbortSignal): Promise<DatabasePrivilegeCatalogue> => {
    return api.get<DatabasePrivilegeCatalogue>(`${DATABASE_USERS_PATH}/privilege-presets`, signal)
  }

  /**
   * Creates a user with no privileges.
   * @param request The owning account and the name chosen.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The user as created, including the password shown once.
   */
  const create = (request: CreateDatabaseUserRequest, signal?: AbortSignal): Promise<CreatedDatabaseUser> => {
    return api.post<CreatedDatabaseUser>(DATABASE_USERS_PATH, request, signal)
  }

  /**
   * Gives a user a new password.
   * @param id The user to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  const resetPassword = (id: string, signal?: AbortSignal): Promise<DatabaseUserPassword> => {
    return api.post<DatabaseUserPassword>(`${DATABASE_USERS_PATH}/${id}/password`, undefined, signal)
  }

  /**
   * Replaces the user's privileges on one database; an empty set removes the grant.
   * @param id The user.
   * @param databaseId The database the privileges are on.
   * @param privileges The complete set the user should hold there.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel applied the change.
   */
  const setPrivileges = (
    id: string,
    databaseId: string,
    privileges: DatabasePrivilegeKey[],
    signal?: AbortSignal,
  ): Promise<void> => {
    return api.put<void>(`${DATABASE_USERS_PATH}/${id}/grants/${databaseId}`, { privileges }, signal)
  }

  /**
   * Drops a user.
   * @param id The user to drop.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Resolves once the panel dropped the user.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<void> => {
    return api.delete<void>(`${DATABASE_USERS_PATH}/${id}`, signal)
  }

  return { list, get, privilegeCatalogue, create, resetPassword, setPrivileges, remove }
}
