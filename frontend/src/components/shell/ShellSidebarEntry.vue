<script setup lang="ts">
/**
 * One top-level row of the expanded sidebar (`ShellSidebar`), and its submenu when it has one.
 *
 * Three shapes, decided by the entry the navigation tree built (issue #311): a page not built yet
 * is a disabled `UiNavPlanned` row; a single page is a `UiNavLink`; a section is a `UiNavSection`
 * (a link to its landing page plus a toggle) over the list of its pages. A section with no page
 * built yet still opens, its row disabled and every page in it disabled too, so the menu shows
 * what is coming. Which sections are open, and which holds the current page, is the sidebar's
 * state, passed in; this component only reports what was pressed.
 *
 * A locked page carries the "Locked" pill; a counted page carries its count, with what is counted
 * read to assistive technology after the number.
 */
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import UiNavPlanned from '../ui/UiNavPlanned.vue'
import UiNavSection from '../ui/UiNavSection.vue'
import { navigationLabelText } from '../../utils/navigationLabelText'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellSidebarEntry}. */
defineProps<{
  /** The top-level entry to draw. */
  entry: NavigationEntry
  /** Whether its submenu is open. */
  open: boolean
  /** Whether the current page is one of its pages. */
  current: boolean
}>()

/** Events emitted by {@link ShellSidebarEntry}. */
const emit = defineEmits<{
  /** A page was chosen: the drawer closes on it. */
  (e: 'navigate'): void
  /** The section's own link was followed: the sidebar opens it as well. */
  (e: 'choose'): void
  /** The section's toggle was pressed. */
  (e: 'toggle'): void
}>()

const { t } = useI18n()

/**
 * The text an entry is drawn with.
 * @param entry The entry to name.
 * @returns Its label.
 */
const labelOf = (entry: NavigationEntry): string => {
  return navigationLabelText(entry.label, t)
}

/**
 * The pill drawn at a row's end: "Locked" for a locked page, else its count, else none.
 * @param entry The row's entry.
 * @returns The pill's visible text, or null when no pill is drawn.
 */
const pillOf = (entry: NavigationEntry): string | null => {
  if (entry.state.kind === 'locked') {
    return t('app.nav.lockedBadge')
  }
  return entry.badge === null ? null : String(entry.badge.count)
}

/**
 * Reports a chosen page.
 * @returns Nothing.
 */
const navigate = (): void => {
  emit('navigate')
}

/**
 * Reports the section's own link.
 * @returns Nothing.
 */
const choose = (): void => {
  emit('choose')
}

/**
 * Reports the section's toggle.
 * @returns Nothing.
 */
const toggle = (): void => {
  emit('toggle')
}
</script>

<template>
  <UiNavPlanned
    v-if="entry.state.kind === 'planned' && entry.children.length === 0"
    :hint="t('app.nav.plannedHint')"
  >
    <UiIcon :name="entry.icon" size="md" />
    <span class="min-w-0 wrap-anywhere">{{ labelOf(entry) }}</span>
  </UiNavPlanned>

  <UiNavLink
    v-else-if="entry.state.kind !== 'planned' && entry.children.length === 0"
    :to="entry.state.target"
    :locked="entry.state.kind === 'locked'"
    @click="navigate"
  >
    <UiIcon :name="entry.icon" size="md" />
    <span class="min-w-0 wrap-anywhere">{{ labelOf(entry) }}</span>
    <template v-if="pillOf(entry) !== null" #badge>
      <UiBadge>
        {{ pillOf(entry) }}
        <span v-if="entry.badge !== null" class="sr-only">{{ t(`app.nav.badges.${entry.badge.kind}`) }}</span>
      </UiBadge>
    </template>
  </UiNavLink>

  <template v-else>
    <UiNavSection
      :to="entry.state.kind === 'planned' ? null : entry.state.target"
      :planned-hint="t('app.nav.plannedHint')"
      :current="current"
      :open="open"
      :section="entry.key"
      :controls="`nav-section-${entry.key}`"
      :toggle-label="t('app.nav.expandSection', { name: labelOf(entry) })"
      @navigate="choose"
      @toggle="toggle"
    >
      <UiIcon :name="entry.icon" size="md" />
      <span class="min-w-0 wrap-anywhere">{{ labelOf(entry) }}</span>
    </UiNavSection>
    <ul
      v-if="open"
      :id="`nav-section-${entry.key}`"
      class="mt-0.5 ml-4.5 flex flex-col gap-0.5 border-l border-border-subtle pl-1.5"
      :aria-label="labelOf(entry)"
    >
      <UiNavItem v-for="child in entry.children" :key="child.key">
        <UiNavPlanned v-if="child.state.kind === 'planned'" :hint="t('app.nav.plannedHint')" nested>
          <span class="min-w-0 wrap-anywhere">{{ labelOf(child) }}</span>
        </UiNavPlanned>
        <UiNavLink
          v-else
          :to="child.state.target"
          :locked="child.state.kind === 'locked'"
          nested
          :data-nav-parent="entry.key"
          @click="navigate"
        >
          <span class="min-w-0 wrap-anywhere">{{ labelOf(child) }}</span>
          <template v-if="pillOf(child) !== null" #badge>
            <UiBadge>
              {{ pillOf(child) }}
              <span v-if="child.badge !== null" class="sr-only">{{ t(`app.nav.badges.${child.badge.kind}`) }}</span>
            </UiBadge>
          </template>
        </UiNavLink>
      </UiNavItem>
    </ul>
  </template>
</template>
