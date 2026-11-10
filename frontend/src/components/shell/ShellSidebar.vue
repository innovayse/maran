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
 * **Entries are grouped as the navigation tree groups them** (`router/navigationTree.ts`, issue
 * #311), with a trailing fallback group for a catalogue module the tree does not know. There is still
 * exactly ONE navigation landmark ("Main navigation"): each group is a list inside it, named by its
 * visible label through `aria-labelledby`, so the page gains no landmark per group.
 *
 * **A page not built yet is drawn disabled** (`UiNavPlanned`): greyed, leading nowhere, with a
 * "coming soon" hint, so the menu shows the whole product. A live count (running tasks) is drawn
 * as a badge beside its row.
 *
 * **Group labels wrap.** They are display text and the panel ships Armenian, whose
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
 * **Current page (issue #221).** A section's row is lit on every one of its children's pages,
 * Plans under Accounts included, by the one rule in `useCurrentSection` that the collapsed rail
 * shares, and it is drawn by `UiNavSection` as wide as every other row. The current row is kept in
 * view on every navigation and when the catalogue arrives (`useRevealCurrentRow`).
 *
 * **Loading (issue #366).** While a customer's first answer for the active account is on its way
 * (`loading`), a fixed number of {@link UiSkeleton} rows stands in for the entries inside the same
 * named navigation landmark, marked `aria-busy`. The rows sit in a `UiLoadingRegion`, so they are
 * mounted at once (the space is held) but stay invisible for the first 150 ms: a fast answer shows
 * no placeholder. The region announces the wait once, politely; the rows are decorative and hidden
 * from assistive technology.
 *
 * **Density (issue #200).** 216px wide, 18px icons, tighter rows and group labels from tablet
 * width up; in the drawer (below 1024px) rows keep a 44px touch height (`UiNavLink`).
 */
import { readStoredKeys } from '../../utils/readStoredKeys'
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import ShellSidebarEntry from './ShellSidebarEntry.vue'
import ShellUserBlock from './ShellUserBlock.vue'
import { groupNavigation } from '../../utils/groupNavigation'
import { useCurrentSection } from '../../composables/useCurrentSection'
import { useRevealCurrentRow } from '../../composables/useRevealCurrentRow'
import type { NavigationEntry } from '../../types/navigation'
import type { NavigationGroup } from '../../types/navigationGroup'

/** Props accepted by {@link ShellSidebar}. */
const props = withDefaults(
  defineProps<{
    /** Navigation entries to render, in catalogue order, as built by `useNavigationState`. */
    entries: readonly NavigationEntry[]
    /**
     * Whether the sidebar is being shown inside the drawer. It then draws no panel of its own
     * and drops the collapse control.
     */
    compact?: boolean
    /**
     * Whether the first answer for the active account is still on its way. The sidebar then draws
     * a skeleton in place of the entries: an empty menu would read as "nothing is allowed" and the
     * permissive one would offer what may be refused a moment later.
     */
    loading?: boolean
  }>(),
  { compact: false, loading: false },
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

/** How many placeholder rows the loading skeleton draws; a UI constant, not a count of anything real. */
const SKELETON_ROWS = 6

/** Storage key under which the open sections are remembered for this viewer. */
const OPEN_SECTIONS_KEY = 'maran.nav.openSections'

const { t } = useI18n()
const route = useRoute()
const holdsCurrentRoute = useCurrentSection()

/** Keys of the sections currently open. */
const openSections: Ref<string[]> = ref(readStoredKeys(OPEN_SECTIONS_KEY))

/** The scroller holding the navigation, searched for the rows arrow keys move between. */
const scroller: Ref<HTMLElement | null> = ref(null)

/** The entries split into labelled groups; empty groups are already left out. */
const groups: ComputedRef<NavigationGroup[]> = computed(() => {
  return groupNavigation(props.entries)
})

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

useRevealCurrentRow(scroller, (): readonly NavigationEntry[] => {
  return props.entries
})
</script>

<template>
  <component
    :is="compact ? 'div' : 'aside'"
    class="flex min-h-0 flex-col"
    :class="compact ? 'flex-1' : 'w-60 flex-none rounded-panel border border-border-subtle bg-surface-1 p-2.5'"
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

      <!-- Loading: the landmark keeps its name and is marked busy. The region mounts the placeholders
           at once, so the space is held, keeps them invisible for the first 150 ms, and announces
           the wait itself; the rows are decorative. The region sits inside the list item, around
           the list, so the markup stays valid. -->
      <UiNav v-if="loading" :label="t('app.nav.ariaLabel')" aria-busy="true">
        <UiNavItem>
          <UiLoadingRegion state="loading">
            <ul class="flex flex-col gap-0.5" aria-hidden="true">
              <UiNavItem v-for="row in SKELETON_ROWS" :key="row" class="py-0.5">
                <UiSkeleton shape="block" control="field" />
              </UiNavItem>
            </ul>
          </UiLoadingRegion>
        </UiNavItem>
      </UiNav>
      <UiNav v-else :label="t('app.nav.ariaLabel')">
        <UiNavItem v-for="group in groups" :key="group.key" class="mt-1.5 first:mt-0">
          <p
            :id="`nav-group-${group.key}`"
            class="min-w-0 px-2.5 pt-1.5 pb-0.5 font-display text-xs font-extrabold wrap-anywhere text-text-muted"
          >
            {{ t(group.labelKey) }}
          </p>
          <ul class="flex flex-col gap-0.5" :aria-labelledby="`nav-group-${group.key}`">
            <UiNavItem v-for="entry in group.entries" :key="entry.key">
              <ShellSidebarEntry
                :entry="entry"
                :open="isOpen(entry.key)"
                :current="holdsCurrentRoute(entry)"
                @navigate="navigate"
                @choose="chooseSection(entry.key)"
                @toggle="toggle(entry.key)"
              />
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
