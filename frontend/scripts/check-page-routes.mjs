// Every routed page must actually be routed. A page component under `src/pages/` that the router
// never names is dead in the browser: no URL leads to it, no link can reach it, and nothing fails
// — not the build, not the type check, and not a single Playwright spec, because a spec navigates
// to a URL and a page nobody routes to is simply a file the bundle never loads.
//
// This is not hypothetical. `BackupSchedulePage.vue` shipped with its store, its API composable and
// its types, and no route: the whole backup-schedule feature — and the retention pruning behind it
// — was unreachable in a running panel, and the defect was found by a person clicking, not by a
// gate. The property is static and cheap, so it is checked mechanically rather than remembered.
//
// Why a static check and not only an end-to-end one: a Playwright spec proves ONE page is reachable
// and says nothing about the next page somebody adds. This reads the whole `src/pages/` tree, so a
// page added tomorrow with no route fails on the day it is written. Both are kept — the spec proves
// a route resolves and renders in a real browser, this proves no page was left out.
//
// The reverse direction is checked too: a route naming a component file that does not exist. The
// build already catches that, but reporting both directions costs one loop and keeps the check
// honest about what it read.

// A second, sibling property is checked in the same pass: every ROUTED page must also be REACHABLE
// FROM THE SHELL BY LINKS. Being routed proved insufficient the day it was relied on: the audit
// journal was routed at `/settings/audit`, rendered, and had Playwright specs — and a person in a
// live browser could not click their way to it, because no `RouterLink` and no navigation entry
// targeted it. The first half of this file cannot see that class at all: it proves a URL exists,
// not that any click leads there.
//
// The property is static: a route name is reachable when at least one link source targets it —
// a `:to="{ name: '…' }"` in a component, a navigation entry built by `useNavigation`, or a module
// landing route (which the sidebar turns into an entry for every module the panel reports). Pages
// deliberately outside the shell's links — the unauthenticated screens the auth guard steers
// visitors to, the catch-all, the screens the account menu opens with a programmatic push — are
// declared in DELIBERATELY_UNLINKED below, each with the reason, and a declaration that stops
// being true (the route gains a link, or stops existing) fails the check so the list cannot rot.

import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { join, relative, resolve, dirname } from 'node:path'
import process from 'node:process'

/** Repository-relative root of the SPA, resolved from this file's own location. */
const FRONTEND_ROOT = new URL('..', import.meta.url).pathname

/** The router module that is the single source of what the browser can reach. */
const ROUTER_FILE = join(FRONTEND_ROOT, 'src', 'router', 'index.ts')

/** The directory whose `*Page.vue` components must every one of them be routed. */
const PAGES_ROOT = join(FRONTEND_ROOT, 'src', 'pages')

/** A page component: the naming rule for a routed page in rules/vue.md. */
const PAGE_SUFFIX = 'Page.vue'

/** A default import of a `.vue` component in the router: captures the name and the path. */
const VUE_IMPORT = /import\s+(\w+)\s+from\s+'([^']+\.vue)'/g

/**
 * A `component: X` entry in a route record. Only an identifier is accepted, because that is the
 * only form this router uses; a lazy `component: () => import(…)` would need its own arm and is
 * reported as unrouted rather than silently understood, which fails loud instead of blind.
 */
const ROUTE_COMPONENT = /\bcomponent:\s*(\w+)\b/g

/** The composable that builds the sidebar's entries — one of the two non-component link sources. */
const NAVIGATION_FILE = join(FRONTEND_ROOT, 'src', 'composables', 'useNavigation.ts')

/** The module-to-route map the sidebar links every reported module through — the other one. */
const LANDING_ROUTES_FILE = join(FRONTEND_ROOT, 'src', 'router', 'moduleLandingRoute.ts')

/** A `name: '…'` route name in a route record. The router names every route, so this is the census. */
const ROUTE_NAME = /\bname:\s*'([\w-]+)'/g

/** A `:to="{ name: '…' }"` link target in a component — the RouterLink form this SPA writes. */
const LINK_TARGET = /:to="\{\s*name:\s*'([\w-]+)'/g

/** A `name: IDENT` reference in the navigation composable, resolved through its own consts. */
const NAVIGATION_TARGET = /\bname:\s*([A-Z][A-Z_0-9]*)\b/g

/** A `const IDENT = '…'` route-name constant in the navigation composable. */
const NAVIGATION_CONST = /\bconst\s+([A-Z][A-Z_0-9]*)\s*=\s*'([\w-]+)'/g

/** One `module: 'route-name',` arm of the landing-route map. */
const LANDING_ROUTE_VALUE = /^\s*\w+:\s*'([\w-]+)',?$/gm

/**
 * Route names deliberately outside the shell's links, each with the reason a reviewer verifies.
 * A name listed here that gains a link, or stops being a route, FAILS the check: a stale
 * exception is how a list like this rots into a bypass.
 */
const DELIBERATELY_UNLINKED = new Map([
  ['login', 'unauthenticated entry: the auth guard sends every signed-out visitor here itself'],
  ['login-two-factor', 'mid-sign-in step: the login flow pushes forward carrying in-flight state'],
  ['setup', 'first-boot screen: the auth guard steers here while the panel has no administrator'],
  ['forgot-password', 'reached from the sign-in screen by its own programmatic push'],
  ['reset-password', 'its URL arrives in the reset mail; nothing in the SPA links it on purpose'],
  ['accept-invitation', 'its URL arrives in the invitation mail; nothing in the SPA links it on purpose'],
  ['two-factor-setup', 'the auth guard steers an obliged administrator here; the router documents that nothing links it'],
  ['not-found', 'the catch-all: a page nothing should ever link to'],
  ['two-factor', 'opened by the account menu (ShellUserBlock), whose dropdown items push programmatically'],
  ['security-policy', 'opened by the account menu (ShellUserBlock), whose dropdown items push programmatically'],
  ['php-versions', 'opened by the account menu (ShellUserBlock), whose dropdown items push programmatically'],
  ['my-account', 'opened by the account menu (ShellUserBlock), whose dropdown items push programmatically'],
  ['accounts-new', "the list page's create button pushes here programmatically"],
  ['sites-new', "the list page's create button pushes here programmatically"],
])

/**
 * Lists every page component beneath a directory.
 * @param {string} directory An absolute directory path.
 * @returns {string[]} Absolute paths of the `*Page.vue` files found beneath it.
 */
const pageFiles = (directory) => {
  const found = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      found.push(...pageFiles(path))
    } else if (entry.name.endsWith(PAGE_SUFFIX)) {
      found.push(path)
    }
  }
  return found
}

/**
 * Lists every `.vue` component beneath a directory — the files a RouterLink can be written in.
 * @param {string} directory An absolute directory path.
 * @returns {string[]} Absolute paths of the `.vue` files found beneath it.
 */
const vueFiles = (directory) => {
  const found = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      found.push(...vueFiles(path))
    } else if (entry.name.endsWith('.vue')) {
      found.push(path)
    }
  }
  return found
}

/**
 * Collects every route name some link in the shell targets, from the three places a link is born:
 * `:to="{ name: '…' }"` in components, the navigation composable's entries (its `name:` references
 * resolved through its own consts), and the module landing-route map the sidebar links every
 * reported module through.
 * @param {string[]} problems The shared problem list; each source's vacuity guard reports into it.
 * @returns {Set<string>} The route names at least one link targets.
 */
const collectLinkTargets = (problems) => {
  const targets = new Set()

  // Source 1: RouterLink targets written in components.
  let componentTargets = 0
  for (const file of vueFiles(join(FRONTEND_ROOT, 'src'))) {
    for (const match of readFileSync(file, 'utf8').matchAll(LINK_TARGET)) {
      targets.add(match[1])
      componentTargets++
    }
  }
  // Vacuity guard on the axis that can go blind: a drifted `:to` spelling would leave this scan
  // finding nothing while every page kept rendering, and "all reachable" would then be a claim
  // about a regex, not about the shell.
  if (componentTargets === 0) {
    problems.push('src/**/*.vue: no `:to="{ name: … }"` link found — the link scan measured nothing')
  }

  // Source 2: the navigation composable's own entries, via its route-name consts.
  const navigation = readFileSync(NAVIGATION_FILE, 'utf8')
  const constants = new Map()
  for (const match of navigation.matchAll(NAVIGATION_CONST)) {
    constants.set(match[1], match[2])
  }
  let navigationTargets = 0
  for (const match of navigation.matchAll(NAVIGATION_TARGET)) {
    const resolved = constants.get(match[1])
    if (resolved !== undefined) {
      targets.add(resolved)
      navigationTargets++
    }
  }
  if (navigationTargets === 0) {
    problems.push(
      'src/composables/useNavigation.ts: no `name: SOME_ROUTE` entry resolved through its consts — ' +
        'the navigation scan measured nothing',
    )
  }

  // Source 3: the landing route the sidebar links every reported module through.
  const landingRoutes = readFileSync(LANDING_ROUTES_FILE, 'utf8')
  let landingTargets = 0
  for (const match of landingRoutes.matchAll(LANDING_ROUTE_VALUE)) {
    targets.add(match[1])
    landingTargets++
  }
  if (landingTargets === 0) {
    problems.push(
      'src/router/moduleLandingRoute.ts: no landing route parsed — the landing-route scan measured nothing',
    )
  }

  return targets
}

/**
 * Reads the router and reports every page it cannot reach, and every component it names that is
 * not on disk.
 * @returns {number} The process exit code: 0 when every page is routed.
 */
const main = () => {
  const router = readFileSync(ROUTER_FILE, 'utf8')

  /** Imported component name -> absolute path of the file it was imported from. */
  const imported = new Map()
  for (const match of router.matchAll(VUE_IMPORT)) {
    imported.set(match[1], resolve(dirname(ROUTER_FILE), match[2]))
  }

  const used = new Set()
  for (const match of router.matchAll(ROUTE_COMPONENT)) {
    used.add(match[1])
  }

  const problems = []

  // The paths the router can actually render: imported AND named by a route record. An import that
  // no route uses is exactly as unreachable as no import at all, so both halves are required.
  const routed = new Set()
  for (const [name, path] of imported) {
    if (!existsSync(path)) {
      problems.push(`src/router/index.ts: imports ${name} from a file that does not exist: ${path}`)
      continue
    }
    if (!used.has(name)) {
      problems.push(`src/router/index.ts: imports ${name} and no route record names it`)
      continue
    }
    routed.add(path)
  }

  const pages = pageFiles(PAGES_ROOT)
  for (const page of pages.sort()) {
    if (!routed.has(page)) {
      problems.push(
        `${relative(FRONTEND_ROOT, page)}: no route in src/router/index.ts renders this page — ` +
          'nothing in a browser can reach it',
      )
    }
  }

  // Vacuity guard on the axis that can go blind: this check reports "everything is routed" just as
  // loudly when it read no pages at all (a moved directory, a renamed suffix) as when it read them
  // and they were fine. A run that found no pages, or a router that named no components, has
  // measured nothing and says so.
  if (pages.length === 0) {
    problems.push(`src/pages: no ${PAGE_SUFFIX} components found — this check measured nothing`)
  }
  if (routed.size === 0) {
    problems.push('src/router/index.ts: no page component is imported and routed — check the parser')
  }

  // ——— The sibling property: every routed page is reachable from the shell by links. ———

  const namedRoutes = new Set()
  for (const match of router.matchAll(ROUTE_NAME)) {
    namedRoutes.add(match[1])
  }
  // Vacuity guard: a router whose names this parser cannot read would make every route "reachable"
  // by making the census empty.
  if (namedRoutes.size === 0) {
    problems.push('src/router/index.ts: no named route parsed — the reachability check measured nothing')
  }

  const linkTargets = collectLinkTargets(problems)

  for (const name of [...namedRoutes].sort()) {
    if (linkTargets.has(name) || DELIBERATELY_UNLINKED.has(name)) {
      continue
    }
    problems.push(
      `src/router/index.ts: route '${name}' is routed but UNREACHABLE BY CLICKS — no RouterLink ` +
        'and no navigation entry targets it. Link it from the shell, or declare it in ' +
        'DELIBERATELY_UNLINKED (scripts/check-page-routes.mjs) with the reason.',
    )
  }

  // The exception list cannot rot: an entry must name a real route that is genuinely unlinked.
  for (const [name, reason] of DELIBERATELY_UNLINKED) {
    if (!namedRoutes.has(name)) {
      problems.push(
        `check-page-routes.mjs: DELIBERATELY_UNLINKED names route '${name}' which does not exist — ` +
          'delete the stale exception',
      )
    } else if (linkTargets.has(name)) {
      problems.push(
        `check-page-routes.mjs: DELIBERATELY_UNLINKED names route '${name}' but a link now targets it — ` +
          `the exception ("${reason}") is stale; delete it`,
      )
    }
  }

  for (const problem of problems) {
    process.stdout.write(`${problem}\n`)
  }
  process.stdout.write(
    `check-page-routes: ${pages.length} page components, ${routed.size} routed, ` +
      `${namedRoutes.size} named routes, ${linkTargets.size} link targets, ` +
      `${DELIBERATELY_UNLINKED.size} declared exceptions, ${problems.length} problems\n`,
  )
  process.stdout.write(
    'UNOBSERVED HERE — a link rendered behind a condition no user can satisfy; a navigation entry ' +
      'built but never returned (the audit e2e spec observes the RENDERED sidebar link); and ' +
      'dropdown items that push programmatically (those pages are declared exceptions by name).\n',
  )
  return problems.length === 0 ? 0 : 1
}

process.exitCode = main()
