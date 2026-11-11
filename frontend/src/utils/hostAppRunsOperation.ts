import type { HostApp } from '../types/hostApp'

/**
 * Tells whether an app's row names an install or removal that is still running. The panel clears a
 * row's task id when the operation settles, and the state is checked as well, so a row that is
 * installed or failed is never followed, whatever task id a stale answer still carries.
 * @param app The row the panel listed.
 * @returns `true` when the row is installing or being removed and names its task (so `taskId` is a string).
 */
export const hostAppRunsOperation = (app: HostApp): app is HostApp & { taskId: string } => {
  return (app.state === 'installing' || app.state === 'removing') && app.taskId !== null
}
