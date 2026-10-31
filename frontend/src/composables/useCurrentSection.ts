import { useRoute, useRouter } from 'vue-router'
import type { NavigationEntry } from '../types/navigation'

/**
 * The one rule for lighting a navigation section's own row (issue #221): a section is current
 * when the current page is any of its children's pages, or anything under a child's path
 * (`/accounts/42` is inside "Account list"). The router's own match cannot say this, because it
 * follows route nesting and the menu does not: Plans is a child of Accounts in the menu but not
 * under `/accounts` in the router. The sidebar and the collapsed rail both ask this, so a section
 * is lit the same way in either presentation.
 * @returns A test answering whether an entry's section holds the current page; an entry without
 * children is never a section and always answers false.
 */
export const useCurrentSection = (): ((entry: NavigationEntry) => boolean) => {
  const route = useRoute()
  const router = useRouter()

  /**
   * Answers whether the current route lies inside one of a section's pages.
   * @param entry The section to test.
   * @returns True when a child's path is the current path or a prefix of it.
   */
  const holdsCurrentRoute = (entry: NavigationEntry): boolean => {
    return entry.children.some((child): boolean => {
      const path = router.resolve(child.target).path
      return route.path === path || route.path.startsWith(`${path}/`)
    })
  }

  return holdsCurrentRoute
}
