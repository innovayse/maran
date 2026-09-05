/**
 * Where a module's own screen lives in this SPA, for the modules this SPA has screens for.
 *
 * A module is one shippable unit on the backend, but its interface is not: every module's screens
 * are written in the core SPA's flat structure and compiled into the single bundle
 * (rules/architecture.md, "Where a module's UI lives"). So which route presents a module is a fact
 * about this SPA's own router, and it is stated here, once, rather than guessed from the module's
 * machine name.
 *
 * Guessing is what this replaces, and it was wrong in two directions at once. The sidebar assumed
 * a module named `x` had a route named `x`, and sent every module without one to `/upgrade/<name>`
 * — so `identity` and `ssl`, which `GET /api/v1/modules` reports as included and enabled, both
 * linked an operator to a wall demanding they buy something they already have. For `ssl` it was
 * wrong twice over: the feature exists, works, and lives inside the site page.
 */

/**
 * Marks a module whose interface lives inside another module's screens rather than on a page of
 * its own, so it contributes no sidebar entry at all.
 *
 * Distinct from "this SPA has no screen for it yet", which is what an ABSENT entry means and which
 * the upgrade page is the honest answer to.
 */
export const NO_LANDING_ROUTE = null

/**
 * Module machine name to the route name that presents it.
 *
 * Keyed on `ModuleDto.Name`, which is the module's stable machine name and equals its PostgreSQL
 * schema — never on its display name, which the backend localizes per request.
 */
const LANDING_ROUTES: Readonly<Record<string, string | typeof NO_LANDING_ROUTE>> = {
  accounts: 'accounts',
  sites: 'sites',
  databases: 'databases',
  sftp: 'sftp-users',
  firewall: 'firewall',
  // The Identity module's own screens are the signed-in user's security ones. Sessions is the
  // first of them and the one that answers "who is in my panel right now".
  identity: 'sessions',
  cron: 'cron',
  tasks: 'tasks',
  monitoring: 'monitoring',
  // No page of its own by design: a certificate belongs to a site, so the SSL module's interface
  // is a tab on the site it protects. A sidebar entry for it would have to lead somewhere, and
  // every somewhere is worse than nowhere: a list of every certificate on the server is a screen
  // nobody asked for, and an upgrade wall is a lie about a module the licence includes.
  ssl: NO_LANDING_ROUTE,
}

/**
 * Answers where a module's own screen is, for a module this SPA has one for.
 * @param moduleName The module's stable machine name, as `GET /api/v1/modules` reports it.
 * @returns The route name presenting it, `null` when it deliberately has no screen of its own, and
 * `undefined` when this SPA knows nothing about it — three different answers the caller acts on
 * differently.
 */
export const moduleLandingRoute = (moduleName: string): string | null | undefined => {
  return Object.hasOwn(LANDING_ROUTES, moduleName) ? LANDING_ROUTES[moduleName] : undefined
}
