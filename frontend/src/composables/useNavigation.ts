import { computed, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useModulesStore } from '../stores/modules'
import { useTasksStore } from '../stores/tasks'
import { buildNavigation } from '../utils/buildNavigation'
import { useNavigationRole } from './useNavigationRole'
import type { NavigationEntry } from '../types/navigation'

/**
 * The shell's navigation for the signed-in caller, built from the navigation tree
 * (`router/navigationTree.ts`) by `utils/buildNavigation.ts` against the caller's role, the module
 * catalogue, the routes this bundle registers and the live counts the shell already holds. The
 * sidebar, rail, drawer, tab bar and jump-to palette all render this one list.
 * @returns A computed list of {@link NavigationEntry} in drawing order.
 */
export const useNavigation = (): ComputedRef<NavigationEntry[]> => {
  const role = useNavigationRole()
  const modulesStore = useModulesStore()
  const tasksStore = useTasksStore()
  const router = useRouter()

  return computed<NavigationEntry[]>(() => {
    return buildNavigation({
      role: role.value,
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
