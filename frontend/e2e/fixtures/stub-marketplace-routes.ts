import type { Page, Route } from '@playwright/test'
import type {
  InstalledModule,
  MarketplaceCatalogue,
  MarketplaceModule,
  MarketplaceModuleDetail,
  MarketplaceStatus,
  ModuleManifestSummary,
  ModuleUpdates,
} from '../../src/types/marketplace'
import type { PanelTask } from '../../src/types/panelTask'

/** The password the stubbed panel accepts when it re-asks for the administrator's password. */
export const MARKETPLACE_PASSWORD = 'correct-horse-battery'

/** The sentence the stubbed panel answers a wrong password with, in the panel's own words. */
export const WRONG_PASSWORD_MESSAGE = 'The password you entered is not correct.'

/** The id of the task every stubbed install, update, removal and rollback is answered with. */
export const OPERATION_TASK_ID = '33333333-3333-3333-3333-333333333333'

/** The part of a path the marketplace's gated reads share: none may be made while the switch is off. */
export const GATED_READ = /\/api\/v1\/marketplace\/(catalogue|modules|updates|installed)/

/** The task endpoint a followed operation is read from (`/api/v1/tasks/{id}`), never the list. */
const TASK_PATTERN = /\/api\/v1\/tasks\/[0-9a-f-]{36}(\?.*)?$/

/** Everything under the marketplace's own path; the panel's module catalogue is `/api/v1/modules`. */
const MARKETPLACE_PATTERN = /\/api\/v1\/marketplace\//

/** The operations the panel starts as a task. */
type OperationName = 'install' | 'update' | 'remove' | 'rollback'

/** A problem the stubbed panel answers a request with instead of its normal body. */
export interface StubbedRefusal {
  /** The HTTP status. */
  status: number
  /** The panel's machine code. */
  code: string
  /** The panel's own localized sentence. */
  detail: string
}

/** One request the page made to the marketplace's path. */
export interface RecordedRequest {
  /** The HTTP method. */
  method: string
  /** The path with its query, e.g. `/api/v1/marketplace/catalogue?search=weather`. */
  path: string
  /** The parsed JSON body, or `null` when there was none. */
  body: unknown
}

/**
 * The state the stubbed marketplace answers from. A spec builds one with {@link marketplaceWorld},
 * changes the fields it is about, and may change them again while the page is open: every route
 * reads the object when a request arrives.
 */
export interface MarketplaceWorld {
  /** The switch and the facts the switch card prints. */
  status: MarketplaceStatus
  /** The catalogue the marketplace offers. */
  catalogue: MarketplaceCatalogue
  /** The modules installed on the panel. */
  installed: InstalledModule[]
  /** The answer to the one "check for updates" call. */
  updates: ModuleUpdates
  /** The detail of each module, by id. */
  details: Record<string, MarketplaceModuleDetail>
  /** Refuses the catalogue read, or `null` to answer it. */
  catalogueRefusal: StubbedRefusal | null
  /** Refuses the start of an operation, or `null` to accept it (after the password check). */
  operationRefusal: StubbedRefusal | null
  /** The task read of a followed operation: the n-th read answers the n-th entry, the last one repeats. */
  taskScript: PanelTask[]
  /** Called when an operation was accepted, so the spec can change what the next reads answer. */
  onOperation: ((name: OperationName, id: string) => void) | null
  /** Called when a restart was accepted. */
  onRestart: ((id: string) => void) | null
}

/** What {@link stubMarketplace} hands back to the spec. */
export interface MarketplaceRecorder {
  /** Every request made to the marketplace's path, oldest first. */
  requests: RecordedRequest[]
  /** How many times a task was read. */
  taskReads: () => number
  /**
   * The requests whose `METHOD path` line matches.
   * @param pattern What to match against `GET /api/v1/marketplace/catalogue?...`.
   * @returns The matching requests, oldest first.
   */
  matching: (pattern: RegExp) => RecordedRequest[]
}

/**
 * The manifest summary the panel's server holds for a module: what it can reach, shown before install.
 * @param overrides The fields to change.
 * @returns The summary.
 */
export const manifestOf = (overrides: Partial<ModuleManifestSummary> = {}): ModuleManifestSummary => {
  return {
    publisher: 'Innovayse',
    publisherId: 'innovayse',
    tierDisplayName: 'Included',
    minPanelVersion: '1.0.0',
    schemaName: 'weather',
    dependencies: [],
    agentCapabilities: [{ key: 'files.read', displayName: 'Read files inside an account' }],
    permissions: [{ key: 'weather.view', displayName: 'See the weather widgets' }],
    ...overrides,
  }
}

/**
 * A catalogue entry for a module that is entitled and can be installed.
 * @param id The module's machine id.
 * @param displayName The name the panel gives it.
 * @param overrides The fields to change.
 * @returns The entry.
 */
export const catalogueModule = (
  id: string,
  displayName: string,
  overrides: Partial<MarketplaceModule> = {},
): MarketplaceModule => {
  return {
    id,
    displayName,
    summary: `${displayName} for the panel.`,
    publisher: 'Innovayse',
    publisherId: 'innovayse',
    tier: 'free',
    tierDisplayName: 'Free',
    price: null,
    latestVersion: '1.2.0',
    installedVersion: null,
    state: 'available',
    entitlement: { state: 'notRequired', stateDisplayName: 'No licence needed', until: null },
    canInstall: true,
    cannotInstallMessage: null,
    restartRequired: false,
    ...overrides,
  }
}

/**
 * A module installed on the panel and running.
 * @param id The module's machine id.
 * @param displayName The name the panel gives it.
 * @param overrides The fields to change.
 * @returns The installed module.
 */
export const installedModule = (
  id: string,
  displayName: string,
  overrides: Partial<InstalledModule> = {},
): InstalledModule => {
  return {
    id,
    displayName,
    version: '2.0.0',
    lifecycle: 'active',
    failureMessage: null,
    origin: 'Marketplace',
    installedAt: '2026-10-01T09:30:00Z',
    previousVersion: null,
    restartRequired: false,
    dataKept: false,
    revoked: false,
    canRollback: false,
    canRemove: true,
    ...overrides,
  }
}

/**
 * The detail screen's answer for a catalogue entry.
 * @param module The catalogue entry.
 * @param overrides The fields to change.
 * @returns The detail, with one installable version and a manifest.
 */
export const detailOf = (
  module: MarketplaceModule,
  overrides: Partial<MarketplaceModuleDetail> = {},
): MarketplaceModuleDetail => {
  return {
    module,
    versions: [
      {
        version: module.latestVersion ?? '1.0.0',
        minPanelVersion: '1.0.0',
        maxPanelVersion: null,
        sizeBytes: 2_400_000,
        publishedAt: '2026-10-20T08:00:00Z',
        releaseNotes: 'Adds a forecast widget.',
        revoked: false,
        revokedMessage: null,
        installable: module.canInstall,
        notInstallableMessage: module.cannotInstallMessage,
      },
    ],
    manifest: manifestOf(),
    ...overrides,
  }
}

/**
 * A task of the panel's tasks list, as a followed operation reports it.
 * @param overrides The fields to change.
 * @returns A running install task at 40 percent.
 */
export const operationTask = (overrides: Partial<PanelTask> = {}): PanelTask => {
  return {
    id: OPERATION_TASK_ID,
    kind: 'ModuleInstall',
    subject: 'weather',
    correlationId: null,
    status: 'running',
    statusName: 'Running',
    percent: 40,
    log: 'Checking entitlement\nDownloading',
    logIncluded: true,
    failure: null,
    startedAt: '2026-11-09T10:00:00Z',
    finishedAt: null,
    revision: 1,
    ...overrides,
  }
}

/**
 * The state of a fresh panel: the marketplace switched OFF, a host configured, nothing installed.
 * @param overrides The fields to change.
 * @returns The world the stubs answer from.
 */
export const marketplaceWorld = (overrides: Partial<MarketplaceWorld> = {}): MarketplaceWorld => {
  return {
    status: {
      enabled: false,
      hostConfigured: true,
      host: 'api-marketplace-maran.innovayse.com',
      sentItems: ['The panel version', 'The licence lease, to check which modules you may install'],
      restartRequiredModules: [],
    },
    catalogue: { items: [], nextCursor: null, verified: true, fetchedAt: '2026-11-09T09:00:00Z', unverifiedMessage: null },
    installed: [],
    updates: { items: [], verified: true, checkedAt: '2026-11-09T09:05:00Z', unverifiedMessage: null },
    details: {},
    catalogueRefusal: null,
    operationRefusal: null,
    taskScript: [operationTask()],
    onOperation: null,
    onRestart: null,
    ...overrides,
  }
}

/**
 * The state of a panel whose administrator already switched the marketplace on.
 * @param overrides The fields to change; the switch itself stays on.
 * @returns The world the stubs answer from.
 */
export const enabledWorld = (overrides: Partial<MarketplaceWorld> = {}): MarketplaceWorld => {
  const world = marketplaceWorld(overrides)
  world.status = { ...world.status, enabled: true }
  return world
}

/**
 * Fulfils a request with a JSON body.
 * @param route The intercepted route.
 * @param status The HTTP status.
 * @param body The value to serialize.
 * @returns Resolves once the route is fulfilled.
 */
const fulfilJson = async (route: Route, status: number, body: unknown): Promise<void> => {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/**
 * Fulfils a request with the panel's RFC 7807 problem.
 * @param route The intercepted route.
 * @param refusal What the panel refuses with.
 * @returns Resolves once the route is fulfilled.
 */
const fulfilRefusal = async (route: Route, refusal: StubbedRefusal): Promise<void> => {
  await route.fulfill({
    status: refusal.status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ code: refusal.code, title: refusal.code, detail: refusal.detail }),
  })
}

/**
 * Reads a request's JSON body, or `null` when it carries none.
 * @param route The intercepted route.
 * @returns The parsed body.
 */
const bodyOf = (route: Route): unknown => {
  const text = route.request().postData()
  return text === null || text === '' ? null : (JSON.parse(text) as unknown)
}

/**
 * Whether a request body carries the password the stubbed panel accepts.
 * @param body The parsed body.
 * @returns True when the password is right.
 */
const hasRightPassword = (body: unknown): boolean => {
  return typeof body === 'object' && body !== null && (body as { password?: unknown }).password === MARKETPLACE_PASSWORD
}

/**
 * Whether an install or update body carries the administrator's acknowledgement.
 * @param body The parsed body.
 * @returns True when `acknowledged` is true.
 */
const isAcknowledged = (body: unknown): boolean => {
  return typeof body === 'object' && body !== null && (body as { acknowledged?: unknown }).acknowledged === true
}

/**
 * Answers `PUT settings`: the switch moves only with the right password.
 * @param route The intercepted route.
 * @param world The state to change.
 * @param body The parsed request body.
 * @returns Resolves once the route is fulfilled.
 */
const answerSettings = async (route: Route, world: MarketplaceWorld, body: unknown): Promise<void> => {
  if (!hasRightPassword(body)) {
    await fulfilRefusal(route, { status: 400, code: 'ReauthenticationFailed', detail: WRONG_PASSWORD_MESSAGE })
    return
  }
  world.status = { ...world.status, enabled: (body as { enabled: boolean }).enabled }
  await fulfilJson(route, 200, world.status)
}

/**
 * Answers the start of an operation: the password, then the acknowledgement where one is owed, then
 * the spec's own refusal, and only then a task.
 * @param route The intercepted route.
 * @param world The state to change.
 * @param name The operation.
 * @param id The module's machine id.
 * @param body The parsed request body.
 * @returns Resolves once the route is fulfilled.
 */
const answerOperation = async (
  route: Route,
  world: MarketplaceWorld,
  name: OperationName,
  id: string,
  body: unknown,
): Promise<void> => {
  if (!hasRightPassword(body)) {
    await fulfilRefusal(route, { status: 400, code: 'ReauthenticationFailed', detail: WRONG_PASSWORD_MESSAGE })
    return
  }
  if ((name === 'install' || name === 'update') && !isAcknowledged(body)) {
    await fulfilRefusal(route, { status: 400, code: 'InstallNotAcknowledged', detail: 'Acknowledge what the module can reach.' })
    return
  }
  if (world.operationRefusal !== null) {
    await fulfilRefusal(route, world.operationRefusal)
    return
  }
  world.onOperation?.(name, id)
  await fulfilJson(route, 202, { taskId: OPERATION_TASK_ID })
}

/**
 * Answers one request under `/api/v1/marketplace/`.
 * @param route The intercepted route.
 * @param world The state the answers come from.
 * @returns Resolves once the route is fulfilled.
 */
const answer = async (route: Route, world: MarketplaceWorld): Promise<void> => {
  const request = route.request()
  const url = new URL(request.url())
  const rest = url.pathname.replace('/api/v1/marketplace/', '')
  const body = bodyOf(route)

  if (rest === 'status') {
    await fulfilJson(route, 200, world.status)
    return
  }
  if (rest === 'settings') {
    await answerSettings(route, world, body)
    return
  }
  if (rest === 'catalogue') {
    if (world.catalogueRefusal !== null) {
      await fulfilRefusal(route, world.catalogueRefusal)
      return
    }
    // The panel searches, not the page: the stub filters the way the server does.
    const term = (url.searchParams.get('search') ?? '').toLowerCase()
    const items = world.catalogue.items.filter((item) => {
      return `${item.displayName} ${item.summary}`.toLowerCase().includes(term)
    })
    await fulfilJson(route, 200, { ...world.catalogue, items })
    return
  }
  if (rest === 'installed') {
    await fulfilJson(route, 200, world.installed)
    return
  }
  if (rest === 'updates') {
    await fulfilJson(route, 200, world.updates)
    return
  }

  const operation = /^modules\/([^/]+)\/(install|update|remove|rollback|restart)$/.exec(rest)
  if (operation !== null) {
    const id = decodeURIComponent(operation[1] ?? '')
    if (operation[2] === 'restart') {
      world.onRestart?.(id)
      await route.fulfill({ status: 202 })
      return
    }
    await answerOperation(route, world, operation[2] as OperationName, id, body)
    return
  }

  const detail = /^modules\/([^/]+)$/.exec(rest)
  const found = detail === null ? undefined : world.details[decodeURIComponent(detail[1] ?? '')]
  if (found === undefined) {
    await fulfilRefusal(route, { status: 404, code: 'ModuleNotInCatalogue', detail: 'The marketplace has no such module.' })
    return
  }
  await fulfilJson(route, 200, found)
}

/**
 * Stands in for the marketplace's whole panel API and the task read of a followed operation, and
 * records what the page asked. The refusals the spec provokes (a wrong password, an unreachable
 * marketplace) must be announced to the guard with `guard.allow` by the spec itself.
 * @param page The Playwright page whose network the routes are installed on.
 * @param world The state the answers come from; change it while the page is open to change later answers.
 * @returns The recorder the spec asserts on.
 */
export const stubMarketplace = async (page: Page, world: MarketplaceWorld): Promise<MarketplaceRecorder> => {
  const requests: RecordedRequest[] = []
  let taskReads = 0

  await page.route(MARKETPLACE_PATTERN, async (route: Route) => {
    const request = route.request()
    const url = new URL(request.url())
    requests.push({ method: request.method(), path: `${url.pathname}${url.search}`, body: bodyOf(route) })
    await answer(route, world)
  })

  await page.route(TASK_PATTERN, async (route: Route) => {
    const index = Math.min(taskReads, world.taskScript.length - 1)
    taskReads += 1
    await fulfilJson(route, 200, world.taskScript[index])
  })

  return {
    requests,
    taskReads: (): number => {
      return taskReads
    },
    matching: (pattern: RegExp): RecordedRequest[] => {
      return requests.filter((entry) => {
        return pattern.test(`${entry.method} ${entry.path}`)
      })
    },
  }
}
