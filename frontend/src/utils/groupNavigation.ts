import { FALLBACK_GROUP, NAVIGATION_TREE } from '../router/navigationTree'
import type { NavigationEntry } from '../types/navigation'
import type { NavigationGroup } from '../types/navigationGroup'

/**
 * Groups in the order they are drawn: the tree's own, then the fallback group for catalogue
 * modules the tree does not know, last so a known item is never pushed below an unknown one.
 */
const GROUP_ORDER: readonly string[] = [
  ...NAVIGATION_TREE.map((group): string => {
    return group.key
  }),
  FALLBACK_GROUP,
]

/**
 * Splits the navigation entries into the labelled groups the expanded sidebar draws.
 *
 * Every entry appears exactly once, inside the group it was built for and in drawing order.
 * Groups with no entries are omitted: a customer's menu has no infrastructure, and an empty label
 * over nothing would read as a bug.
 * @param entries The entries `useNavigation` built.
 * @returns The non-empty groups, in drawing order.
 */
export const groupNavigation = (entries: readonly NavigationEntry[]): NavigationGroup[] => {
  return GROUP_ORDER.flatMap((key): NavigationGroup[] => {
    const members = entries.filter((entry): boolean => {
      return entry.groupKey === key
    })
    return members.length === 0 ? [] : [{ key, labelKey: `app.nav.groups.${key}`, entries: members }]
  })
}
