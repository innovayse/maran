import type { Page, Route } from '@playwright/test'
import { pagedAnswer, type PagedRules } from './paged-answer'
import type { HostApp, HostAppServiceAction, InstallableHostApp } from '../../src/types/hostApp'
import type { PanelTask } from '../../src/types/panelTask'

/** The id of the task every stubbed install and uninstall is answered with. */
export const APP_TASK_ID = '55555555-5555-5555-5555-555555555555'

/** The paged list of installed apps, with or without a query. `installable` and `stack` are not an app's id. */
const APPS_LIST_PATTERN = /\/api\/v1\/apps(\?.*)?$/

/** The catalogue of what the package sources can install. */
const INSTALLABLE_PATTERN = /\/api\/v1\/apps\/installable(\?.*)?$/

/** An install or uninstall of one app. */
const OPERATION_PATTERN = /\/api\/v1\/apps\/([^/]+)\/(install|uninstall)$/

/** A service control of one app. */
const SERVICE_PATTERN = /\/api\/v1\/apps\/([^/]+)\/service\/(start|stop|restart|reload)$/

/** The read of one followed task. */
const TASK_PATTERN = /\/api\/v1\/tasks\/[0-9a-f-]{36}(\?.*)?$/

/** How the apps endpoint searches (the name), sorts and filters, as the panel does. */
const APP_RULES: PagedRules<HostApp> = {
  matches: (app, term) => {
    return app.name.toLowerCase().includes(term) || app.appId.toLowerCase().includes(term)
  },
  sorts: {
    appId: (app) => {
      return app.appId
    },
    name: (app) => {
      return app.name
    },
    kind: (app) => {
      return app.kind
    },
    state: (app) => {
      return app.state
    },
    installedAt: (app) => {
      return app.installedAt ?? ''
    },
  },
  filters: {
    kind: (app, value) => {
      return app.kind === value
    },
    state: (app, value) => {
      return app.state === value
    },
  },
}

/** A request the page made to the apps endpoints. */
export interface RecordedAppRequest {
  /** The HTTP method. */
  method: string
  /** The path with its query. */
  path: string
}

/**
 * The state the stubbed apps endpoints answer from. A spec builds one with {@link appsWorld},
 * changes the fields it is about, and may change them again while the page is open.
 */
export interface AppsWorld {
  /** The apps installed on the host. */
  installed: HostApp[]
  /** The catalogue of what can be installed, installed apps flagged. */
  installable: InstallableHostApp[]
  /** Apps whose removal is refused with `AppsInUse`, by id, with the domains of the using sites. */
  inUse: Record<string, string[]>
  /** The task reads of a followed operation: the n-th read answers the n-th entry, the last repeats. */
  taskScript: PanelTask[]
  /** Called when an install was accepted, so the spec can change what the next reads answer. */
  onInstall: ((id: string) => void) | null
  /** Called when an uninstall was accepted. */
  onUninstall: ((id: string) => void) | null
}

/** What {@link stubApps} reports back to a spec. */
export interface AppsRecorder {
  /** Every request made to the apps endpoints, oldest first. */
  requests: RecordedAppRequest[]
  /**
   * The requests whose `METHOD path` matches a pattern.
   * @param pattern What to look for.
   * @returns The matching requests.
   */
  matching: (pattern: RegExp) => RecordedAppRequest[]
}

/**
 * An installed app, as the panel lists it.
 * @param id The catalogue id.
 * @param name The app as an operator reads it.
 * @param overrides The fields to change.
 * @returns A running, installed app.
 */
export const hostApp = (id: string, name: string, overrides: Partial<HostApp> = {}): HostApp => {
  const isWebServer = id === 'nginx' || id === 'apache'
  return {
    appId: id,
    name,
    kind: isWebServer ? 'webServer' : 'runtime',
    kindName: isWebServer ? 'Web server' : 'Runtime',
    version: isWebServer ? '1.24' : id.split('-')[1] ?? '1',
    state: 'installed',
    stateName: 'Installed',
    serviceStatus: 'active',
    serviceStatusName: 'Running',
    installedAt: '2026-11-09T09:00:00Z',
    taskId: null,
    ...overrides,
  }
}

/**
 * An app the package sources offer.
 * @param id The catalogue id.
 * @param name The app as an operator reads it.
 * @param overrides The fields to change.
 * @returns A not yet installed app.
 */
export const installableApp = (
  id: string,
  name: string,
  overrides: Partial<InstallableHostApp> = {},
): InstallableHostApp => {
  const isWebServer = id === 'nginx' || id === 'apache'
  return {
    appId: id,
    name,
    kind: isWebServer ? 'webServer' : 'runtime',
    kindName: isWebServer ? 'Web server' : 'Runtime',
    installed: false,
    ...overrides,
  }
}

/**
 * A task of the panel's tasks list, as a followed install reports it.
 * @param overrides The fields to change.
 * @returns A running install task at 40 percent.
 */
export const appTask = (overrides: Partial<PanelTask> = {}): PanelTask => {
  return {
    id: APP_TASK_ID,
    kind: 'AppInstall',
    subject: 'nginx',
    correlationId: null,
    status: 'running',
    statusName: 'Running',
    percent: 40,
    log: 'Installing packages',
    logIncluded: true,
    failure: null,
    startedAt: '2026-11-09T10:00:00Z',
    finishedAt: null,
    revision: 1,
    ...overrides,
  }
}

/** The finished form of {@link appTask}. */
export const FINISHED_APP_TASK: PanelTask = appTask({
  status: 'completed',
  statusName: 'Completed',
  percent: 100,
  finishedAt: '2026-11-09T10:00:05Z',
  revision: 2,
})

/**
 * A fresh host's apps: nothing installed, and a web server, PHP and Node.js on offer.
 * @param overrides The fields to change.
 * @returns The world the stubs answer from.
 */
export const appsWorld = (overrides: Partial<AppsWorld> = {}): AppsWorld => {
  return {
    installed: [],
    installable: [
      installableApp('nginx', 'nginx'),
      installableApp('apache', 'Apache'),
      installableApp('php-8.3', 'PHP 8.3'),
      installableApp('php-8.4', 'PHP 8.4'),
      installableApp('nodejs-22', 'Node.js 22'),
    ],
    inUse: {},
    taskScript: [appTask(), FINISHED_APP_TASK],
    onInstall: null,
    onUninstall: null,
    ...overrides,
  }
}

/**
 * Fulfils one JSON route.
 * @param route The intercepted request.
 * @param status The HTTP status.
 * @param body The value to serialize.
 * @returns Resolves once fulfilled.
 */
const fulfilJson = async (route: Route, status: number, body: unknown): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Installs the apps endpoints for a world: the paged list, the catalogue, install and uninstall as
 * 202 with a task, service controls, and the task read. Every answer is read from the world when the
 * request arrives, so a spec can change it while the page is open.
 * @param page The Playwright page whose network the routes are installed on.
 * @param world The state the stubs answer from.
 * @returns What the stubs recorded.
 */
export const stubApps = async (page: Page, world: AppsWorld): Promise<AppsRecorder> => {
  const requests: RecordedAppRequest[] = []
  let taskReads = 0

  await page.route(APPS_LIST_PATTERN, async (route: Route) => {
    requests.push({ method: route.request().method(), path: new URL(route.request().url()).pathname + new URL(route.request().url()).search })
    await fulfilJson(route, 200, pagedAnswer(world.installed, route.request().url(), APP_RULES))
  })

  await page.route(INSTALLABLE_PATTERN, async (route: Route) => {
    requests.push({ method: 'GET', path: '/api/v1/apps/installable' })
    await fulfilJson(route, 200, world.installable)
  })

  await page.route(OPERATION_PATTERN, async (route: Route) => {
    const url = new URL(route.request().url())
    requests.push({ method: route.request().method(), path: url.pathname })
    const match = OPERATION_PATTERN.exec(url.pathname)
    const id = decodeURIComponent(match?.[1] ?? '')
    const operation = match?.[2]
    if (operation === 'uninstall' && world.inUse[id] !== undefined) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          code: 'AppsInUse',
          title: 'App in use',
          detail: `${id} cannot be removed while sites use it.`,
          sites: { total: world.inUse[id].length, domains: world.inUse[id] },
        }),
      })
      return
    }
    taskReads = 0
    if (operation === 'install') {
      world.onInstall?.(id)
    } else {
      world.onUninstall?.(id)
    }
    await fulfilJson(route, 202, { taskId: APP_TASK_ID })
  })

  await page.route(SERVICE_PATTERN, async (route: Route) => {
    const url = new URL(route.request().url())
    requests.push({ method: route.request().method(), path: url.pathname })
    const match = SERVICE_PATTERN.exec(url.pathname)
    const id = decodeURIComponent(match?.[1] ?? '')
    const action = match?.[2] as HostAppServiceAction
    world.installed = world.installed.map((app) => {
      if (app.appId !== id) {
        return app
      }
      const running = action === 'stop' ? false : true
      return {
        ...app,
        serviceStatus: running ? 'active' : 'inactive',
        serviceStatusName: running ? 'Running' : 'Stopped',
      }
    })
    await fulfilJson(route, 200, {
      serviceStatus: action === 'stop' ? 'inactive' : 'active',
      serviceStatusName: action === 'stop' ? 'Stopped' : 'Running',
    })
  })

  await page.route(TASK_PATTERN, async (route: Route) => {
    requests.push({ method: 'GET', path: new URL(route.request().url()).pathname })
    const index = Math.min(taskReads, world.taskScript.length - 1)
    taskReads += 1
    await fulfilJson(route, 200, world.taskScript[index])
  })

  return {
    requests,
    matching: (pattern: RegExp): RecordedAppRequest[] => {
      return requests.filter((entry) => {
        return pattern.test(`${entry.method} ${entry.path}`)
      })
    },
  }
}
