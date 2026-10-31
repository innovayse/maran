import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { PanelTask, TaskKindChoice } from '../../src/types/panelTask'

/** The paged list endpoint the tasks page reads. */
const TASKS_PATTERN = /\/api\/v1\/tasks(\?.*)?$/

/** The shell's one running-tasks stream, which the header's badge counts on every screen. */
export const RUNNING_STREAM_PATTERN = /\/api\/v1\/tasks\/running\/stream(\?.*)?$/

/** The kinds endpoint the kind filter reads. */
const KINDS_PATTERN = /\/api\/v1\/tasks\/kinds(\?.*)?$/

/** The failed-count endpoint the attention notice reads. */
const FAILED_COUNT_PATTERN = /\/api\/v1\/tasks\/failed-count(\?.*)?$/

/** The per-task SSE endpoint, which the live pane opens. `running/stream` is the tray's, not a task's. */
export const TASK_STREAM_PATTERN = /\/api\/v1\/tasks\/(?!running\/)[^/?]+\/stream(\?.*)?$/

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
 * `from` and `to` (the stub holds no accounts, so `accountId` narrows nothing); `/running/stream`
 * with a frame per running task and their set (the body ends, so the shell reconnects after its
 * wait and is answered the same again); `/kinds` with the distinct kinds, named by their machine names; and `/failed-count`.
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
  const running = tasks.filter((task) => {
    return task.status === 'running'
  })
  await page.route(RUNNING_STREAM_PATTERN, async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body: runningStreamBody(running) })
  })
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
 * Builds the `running` frame of the running-tasks stream: the ids of every running task.
 * @param ids The running task ids.
 * @returns The frame, terminated by the blank line SSE requires.
 */
export const runningFrame = (ids: readonly string[]): string => {
  return `event: running\ndata: ${JSON.stringify({ ids })}\n\n`
}

/**
 * Builds the body the running-tasks stream opens with: a `task` frame per running task, then their
 * set — what `RunningTasksStreamService` sends first.
 * @param running The running tasks.
 * @returns The raw `text/event-stream` body.
 */
export const runningStreamBody = (running: readonly PanelTask[]): string => {
  return (
    ': open\n\n' +
    running.map(taskFrame).join('') +
    runningFrame(
      running.map((task) => {
        return task.id
      }),
    )
  )
}

/**
 * Answers the running-tasks stream with `first` at once, then holds every later connection — the
 * shell's reconnect after `first` ended — until `release` is called, and answers those with `then`.
 *
 * A stubbed stream cannot stay open and speak again later, so a change on the tray is staged as two
 * connections. Holding the second puts the two states either side of a line the spec draws: answered
 * at once, the change could land before the first state was ever observed, and an assertion about
 * the change would pass without it having happened.
 * @param page The Playwright page whose network the route is installed on.
 * @param first The raw body of the first connection.
 * @param then The raw body of every later connection, once released.
 * @returns A `release` function that lets the later connections go.
 */
export const stubRunningStreamSequence = async (
  page: Page,
  first: string,
  then: string,
): Promise<{ release: () => void }> => {
  let release = (): void => {}
  const held = new Promise<void>((resolve) => {
    release = resolve
  })
  let served = 0

  await page.route(RUNNING_STREAM_PATTERN, async (route: Route) => {
    served += 1
    const isFirst = served === 1
    if (!isFirst) {
      await held
    }
    await route.fulfill({ status: 200, contentType: 'text/event-stream', body: isFirst ? first : then })
  })

  return {
    release: (): void => {
      release()
    },
  }
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
 * Fulfils every tasks endpoint as empty — the default every authenticated spec gets.
 *
 * The shell's tasks badge opens the running-tasks stream on every screen, so without this the suite
 * fires an unstubbed request per authenticated navigation: measured at 197 of the 200 `ECONNREFUSED`
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
