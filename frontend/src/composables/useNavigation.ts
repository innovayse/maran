import { computed, watch, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useAccountAccessStore } from '../stores/accountAccess'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { useModulesStore } from '../stores/modules'
import { useNavigationBadgesStore } from '../stores/navigationBadges'
import { useTasksStore } from '../stores/tasks'
import { buildNavigation } from '../utils/buildNavigation'
import { useNavigationRole } from './useNavigationRole'
import type { ModuleState } from '../types/module'
import type { NavigationAccountAccess, NavigationEntry, NavigationPlanFeatures } from '../types/navigation'

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
  const accessStore = useAccountAccessStore()
  const tasksStore = useTasksStore()
  const badgesStore = useNavigationBadgesStore()
  const router = useRouter()

  // A customer's menu follows their plan and, for a member, what the owner granted, so the shell
  // reads their own account and their standing on it once they are signed in, and again when the
  // account they act for changes. An administrator owns none and is never asked. A failed read
  // leaves the features unknown, which offers every item: hiding is advice, and the endpoints
  // refuse regardless.
  watch(
    () => {return authStore.user?.accountId ?? null},
    (accountId) => {
      if (accountId === null) {
        accessStore.clear()
        return
      }
      void accountsStore.loadMine()
      // Operators hold no grants on an account they view: only a customer's standing is asked for.
      if (role.value === 'customer') {
        void accessStore.load(accountId)
      }
    },
    { immediate: true },
  )

  /**
   * The features of the signed-in customer's plan, or unreported when they are not known: an
   * administrator, a read still on its way or failed, and an account read left over from somebody
   * who signed in before (its id is not this caller's).
   */
  const planFeatures = computed<NavigationPlanFeatures>(() => {
    const mine = accountsStore.mine
    if (mine === null || mine.id !== authStore.user?.accountId || mine.plan.features === undefined) {
      return { state: 'unreported' }
    }
    return { state: 'reported', features: new Set(mine.plan.features) }
  })

  /**
   * The caller's standing on the account they act for: owner, or a member with the features the
   * owner granted. Unreported for an administrator, a read still on its way or failed, a login that
   * acts for no account, and a standing read for another account than the one now active.
   */
  const accountAccess = computed<NavigationAccountAccess>(() => {
    const held = accessStore.held
    const current = role.value === 'customer' && held?.accountId === authStore.user?.accountId
    if (!current || held === null || held.access === null) {
      return { state: 'unreported' }
    }
    return held.access.role === 'owner'
      ? { state: 'owner' }
      : { state: 'member', features: new Set(held.access.features) }
  })

  return computed<NavigationEntry[]>(() => {
    return buildNavigation({
      role: role.value,
      catalogue: modulesStore.modules,
      modules: new Map(
        modulesStore.modules.map((module): [string, ModuleState] => {
          return [module.name, module.state]
        }),
      ),
      planFeatures: planFeatures.value,
      accountAccess: accountAccess.value,
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
