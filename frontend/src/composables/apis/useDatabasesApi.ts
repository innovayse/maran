import { useApi } from '../useApi'
import type {
  CreateDatabaseRequest,
  CreatedDatabase,
  Database,
  DatabasePassword,
  DatabasesApi,
} from '../../types/database'

/** The endpoint databases are listed and created through. */
const DATABASES_PATH = '/api/v1/databases'

/**
 * Builds the databases API on top of the shared low-level client.
 *
 * Each call is a named `const` arrow function with its own JSDoc rather than an anonymous entry in
 * the returned object: the name is what appears in a stack trace, and the doc block sits next to
 * the call it describes (rules/vue.md).
 *
 * Two of the four calls answer with a password, and both are named for it — `create` and
 * `resetPassword`. Nothing else in this file returns a credential, and nothing else may be made to:
 * the value exists for one response and is never stored anywhere it could be read back.
 * @returns The {@link DatabasesApi} bound to the panel's database endpoints.
 */
export const useDatabasesApi = (): DatabasesApi => {
  const api = useApi()

  /**
   * Lists the databases the caller may see. Another customer's rows are not in the answer at all —
   * the server scopes the query, so there is nothing here to filter.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The databases, in the order the panel reports them.
   */
  const list = (signal?: AbortSignal): Promise<Database[]> => {
    return api.get<Database[]>(DATABASES_PATH, signal)
  }

  /**
   * Creates a database and its dedicated user.
   * @param request The owning account and the two names the customer chose.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The database as created, including the password shown once.
   */
  const create = (request: CreateDatabaseRequest, signal?: AbortSignal): Promise<CreatedDatabase> => {
    return api.post<CreatedDatabase>(DATABASES_PATH, request, signal)
  }

  /**
   * Gives the database's user a new password. A database belonging to somebody else answers 404,
   * never 403.
   * @param id The database whose user to re-credential.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The login and its new password, shown once.
   */
  const resetPassword = (id: string, signal?: AbortSignal): Promise<DatabasePassword> => {
    return api.post<DatabasePassword>(`${DATABASES_PATH}/${id}/password`, undefined, signal)
  }

  /**
   * Drops the database and its dedicated user.
   * @param id The database to drop.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns Whether the panel dropped the database.
   */
  const remove = (id: string, signal?: AbortSignal): Promise<boolean> => {
    return api.delete<boolean>(`${DATABASES_PATH}/${id}`, signal)
  }

  return { list, create, resetPassword, remove }
}
