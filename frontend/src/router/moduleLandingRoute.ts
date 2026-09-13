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
 *
 * That fix repaired two modules and left the third: `notifications` was still absent from the map
 * below, so the panel told an operator that the Notifications module was not covered by their
 * licence and, in the next sentence, that its licence tier was "included in the distribution" —
 * selling, in one breath, what it had just called bundled. `GET /api/v1/modules` reported it
 * `included` and `isEnabled`, and its screen worked the whole time.
 *
 * Adding a key would fix that module and nothing else, so the class is closed by a check
 * instead: `e2e/shell/module-landing-coverage.spec.ts` reads the module ids the backend actually
 * composes — out of `Maran.Host/Modules/ModuleRegistry.cs` and each module's own `*Manifest.cs`,
 * which is where `GET /api/v1/modules` gets them — serves that exact catalogue to a real browser,
 * and follows every sidebar entry to the screen it lands on. A module added on the backend with no
 * entry here fails that spec on the day it is registered, with nobody having remembered anything.
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
  // The merged File transfer screen. Both transfer modules land on it, and only the `sftp` key
  // carries a route: SFTP is the half that is always present, so gating the screen on `ftp` would
  // hide the SFTP logins on a panel without FTPS.
  sftp: 'file-transfer',
  // No page of its own by design: the FTPS module's whole interface — the login rows, the create
  // form's second protocol and the administrator's daemon panel — lives inside the File transfer
  // screen the `sftp` key names. A sidebar entry for it would be a second door to one room.
  ftp: NO_LANDING_ROUTE,
  firewall: 'firewall',
  // The Identity module's own screens are the signed-in user's security ones. Sessions is the
  // first of them and the one that answers "who is in my panel right now".
  identity: 'sessions',
  cron: 'cron',
  tasks: 'tasks',
  monitoring: 'monitoring',
  // The Notifications module's own screen is the outgoing-mail settings page: what the module does
  // for an operator is send mail, and where it sends it from is the only thing about it there is
  // to configure. The account menu reaches the same page; a sidebar entry is not a duplicate of
  // that, it is where an operator looks when they are thinking about the module rather than about
  // their own account.
  notifications: 'smtp-settings',
  backups: 'backups',
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
