/**
 * Where the navigation entries the shell owns itself lead.
 *
 * Most entries come from the module catalogue and lead where `moduleLandingRoute.ts` says. These do
 * not: system status, the audit journal and the security policy ship with the shell rather than as a
 * licensed module, so the catalogue never reports them. Stated here once, so the sidebar that draws
 * them (`useNavigation`) and the breadcrumb and page eyebrow that place a screen under them
 * (`utils/locateNavigation.ts`) read one map rather than each keeping its own (issue #209).
 */

import type { ShellEntryKey } from '../types/navigation'

/** Shell entry key to the route it leads to. */
export const SHELL_ENTRY_ROUTES: Readonly<Record<ShellEntryKey, string>> = {
  'system-status': 'system-status',
  'audit-journal': 'audit',
  'security-policy': 'security-policy',
}

/**
 * Answers which shell entry a route is the destination of.
 * @param routeName The route's name.
 * @returns The shell entry's key, or `undefined` when no shell entry leads there.
 */
export const shellEntryOfRoute = (routeName: string): ShellEntryKey | undefined => {
  return (Object.keys(SHELL_ENTRY_ROUTES) as ShellEntryKey[]).find((key): boolean => {
    return SHELL_ENTRY_ROUTES[key] === routeName
  })
}
