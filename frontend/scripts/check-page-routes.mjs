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

  for (const problem of problems) {
    process.stdout.write(`${problem}\n`)
  }
  process.stdout.write(
    `check-page-routes: ${pages.length} page components, ${routed.size} routed, ` +
      `${problems.length} problems\n`,
  )
  return problems.length === 0 ? 0 : 1
}

process.exitCode = main()
