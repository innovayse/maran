import type { NavigationIcon } from '../types/navigation'

/**
 * Which glyph a module's sidebar entry is drawn with.
 *
 * The catalogue reports no icon, and it never will: an icon is not a fact about the module, it is
 * a fact about how this SPA draws it — the same kind of fact as `router/moduleLandingRoute.ts`,
 * which states where a module's entry leads. So it is stated here, once, rather than guessed from
 * the module's name or left the same for every module.
 *
 * Leaving it the same is what this replaces: every module took the neutral four-tile glyph, so
 * "Users and access", "Accounts" and "Sites" drew one identical mark in a column of three rows and
 * the icons told the reader nothing they could not already read in the label. An icon that cannot
 * be distinguished from its neighbours is decoration paid for in vertical space.
 */

/**
 * Module machine name to the glyph its entry is drawn with.
 *
 * Keyed on `ModuleDto.Name`, the module's stable machine name — never on its display name, which
 * the backend localizes per request. A module absent from this map keeps the neutral glyph, which
 * is the honest answer for a module this bundle was built before.
 */
const MODULE_ICONS: Readonly<Record<string, NavigationIcon>> = {
  accounts: 'users',
  sites: 'earth',
  databases: 'database',
  // A folder with a key rather than a second generic transfer glyph: an SFTP login opens one
  // account's directory and nothing else on the host, which is the fact worth drawing.
  sftp: 'folderKey',
  // The Identity module's entry leads to the signed-in person's security screens (sessions,
  // two-factor), so it is marked as protection rather than as people.
  identity: 'shieldCheck',
}

/**
 * Answers which glyph a module's sidebar entry is drawn with.
 * @param moduleName The module's stable machine name, as `GET /api/v1/modules` reports it.
 * @returns The module's own glyph, or the neutral module glyph when this bundle knows none.
 */
export const moduleNavigationIcon = (moduleName: string): NavigationIcon => {
  return MODULE_ICONS[moduleName] ?? 'grid'
}
