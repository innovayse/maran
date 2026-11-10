import { computed, onBeforeUnmount, watch, type ComputedRef } from 'vue'
import { useLocaleStore } from '../stores/locale'
import { useAuthStore } from '../stores/auth'
import { useDashboardStore } from '../stores/dashboard'
import { useLiveDashboardStore } from '../stores/liveDashboard'
import { useNavigationBadgesStore } from '../stores/navigationBadges'
import { useTasksStore } from '../stores/tasks'
import { useCallerRole } from './useCallerRole'

/**
 * Owns the lifetime of everything the shell polls or streams on its own, in one place: the running
 * tasks stream (the tray), the failed-task count, and the needs-attention, pending-updates and
 * mail-queue figures behind the navigation's badges.
 *
 * **Which role may call what** (read from the endpoints' policies, issue #415):
 *
 * | read | administrator | reseller | customer |
 * |---|---|---|---|
 * | `GET /api/v1/tasks/running/stream`, `GET /api/v1/tasks/failed-count` | yes | no | yes |
 * | `GET /api/v1/dashboard/attention-count`, `GET /api/v1/monitoring/updates/count`, `GET /api/v1/mail-queue/count` | yes | no | no |
 *
 * A reseller is asked for nothing because every one of these reads is empty for it: the task reads
 * answer 200 with no rows (the tasks module's query filter shows a reseller none), and the
 * server-wide figures are administrator-only (403). The matrix is advice: the panel
 * refuses by role on every request regardless (rules/security.md item 6), so a wrong entry here
 * costs a refused request in the console, never a leak. A role not named here (one added to
 * `UserRole` later) is asked for nothing.
 *
 * **The identity owns the lifetimes.** Everything started here belongs to the person (and the role,
 * and the account) the session currently is. When that changes (a sign-out, a sign-in as someone
 * else, "View as this account" starting or ending) the tray is closed, the timers stop, in-flight
 * reads are abandoned and every held figure is forgotten, and only then are the reads the NEW
 * identity may make started. So an administrator who starts viewing as a customer does not carry a
 * badge, a poll or a stream over into the customer's shell, nor the landing screen's answer: the
 * dashboard read and its live stream are forgotten with the rest (issue #513). The same teardown runs
 * when the shell unmounts.
 *
 * Call it once, from the layout that is the shell.
 * @returns Nothing.
 */
export const useShellLiveReads = (): void => {
  const authStore = useAuthStore()
  const localeStore = useLocaleStore()
  const tasksStore = useTasksStore()
  const badgesStore = useNavigationBadgesStore()
  const dashboardStore = useDashboardStore()
  const liveDashboardStore = useLiveDashboardStore()
  const { isAdmin, isCustomer } = useCallerRole()

  /** Whether the caller's role may read the running-tasks stream and the failed-task count. */
  const readsTasks: ComputedRef<boolean> = computed(() => {
    return isAdmin.value || isCustomer.value
  })

  /** Whether the caller's role may read the server-wide needs-attention, updates and mail-queue figures. */
  const readsServerFigures: ComputedRef<boolean> = computed(() => {
    return isAdmin.value
  })

  /**
   * What identifies the session the reads belong to, or `null` when nobody is signed in. A view-as
   * session is another user with another role, so it changes the key like any sign-in does.
   */
  const identity: ComputedRef<string | null> = computed(() => {
    const user = authStore.user
    return user === null ? null : `${user.id}:${user.role}:${user.accountId ?? ''}`
  })

  /**
   * Drops everything the previous identity had running or held.
   * @returns Nothing.
   */
  const stopAll = (): void => {
    tasksStore.reset()
    badgesStore.stop()
    // The landing screen's answer and its stream are the previous identity's too: a page mounted
    // for the next one must start from "not read yet", or it believes the administrator's answer is
    // its own and asks for the stream and the checklist the panel refuses it (issue #513).
    liveDashboardStore.close()
    dashboardStore.reset()
  }

  /**
   * Starts exactly the reads the current role may make.
   * @returns Nothing.
   */
  const startAllowed = (): void => {
    if (readsTasks.value) {
      tasksStore.openTray()
      void tasksStore.loadFailures()
    }
    if (readsServerFigures.value) {
      badgesStore.start()
    }
  }

  watch(
    identity,
    (current: string | null): void => {
      stopAll()
      if (current !== null) {
        startAllowed()
      }
    },
    { immediate: true },
  )

  // The frames carry backend-localized names, resolved when the stream opens, so a language switch
  // reopens it, for the roles that hold one.
  watch(
    (): string => {
      return localeStore.current
    },
    (): void => {
      if (identity.value !== null && readsTasks.value) {
        tasksStore.reopenTray()
      }
    },
  )

  // The stores outlive the shell: a stream or timer nobody stops keeps running after sign-out.
  onBeforeUnmount(stopAll)
}
