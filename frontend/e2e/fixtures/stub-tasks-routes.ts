import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { PanelTask, TaskKindChoice } from '../../src/types/panelTask'

/** The paged list endpoint the tasks page reads. */
const TASKS_PATTERN = /\/api\/v1\/tasks(\?.*)?$/

/** The running-tasks endpoint the shell header's badge reads on every screen. */
const RUNNING_PATTERN = /\/api\/v1\/tasks\/running(\?.*)?$/

/** The kinds endpoint the kind filter reads. */
const KINDS_PATTERN = /\/api\/v1\/tasks\/kinds(\?.*)?$/

/** The failed-count endpoint the attention notice reads. */
const FAILED_COUNT_PATTERN = /\/api\/v1\/tasks\/failed-count(\?.*)?$/

/** The per-task SSE endpoint. */
const TASK_STREAM_PATTERN = /\/api\/v1\/tasks\/[^/?]+\/stream(\?.*)?$/

/** How the tasks endpoint searches (the subject), sorts and filters, as the panel does. */
const TASK_RULES: PagedRules<PanelTask> = {
  matches: (task, term) => {
    return task.subject.toLowerCase().includes(term)
  },
  sorts: {
    startedAt: (task) => {
      return task.startedAt
    },
    subject: (task) => {
      return task.subject
    },
  },
  filters: {
    status: (task, value) => {
      return task.status === value
    },
    kind: (task, value) => {
      return task.kind === value
    },
    from: (task, value) => {
      return Date.parse(task.startedAt) >= Date.parse(value)
    },
    to: (task, value) => {
      return Date.parse(task.startedAt) <= Date.parse(value)
    },
  },
}

/**
 * Fulfils one JSON route with a fixed body.
 * @param page The Playwright page whose network the route is installed on.
 * @param pattern The route's URL pattern.
 * @param body The value to serialize.
 * @returns Resolves once the route is installed.
 */
const fulfilJson = async (page: Page, pattern: RegExp, body: unknown): Promise<void> => {
  await page.route(pattern, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

/**
 * Fulfils every tasks endpoint from one set of tasks, as the panel would answer it: `GET
 * /api/v1/tasks` as a page honouring `page`, `pageSize`, `search`, `sort`, `status`, `kind`,
 * `from` and `to` (the stub holds no accounts, so `accountId` narrows nothing); `/running` with the
 * running ones; `/kinds` with the distinct kinds, named by their machine names; and `/failed-count`.
 * @param page The Playwright page whose network the routes are installed on.
 * @param tasks The tasks the stub knows, newest first — the panel's default order.
 * @returns A function reporting every list request so far, oldest first.
 */
export const stubTasks = async (page: Page, tasks: PanelTask[]): Promise<() => readonly URL[]> => {
  const listRequests: URL[] = []
  await page.route(TASKS_PATTERN, async (route: Route) => {
    listRequests.push(new URL(route.request().url()))
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(pagedAnswer(tasks, route.request().url(), TASK_RULES)),
    })
  })
  await fulfilJson(
    page,
    RUNNING_PATTERN,
    tasks.filter((task) => {
      return task.status === 'running'
    }),
  )
  const kinds: TaskKindChoice[] = [...new Set(tasks.map((task) => {
    return task.kind
  }))].map((kind) => {
    return { kind, kindName: kind }
  })
  await fulfilJson(page, KINDS_PATTERN, kinds)
  await fulfilJson(
    page,
    FAILED_COUNT_PATTERN,
    tasks.filter((task) => {
      return task.status === 'failed'
    }).length,
  )
  return () => {
    return listRequests
  }
}

/**
 * Fulfils `GET /api/v1/tasks` with an RFC 7807 refusal, so a spec can assert the page renders the
 * panel's own message verbatim rather than an empty list. The other tasks endpoints answer empty.
 * @param page The Playwright page whose network the route is installed on.
 * @param detail The backend-localized message the stub reports in `detail`.
 * @returns Resolves once the routes are installed.
 */
export const stubTasksNotFound = async (page: Page, detail: string): Promise<void> => {
  await stubTasks(page, [])
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

/**
 * Fulfils every tasks endpoint as empty — the default every authenticated spec gets.
 *
 * The shell's tasks badge reads the running tasks once on every screen, so without this the suite fires
 * one unstubbed request per authenticated navigation: measured at 197 of the 200 `ECONNREFUSED`
 * lines in a full local run. Those cost real latency on every navigation, and a page whose network
 * is only half stubbed is a page under test conditions nobody wrote down.
 *
 * It is installed by `stubSignedIn`, which runs BEFORE any stub a spec installs itself, and
 * Playwright matches routes newest-first — so a spec that stubs the listing (or answers 404 for a
 * caller the surface does not exist for) still wins, and this default only covers the screens that
 * never mentioned tasks at all.
 * @param page The Playwright page whose network the route is installed on.
 * @returns Resolves once the route is installed.
 */
export const stubEmptyTasks = async (page: Page): Promise<void> => {
  await stubTasks(page, [])
}
