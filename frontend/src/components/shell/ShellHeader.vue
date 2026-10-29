<script setup lang="ts">
/**
 * The shell's app bar: a 56px rounded panel floating on the desktop, holding the mark and
 * wordmark, the command-palette trigger, the breadcrumb, and the right-aligned interface
 * controls (tasks badge, theme, language, the signed-in person's avatar).
 *
 * **The wordmark is a link, not a heading.** Every authenticated page opens with its own `<h1>`
 * (`UiPageHeading`), and a document has one; the product name is chrome that the page's title
 * sits under, so it is a plain link to the landing screen inside the banner. The mark is the real
 * keystone from `/favicon.svg` with no plate behind it (see `UiBrandMark`).
 *
 * **The palette trigger is a button styled as a search field**, with a `⌘K` hint. It carries
 * visible text and a `border-border-control` outline: a control must never be identified by a
 * fill alone (`--s2` against a panel is 1.06:1), and the subtle border is 1.2 to 1.5:1, so the
 * outline is the control-strength one. Below `sm` the text and hint are dropped and it collapses
 * to a 40px icon button with the same accessible name — one element, not a second hidden control,
 * so the name is never present twice; there the glyph itself is the identifier, so the fill and
 * outline go with the text. It must never wrap: the placeholder and the hint wrapped onto two
 * lines at 768px in the mockup, so the text truncates instead.
 *
 * **The hamburger exists only while the shell is compact**, where no sidebar column is rendered;
 * the layout owns that state (one media query, see `DefaultLayout.vue`) and passes it in as
 * `compact`, so the button and the layout can never disagree about which presentation is showing.
 * Its `aria-expanded` follows the drawer.
 *
 * **The bar gives way by its own width, not the window's.** A media query counts CSS pixels of
 * the window and ignores the reader's text size, so a bar that fits at 100% overflowed under a
 * 200% text-only zoom at the same window width: the search field shrank to an icon, its `⌘K` hint
 * spilled over the tasks badge, and the language button and avatar fell off the canvas (WCAG
 * 1.4.4 and 1.4.10). The bar is a size container (`@container`) and every step below is a
 * container query in `rem`, so it follows the text size exactly as the content does. From the
 * lowest priority up, as the bar narrows:
 *
 * 1. The breadcrumb hides before it could be squeezed into "Pa…" (it is the only item that is
 *    pure orientation; the page's own `<h1>` says the same).
 * 2. The `⌘K` hint hides when the search field itself is too narrow to hold it (the field is its
 *    own container), then the field collapses to the icon button, one element at a time, never
 *    both.
 * 3. The theme and language labels fall back to their icons (see those components), and the
 *    wordmark's name steps aside (see `ShellWordmark`).
 * 4. The avatar is dropped. Nothing becomes unreachable: the account menu is the sidebar's
 *    identity card, in the sidebar or, when there is none, in the navigation drawer. The banner
 *    must hold exactly one `aria-haspopup="menu"` element at a phone width, the language picker,
 *    which the locale specs find that way; a second trigger here would make their clicks ambiguous.
 *
 * The theme switch is the one control that does not follow the bar: below `sm` of the window it
 * lives in the navigation drawer's head (`ShellDrawer`), which the menu button and the tab bar's
 * "More" both open, so it stays one tap from every screen with its accessible name unchanged. The
 * drawer decides that by the window, so this bar does too; otherwise there would be a text size at
 * which neither showed it.
 *
 * **If even that is not enough, the row wraps instead of clipping.** At a text size and window
 * where the four controls that must always be there (menu, search, tasks badge, language) cannot
 * share one line, the bar grows a second line. A taller bar costs a little page height; a
 * clipped control costs the person the control. The row's items never shrink below their own
 * width, so the wrap is the only thing that gives.
 *
 * The bar's row is a child of the container because a container's own classes cannot be
 * container-queried: the gap and padding step down with the bar, and the header could not do that
 * for itself.
 *
 * The icon buttons are the kit button's `icon` size and the search field its `field` variant,
 * so no class here restates a box the kit already draws.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import ShellAvatar from './ShellAvatar.vue'
import ShellBreadcrumb from './ShellBreadcrumb.vue'
import ShellLocaleSwitcher from './ShellLocaleSwitcher.vue'
import ShellTasksBadge from './ShellTasksBadge.vue'
import ShellThemeSwitcher from './ShellThemeSwitcher.vue'
import ShellWordmark from './ShellWordmark.vue'
import type { NavigationEntry } from '../../types/navigation'

/** Props accepted by {@link ShellHeader}. */
const props = defineProps<{
  /** Navigation entries the shell is showing, used to name a module-backed screen in the breadcrumb. */
  entries: readonly NavigationEntry[]
  /** Whether the compact navigation drawer is open, reported as `aria-expanded`. */
  navigationOpen: boolean
  /** Whether the shell is in its compact presentation (a drawer, no sidebar column); owned by the layout. */
  compact: boolean
}>()

/** Events emitted by {@link ShellHeader}. */
const emit = defineEmits<{
  /** The user pressed the menu button; the layout owns the drawer. */
  (e: 'openNavigation'): void
  /** The user pressed the search field; the layout owns the palette. */
  (e: 'openPalette'): void
}>()

const { t } = useI18n()

/**
 * The classes that swap the search field and the search icon, as whole literal strings so the
 * stylesheet generator can see them. The field needs about 10rem of its own beside everything
 * that is always drawn, and a compact bar also carries the menu button and, from 38rem of the
 * window up, the wordmark's name, so it is given 6rem more before the field is worth drawing: the
 * field at 640px of window and a normal text size was 90px wide without that, and drawn as a stub
 * it is worse than an icon. Each pair is exact complements, so exactly one of them is drawn.
 */
const searchSwap: ComputedRef<{ field: string; icon: string; group: string }> = computed(() => {
  return props.compact
    ? { field: '@max-[42rem]:hidden', icon: '@min-[42rem]:hidden', group: '@max-[42rem]:ml-0' }
    : { field: '@max-[36rem]:hidden', icon: '@min-[36rem]:hidden', group: '@max-[36rem]:ml-0' }
})
</script>

<template>
  <header class="@container flex-none rounded-panel border border-border-subtle bg-surface-1">
    <div
      class="flex min-h-[calc(3.5rem-2px)] flex-wrap items-center gap-x-2 gap-y-1 px-2 py-1.5 @min-[38rem]:gap-x-4 @min-[38rem]:px-4"
    >
      <UiButton
        v-if="compact"
        variant="ghost"
        size="icon"
        class="shrink-0"
        :aria-label="t('app.shell.openNavigation')"
        :aria-expanded="navigationOpen"
        @click="emit('openNavigation')"
      >
        <UiIcon name="menu" size="lg" />
      </UiButton>

      <!-- On the desktop this cell is the sidebar column's own width (`w-60`, as in
           `ShellSidebar.vue`), so the search field starts at the same x as the content panel
           beside the sidebar. Compact, there is no column to line up with and the cell is as wide
           as its content. It is also as wide as its content on a bar too narrow to spare 15rem
           for alignment, which is what a large text size does to a wide window. On a bar under
           16rem (a phone at 150% text) it is not drawn at all: the landing screen is the first tab
           and the first entry of the drawer, and the room is worth more to the four controls. -->
      <div class="shrink-0" :class="compact ? '@max-[16rem]:hidden' : 'w-60 @max-[62rem]:w-auto @max-[16rem]:hidden'">
        <RouterLink
          :to="{ name: 'system-status' }"
          class="inline-flex rounded-control focus-visible:shadow-focus focus-visible:outline-none"
        >
          <ShellWordmark collapsible />
        </RouterLink>
      </div>

      <!-- The search is two buttons rather than one that restyles itself: a field with the
           placeholder and the shortcut hint on a bar wide enough for it (see `searchSwap`), and below that a bare icon
           button, the same `aria-label` and the same event. The one that does not apply is
           `display: none`, so the control is never in the accessibility tree twice. The field is
           a container of its own so the hint leaves when the field is too narrow for it, rather
           than when the window is. -->
      <UiButton
        variant="field"
        class="@container max-w-md min-w-40 flex-[1000_1_0%]"
        :class="searchSwap.field"
        :aria-label="t('app.shell.search')"
        aria-keyshortcuts="Control+K Meta+K"
        @click="emit('openPalette')"
      >
        <UiIcon name="search" size="md" />
        <span class="min-w-0 flex-1 truncate text-left">{{ t('app.shell.search') }}</span>
        <kbd
          class="shrink-0 rounded-mark border border-border-strong px-1.5 py-0.5 font-sans text-xs font-semibold whitespace-nowrap text-text-muted @max-[16rem]:hidden"
        >
          {{ t('app.shell.searchShortcut') }}
        </kbd>
      </UiButton>
      <UiButton
        variant="quiet"
        size="icon"
        class="ml-auto shrink-0"
        :class="searchSwap.icon"
        :aria-label="t('app.shell.search')"
        aria-keyshortcuts="Control+K Meta+K"
        @click="emit('openPalette')"
      >
        <UiIcon name="search" size="md" />
      </UiButton>

      <!-- It takes only what the search field leaves beyond its own maximum width (the field's
           grow factor is a thousand times its own), and the trail hides itself when the whole of
           it does not fit that (see `ShellBreadcrumb`), so it can never be cut to "Pa…" or touch
           the hint. The wrapper itself is not drawn below the width at which there could be
           anything left. -->
      <div class="hidden min-w-0 flex-[1_1_0%] @min-[70rem]:block">
        <ShellBreadcrumb :entries="entries" />
      </div>

      <div
        class="ml-auto flex shrink-0 items-center gap-1 @min-[38rem]:gap-2"
        :class="searchSwap.group"
      >
        <!-- Background work in flight, on every screen. It is a view of the tasks store rather
             than a fetch of its own, which is what lets it move the moment a task's stream
             reports a change — no navigation, no reload. It draws nothing while nothing runs. -->
        <ShellTasksBadge />
        <!-- Below `sm` the theme switch lives in the drawer's head instead (see the doc comment).
             The wrapper hides this copy with `display: none`, which also removes it from the
             accessibility tree, so the control is never present twice. -->
        <div class="max-sm:hidden">
          <ShellThemeSwitcher />
        </div>
        <ShellLocaleSwitcher />
        <ShellAvatar size="sm" class="ml-1 @max-[30rem]:hidden" />
      </div>
    </div>
  </header>
</template>
