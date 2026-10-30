<script setup lang="ts">
/**
 * The shell's expanded navigation: the collapse control, the backend-driven entries under small
 * group labels, and the signed-in person's identity card at the bottom.
 *
 * Two presentations from one component, because the markup inside is identical:
 *
 * - **In the flow (desktop, 1024px and up):** a 216px rounded panel floating on the desktop
 *   beside the content panel, an `<aside>`.
 * - **`compact` (inside the drawer):** the same content with no panel chrome of its own —
 *   the drawer is the panel, with the mark and the close button above this. A `<div>`,
 *   because a complementary landmark inside a modal dialog is noise.
 *
 * **The entries are `UiNavLink`s, never buttons.** An entry is a destination, so it must be
 * a real link that middle-click, "open in new tab" and screen readers understand
 * (rules/vue.md). The current entry is a solid `--acf` fill with white text and the accent
 * glow (drawn by `UiNavLink`); inactive rows are `--t2` with a `--s2` hover.
 *
 * **The list scrolls between a fixed head and a fixed foot.** The identity card must stay on
 * screen however many modules the licence adds, and in the 390px drawer a long list would
 * otherwise push the sign-out control off the bottom. `min-h-0` is what lets a flex child
 * scroll instead of growing past its parent.
 *
 * **Entries are grouped by a presentation map, not by the catalogue.** The catalogue reports no
 * grouping, so `utils/groupNavigation.ts` states which group each entry key is drawn under, and
 * puts a key it does not know into a trailing fallback group: a newly licensed module must
 * appear, never vanish because nobody extended a map. There is still exactly ONE navigation
 * landmark ("Main navigation"): each group is a list inside it, named by its visible label
 * through `aria-labelledby`, so a screen reader hears "Hosting, list, 5 items" without the page
 * gaining a landmark per group (specs find the landmark by name and would see several).
 *
 * **Group labels wrap.** They are uppercase display text and the panel ships Armenian, whose
 * long words ("ՀԱՅՏԱՐԱՐՈՒԹՅՈՒՆՆԵՐ") have no break points: left alone they push the panel into a
 * horizontal scroll at 390px. `wrap-anywhere` lets them break. The collapsed rail stays
 * ungrouped: 64px has no room for a label, and the rail draws icons only.
 *
 * The collapse control is hidden in the drawer: collapsing trades navigation for content
 * width, and a drawer that is already over the content has nothing to trade. The identity card
 * carries `data-testid="shell-identity"`, the account-menu spec's hook for asserting the person
 * is named exactly once inside it.
 *
 * The palette trigger and the theme control are no longer here: both live in the app bar on
 * every width, so the sidebar carries navigation and identity only.
 *
 * **Submenus (issue #195).** An entry with `children` is a row of two controls: the link, which
 * navigates to the module's landing page AND opens its section (it is never a dead link), and a
 * chevron button carrying `aria-expanded` and `aria-controls` that only toggles. The section that
 * holds the current route opens by itself on every navigation, deep links included. Which
 * sections are open is remembered per viewer in `localStorage`; every access is wrapped, so the
 * sidebar renders correctly when storage is blocked. The child list is rendered with `v-if`, so a
 * closed section's links are absent rather than hidden and the drawer's focus trap never lands on
 * an invisible link.
 *
 * **Keyboard.** Tab walks every visible link and toggle as usual. Inside the navigation, ArrowDown
 * and ArrowUp move between visible links, ArrowRight opens the section of the focused parent, and
 * ArrowLeft closes it or, from a child, moves focus back to its parent.
 *
 * **Current page.** A section's row is lit on every one of its children's pages, Plans under
 * Accounts included (`UiNavLink`'s `active`), and the current row is scrolled into view on every
 * navigation (issue #221).
 *
 * **Density (issue #200).** 216px wide, 18px icons, tighter rows and group labels from tablet
 * width up; in the drawer (below 1024px) rows keep a 44px touch height (`UiNavLink`).
 */
import { readStoredKeys } from '../../utils/readStoredKeys'
import { computed, nextTick, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import ShellUserBlock from './ShellUserBlock.vue'
import { groupNavigation } from '../../utils/groupNavigation'
import type { NavigationEntry } from '../../types/navigation'
import type { NavigationGroup } from '../../types/navigationGroup'

/** Props accepted by {@link ShellSidebar}. */
const props = withDefaults(
  defineProps<{
    /** Navigation entries to render, in catalogue order, as built by `useNavigation`. */
    entries: readonly NavigationEntry[]
    /**
     * Whether the sidebar is being shown inside the drawer. It then draws no panel of its own
     * and drops the collapse control.
     */
    compact?: boolean
  }>(),
  { compact: false },
)

/** Events emitted by {@link ShellSidebar}. */
const emit = defineEmits<{
  /** Requests that the shell collapse this sidebar into the icon rail. */
  (e: 'collapse'): void
  /**
   * The user chose a navigation entry. The drawer presentation closes on this; a drawer left
   * open over the page just navigated to reads as a stuck menu.
   */
  (e: 'navigate'): void
}>()

/** Storage key under which the open sections are remembered for this viewer. */
const OPEN_SECTIONS_KEY = 'maran.nav.openSections'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

/** Keys of the sections currently open. */
const openSections: Ref<string[]> = ref(readStoredKeys(OPEN_SECTIONS_KEY))

/** The scroller holding the navigation, searched for the rows arrow keys move between. */
const scroller: Ref<HTMLElement | null> = ref(null)

/** The entries split into labelled groups; empty groups are already left out. */
const groups: ComputedRef<NavigationGroup[]> = computed(() => {
  return groupNavigation(props.entries)
})

/**
 * Text drawn for an entry: the shell's own entries carry an i18n key, modules a backend label.
 * @param entry The entry to name.
 * @returns The label to render.
 */
const labelOf = (entry: NavigationEntry): string => {
  return entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey)
}

/**
 * Answers whether the current route lies inside one of a section's pages: the page itself or
 * anything under its path (`/accounts/42` is inside "All accounts").
 * @param entry The section to test.
 * @returns True when a child's path is the current path or a prefix of it.
 */
const holdsCurrentRoute = (entry: NavigationEntry): boolean => {
  return entry.children.some((child): boolean => {
    const path = router.resolve(child.target).path
    return route.path === path || route.path.startsWith(`${path}/`)
  })
}

/**
 * Answers whether a section is open.
 * @param key The section's entry key.
 * @returns True when its children are shown.
 */
const isOpen = (key: string): boolean => {
  return openSections.value.includes(key)
}

/**
 * Opens or closes a section and remembers the choice. Storage failures are swallowed: the state
 * still changes for this visit.
 * @param key The section's entry key.
 * @param open Whether it should end up open.
 * @returns Nothing.
 */
const setOpen = (key: string, open: boolean): void => {
  if (isOpen(key) === open) {
    return
  }
  openSections.value = open
    ? [...openSections.value, key]
    : openSections.value.filter((candidate): boolean => {
        return candidate !== key
      })
  try {
    window.localStorage.setItem(OPEN_SECTIONS_KEY, JSON.stringify(openSections.value))
  } catch {
    // Remembering is a convenience; the section still toggles without it.
  }
}

/**
 * Flips a section between open and closed.
 * @param key The section's entry key.
 * @returns Nothing.
 */
const toggle = (key: string): void => {
  setOpen(key, !isOpen(key))
}

/**
 * Handles a click on a section's own link: it navigates (the router does that) and opens the
 * section, then reports the choice like any other destination.
 * @param key The section's entry key.
 * @returns Nothing.
 */
const chooseSection = (key: string): void => {
  setOpen(key, true)
  navigate()
}

/**
 * Arrow-key movement inside the navigation. Only links take part in Up/Down — the toggles stay on
 * the Tab path — so the arrows walk the menu the way it reads.
 * @param event The keydown event from inside the scroller.
 * @returns Nothing.
 */
const onKeydown = (event: KeyboardEvent): void => {
  const target = event.target
  if (!(target instanceof HTMLElement) || scroller.value === null) {
    return
  }
  const links = Array.from(scroller.value.querySelectorAll<HTMLElement>('nav a'))
  const index = links.indexOf(target)
  if (index === -1) {
    return
  }

  const section = target.dataset.navSection
  const parent = target.dataset.navParent
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    const next = links[index + (event.key === 'ArrowDown' ? 1 : -1)]
    next?.focus()
  } else if (event.key === 'ArrowRight' && section !== undefined) {
    event.preventDefault()
    setOpen(section, true)
  } else if (event.key === 'ArrowLeft' && section !== undefined) {
    event.preventDefault()
    setOpen(section, false)
  } else if (event.key === 'ArrowLeft' && parent !== undefined) {
    event.preventDefault()
    scroller.value.querySelector<HTMLElement>(`[data-nav-section="${parent}"]`)?.focus()
  }
}

/**
 * Asks the shell to collapse the sidebar. The collapsed/expanded state lives in the layout,
 * which owns both presentations, so this component only reports the intent.
 * @returns Nothing; the event is emitted synchronously.
 */
const collapse = (): void => {
  emit('collapse')
}

/**
 * Reports that a destination was chosen, so the drawer can close.
 * @returns Nothing; the event is emitted synchronously.
 */
const navigate = (): void => {
  emit('navigate')
}

// The section holding the current route opens on every navigation, deep links included. It only
// ever opens: closing the section one is standing in is the viewer's own choice to keep.
watch(
  (): [string, readonly NavigationEntry[]] => {
    return [route.path, props.entries]
  },
  (): void => {
    for (const entry of props.entries) {
      if (entry.children.length > 0 && holdsCurrentRoute(entry)) {
        setOpen(entry.key, true)
      }
    }
  },
  { immediate: true },
)

// The current row is scrolled into view on every navigation (issue #221): on a short window the
// lit child of a lower section ("Backup list") sat hidden under the identity card. The wait for
// the next tick lets a section that just opened render its children first. The list moves only as
// far as needed, and not at all when the row is already visible.
watch(
  (): string => {
    return route.fullPath
  },
  async (): Promise<void> => {
    await nextTick()
    const box = scroller.value
    const current =
      box?.querySelector<HTMLElement>('nav a[aria-current="page"]') ??
      box?.querySelector<HTMLElement>('nav a.router-link-active')
    if (box === null || box === undefined || current === null || current === undefined) {
      return
    }
    // The scroller is moved by hand rather than with `scrollIntoView`, which also scrolls every
    // scrollable ancestor, the shell's `overflow: hidden` frame included, and shifted the app bar.
    const row = current.getBoundingClientRect()
    const view = box.getBoundingClientRect()
    if (row.top < view.top) {
      box.scrollTop -= view.top - row.top
    } else if (row.bottom > view.bottom) {
      box.scrollTop += row.bottom - view.bottom
    }
  },
  { immediate: true, flush: 'post' },
)
</script>

<template>
  <component
    :is="compact ? 'div' : 'aside'"
    class="flex min-h-0 flex-col"
    :class="
      compact
        ? 'flex-1'
        : 'w-54 flex-none rounded-panel border border-border-subtle bg-surface-1 p-2.5'
    "
  >
    <!-- The scroll container clips whatever leaves its padding box, and the active row's glow
         (`--glow`: 30px blur, 8px down, -8px spread) reaches about 22px out sideways and 30px
         below the row. A wrapper exactly as wide as the rows cut it into a pale rectangle, loud
         on the light theme. So the wrapper owns the 10px the panel's own padding gives the rows
         (`px-2.5` with the matching `-mx-2.5`, so the rows do not move) and 32px under the last
         row: the cut then falls on the panel's inner edge, where the glow is a few percent of its
         strength, and never inside the panel. The same box is what keeps the 4px focus ring
         whole: `pt-1.5` (with `-mt-1.5`, so nothing moves) leaves it room above the collapse
         button, and the 32px under the last row leaves it room below the last link. -->
    <!-- The keydown listener only adds arrow-key movement between links that are already
         focusable; every row stays reachable with Tab, so no role is implied. -->
    <div
      ref="scroller"
      class="-mx-2.5 -mt-1.5 flex min-h-0 flex-1 flex-col gap-0.5 overflow-x-hidden overflow-y-auto px-2.5 pt-1.5 pb-8"
      @keydown="onKeydown"
    >
      <!-- The collapse control gets a row of its own: the group labels are a loop, and a control
           that belongs to the panel must not move into whichever group happens to be first. -->
      <div v-if="!compact" class="flex justify-end">
        <UiButton
          variant="quiet"
          size="icon-sm"
          :aria-label="t('app.shell.collapseSidebar')"
          :title="t('app.shell.collapseSidebar')"
          @click="collapse"
        >
          <UiIcon name="chevronLeft" size="md" />
        </UiButton>
      </div>

      <UiNav :label="t('app.nav.ariaLabel')">
        <UiNavItem v-for="group in groups" :key="group.key" class="mt-1.5 first:mt-0">
          <p
            :id="`nav-group-${group.key}`"
            class="min-w-0 px-2.5 pt-1.5 pb-0.5 font-display text-xs font-extrabold tracking-caps wrap-anywhere text-text-muted uppercase"
          >
            {{ t(group.labelKey) }}
          </p>
          <ul class="flex flex-col gap-0.5" :aria-labelledby="`nav-group-${group.key}`">
            <UiNavItem v-for="entry in group.entries" :key="entry.key">
              <UiNavLink
                v-if="entry.children.length === 0"
                :to="entry.target"
                :locked="entry.locked"
                @click="navigate"
              >
                <UiIcon :name="entry.icon" size="md" />
                <span class="min-w-0 wrap-anywhere">{{ labelOf(entry) }}</span>
                <template v-if="entry.locked" #badge>
                  <UiBadge>{{ t('app.nav.lockedBadge') }}</UiBadge>
                </template>
              </UiNavLink>

              <template v-else>
                <div class="flex items-center gap-0.5">
                  <UiNavLink
                    class="min-w-0 flex-1"
                    :to="entry.target"
                    :active="holdsCurrentRoute(entry)"
                    :data-nav-section="entry.key"
                    @click="chooseSection(entry.key)"
                  >
                    <UiIcon :name="entry.icon" size="md" />
                    <span class="min-w-0 wrap-anywhere">{{ labelOf(entry) }}</span>
                  </UiNavLink>
                  <UiButton
                    variant="quiet"
                    size="icon-sm"
                    :aria-label="t('app.nav.expandSection', { name: labelOf(entry) })"
                    :aria-expanded="isOpen(entry.key) ? 'true' : 'false'"
                    :aria-controls="isOpen(entry.key) ? `nav-section-${entry.key}` : undefined"
                    @click="toggle(entry.key)"
                  >
                    <UiIcon
                      name="chevronDown"
                      size="sm"
                      class="transition-transform motion-reduce:transition-none"
                      :class="isOpen(entry.key) ? 'rotate-180' : ''"
                    />
                  </UiButton>
                </div>
                <ul
                  v-if="isOpen(entry.key)"
                  :id="`nav-section-${entry.key}`"
                  class="mt-0.5 ml-4.5 flex flex-col gap-0.5 border-l border-border-subtle pl-1.5"
                  :aria-label="labelOf(entry)"
                >
                  <UiNavItem v-for="child in entry.children" :key="child.key">
                    <UiNavLink
                      :to="child.target"
                      nested
                      :data-nav-parent="entry.key"
                      @click="navigate"
                    >
                      <span class="min-w-0 wrap-anywhere">{{ labelOf(child) }}</span>
                    </UiNavLink>
                  </UiNavItem>
                </ul>
              </template>
            </UiNavItem>
          </ul>
        </UiNavItem>
      </UiNav>
    </div>

    <!-- The identity card: the one place the signed-in person is named. -->
    <div
      data-testid="shell-identity"
      class="mt-2.5 flex-none rounded-control border border-border-subtle bg-surface-2 p-2.5"
    >
      <ShellUserBlock />
    </div>
  </component>
</template>
