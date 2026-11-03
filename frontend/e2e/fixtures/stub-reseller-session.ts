import type { Page, Route } from '@playwright/test'
import { ACME_HOSTING } from './stub-resellers-routes'
import { stubSignedIn } from './stub-auth-routes'
import { stubHealthy } from './stub-health-route'
import { stubModules } from './stub-modules-route'
import { PLAN_FEATURE_CHOICES } from './stub-plans-route'
import { stubShellReads } from './stub-shell-reads'
import type { AuthenticatedUser } from '../../src/types/auth'
import type { PanelModule } from '../../src/types/module'
import type { ResellerDetail } from '../../src/types/reseller'
import type { ViewAsSession, ViewingAs } from '../../src/types/viewAs'

/** The reseller's own envelope, usage and profile: the read its overview and its plan form make. */
const MY_RESELLER_PATTERN = /\/api\/v1\/reseller\/me(\?.*)?$/

/** The features a plan can sell, which the reseller's overview and plan form read. */
const FEATURES_PATTERN = /\/api\/v1\/plans\/features(\?.*)?$/

/** The token the stubbed panel issues for a view-as session; distinct from the operator's. */
export const VIEW_AS_CHAIN_TOKEN = 'stub.view-as-chain.token'

/** The reseller login every reseller spec signs in as. It owns no account, so `accountId` is null. */
export const RESELLER_USER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-0000000000a1',
  username: 'acmehosting',
  email: 'owner@acmehosting.example.com',
  role: 'reseller',
  accountId: null,
}

/**
 * The modules a reseller's catalogue lists: the panel filters the catalogue by role, and the
 * Accounts module is the one that carries a reseller's screens.
 */
export const RESELLER_MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', state: 'enabled' },
]

/**
 * The catalogue an administrator would see, a superset of {@link RESELLER_MODULES}. Role-exactness
 * specs hand this to a reseller or a customer on purpose, so a refusal cannot be the module guard's.
 */
export const ADMIN_MODULES: PanelModule[] = [
  { name: 'accounts', displayName: 'Accounts', tier: 'included', state: 'enabled' },
  { name: 'sites', displayName: 'Sites', tier: 'included', state: 'enabled' },
  { name: 'databases', displayName: 'Databases', tier: 'included', state: 'enabled' },
  { name: 'backups', displayName: 'Backups', tier: 'included', state: 'enabled' },
  { name: 'firewall', displayName: 'Firewall', tier: 'included', state: 'enabled' },
  { name: 'monitoring', displayName: 'Monitoring', tier: 'included', state: 'enabled' },
  { name: 'tasks', displayName: 'Tasks', tier: 'included', state: 'enabled' },
]

/**
 * Signs a reseller in on a desktop and answers what the reseller's shell reads: health, the quiet
 * shell reads, the reseller's own catalogue, `GET /api/v1/reseller/me` and the plan features.
 * @param page The Playwright page whose network the routes are installed on.
 * @param mine What the panel reports about the reseller itself.
 * @param modules The catalogue the panel reports.
 * @returns Resolves once the routes are installed.
 */
export const stubResellerSession = async (
  page: Page,
  mine: ResellerDetail = ACME_HOSTING,
  modules: PanelModule[] = RESELLER_MODULES,
): Promise<void> => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await stubSignedIn(page, RESELLER_USER)
  await stubHealthy(page)
  await stubShellReads(page)
  await stubModules(page, modules)
  await stubMyReseller(page, mine)
}

/**
 * Fulfils `GET /api/v1/reseller/me` and the plan features read, the two reads a reseller's screens
 * make that an administrator's never do. A spec in which an administrator views the panel as a
 * reseller needs them without the reseller being the signed-in caller.
 * @param page The Playwright page whose network the routes are installed on.
 * @param mine What the panel reports about the reseller itself.
 * @returns Resolves once the routes are installed.
 */
export const stubMyReseller = async (page: Page, mine: ResellerDetail = ACME_HOSTING): Promise<void> => {
  await page.route(MY_RESELLER_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(mine) })
  })
  await page.route(FEATURES_PATTERN, async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(PLAN_FEATURE_CHOICES) })
  })
}

/** Records what the stubbed view-as endpoints were asked. */
export interface ViewAsChainCalls {
  /** How many times a view-as session was started, of either kind. */
  started: number
  /** How many times one was ended. */
  ended: number
}

/** What {@link stubViewAsChain} is told about the session it plays. */
export interface ViewAsChainScript {
  /** The login the view-as token signs in as. */
  viewedAs: AuthenticatedUser
  /** What the panel reports about the session while it lasts. */
  viewing: ViewingAs
  /** When set, every start is refused with this sentence instead of issuing a token. */
  refusal?: string
}

/**
 * Stubs both view-as starts (`POST /api/v1/view-as` for an account and `POST /api/v1/view-as/reseller`)
 * the way the panel does: a start issues {@link VIEW_AS_CHAIN_TOKEN} for `viewedAs`, the state read
 * answers "viewing" only for a request carrying that token while the session has not been ended, and
 * the end marks it ended. The banner is therefore drawn from what this stub reports per token, which
 * is the property under test, not from anything the SPA remembers. A refusal answers 404 with the
 * panel's own sentence, as it does for a subject outside the caller's scope.
 * @param page The Playwright page whose network the routes are installed on.
 * @param script The session to play, or the refusal to answer with.
 * @returns A live view of the calls made.
 */
export const stubViewAsChain = async (page: Page, script: ViewAsChainScript): Promise<ViewAsChainCalls> => {
  const calls: ViewAsChainCalls = { started: 0, ended: 0 }

  /**
   * Answers a start request: the refusal when one is scripted, otherwise the session.
   * @param route The intercepted request.
   * @param code The problem code a refusal carries.
   * @returns Resolves once the response is sent.
   */
  const start = async (route: Route, code: string): Promise<void> => {
    if (script.refusal !== undefined) {
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: script.refusal, detail: script.refusal, status: 404, code }),
      })
      return
    }
    calls.started += 1
    const session: ViewAsSession = {
      accessToken: VIEW_AS_CHAIN_TOKEN,
      expiresAt: script.viewing.expiresAt,
      user: script.viewedAs,
      viewing: script.viewing,
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(session) })
  }

  await page.route('**/api/v1/view-as/reseller', async (route) => {
    await start(route, 'ViewAsResellerNotFound')
  })

  await page.route('**/api/v1/view-as', async (route) => {
    const method = route.request().method()

    if (method === 'POST') {
      await start(route, 'ViewAsAccountNotFound')
      return
    }

    if (method === 'DELETE') {
      calls.ended += 1
      await route.fulfill({ status: 204 })
      return
    }

    const isViewAsToken = route.request().headers()['authorization'] === `Bearer ${VIEW_AS_CHAIN_TOKEN}`
    const live = isViewAsToken && calls.ended === 0
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ viewing: live ? script.viewing : null }),
    })
  })

  return calls
}
