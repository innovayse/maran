<script setup lang="ts">
/**
 * The shell's expanded sidebar: brand block, command trigger, the
 * backend-driven navigation, and a footer holding the theme toggle.
 *
 * Every measurement here is taken from the design canvas: 246px wide on
 * `--s1` with a `--b1` right border, a 28px/7px-radius accent brand square,
 * the product name at 13px/600 above an 11px muted line, and navigation rows
 * built to the design's `navBtn` helper — `padding:5px 8px`,
 * `border-radius:6px`, body copy on the shared `--text-base` step, active rows on `--acs` in `--ac`
 * at weight 600.
 *
 * The rows themselves are `UiNavLink`s, not the design's `<button>`s: an
 * entry is a destination, so it must be a real link that middle-click,
 * "open in new tab" and screen readers understand (rules/vue.md). The
 * design's row styling is applied to them through scoped `:deep()` rules
 * rather than by editing the kit primitive.
 */
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiNav from '../ui/UiNav.vue'
import UiNavItem from '../ui/UiNavItem.vue'
import UiNavLink from '../ui/UiNavLink.vue'
import UiIcon from '../ui/UiIcon.vue'
import ShellUserBlock from './ShellUserBlock.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellSidebar}. */
withDefaults(
  defineProps<{
    /** Navigation entries to render, in catalogue order, as built by `useNavigation`. */
    entries: readonly NavigationEntry[]
    /**
     * Whether the sidebar is being shown as the compact off-canvas drawer. It
     * then drops the collapse control: collapsing trades navigation for content
     * width, and a drawer that is already over the content has nothing to trade.
     */
    compact?: boolean
  }>(),
  { compact: false },
)

/** Events emitted by {@link ShellSidebar}. */
const emit = defineEmits<{
  /** Requests that the shell collapse this sidebar into the icon rail. */
  (e: 'collapse'): void
  /** Requests that the shell flip the interface theme. */
  (e: 'toggleTheme'): void
  /** Requests that the shell open the jump-to palette. */
  (e: 'openPalette'): void
  /**
   * The user chose a navigation entry. The drawer presentation closes on this;
   * a drawer left open over the page just navigated to reads as a stuck menu.
   */
  (e: 'navigate'): void
}>()

const { t } = useI18n()

/**
 * Asks the shell to collapse the sidebar. The collapsed/expanded state lives
 * in the layout, which owns both presentations, so this component only
 * reports the intent.
 * @returns Nothing; the event is emitted synchronously.
 */
const collapse = (): void => {
  emit('collapse')
}

/**
 * Asks the shell to flip the theme. The theme store is the single source of
 * truth and the layout owns the call into it, so this component only reports
 * the intent.
 * @returns Nothing; the event is emitted synchronously.
 */
const toggleTheme = (): void => {
  emit('toggleTheme')
}
</script>

<template>
  <!-- 246px is the design's own sidebar width and Tailwind has nothing within a
       pixel of it (w-56 is 224px), so the number stays literal here. -->
  <aside class="flex w-[246px] shrink-0 flex-col border-r border-border-subtle bg-surface-1">
    <div class="flex items-center gap-2.25 px-3 pt-3 pb-2.5">
      <span
        class="grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-accent font-bold text-white"
        aria-hidden="true"
      >
        {{ t('app.brandInitial') }}
      </span>
      <h1 class="min-w-0 flex-1 truncate leading-tight font-semibold">{{ t('app.title') }}</h1>
      <UiButton
        v-if="!compact"
        variant="ghost"
        class="shell-icon-button"
        :aria-label="t('app.shell.collapseSidebar')"
        :title="t('app.shell.collapseSidebar')"
        @click="collapse"
      >
        <UiIcon name="chevronLeft" />
      </UiButton>
    </div>

    <div class="px-3 pb-2.5">
      <!-- Opens the jump-to palette. It searches the navigation the panel already
           holds, not the server: there is no search endpoint, and the palette's
           scope is stated in its own placeholder rather than implied. -->
      <UiButton variant="secondary" class="shell-search-trigger" @click="emit('openPalette')">
        <UiIcon name="search" :size="13" />
        <span class="flex-1 text-left">{{ t('app.shell.search') }}</span>
        <span class="rounded border border-border-strong px-1 py-px font-mono text-sm">
          {{ t('app.shell.searchShortcut') }}
        </span>
      </UiButton>
    </div>

    <div class="shell-nav min-h-0 flex-1 overflow-y-auto px-2 pb-3">
      <p class="shell-nav-group px-2 text-sm font-semibold text-text-muted uppercase">
        {{ t('app.nav.groups.panel') }}
      </p>
      <UiNav :label="t('app.nav.ariaLabel')">
        <UiNavItem v-for="entry in entries" :key="entry.key">
          <UiNavLink :to="entry.target" :locked="entry.locked" @click="emit('navigate')">
            <UiIcon :name="entry.icon" />
            <span class="shell-nav-label">
              {{ entry.labelKey === null ? entry.label : t(entry.labelKey) }}
            </span>
            <template v-if="entry.locked" #badge>
              <UiBadge>{{ t('app.nav.lockedBadge') }}</UiBadge>
            </template>
          </UiNavLink>
        </UiNavItem>
      </UiNav>
    </div>

    <div class="shell-footer flex items-center gap-2 border-t border-border-subtle px-3">
      <!-- The design's identity block. It is passed `null` because this build has
           no authentication and therefore no user to name; the day a session
           exists, a store supplies one here and nothing else changes. -->
      <ShellUserBlock />
      <UiButton
        variant="secondary"
        class="shell-icon-button shell-icon-button--boxed"
        :aria-label="t('app.shell.toggleTheme')"
        :title="t('app.shell.toggleTheme')"
        @click="toggleTheme"
      >
        <UiIcon name="moon" :size="13" />
      </UiButton>
    </div>
  </aside>
</template>

<style scoped>
/*
 * The kit primitives now carry the design tokens themselves, so the shell no
 * longer re-colours them: `UiNavLink` already draws the design's `navBtn`
 * (--t2 on nothing, --s3/--t1 on hover, --acs/--ac at 600 when current) and
 * `UiButton` already draws the surfaces and the focus ring. What is left here
 * is only what the kit cannot know: this shell's geometry.
 */

/* UiNav sets its own width and inset for a standalone menu; inside the shell the
   scroll container owns both, and the nav's own inset would push every row 8px
   further in than the group label above it. */
.shell-nav :deep(nav) {
  width: 100%;
  padding-inline: 0;
}

/* The row's default slot is a plain span in the kit; the shell needs it to be
   the flexible middle so a long module name truncates instead of shoving the
   locked badge out of the row. */
.shell-nav :deep(a > span:first-child) {
  display: flex;
  align-items: center;
  gap: 9px;
  min-width: 0;
  flex: 1;
}

.shell-nav-label {
  min-width: 0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: left;
}

/* The design's 24px square chrome controls. The kit's button is sized for a
   labelled action, so the shell restates the box — and, because restating the
   background and border also overrides the kit's focus treatment, restates the
   focused border with it rather than leaving a keyboard user a partial ring. */
.shell-icon-button {
  display: grid;
  place-items: center;
  width: 24px;
  height: 24px;
  padding: 0;
  border-radius: 6px;
  background: none;
  color: var(--t3);
}

.shell-icon-button--boxed {
  background: var(--s2);
  border: 1px solid var(--b1);
  color: var(--t2);
}

.shell-icon-button:hover {
  background: var(--s3);
  color: var(--t1);
}

.shell-icon-button:focus-visible {
  border: 1px solid var(--ac);
}

/* The design's footer band: 9px vertical, which Tailwind's scale steps over. */
.shell-footer {
  padding-top: 9px;
  padding-bottom: 9px;
}

/* The design's group label: .07em is a whole step wider than the caps tracking
   used elsewhere, and 5px below it — small numbers that set how the sidebar
   breathes, so they are taken literally rather than snapped to the scale. */
.shell-nav-group {
  letter-spacing: 0.07em;
  padding-bottom: 5px;
}

/* The design's search trigger: full width, boxed on --s2, 12px muted text. */
.shell-search-trigger {
  display: flex;
  align-items: center;
  gap: 7px;
  width: 100%;
  padding: 6px 8px;
  background: var(--s2);
  border: 1px solid var(--b1);
  border-radius: 7px;
  color: var(--t3);
  font-size: var(--text-base);
  font-weight: 400;
  text-align: left;
}

.shell-search-trigger:focus-visible {
  border-color: var(--ac);
}
</style>
