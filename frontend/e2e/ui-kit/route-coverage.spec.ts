import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '../fixtures/guarded-test'
import {
  routesOf,
  sweptPathsOf,
  uncoveredRoutes,
  type RouteExemption,
} from '../fixtures/route-coverage'

/*
 * The census behind the 390 px contract (issue #82): every routed screen is opened by a row of
 * one of the three horizontal-scroll sweeps, or is named here with the reason it is not.
 *
 * Why it exists: the sweeps are hand-written tables, and a new route ships uncovered unless
 * someone remembers to add a row. That is how the settings and reseller screens went
 * unmeasured. This spec reads `src/router/index.ts` as text and each sweep's `path:` literals as
 * text, so a route added tomorrow without a row fails here, naming the route.
 *
 * What it cannot see: it proves a sweep row opens the address, not that the row waits for the
 * right content before measuring. A row that opens a screen still showing a skeleton passes the
 * census; the sweeps' own readiness checks are what guard that.
 */

/** The frontend folder, where `src/` and `e2e/` live. */
const FRONTEND = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')

/** The specs that sweep screens at 390 px, relative to the frontend folder. */
const SWEEP_SPECS = [
  'e2e/ui-kit/no-horizontal-scroll-admin.spec.ts',
  'e2e/ui-kit/no-horizontal-scroll-other.spec.ts',
  'e2e/ui-kit/no-horizontal-scroll-settings-and-resellers.spec.ts',
  'e2e/dns/no-horizontal-scroll.spec.ts',
]

/**
 * How many distinct addresses the reader finds in each sweep spec today, measured with
 * `sweptPathsOf` (the DNS sweep opens seven addresses, so a shared floor of "more than three" had to
 * be loosened for it). The census itself guarantees every route a row (the first test below, which
 * diffs the router against ALL the addresses read); these floors only guard the reader going blind
 * on one spec, and a single shared floor cannot do that for a 24-row spec: it would let the reader
 * lose twenty of them unnoticed. Each is the exact current count, so a reader regression on any spec
 * is named, and a row removed on purpose lowers its floor in the same change.
 */
const SWEEP_ADDRESS_FLOORS: Record<string, number> = {
  'e2e/ui-kit/no-horizontal-scroll-admin.spec.ts': 24,
  'e2e/ui-kit/no-horizontal-scroll-other.spec.ts': 15,
  'e2e/ui-kit/no-horizontal-scroll-settings-and-resellers.spec.ts': 17,
  'e2e/dns/no-horizontal-scroll.spec.ts': 7,
}

/**
 * Feature specs that measure their own one to three screens at 390 px and declare each address as a
 * `path:` row. They are not tables of screens, so they are read for at least one address, not five.
 */
const SCREEN_SWEEP_SPECS = [
  'e2e/mail/mail-layout.spec.ts',
  'e2e/mail/mail-queue.spec.ts',
  'e2e/databases/users.spec.ts',
  'e2e/backups/my-schedule.spec.ts',
  'e2e/sites/php-settings.spec.ts',
  'e2e/files/browser.spec.ts',
  'e2e/files/editor.spec.ts',
  'e2e/files/trash.spec.ts',
  'e2e/settings/alert-settings.spec.ts',
  'e2e/settings/licence.spec.ts',
  'e2e/marketplace/layout.spec.ts',
  'e2e/apps/layout.spec.ts',
  'e2e/migration/no-horizontal-scroll.spec.ts',
]

/** Routes no sweep row opens, each with its reason. A stale entry fails the spec below. */
const EXEMPTIONS: RouteExemption[] = [
  {
    path: '/:pathMatch(.*)*',
    reason:
      'the catch-all draws the not-found screen, which the "not found" row of the other-screens sweep opens through an address that matches no route',
  },
  {
    path: '/login/two-factor',
    reason:
      'the "two-factor code step" row of the other-screens sweep opens /login and submits the form, which is how a visitor arrives here; the address is not opened directly',
  },
  {
    path: '/settings/panel-update',
    reason:
      'the panel-update backend (issue #382) does not exist yet, so the screen cannot be brought up with a faithful stub; add its row when the endpoint lands',
  },
]

/**
 * Reads a file of the frontend folder.
 * @param relative The path relative to the frontend folder.
 * @returns The file's text.
 */
const read = (relative: string): string => {
  return readFileSync(join(FRONTEND, relative), 'utf8')
}

/** A router of three screens, one of them a parameter route with a literal sibling. */
const PLANTED_ROUTER = `
createRouter({ routes: [
  { path: '/things', component: Layout, children: [
    { path: '', name: 'things', component: Things },
    { path: 'new', name: 'things-new', component: ThingForm },
    { path: ':id', name: 'thing', component: Thing },
  ] },
  { path: '/old', redirect: { name: 'things' } },
  { path: '/planted-screen-zz', component: Layout, children: [{ path: '', component: Planted }] },
] })
`

test.describe('every routed screen is swept at 390 px or excused', () => {
  test('no route of the real router is left without a row or an exemption', () => {
    const routes = routesOf(read('src/router/index.ts'))
    const swept = sweptPathsOf(
      [...SWEEP_SPECS, ...SCREEN_SWEEP_SPECS].map((spec) => {
        return read(spec)
      }),
    )

    expect(uncoveredRoutes(routes, swept, EXEMPTIONS), 'routes that no 390 px sweep row opens').toEqual([])
  })

  test('every exemption names a route the router still declares and gives a reason', () => {
    const declared = routesOf(read('src/router/index.ts')).map((route) => {
      return route.path
    })

    for (const exemption of EXEMPTIONS) {
      expect(declared, `the exemption for ${exemption.path} names no route`).toContain(exemption.path)
      expect(exemption.reason.trim().length, `the exemption for ${exemption.path} has no reason`).toBeGreaterThan(20)
    }
  })

  test('the reader finds the routes it is expected to find in the real router', () => {
    // Vacuity guard on the axis that can go blind: a reader that parsed nothing would report no
    // uncovered route. These are known to exist, at three nesting depths and one constant segment.
    const paths = routesOf(read('src/router/index.ts')).map((route) => {
      return route.path
    })

    expect(paths.length).toBeGreaterThan(40)
    expect(paths).toContain('/')
    expect(paths).toContain('/accounts/:id')
    expect(paths).toContain('/resellers/:id/profile')
    expect(paths).toContain('/settings/api-keys')
    expect(paths).toContain('/cron/:entryId/runs')
    // The site route is declared through a constant, whose value must have been resolved.
    expect(
      paths.some((path) => {
        return path.startsWith('/sites/:id(')
      }),
    ).toBe(true)
  })

  test('the sweeps are read: each yields addresses, and the known ones are among them', () => {
    const perSpec = SWEEP_SPECS.map((spec) => {
      return sweptPathsOf([read(spec)])
    })

    for (const [index, addresses] of perSpec.entries()) {
      const spec = SWEEP_SPECS[index] ?? ''
      const floor = SWEEP_ADDRESS_FLOORS[spec]
      expect(floor, `${spec} has no address floor`).toBeDefined()
      expect(addresses.length, `${spec} yields fewer addresses than its floor`).toBeGreaterThanOrEqual(floor ?? Infinity)
    }
    for (const spec of SCREEN_SWEEP_SPECS) {
      expect(sweptPathsOf([read(spec)]).length, `${spec} yields no address`).toBeGreaterThan(0)
    }
    expect(perSpec.flat()).toContain('/accounts')
    expect(perSpec.flat()).toContain('/login')
  })

  test('prose naming the key in backticks does not hide the rows after it (positive control and inverse)', () => {
    // A comment reading "the `path:` rows below" opens a template string at its own backtick; the
    // reader must skip it and still find the real rows, or the whole spec reads as having none.
    const source = [
      '// The census reads the `path:` rows below, and `other` things.',
      "const SCREENS = [{ path: '/things' }, { path: `/things/${id}` }]",
    ].join('\n')

    expect(sweptPathsOf([source])).toEqual(['/things', '/things/x'])
    expect(sweptPathsOf(["const row = { path: '/plain' }"])).toEqual(['/plain'])
  })

  test('a route that no sweep opens is reported (positive control)', () => {
    const routes = routesOf(PLANTED_ROUTER)

    expect(uncoveredRoutes(routes, ['/things', '/things/new', '/things/x'], [])).toEqual(['/planted-screen-zz'])
  })

  test('a route whose row exists, or that is excused, is accepted (inverse control)', () => {
    const routes = routesOf(PLANTED_ROUTER)

    expect(uncoveredRoutes(routes, ['/things', '/things/new', '/things/x', '/planted-screen-zz'], [])).toEqual([])
    expect(
      uncoveredRoutes(routes, ['/things', '/things/new', '/things/x'], [
        { path: '/planted-screen-zz', reason: 'planted for the inverse control of the census' },
      ]),
    ).toEqual([])
  })

  test('a row for a literal sibling does not also cover the parameter route', () => {
    const routes = routesOf(PLANTED_ROUTER)

    expect(uncoveredRoutes(routes, ['/things', '/things/new', '/planted-screen-zz'], [])).toEqual(['/things/:id'])
  })

  test('a redirect draws no screen and needs no row', () => {
    const routes = routesOf(PLANTED_ROUTER)

    expect(routes.map((route) => {return route.path})).toContain('/old')
    expect(uncoveredRoutes(routes, ['/things', '/things/new', '/things/x', '/planted-screen-zz'], [])).not.toContain('/old')
  })
})
