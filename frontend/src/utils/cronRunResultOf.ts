import type { CronRunResult } from '../components/cron/CronRunResultDialog.vue'
import type { CronRun } from '../types/cronRun'

/**
 * Names which of the four things a run dialog can be showing the cron run store's fields amount to.
 *
 * The store keeps the run and the failure as two nullable values, and the empty string stands in
 * `errorMessage` for "no answer arrived"; this is the one place that reading is done, so the two
 * pages that open the dialog cannot disagree about it.
 *
 * @param run The run the store holds, or `null` while it is still running, being read, or failed.
 * @param errorMessage The panel's refusal, `''` when no answer arrived, or `null` when nothing failed.
 * @returns The state the dialog should draw.
 */
export const cronRunResultOf = (run: CronRun | null, errorMessage: string | null): CronRunResult => {
  if (errorMessage !== null) {
    return errorMessage === '' ? { state: 'unreachable' } : { state: 'refused', message: errorMessage }
  }
  return run === null ? { state: 'running' } : { state: 'done', run }
}
