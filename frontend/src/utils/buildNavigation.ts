import type { RouteLocationRaw } from 'vue-router'
import { NO_LANDING_ROUTE, moduleLandingRoute } from '../router/moduleLandingRoute'
import { FALLBACK_GROUP, MODULES_WITHOUT_ITEM, NAVIGATION_TREE } from '../router/navigationTree'
import type {
  NavigationContext,
  NavigationEntry,
  NavigationEntryState,
  NavigationIcon,
  NavigationNodeSpec,
  NavigationPageSpec,
} from '../types/navigation'

/** Route name of the upgrade prompt a locked module's pages lead to. */
const UPGRADE_ROUTE = 'upgrade'

/** Every module the tree names, so the catalogue fallback can tell a known module from a new one. */
const TREE_MODULES: ReadonlySet<string> = new Set(
  NAVIGATION_TREE.flatMap((group): NavigationPageSpec[] => {
    return group.nodes.flatMap((node): NavigationPageSpec[] => {
      return node.kind === 'link' ? [node] : [...node.children]
    })
  }).flatMap((page): string[] => {
    return page.module === null ? [] : [page.module]
  }),
)

/**
 * The upgrade page of a module.
 * @param module The module's machine name.
 * @returns The upgrade route with the module as its parameter.
 */
const upgradeOf = (module: string): RouteLocationRaw => {
  return { name: UPGRADE_ROUTE, params: { module } }
}

/**
 * Where a page leads for this caller, or null when the page is not offered to them at all.
 * @param page The page from the tree.
 * @param context What the shell knows about the caller and the panel.
 * @returns The page's state, or null when it is hidden.
 */
const stateOf = (page: NavigationPageSpec, context: NavigationContext): NavigationEntryState | null => {
  // Advice, never a gate: the endpoints refuse a caller whatever the menu offers. An unreported
  // plan-feature list offers the page rather than hiding it.
  const featureOffered =
    page.planFeature === null || context.planFeatures === null || context.planFeatures.has(page.planFeature)
  if (!page.roles.includes(context.role) || !featureOffered) {
    return null
  }
  if (page.availability.state === 'planned') {
    return { kind: 'planned', issue: page.availability.issue }
  }
  if (page.module !== null) {
    const enabled = context.modules.get(page.module)
    // A module the catalogue does not report is not installed: nothing of it is offered.
    if (enabled === undefined) {
      return null
    }
    if (!enabled) {
      return { kind: 'locked', target: upgradeOf(page.module) }
    }
  }
  // Never a route name the router does not know: linking to one throws while rendering the shell.
  if (!context.hasRoute(page.availability.route)) {
    return null
  }
  return { kind: 'open', target: { name: page.availability.route } }
}

/**
 * Builds the entry for one page.
 * @param page The page from the tree.
 * @param groupKey The group its top-level item is drawn under.
 * @param icon The glyph of its top-level item.
 * @param context What the shell knows about the caller and the panel.
 * @returns The entry, or null when the page is hidden from this caller.
 */
const pageEntry = (
  page: NavigationPageSpec,
  groupKey: string,
  icon: NavigationIcon,
  context: NavigationContext,
): NavigationEntry | null => {
  const state = stateOf(page, context)
  if (state === null) {
    return null
  }
  const count = page.badge === null ? 0 : context.badges[page.badge]
  const badge = page.badge !== null && count > 0 ? { kind: page.badge, count } : null
  return {
    key: page.key,
    groupKey,
    state,
    label: { kind: 'key', key: page.labelKey },
    icon,
    badge,
    children: [],
  }
}

/**
 * The state a section's own row takes: its first usable page; else the first locked one, whose
 * upgrade page is the only door there is; else planned, since nothing in it is built yet.
 * @param children The section's offered pages, in order.
 * @returns The section row's state.
 */
const sectionStateOf = (children: readonly [NavigationEntry, ...NavigationEntry[]]): NavigationEntryState => {
  const open = children.find((child): boolean => {
    return child.state.kind === 'open'
  })
  const locked = children.find((child): boolean => {
    return child.state.kind === 'locked'
  })
  return open?.state ?? locked?.state ?? children[0].state
}

/**
 * Builds the entry for one top-level item.
 * @param node The item from the tree.
 * @param groupKey The group it is drawn under.
 * @param context What the shell knows about the caller and the panel.
 * @returns The entry in a list of one, or an empty list when nothing of it is offered.
 */
const nodeEntries = (node: NavigationNodeSpec, groupKey: string, context: NavigationContext): NavigationEntry[] => {
  if (node.kind === 'link') {
    const entry = pageEntry(node, groupKey, node.icon, context)
    return entry === null ? [] : [entry]
  }
  const children = node.children.flatMap((child): NavigationEntry[] => {
    const entry = pageEntry(child, groupKey, node.icon, context)
    return entry === null ? [] : [entry]
  })
  const [first, ...rest] = children
  if (first === undefined) {
    return []
  }
  const state = sectionStateOf([first, ...rest])
  // The section repeats its badged page's count, so it is seen with the submenu closed. The tree
  // gives a section at most one badged page, so there is nothing to add up.
  const badge =
    children.find((child): boolean => {
      return child.badge !== null
    })?.badge ?? null
  // A submenu needs a second page: a section left with one page is that page under the section's
  // name. A section whose usable pages are all locked is one row to its upgrade page rather than a
  // list of doors to the same wall. A section with nothing built yet still opens, every page in it
  // drawn disabled, so the menu shows what is coming.
  const submenu = children.length > 1 && state.kind !== 'locked'
  return [
    {
      key: node.key,
      groupKey,
      state,
      label: { kind: 'key', key: node.labelKey },
      icon: node.icon,
      badge,
      children: submenu ? children : [],
    },
  ]
}

/**
 * Entries for catalogue modules the tree does not know: a module newer than this bundle must still
 * be reachable, so it is drawn under the fallback group with the panel's own localized name.
 * @param context What the shell knows about the caller and the panel.
 * @returns One entry per unknown module, in catalogue order.
 */
const catalogueEntries = (context: NavigationContext): NavigationEntry[] => {
  return context.catalogue.flatMap((module): NavigationEntry[] => {
    // A module without an item of its own still shows while the licence does not permit it: a
    // locked module stays visible and leads to its upgrade page, rather than hiding the product.
    if (TREE_MODULES.has(module.name) || (module.isEnabled && MODULES_WITHOUT_ITEM.has(module.name))) {
      return []
    }
    const landing = moduleLandingRoute(module.name)
    if (module.isEnabled && landing === NO_LANDING_ROUTE) {
      return []
    }
    const reachable = module.isEnabled && typeof landing === 'string' && context.hasRoute(landing)
    return [
      {
        key: module.name,
        groupKey: FALLBACK_GROUP,
        state:
          reachable && typeof landing === 'string'
            ? { kind: 'open', target: { name: landing } }
            : { kind: 'locked', target: upgradeOf(module.name) },
        label: { kind: 'text', text: module.displayName ?? module.name },
        icon: 'grid',
        badge: null,
        children: [],
      },
    ]
  })
}

/**
 * Builds the navigation for one caller from the tree (`router/navigationTree.ts`): the tree filtered
 * by role and plan feature, each page resolved against the module catalogue and the router, then
 * any catalogue module the tree does not know. Pure, so the role filter is the same function for
 * every role.
 * @param context What the shell knows about the caller and the panel.
 * @returns The entries in drawing order.
 */
export const buildNavigation = (context: NavigationContext): NavigationEntry[] => {
  const treeEntries = NAVIGATION_TREE.flatMap((group): NavigationEntry[] => {
    return group.nodes.flatMap((node): NavigationEntry[] => {
      return nodeEntries(node, group.key, context)
    })
  })
  return [...treeEntries, ...catalogueEntries(context)]
}
