import { computed, watch, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { useModulesStore } from '../stores/modules'
import { useNavigationBadgesStore } from '../stores/navigationBadges'
import { useTasksStore } from '../stores/tasks'
import { buildNavigation } from '../utils/buildNavigation'
import { useNavigationRole } from './useNavigationRole'
import type { AccountFeatureName } from '../types/accountFeature'
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
  const authStore = useAuthStore()
  const accountsStore = useAccountsStore()
  const tasksStore = useTasksStore()
  const badgesStore = useNavigationBadgesStore()
  const router = useRouter()

  // A customer's menu follows their plan, so the shell reads their own account once they are signed
  // in. An administrator owns none and is never asked. A failed read leaves the features unknown,
  // which offers every item: hiding is advice, and the endpoints refuse regardless.
  watch(
    () => {return authStore.user?.accountId ?? null},
    (accountId) => {
      if (accountId !== null) {
        void accountsStore.loadMine()
      }
    },
    { immediate: true },
  )

  /**
   * The features of the signed-in customer's plan, or null when they are not known: an
   * administrator, a read still on its way or failed, and an account read left over from somebody
   * who signed in before (its id is not this caller's).
   */
  const planFeatures = computed<ReadonlySet<AccountFeatureName> | null>(() => {
    const mine = accountsStore.mine
    if (mine === null || mine.id !== authStore.user?.accountId || mine.plan.features === undefined) {
      return null
    }
    return new Set(mine.plan.features)
  })

  return computed<NavigationEntry[]>(() => {
    return buildNavigation({
      role: role.value,
      catalogue: modulesStore.modules,
      modules: new Map(
        modulesStore.modules.map((module): [string, boolean] => {
          return [module.name, module.isEnabled]
        }),
      ),
      planFeatures: planFeatures.value,
      // Each count is present only when the panel answered with a real one: a kind still loading,
      // refused or failed is left out, so it draws no badge (never a zero standing in for it). The
      // failed count is read once by the shell (`DefaultLayout`); the others by the badges store.
      badges: {
        ...badgesStore.counts,
        ...(tasksStore.failures.state === 'answered' ? { failedTasks: tasksStore.failures.count } : {}),
      },
      hasRoute: (name: string): boolean => {
        return router.hasRoute(name)
      },
    })
  })
}
