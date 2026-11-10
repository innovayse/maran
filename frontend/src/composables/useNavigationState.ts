import { computed, watch, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'
import { useAccountAccessStore } from '../stores/accountAccess'
import { useAccountsStore } from '../stores/accounts'
import { useAuthStore } from '../stores/auth'
import { useModulesStore } from '../stores/modules'
import { useNavigationBadgesStore } from '../stores/navigationBadges'
import { useStackStore } from '../stores/stack'
import { useTasksStore } from '../stores/tasks'
import { buildNavigation } from '../utils/buildNavigation'
import { useNavigationRole } from './useNavigationRole'
import { useViewingAs } from './useViewingAs'
import type { ModuleState } from '../types/module'
import type { MyAccount } from '../types/account'
import type { HeldAccountAccess } from '../types/profile'
import type {
  NavigationAccountAccess,
  NavigationGrants,
  NavigationPlanFeatures,
  NavigationRuntime,
  NavigationRuntimes,
  NavigationState,
} from '../types/navigation'

/**
 * The state of the shell's navigation for the signed-in caller. Once ready it carries the entries
 * built from the navigation tree (`router/navigationTree.ts`) by `utils/buildNavigation.ts` against
 * the caller's role, the module catalogue, the routes this bundle registers and the live counts the
 * shell already holds; the sidebar, rail, drawer, tab bar and jump-to palette all render that one
 * list. It is `loading`, with no entries, only while a customer's first answer for the account they
 * act for is on its way; operators, logins with no account and failed reads are always `ready`.
 * @returns A computed {@link NavigationState}: loading, or ready with the entries in drawing order.
 */
export const useNavigationState = (): ComputedRef<NavigationState> => {
  const role = useNavigationRole()
  const modulesStore = useModulesStore()
  const authStore = useAuthStore()
  const accountsStore = useAccountsStore()
  const accessStore = useAccountAccessStore()
  const tasksStore = useTasksStore()
  const badgesStore = useNavigationBadgesStore()
  const stackStore = useStackStore()
  const router = useRouter()
  const viewingAs = useViewingAs()

  /**
   * What the plan read says about features, for the account the caller acts for.
   * @param mine The plan read as held, or `null` when the caller owns no account or the read failed.
   * @param active The account the caller acts for.
   * @returns Reported features only when the read is for the active account and lists them; otherwise unreported.
   */
  const planFeaturesOf = (mine: MyAccount | null, active: string): NavigationPlanFeatures => {
    if (mine === null || mine.id !== active || mine.plan.features === undefined) {
      return { state: 'unreported' }
    }
    return { state: 'reported', features: new Set(mine.plan.features) }
  }

  /**
   * What the caller's standing on the account says about what a member may reach.
   * @param held The standing as held, or `null` before any answer.
   * @param customer True when the caller is a customer; an operator's standing is never asked for.
   * @returns The owner, a member with their granted features, or unreported when nothing is known.
   */
  const accountAccessOf = (held: HeldAccountAccess | null, customer: boolean): NavigationAccountAccess => {
    if (!customer || held === null || held.access === null) {
      return { state: 'unreported' }
    }
    if (held.access.role === 'owner') {
      return { state: 'owner' }
    }
    return { state: 'member', features: new Set(held.access.features) }
  }

  /**
   * Which runtimes the stack status says the host has.
   * @returns Reported runtimes once the panel answered, otherwise unreported (every runtime page is offered).
   */
  const runtimesOf = (): NavigationRuntimes => {
    const php = stackStore.phpVersions
    if (php === null) {
      return { state: 'unreported' }
    }
    const installed = new Set<NavigationRuntime>()
    if (php.length > 0) {
      installed.add('php')
    }
    return { state: 'reported', installed }
  }

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
   * The grants the menu is drawn from. They follow the account the caller acts for, but only once
   * BOTH reads have settled (arrived, or failed, which leaves them unreported and so offers every
   * item). A plan read that ended for another account than the active one counts as settled with
   * no answer, never as pending. While either is still on its way the previous grants are kept as they
   * were, so a switch of account never draws a menu from the plan of one account and the grants of
   * the other, and never from neither. For a login that acts for no account nothing is reported:
   * every item is offered, since hiding is advice. `null` means the first answer for the account is
   * still on its way and there are no earlier grants to keep, which a customer's navigation reports
   * as `loading` (an operator's reads `null` as nothing reported): a switch of account reloads the panel
   * (`ShellAccountSelector`), so the page that asks for the new account's standing starts with none.
   */
  const grants = computed<NavigationGrants | null>((previous) => {
    const unreported: NavigationGrants = {
      planFeatures: { state: 'unreported' },
      accountAccess: { state: 'unreported' },
    }
    const active = authStore.user?.accountId ?? null
    if (active === null) {
      // Nobody acts for an account: nothing carried over from a previous sign-in may colour the menu.
      return unreported
    }
    const mine = accountsStore.mine
    const held = accessStore.held
    const customer = role.value === 'customer'
    // Pending means only that a read is in flight. A plan read that has ended, for whatever account
    // (or none), is an answer: if it is not for the active account it says nothing and offers every
    // item, rather than holding the menu on stale grants for good. An operator's standing is never
    // asked for, so only a customer waits on it.
    const planSettled = accountsStore.mineState !== 'loading'
    const accessSettled = !customer || held?.accountId === active
    if (!planSettled || !accessSettled) {
      return previous ?? null
    }
    return {
      planFeatures: planFeaturesOf(mine, active),
      accountAccess: accountAccessOf(held, customer),
    }
  })

  return computed<NavigationState>(() => {
    const known = grants.value
    // A customer's first answer is still on its way: offering every item now would flash a menu the
    // answer then shrinks, so the state is `loading` until it arrives (a failed read settles it as
    // unreported and offers everything). An operator's menu never waits on a customer's standing.
    if (known === null && role.value === 'customer') {
      return { state: 'loading' }
    }
    const { planFeatures, accountAccess } = known ?? {
      planFeatures: { state: 'unreported' as const },
      accountAccess: { state: 'unreported' as const },
    }
    const entries = buildNavigation({
      role: role.value,
      viewingAs: viewingAs.value,
      catalogue: modulesStore.modules,
      modules: new Map(
        modulesStore.modules.map((module): [string, ModuleState] => {
          return [module.name, module.state]
        }),
      ),
      planFeatures,
      accountAccess,
      runtimes: runtimesOf(),
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
    return { state: 'ready', entries }
  })
}
