import type { Page, Route } from '@playwright/test'
import type { PanelTask } from '../../src/types/panelTask'

/** The listing endpoint the tasks page and the shell header's badge both read. */
const TASKS_PATTERN = /\/api\/v1\/tasks(\?.*)?$/

/** The per-task SSE endpoint. */
const TASK_STREAM_PATTERN = /\/api\/v1\/tasks\/[^/?]+\/stream(\?.*)?$/

/**
 * Fulfils `GET /api/v1/tasks` with the given listing.
 * @param page The Playwright page whose network the route is installed on.
 * @param tasks The tasks the stub reports, newest first.
 * @returns Resolves once the route is installed.
 */
export const stubTasks = async (page: Page, tasks: PanelTask[]): Promise<void> => {
  await page.route(TASKS_PATTERN, async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(tasks),
    })
  })
}

/**
 * Fulfils `GET /api/v1/tasks` the way the module answers a caller the surface does not exist for.
 *
 * **404, not an empty 200**, and that is the module's own decision rather than this stub's:
 * `ListTasksQueryHandler` answers `TaskNotFound` to a non-administrator precisely so a customer is
 * not told there is an administrator-only feed they were refused. A stub serving `[]` here would
 * agree with a belief the SPA might hold instead of with the module.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the route is installed.
 */
export const stubTasksNotFound = async (page: Page, detail: string): Promise<void> => {
  await page.route(TASKS_PATTERN, async (route: Route) => {
    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ code: 'TaskNotFound', title: 'Not found', detail }),
    })
  })
}

/**
 * Fulfils one task's SSE endpoint with a fixed body.
 *
 * The whole body is delivered at once, which is all these specs need: what is under test is what
 * the panel DOES with a frame, not how the decoder handles a chunk boundary — that belongs to
 * `useApi`'s stream helper, which this feature reuses untouched (R9) and which the site-log store
 * specs already exercise across split chunks.
 * @param page The Playwright page whose network the route is installed on.
 * @param body The raw `text/event-stream` body to serve.
 * @returns Resolves once the route is installed.
 */
export const stubTaskStream = async (page: Page, body: string): Promise<void> => {
  await page.route(TASK_STREAM_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body })
  })
}

/**
 * Builds one `task` frame of a task stream's body.
 *
 * The payload is the whole `PanelTaskDto`, which is what `TaskStreamWriter` serializes — one shape
 * for the listing, the single read and every frame, so there is no second DTO to drift.
 * @param task The task the frame carries.
 * @returns The frame, terminated by the blank line SSE requires.
 */
export const taskFrame = (task: PanelTask): string => {
  return `event: task\ndata: ${JSON.stringify(task)}\n\n`
}

/**
 * Builds the terminal `end` frame of a task stream's body.
 * @param status The final status the module names.
 * @returns The frame, terminated by the blank line SSE requires.
 */
export const taskEndFrame = (status: string): string => {
  return `event: end\ndata: ${JSON.stringify({ status })}\n\n`
}

/**
 * Fulfils one task's SSE endpoint only when the spec says so.
 *
 * A route that answers immediately makes "the badge fell" untestable: the frame that finishes the
 * task can land before the badge has ever been observed running, and an assertion that the badge is
 * absent would then pass without the behaviour it claims to measure having happened at all. Holding
 * the response until `release` is called puts the two states either side of a line the spec draws.
 * @param page The Playwright page whose network the route is installed on.
 * @param body The raw `text/event-stream` body to serve once released.
 * @returns A `release` function that lets the response go.
 */
export const stubDeferredTaskStream = async (
  page: Page,
  body: string,
): Promise<{ release: () => void }> => {
  let release = (): void => {}
  const held = new Promise<void>((resolve) => {
    release = resolve
  })

  await page.route(TASK_STREAM_PATTERN, async (route: Route) => {
    await held
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body })
  })

  return {
    release: (): void => {
      release()
    },
  }
}
