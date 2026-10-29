<script setup lang="ts">
/**
 * The shell's expanded navigation: the collapse control, the backend-driven entries under small
 * group labels, and the signed-in person's identity card at the bottom.
 *
 * Two presentations from one component, because the markup inside is identical:
 *
 * - **In the flow (desktop, 1024px and up):** a 240px rounded panel floating on the desktop
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
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
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

const { t } = useI18n()

/** The entries split into labelled groups; empty groups are already left out. */
const groups: ComputedRef<NavigationGroup[]> = computed(() => {
  return groupNavigation(props.entries)
})

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
</script>

<template>
  <component
    :is="compact ? 'div' : 'aside'"
    class="flex min-h-0 flex-col"
    :class="
      compact
        ? 'flex-1'
        : 'w-60 flex-none rounded-panel border border-border-subtle bg-surface-1 p-3'
    "
  >
    <!-- The scroll container clips whatever leaves its padding box, and the active row's glow
         (`--glow`: 30px blur, 8px down, -8px spread) reaches about 22px out sideways and 30px
         below the row. A wrapper exactly as wide as the rows cut it into a pale rectangle, loud
         on the light theme. So the wrapper owns the 12px the panel's own padding gives the rows
         (`px-3` with the matching `-mx-3`, so the rows do not move) and 32px under the last row:
         the cut then falls on the panel's inner edge, where the glow is a few percent of its
         strength, and never inside the panel. The padding is not larger because the wrapper
         cannot reach past the panel's border without painting the glow onto the page behind it.
         The same box is what keeps the 4px focus ring whole: `pt-1.5` (with `-mt-1.5`, so nothing
         moves) leaves it room above the collapse button, and the 32px under the last row leaves
         it room below the last link. -->
    <div class="-mx-3 -mt-1.5 flex min-h-0 flex-1 flex-col gap-1 overflow-x-hidden overflow-y-auto px-3 pt-1.5 pb-8">
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
        <UiNavItem v-for="group in groups" :key="group.key" class="mt-2 first:mt-0">
          <p
            :id="`nav-group-${group.key}`"
            class="min-w-0 px-3 pt-2 pb-1 font-display text-xs font-extrabold tracking-caps wrap-anywhere text-text-muted uppercase"
          >
            {{ t(group.labelKey) }}
          </p>
          <ul class="flex flex-col gap-1" :aria-labelledby="`nav-group-${group.key}`">
            <UiNavItem v-for="entry in group.entries" :key="entry.key">
              <UiNavLink :to="entry.target" :locked="entry.locked" @click="navigate">
                <UiIcon :name="entry.icon" size="lg" />
                <span class="min-w-0 wrap-anywhere">
                  {{ entry.labelKey === null ? entry.label : t(entry.labelKey) }}
                </span>
                <template v-if="entry.locked" #badge>
                  <UiBadge>{{ t('app.nav.lockedBadge') }}</UiBadge>
                </template>
              </UiNavLink>
            </UiNavItem>
          </ul>
        </UiNavItem>
      </UiNav>
    </div>

    <!-- The identity card: the one place the signed-in person is named. -->
    <div data-testid="shell-identity" class="mt-3 flex-none rounded-control border border-border-subtle bg-surface-2 p-3">
      <ShellUserBlock />
    </div>
  </component>
</template>
