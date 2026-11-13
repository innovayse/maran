import { expect, test } from '@playwright/test'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteLocationNormalized } from 'vue-router'
import { useStackWizard } from '../../src/composables/useStackWizard'
import { createStackSetupGuard } from '../../src/router/stackSetupGuard'
import { useAuthStore } from '../../src/stores/auth'
import { useHostAppsStore } from '../../src/stores/hostApps'
import { useStackStore } from '../../src/stores/stack'
import { hostAppFamily } from '../../src/utils/hostAppFamily'
import { hostAppInUseSites } from '../../src/utils/hostAppInUseSites'
import { hostAppRunsOperation } from '../../src/utils/hostAppRunsOperation'
import { EMPTY_STACK, READY_STACK } from '../fixtures/stub-stack-routes'
import type { HostApp, StackStatus } from '../../src/types/hostApp'

// These run in the Playwright runner's Node process rather than in a browser, like the site-log
// store specs: the real stores, the real API composables and the real guard, with only `fetch`
// replaced. They pin the logic the browser specs only see through a screen: what the stack store
// holds and when it asks again, where the guard sends whom, how an in-use refusal becomes a list of
// sites. Every refusal is paired with the accepting case.

/** One request the replaced `fetch` saw. */
interface SeenRequest {
  /** The HTTP method. */
  method: string
  /** The path with its query. */
  path: string
}

/** What the replaced `fetch` answers a request with. */
interface Answer {
  /** The HTTP status. */
  status: number
  /** The JSON body, or `undefined` for none. */
  body?: unknown
  /** The content type; JSON by default. */
  contentType?: string
}

/**
 * Replaces the global `fetch` with a function answering from a handler and recording requests.
 * Assigned through Reflect, like the site-log specs, because the lint rules forbid assigning
 * `globalThis.fetch` outside `useApi.ts`.
 * @param handler Decides the answer for a request.
 * @returns The requests seen so far, oldest first.
 */
const replaceFetch = (handler: (request: SeenRequest) => Answer): SeenRequest[] => {
  const seen: SeenRequest[] = []
  const stub = ((input: string, init?: RequestInit): Promise<Response> => {
    const request = { method: init?.method ?? 'GET', path: input }
    seen.push(request)
    const answer = handler(request)
    return Promise.resolve(
      new Response(answer.body === undefined ? null : JSON.stringify(answer.body), {
        status: answer.status,
        headers: { 'Content-Type': answer.contentType ?? 'application/json' },
      }),
    )
  }) as typeof fetch
  Reflect.set(globalThis, 'fetch', stub)
  return seen
}

/**
 * Signs a user of a role in through the real auth store, with the login endpoint answered.
 * @param role The role to sign in as.
 * @param other The answers for every other request.
 * @returns The requests seen so far.
 */
const signInAs = async (
  role: 'admin' | 'customer',
  other: (request: SeenRequest) => Answer,
): Promise<SeenRequest[]> => {
  const seen = replaceFetch((request) => {
    if (request.path.includes('/api/v1/auth/login')) {
      return {
        status: 200,
        body: {
          session: {
            accessToken: 'stub.access.token',
            expiresAt: '2099-01-01T00:00:00+00:00',
            user: { id: '1', username: 'u', email: 'u@example.com', role, accountId: null },
            requiresTwoFactorSetup: false,
            requiresPasswordChange: false,
          },
        },
      }
    }
    return other(request)
  })
  const signedIn = await useAuthStore().login({ username: 'u', password: 'p' })
  expect(signedIn).toBe(true)
  return seen
}

/**
 * Runs the stack guard for a target route, the way the router does.
 * @param name The route name navigated to.
 * @returns What the guard decided: `true`, or the redirect.
 */
const decide = async (name: string): Promise<unknown> => {
  const guard = createStackSetupGuard()
  const to = { name, fullPath: `/${name}`, meta: {} } as unknown as RouteLocationNormalized
  return guard(to)
}

/**
 * Answers the stack read with a status, and nothing else.
 * @param status The status the host reports.
 * @returns The handler.
 */
const stackAnswer = (status: StackStatus) => {
  return (request: SeenRequest): Answer => {
    return request.path.endsWith('/api/v1/apps/stack') ? { status: 200, body: status } : { status: 404 }
  }
}

test.beforeEach(() => {
  setActivePinia(createPinia())
})

test('the stack store holds a ready answer and does not ask again, unless forced', async () => {
  const seen = replaceFetch(stackAnswer(READY_STACK))
  const store = useStackStore()
  expect(store.isReady).toBeNull()

  await store.load()
  await store.load()
  expect(store.isReady).toBe(true)
  expect(seen).toHaveLength(1)

  await store.load(true)
  expect(seen).toHaveLength(2)
})

test('the stack store asks again while the host is not ready, because that is what a visitor waits for', async () => {
  const seen = replaceFetch(stackAnswer(EMPTY_STACK))
  const store = useStackStore()

  await store.load()
  await store.load()

  expect(store.isReady).toBe(false)
  expect(seen).toHaveLength(2)
})

test('the stack store keeps "not known" apart from "not ready" when the read fails', async () => {
  replaceFetch(() => {
    return { status: 503, body: { code: 'ServiceUnavailable', detail: 'down' }, contentType: 'application/problem+json' }
  })
  const store = useStackStore()

  await store.load()

  expect(store.isReady).toBeNull()
  expect(store.phpVersions).toBeNull()
})

test('the guard sends an administrator of a host without a web server to the wizard, from any route', async () => {
  await signInAs('admin', stackAnswer(EMPTY_STACK))

  expect(await decide('sites')).toEqual({ name: 'stack-setup', replace: true })
  expect(await decide('stack-pending')).toEqual({ name: 'stack-setup', replace: true })
  // The accepting case: the wizard itself is not redirected.
  expect(await decide('stack-setup')).toBe(true)
})

test('the guard sends everyone else to the "being set up" screen', async () => {
  await signInAs('customer', stackAnswer(EMPTY_STACK))

  expect(await decide('sites')).toEqual({ name: 'stack-pending', replace: true })
  expect(await decide('stack-setup')).toEqual({ name: 'stack-pending', replace: true })
  expect(await decide('stack-pending')).toBe(true)
})

test('the guard lets a ready host through and keeps the wizard closed to everyone', async () => {
  await signInAs('admin', stackAnswer(READY_STACK))

  expect(await decide('sites')).toBe(true)
  expect(await decide('stack-setup')).toEqual({ name: 'system-status', replace: true })
  expect(await decide('stack-pending')).toEqual({ name: 'system-status', replace: true })
})

test('the guard lets a visit through when the stack cannot be read', async () => {
  await signInAs('admin', () => {
    return { status: 503, body: { code: 'ServiceUnavailable', detail: 'down' }, contentType: 'application/problem+json' }
  })

  expect(await decide('sites')).toBe(true)
})

test('the guard asks nothing of a visitor who is not signed in', async () => {
  const seen = replaceFetch(stackAnswer(EMPTY_STACK))

  expect(await decide('login')).toBe(true)
  expect(seen).toHaveLength(0)
})

test('an in-use refusal becomes a list of the sites that use the app', async () => {
  replaceFetch((request) => {
    if (request.path.endsWith('/uninstall')) {
      return {
        status: 409,
        contentType: 'application/problem+json',
        body: { code: 'AppsInUse', detail: 'In use.', sites: { total: 7, domains: ['a.example.com', 'b.example.com'] } },
      }
    }
    return { status: 404 }
  })
  const store = useHostAppsStore()

  const accepted = await store.uninstall('php-8.3')

  expect(accepted).toBe(false)
  expect(store.inUse).toEqual({
    appId: 'php-8.3',
    message: 'In use.',
    sites: ['a.example.com', 'b.example.com'],
    total: 7,
  })
  expect(store.actionErrorMessage).toBeNull()
})

test('any other refusal keeps the panel\'s sentence and no list of sites', async () => {
  replaceFetch(() => {
    return { status: 409, contentType: 'application/problem+json', body: { code: 'AppsNotInstalled', detail: 'Not installed.' } }
  })
  const store = useHostAppsStore()

  expect(await store.uninstall('php-8.3')).toBe(false)

  expect(store.inUse).toBeNull()
  expect(store.actionErrorMessage).toBe('Not installed.')
})

test('an accepted install is followed as a task until the panel reports it finished', async () => {
  const taskId = '55555555-5555-5555-5555-555555555555'
  const finished = { id: taskId, percent: 100, finishedAt: '2026-11-09T10:00:05Z', failure: null, revision: 2 }
  replaceFetch((request) => {
    if (request.path.endsWith('/install')) {
      return { status: 202, body: { taskId } }
    }
    return request.path.includes(`/api/v1/tasks/${taskId}`) ? { status: 200, body: finished } : { status: 404 }
  })
  const store = useHostAppsStore()

  expect(await store.install('nginx')).toBe(true)
  await expect.poll(() => {return store.operations['nginx']}).toBeUndefined()

  expect(store.operationTasks['nginx']?.percent).toBe(100)
  store.stopAll()
})

test('the app id decides the family, and a stranger is left out rather than guessed', () => {
  expect(hostAppFamily('nginx')).toBe('webServer')
  expect(hostAppFamily('apache')).toBe('webServer')
  expect(hostAppFamily('php-8.3')).toBe('php')
  expect(hostAppFamily('nodejs-22')).toBe('nodejs')
  expect(hostAppFamily('mysql')).toBeNull()
})

test('only text domains of the refusal\'s sites member are taken, and the total is the panel\'s', () => {
  expect(hostAppInUseSites({ sites: { total: 9, domains: ['a.example.com', 3, null, 'b.example.com'] } })).toEqual({
    sites: ['a.example.com', 'b.example.com'],
    total: 9,
  })
  // A member of any other shape names no site, and is not mistaken for one.
  expect(hostAppInUseSites({ sites: 'a.example.com' })).toEqual({ sites: [], total: 0 })
  expect(hostAppInUseSites({})).toEqual({ sites: [], total: 0 })
})

test('a list page that names a task in flight is followed again after a reload', async () => {
  const taskId = '66666666-6666-6666-6666-666666666666'
  replaceFetch((request) => {
    if (request.path.includes('/api/v1/apps?')) {
      return {
        status: 200,
        body: {
          items: [
            {
              appId: 'php-8.4',
              name: 'PHP 8.4',
              kind: 'runtime',
              kindName: 'Runtime',
              version: '8.4',
              state: 'installing',
              stateName: 'Installing',
              serviceStatus: 'unknown',
              serviceStatusName: 'Unknown',
              installedAt: null,
              taskId,
            },
          ],
          total: 1,
          page: 1,
          pageSize: 25,
        },
      }
    }
    return request.path.includes(`/api/v1/tasks/${taskId}`)
      ? { status: 200, body: { id: taskId, percent: 30, finishedAt: null, failure: null, revision: 1 } }
      : { status: 404 }
  })
  const store = useHostAppsStore()

  await store.list({ page: 1, pageSize: 25, search: '', sort: '', filters: {} })

  expect(store.operations['php-8.4']).toBe(taskId)
  store.stopAll()
})

/** The task a followed install in the specs below carries. */
const RUNNING_TASK_ID = '77777777-7777-7777-7777-777777777777'

/**
 * A row of the apps list, as the panel answers it.
 * @param appId The catalogue id.
 * @param state The ledger state.
 * @param taskId The task the row names, or `null`.
 * @returns The row.
 */
const listRow = (appId: string, state: HostApp['state'], taskId: string | null): HostApp => {
  return {
    appId,
    name: appId,
    kind: appId === 'nginx' ? 'webServer' : 'runtime',
    kindName: 'Kind',
    version: '1',
    state,
    stateName: state,
    serviceStatus: 'unknown',
    serviceStatusName: 'Unknown',
    installedAt: null,
    taskId,
  }
}

/**
 * Answers the apps list with rows, a followed task with a state, and the stack with a status.
 * @param rows The rows the list answers.
 * @param task The task read's answer.
 * @param stack The stack status.
 * @returns The handler.
 */
const appsAnswer = (rows: HostApp[], task: unknown, stack: StackStatus) => {
  return (request: SeenRequest): Answer => {
    if (request.path.endsWith('/api/v1/apps/stack')) {
      return { status: 200, body: stack }
    }
    if (request.path.includes('/api/v1/apps?')) {
      return { status: 200, body: { items: rows, total: rows.length, page: 1, pageSize: 25 } }
    }
    return request.path.includes('/api/v1/tasks/') ? { status: 200, body: task } : { status: 404 }
  }
}

test('only a row that is installing or being removed counts as running', () => {
  expect(hostAppRunsOperation(listRow('nginx', 'installing', RUNNING_TASK_ID))).toBe(true)
  expect(hostAppRunsOperation(listRow('nginx', 'removing', RUNNING_TASK_ID))).toBe(true)
  // A settled row is not running, whatever task id a stale answer still carries.
  expect(hostAppRunsOperation(listRow('nginx', 'installed', RUNNING_TASK_ID))).toBe(false)
  expect(hostAppRunsOperation(listRow('nginx', 'failed', RUNNING_TASK_ID))).toBe(false)
  // And a row that names no task has nothing to follow.
  expect(hostAppRunsOperation(listRow('nginx', 'installing', null))).toBe(false)
})

test('a list page with settled rows follows nothing', async () => {
  const seen = replaceFetch(
    appsAnswer(
      [listRow('nginx', 'installed', RUNNING_TASK_ID), listRow('php-8.3', 'failed', RUNNING_TASK_ID)],
      { id: RUNNING_TASK_ID, percent: 100, finishedAt: '2026-11-09T10:00:05Z', failure: null, revision: 2 },
      READY_STACK,
    ),
  )
  const store = useHostAppsStore()

  await store.list({ page: 1, pageSize: 25, search: '', sort: '', filters: {} })

  expect(store.operations).toEqual({})
  expect(
    seen.filter((request) => {
      return request.path.includes('/api/v1/tasks/')
    }),
  ).toHaveLength(0)
})

test('the wizard resumes an install the panel is running and ends with the stack ready', async () => {
  const seen = replaceFetch(
    appsAnswer(
      [listRow('nginx', 'installing', RUNNING_TASK_ID)],
      { id: RUNNING_TASK_ID, percent: 100, finishedAt: '2026-11-09T10:00:05Z', failure: null, revision: 2 },
      READY_STACK,
    ),
  )
  const wizard = useStackWizard()

  await wizard.resume()

  expect(wizard.step.value).toBe('install')
  expect(wizard.queue.value.map((item) => {return [item.id, item.state]})).toEqual([['nginx', 'done']])
  expect(wizard.webServerReady.value).toBe(true)
  expect(wizard.installing.value).toBe(false)
  // It watched the running install and started none of its own.
  expect(
    seen.filter((request) => {
      return request.method === 'POST'
    }),
  ).toHaveLength(0)
})

test('a resumed install the panel reports failed leaves the wizard open for a retry', async () => {
  replaceFetch(
    appsAnswer(
      [listRow('nginx', 'installing', RUNNING_TASK_ID)],
      {
        id: RUNNING_TASK_ID,
        percent: 60,
        finishedAt: '2026-11-09T10:00:05Z',
        failure: { code: 'AgentAppInstallFailed', message: 'No.' },
        revision: 2,
      },
      EMPTY_STACK,
    ),
  )
  const wizard = useStackWizard()

  await wizard.resume()

  expect(wizard.queue.value.map((item) => {return item.state})).toEqual(['failed'])
  expect(wizard.webServerReady.value).toBe(false)
})

test('the control: with no install running the wizard stays on the first step', async () => {
  replaceFetch(appsAnswer([listRow('nginx', 'failed', null)], {}, EMPTY_STACK))
  const wizard = useStackWizard()

  await wizard.resume()

  expect(wizard.step.value).toBe('webServer')
  expect(wizard.queue.value).toEqual([])
})

test('the web server choice ignores the agent\'s installed flag, and the runtime choice honours it', async () => {
  replaceFetch((request) => {
    if (request.path.endsWith('/api/v1/apps/installable')) {
      return {
        status: 200,
        body: {
          apps: [
            { appId: 'nginx', name: 'nginx', kind: 'webServer', kindName: 'Web server', installed: true },
            { appId: 'php-8.3', name: 'PHP 8.3', kind: 'runtime', kindName: 'Runtime', installed: true },
            { appId: 'php-8.4', name: 'PHP 8.4', kind: 'runtime', kindName: 'Runtime', installed: false },
          ],
          nodejsUnavailable: null,
          nodejsUnavailableName: null,
        },
      }
    }
    return { status: 404 }
  })
  const store = useHostAppsStore()
  const wizard = useStackWizard()

  await store.loadInstallable()

  expect(wizard.webServerChoices.value.map((app) => {return app.appId})).toEqual(['nginx'])
  expect(wizard.phpChoices.value.map((app) => {return app.appId})).toEqual(['php-8.4'])
})
