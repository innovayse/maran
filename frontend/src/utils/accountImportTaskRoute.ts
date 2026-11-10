import type { RouteLocationRaw } from 'vue-router'

/** The kind of the background task an import job records; the tasks list's `kind` filter takes it as written. */
const ACCOUNT_IMPORT_TASK_KIND = 'AccountImport'

/**
 * The place an import's background task is read: the tasks list narrowed to the import kind and to
 * the account's name, which is the task's subject.
 * @param accountName The imported account's system user name.
 * @returns The route to the narrowed tasks list.
 */
export const accountImportTaskRoute = (accountName: string): RouteLocationRaw => {
  return { name: 'tasks', query: { kind: ACCOUNT_IMPORT_TASK_KIND, search: accountName } }
}
