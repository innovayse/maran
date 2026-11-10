// The English words the panel sends beside a migration state (`stateName`), as its display-name table words them.
// The stubs answer with the panel's shape, so a state a stub moves into carries the words the panel would send.

/** A job's state to its English name. */
const JOB_STATE_NAMES: Readonly<Record<string, string>> = {
  Draft: 'Draft',
  Running: 'Running',
  Paused: 'Paused',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
}

/** A job account's state to its English name. */
const ACCOUNT_STATE_NAMES: Readonly<Record<string, string>> = {
  Pending: 'Waiting',
  Running: 'Running',
  Synced: 'Synced, awaiting cut-over',
  CuttingOver: 'Cutting over',
  Completed: 'Completed',
  Paused: 'Paused',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
  RolledBack: 'Rolled back',
  RolledBackIncomplete: 'Rollback incomplete',
}

/** An import's state to its English name. */
const IMPORT_STATE_NAMES: Readonly<Record<string, string>> = {
  Staged: 'Waiting to start',
  Running: 'Running',
  Completed: 'Completed',
  RolledBack: 'Rolled back',
  RolledBackIncomplete: 'Rolled back incompletely',
  Discarded: 'Discarded',
}

/** A pull grant's state to its English name. */
const GRANT_STATE_NAMES: Readonly<Record<string, string>> = {
  Live: 'Live',
  Used: 'Used',
  Revoked: 'Revoked',
  Expired: 'Expired',
}

/**
 * The English name of a job's state.
 * @param state The state, as the panel spells it.
 * @returns The words the panel sends beside it.
 */
export const jobStateName = (state: string): string => {
  return JOB_STATE_NAMES[state] ?? state
}

/**
 * The English name of a job account's state.
 * @param state The state, as the panel spells it.
 * @returns The words the panel sends beside it.
 */
export const accountStateName = (state: string): string => {
  return ACCOUNT_STATE_NAMES[state] ?? state
}

/**
 * The English name of an import's state.
 * @param state The state, as the panel spells it.
 * @returns The words the panel sends beside it.
 */
export const importStateName = (state: string): string => {
  return IMPORT_STATE_NAMES[state] ?? state
}

/**
 * The English name of a pull grant's state.
 * @param state The state, as the panel spells it.
 * @returns The words the panel sends beside it.
 */
export const grantStateName = (state: string): string => {
  return GRANT_STATE_NAMES[state] ?? state
}
