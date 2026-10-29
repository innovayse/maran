<script setup lang="ts">
/**
 * Accessible router link styled as a navigation row: a real `<a>` (via
 * `RouterLink`) so browser navigation, middle-click and screen readers all
 * work, with an optional locked state for licence-gated entries that must
 * stay visible rather than disappear (rules/architecture.md: a disabled
 * module's routes resolve to the upgrade page, never a blank screen).
 * Used by the sidebar (`useNavigation` entries), by the collapsed rail, and by
 * plain in-content "go here" links (e.g. the 404 page's way back).
 *
 * **The current destination is a solid `--acf` fill with white text and the
 * accent glow under it.** White sits on `--acf` (5.14:1; `--ach` on hover,
 * 6.80:1) and never on `--ac`, which is 3.89:1 and fails for text. The state is
 * read from the classes `RouterLink` itself maintains, so a section stays lit
 * on its own child screens (`/sites/new` keeps "Sites" lit): the exact-match
 * `aria-current` is only set on the page itself, and a sidebar that went dark
 * the moment the user opened a record would stop saying where they are.
 * `aria-current="page"` is left to the router and still marks the exact page for
 * assistive technology.
 *
 * Inactive rows are `--t2` with a `--s2` hover (7.08:1 and 6.65:1 on a panel).
 *
 * **A long label wraps at word boundaries, never inside a word, and is capped at two lines.** The
 * label sits beside the LOCKED pill in a row that is only ~200px wide, and a caller's label span
 * that was allowed to wrap anywhere split "Monitoring" into "Monitorin" and "g". The row's label
 * cluster therefore forces `overflow-wrap: normal` on any text span it holds
 * (`[&>span]:wrap-normal`, which beats the caller's own `wrap-anywhere`) and clamps it to two
 * lines (`line-clamp-2`), so "System status" in a narrow row takes two lines rather than losing
 * letters, while the pill, which is `shrink-0`, keeps its width. A label that still does not fit
 * in two lines (a very long catalogue name under a large text size) ends in an ellipsis, so the
 * link carries the whole text as its `title`, read from the label cluster after every render: a
 * cut label is then always readable on hover, whichever caller drew it. Assistive technology
 * never needed it: the clamp is visual, so the full text is in the link's accessible name. The
 * compact rail is excluded because its only span is the screen-reader-only name, which must stay
 * `sr-only` (and the rail passes its own `title`).
 *
 * **The pill gives way before the label does.** A label cannot wrap inside a word, so when its
 * longest word and the pill do not fit one line (the Armenian "locked" is far wider than the
 * English), the row wraps and the pill drops to a line of its own under the label: the row grows
 * a line, and neither the label nor the pill is cut. The label cluster's `min-w-min` is what
 * makes it count as one word wide when the row decides to wrap.
 */
import { onMounted, onUpdated, ref, type Ref } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

/** Props accepted by {@link UiNavLink}. */
const props = defineProps<{
  /** Destination, passed straight to `RouterLink`'s `to`. */
  to: RouteLocationRaw
  /** Marks the entry as licence-locked: still navigable (to the upgrade page), but visually and semantically flagged. */
  locked?: boolean
  /**
   * Draws the row as a 40px icon-only square, for the collapsed rail. The caller
   * keeps the accessible name in the slot as screen-reader-only text: dropping
   * the label from the markup, not just from the screen, would leave the link
   * unnamed.
   */
  compact?: boolean
}>()

/** The label cluster, read for the text the link offers as its `title`. */
const cluster: Ref<HTMLElement | null> = ref(null)

/** The label's whole text, or `undefined` while there is none; the link's `title`. */
const fullText: Ref<string | undefined> = ref(undefined)

/**
 * Copies the label cluster's text into {@link fullText}. Assigning an unchanged string does not
 * re-render, so reading after every update cannot loop.
 * @returns Nothing.
 */
const readText = (): void => {
  const text = (cluster.value?.textContent ?? '').replace(/\s+/g, ' ').trim()
  fullText.value = text === '' || props.compact === true ? undefined : text
}

onMounted(readText)
onUpdated(readText)
</script>

<template>
  <!-- `focus-visible:shadow-focus` is the contract's ring; while focused it takes the
       place of the active row's glow, which is the right trade — the ring is
       the thing that must never be missing. -->
  <RouterLink
    :to="to"
    class="flex items-center gap-3 rounded-control text-sm font-medium text-text-secondary transition-colors hover:bg-surface-2 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none [&.router-link-active]:bg-accent-fill [&.router-link-active]:text-white [&.router-link-active]:shadow-glow [&.router-link-active]:hover:bg-accent-hover [&.router-link-active]:hover:text-white [&.router-link-active]:focus-visible:shadow-focus"
    :class="compact ? 'mx-auto size-10 justify-center' : 'w-full flex-wrap justify-between gap-y-1 px-3 py-2.5'"
    :aria-disabled="locked ? 'true' : undefined"
    :title="fullText"
  >
    <span
      ref="cluster"
      class="flex items-center gap-3"
      :class="compact ? 'flex-none' : 'min-w-min flex-1 [&>span]:min-w-0 [&>span]:line-clamp-2 [&>span]:wrap-normal'"
    >
      <slot />
    </span>
    <slot name="badge" />
  </RouterLink>
</template>
