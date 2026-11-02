import type { NavigationEntry } from '../types/navigation'

/**
 * The pages of the navigation a person can open right now, flat and in drawing order: a link
 * entry is itself a page, a section contributes its pages. A planned page leads nowhere and a
 * locked one leads to the upgrade screen, so neither is a page to pin.
 * @param entries The navigation as `useNavigation` built it.
 * @returns Every openable page entry.
 */
export const openNavigationPages = (entries: readonly NavigationEntry[]): NavigationEntry[] => {
  const pages: NavigationEntry[] = []
  for (const entry of entries) {
    if (entry.children.length > 0) {
      pages.push(...openNavigationPages(entry.children))
    } else if (entry.state.kind === 'open') {
      pages.push(entry)
    }
  }
  return pages
}
