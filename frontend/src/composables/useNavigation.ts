import { computed, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useCallerRole } from './useCallerRole'
import { useModulesStore } from '../stores/modules'
import { NO_LANDING_ROUTE, moduleLandingRoute } from '../router/moduleLandingRoute'
import { moduleNavigationChildren } from '../router/moduleNavigationChildren'
import { moduleNavigationIcon } from '../utils/moduleNavigationIcon'
import { groupNavigation } from '../utils/groupNavigation'
import type { NavigationEntry } from '../types/navigation'

/**
 * Route name of the always-present system status screen. It ships with the
 * shell itself rather than as a licensed module, so it is not read from the
 * catalogue.
 */
const SYSTEM_STATUS_ROUTE = 'system-status'

/**
 * Route name of the audit journal. Like system status it ships with the shell
 * itself — the journal is the panel-wide record every module writes into — so
 * its entry is the shell's own rather than read from the catalogue.
 */
const AUDIT_ROUTE = 'audit'

/**
 * Route name of the upgrade prompt a locked module's entry points to.
 */
const UPGRADE_ROUTE = 'upgrade'

/**
 * Modules whose entry leads to a screen narrower than the module's own name promises, and the
 * SPA's own label for that screen (issue #209). Where an entry leads is this SPA's fact
 * (`router/moduleLandingRoute.ts`), so naming the destination is too: "Users and access" opens
 * the signed-in devices screen and has no user management behind it, and "Notifications" opens
 * the outgoing-mail settings. Naming the module there would promise a screen that does not exist.
 */
const DESTINATION_LABEL_KEYS: Readonly<Record<string, string>> = {
  identity: 'app.nav.signedInDevices',
  notifications: 'app.nav.outgoingMail',
}

/**
 * Builds the sidebar navigation from the module catalogue: one entry per
 * module the panel reported, ordered by the sidebar's groups, plus the always-present
 * system status entry first. A locked module's entry stays visible and
 * routes to the upgrade page instead of disappearing (rules/architecture.md:
 * "renders the module's entries as locked rather than hiding the product's
 * existence").
 *
 * A module's label is the panel's own, already localized, so no translation key
 * is added here for one. Where a module's entry LEADS is this SPA's own fact and
 * is stated in `router/moduleLandingRoute.ts`, never guessed from its name.
 *
 * A module with more than one page carries them as `children` (issue #195), as declared in
 * `router/moduleNavigationChildren.ts`: a child whose route is not registered is dropped, and an
 * administrator-only child is offered to roles above customer only (advice — the backend still
 * enforces). A section left with a single child is drawn as a plain link: a submenu that repeats
 * its parent and nothing else is noise.
 *
 * @returns A computed list of {@link NavigationEntry} to render in order.
 */
export const useNavigation = (): ComputedRef<NavigationEntry[]> => {
  const { isAdmin } = useCallerRole()
  const modulesStore = useModulesStore()
  const router = useRouter()

  return computed<NavigationEntry[]>(() => {
    const systemStatusEntry: NavigationEntry = {
      key: 'system-status',
      target: { name: SYSTEM_STATUS_ROUTE },
      labelKey: 'app.nav.systemStatus',
      label: null,
      moduleName: null,
      icon: 'pulse',
      locked: false,
      children: [],
    }

    // Whether to offer the administrator-only entries (the audit journal, the plans screen). NOT a
    // gate — the endpoint refuses a customer whatever the sidebar shows — and `useCallerRole` errs
    // permissive, so a role it does not recognise is offered the entry (rules/security.md).
    const offersAdminEntries = isAdmin.value

    const moduleEntries: NavigationEntry[] = modulesStore.modules.flatMap((module) => {
      const landing = moduleLandingRoute(module.name)

      // A module whose interface lives inside another module's screens gets no entry of its own.
      // SSL is the case: its tab is on the site it protects, so there is nowhere for a sidebar
      // entry to lead, and every candidate is worse than none.
      if (module.isEnabled && landing === NO_LANDING_ROUTE) {
        return []
      }

      // The upgrade page answers exactly two questions and no others: a module the licence does
      // not permit, and a module this SPA has no screen for yet. A module that IS licensed and
      // DOES have a screen must never land there — which is what "Users and access" and "SSL
      // certificates" both did, because the entry guessed that a module named `x` has a route
      // named `x` and sent everything else to the upgrade wall.
      const reachable = module.isEnabled && typeof landing === 'string' && router.hasRoute(landing)
      const upgrade = { name: UPGRADE_ROUTE, params: { module: module.name } }

      // The submenu exists only for a usable module: a locked one has a single destination, the
      // upgrade page, and children leading elsewhere would be doors to a wall.
      const children: NavigationEntry[] = reachable
        ? moduleNavigationChildren(module.name).flatMap((child): NavigationEntry[] => {
            if (!router.hasRoute(child.route) || (child.adminOnly && !offersAdminEntries)) {
              return []
            }
            return [
              {
                key: child.key,
                target: { name: child.route },
                labelKey: child.labelKey,
                label: null,
                moduleName: module.name,
                icon: moduleNavigationIcon(module.name),
                locked: false,
                children: [],
              },
            ]
          })
        : []

      const entry: NavigationEntry = {
        key: module.name,
        // Never a route name the router does not know: linking to one throws while rendering
        // the sidebar, which takes the whole shell down with it.
        target: reachable && typeof landing === 'string' ? { name: landing } : upgrade,
        // The label comes from the panel, already localized: the SPA cannot own translations for
        // modules it learns about at runtime. The machine name is the honest fallback.
        // A usable entry whose destination the SPA names itself takes that key instead
        // (DESTINATION_LABEL_KEYS); a locked one keeps the module's name, since it leads to the
        // module's upgrade page rather than to the screen.
        labelKey:
          reachable && Object.hasOwn(DESTINATION_LABEL_KEYS, module.name)
            ? (DESTINATION_LABEL_KEYS[module.name] ?? null)
            : null,
        label: module.displayName ?? module.name,
        moduleName: module.name,
        // The catalogue reports no icon, so this SPA states which glyph each module it knows is
        // drawn with, the same way it states where the module's entry leads. A module it does
        // not know keeps the neutral glyph rather than a guess made from its name.
        icon: moduleNavigationIcon(module.name),
        locked: !module.isEnabled,
        children: children.length > 1 ? children : [],
      }

      return [entry]
    })

    // The journal sits after the module entries: it is the shell's own record OF them — the place
    // an operator goes after something on the screens above did not happen as expected.
    const auditEntry: NavigationEntry[] = offersAdminEntries
      ? [
          {
            key: 'audit-journal',
            target: { name: AUDIT_ROUTE },
            labelKey: 'app.nav.auditJournal',
            label: null,
            moduleName: null,
            icon: 'scrollText',
            locked: false,
            children: [],
          },
        ]
      : []

    // Returned in the expanded sidebar's group order, not catalogue order, so the collapsed rail,
    // the jump-to palette and the tab bar's fill-up all walk the entries the way the sidebar draws
    // them (issue #209): the rail used to list "Users and access" second while the sidebar drew it
    // under System, near the end.
    return groupNavigation([systemStatusEntry, ...moduleEntries, ...auditEntry]).flatMap(
      (group): NavigationEntry[] => {
        return group.entries
      },
    )
  })
}
