import { computed, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useModulesStore } from '../stores/modules'
import { useTasksStore } from '../stores/tasks'
import { buildNavigation } from '../utils/buildNavigation'
import type { UserRole } from '../types/auth'
import type { NavigationEntry, NavigationRole } from '../types/navigation'

/**
 * The navigation role of a signed-in role. Written as a switch so that a role added to
 * {@link UserRole} (the reseller, issue #179) fails the type check here until it is mapped.
 * @param role The signed-in user's role, or undefined before sign-in has resolved.
 * @returns The role the tree is filtered for; an unknown caller is offered the administrator's menu,
 *   which is advice that errs permissive (rules/security.md) — the endpoints still refuse.
 */
const navigationRoleOf = (role: UserRole | undefined): NavigationRole => {
  switch (role) {
    case 'customer':
      return 'customer'
    case 'admin':
    case undefined:
      return 'admin'
  }
}

/**
 * The shell's navigation for the signed-in caller, built from the navigation tree
 * (`router/navigationTree.ts`) by `utils/buildNavigation.ts` against the caller's role, the module
 * catalogue, the routes this bundle registers and the live counts the shell already holds. The
 * sidebar, rail, drawer, tab bar and jump-to palette all render this one list.
 * @returns A computed list of {@link NavigationEntry} in drawing order.
 */
export const useNavigation = (): ComputedRef<NavigationEntry[]> => {
  const authStore = useAuthStore()
  const modulesStore = useModulesStore()
  const tasksStore = useTasksStore()
  const router = useRouter()

  return computed<NavigationEntry[]>(() => {
    return buildNavigation({
      role: navigationRoleOf(authStore.user?.role),
      catalogue: modulesStore.modules,
      modules: new Map(
        modulesStore.modules.map((module): [string, boolean] => {
          return [module.name, module.isEnabled]
        }),
      ),
      // The panel reports no plan features to the shell yet, so every item is offered.
      planFeatures: null,
      badges: { runningTasks: tasksStore.runningCount },
      hasRoute: (name: string): boolean => {
        return router.hasRoute(name)
      },
    })
  })
}
