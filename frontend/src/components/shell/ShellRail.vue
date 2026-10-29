<script setup lang="ts">
/**
 * The shell's collapsed sidebar: the same backend-driven entries as {@link ShellSidebar},
 * drawn icon-only in a 64px rounded panel, with the expand control at the foot. The layout
 * renders exactly one of the two at a time, and only on desktop — below 1024px the sidebar
 * is a drawer and a 64px strip beside a phone-width page would be no better than the sidebar
 * it replaced.
 *
 * **Each entry keeps its text as screen-reader-only content**, not only a `title` or an
 * `aria-label`: the accessible name then survives translation, the link has real content for
 * assistive technology, and a locked entry can say "Locked" in the same breath. The `title`
 * is the sighted user's tooltip, since an icon alone does not say which module it is.
 *
 * The brand and the document's `h1` are in the app bar now, so the rail carries no heading of
 * its own: collapsing the navigation no longer costs the document its title.
 *
 * The list scroller spans the panel's full width with its padding inside it, so the active
 * square's glow is clipped at the panel edge rather than at the 40px square (a clipped glow
 * reads as a rendering fault), and it never scrolls sideways (the screen-reader-only labels are
 * absolutely positioned and would otherwise create a horizontal scrollbar).
 *
 * Active and hover states are drawn by `UiNavLink` (a solid `--acf` square with white icon
 * and glow); this component adds no colour of its own.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellRail}. */
defineProps<{
  /** Navigation entries to render, in catalogue order, as built by `useNavigation`. */
  entries: readonly NavigationEntry[]
}>()

/** Events emitted by {@link ShellRail}. */
const emit = defineEmits<{
  /** Requests that the shell expand the rail back into the full sidebar. */
  (e: 'expand'): void
}>()

const { t } = useI18n()

/**
 * Asks the shell to expand the rail. The collapsed/expanded state lives in the layout, which
 * owns both presentations.
 * @returns Nothing; the event is emitted synchronously.
 */
const expand = (): void => {
  emit('expand')
}
</script>

<template>
  <aside
    class="flex w-16 flex-none flex-col gap-3 rounded-panel border border-border-subtle bg-surface-1 py-3"
  >
    <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto px-3">
      <UiNav :label="t('app.nav.ariaLabel')">
        <UiNavItem v-for="entry in entries" :key="entry.key">
          <UiNavLink
            :to="entry.target"
            :locked="entry.locked"
            compact
            :title="entry.labelKey === null ? (entry.label ?? '') : t(entry.labelKey)"
          >
            <UiIcon :name="entry.icon" size="lg" />
            <span class="sr-only">
              {{ entry.labelKey === null ? entry.label : t(entry.labelKey) }}
              <template v-if="entry.locked">{{ t('app.nav.lockedBadge') }}</template>
            </span>
          </UiNavLink>
        </UiNavItem>
      </UiNav>
    </div>

    <!-- One control at the foot of the rail: expand. The theme and the palette live in the app
         bar, and a copy here would only add a button to a column that exists to be narrow. -->
    <UiButton
      variant="quiet"
      size="icon"
      class="mx-auto"
      :aria-label="t('app.shell.expandSidebar')"
      :title="t('app.shell.expandSidebar')"
      @click="expand"
    >
      <UiIcon name="chevronRight" size="md" />
    </UiButton>
  </aside>
</template>
