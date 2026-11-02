<script setup lang="ts">
/**
 * A navigation row for a page that is not built yet (issue #311): drawn where the page will sit, in
 * the same box as a `UiNavLink`, but greyed out and leading nowhere.
 *
 * It is not a link with a dead `href` and not a hidden row. It renders `role="link"` with
 * `aria-disabled="true"` so a screen reader lists it with the other destinations and says it cannot
 * be followed, and it stays on the Tab path (`tabindex="0"`) so a keyboard user can reach the
 * "coming soon" hint a mouse user reads as the tooltip. The hint is also in the accessible name, as
 * screen-reader-only text: a `title` alone is not announced reliably.
 *
 * The muted text is `--t3` (`text-text-muted`, 4.62:1 or better on every surface), so the row is
 * visibly disabled without falling under the contrast a label needs.
 */

/** Props accepted by {@link UiNavPlanned}. */
withDefaults(
  defineProps<{
    /** The "coming soon" hint, already translated; shown as the tooltip and read after the label. */
    hint: string
    /** Draws the row as a 36px icon-only square, for the collapsed rail. */
    compact?: boolean
    /** Draws the row as a child inside a sidebar submenu: shorter, text only. */
    nested?: boolean
  }>(),
  { compact: false, nested: false },
)
</script>

<template>
  <span
    role="link"
    aria-disabled="true"
    tabindex="0"
    data-nav-planned
    class="flex cursor-not-allowed items-center gap-2.5 rounded-control text-sm font-medium text-text-muted focus-visible:shadow-focus focus-visible:outline-none"
    :class="
      compact
        ? 'mx-auto size-9 justify-center'
        : nested
          ? 'w-full px-2.5 py-1 max-lg:py-3'
          : 'w-full px-2.5 py-1.5 max-lg:py-3'
    "
    :title="hint"
  >
    <span
      class="flex items-center gap-2.5"
      :class="compact ? 'flex-none' : 'min-w-min flex-1 [&>span]:min-w-0 [&>span]:line-clamp-2 [&>span]:wrap-normal'"
    >
      <slot />
    </span>
    <span class="sr-only">{{ hint }}</span>
  </span>
</template>
