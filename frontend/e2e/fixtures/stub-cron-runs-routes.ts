import type { Page, Route } from '@playwright/test'
import type { CronRun, CronRunSummary } from '../../src/types/cronRun'
import type { Paged } from '../../src/types/list'

/** A run by hand: `POST /api/v1/cron-entries/{id}/runs`, and the history list on the same path. */
const RUNS_PATTERN = /\/api\/v1\/cron-entries\/[^/?]+\/runs(\?.*)?$/

/** One run's read: `GET /api/v1/cron-entries/{id}/runs/{runId}`. */
const RUN_PATTERN = /\/api\/v1\/cron-entries\/[^/?]+\/runs\/[^/?]+(\?.*)?$/

/**
 * Answers a run by hand with `run`, the history with the runs whose outcome matches the `outcome`
 * parameter (all when it is absent), and a single read with `run`.
 * @param page The page to stub.
 * @param run The run every run-now and single read answers with.
 * @param history The rows the history holds.
 * @returns Resolves once the routes are installed.
 */
export const stubCronRuns = async (page: Page, run: CronRun, history: CronRunSummary[]): Promise<void> => {
  await page.route(RUNS_PATTERN, async (route: Route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({ status: 200, json: run })
      return
    }
    const outcome = new URL(route.request().url()).searchParams.get('outcome')
    const items = history.filter((row) => {
      return outcome === null || row.outcome === outcome
    })
    const body: Paged<CronRunSummary> = { items, total: items.length, page: 1, pageSize: 25 }
    await route.fulfill({ status: 200, json: body })
  })
  await page.route(RUN_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, json: run })
  })
}
