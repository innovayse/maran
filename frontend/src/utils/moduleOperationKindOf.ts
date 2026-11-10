import type { ModuleOperationName } from '../types/marketplace'

/** The operation each of the panel's marketplace task kinds carries. */
const OPERATIONS: Readonly<Record<string, ModuleOperationName>> = {
  ModuleInstall: 'install',
  ModuleUpdate: 'update',
  ModuleRemove: 'remove',
  ModuleRollback: 'rollback',
}

/**
 * Names the operation a task of the panel carries, so a failed one can be offered again.
 * @param taskKind The task's kind as the panel names it.
 * @returns The operation, or `undefined` for a kind that is not a marketplace operation.
 */
export const moduleOperationKindOf = (taskKind: string): ModuleOperationName | undefined => {
  return OPERATIONS[taskKind]
}
