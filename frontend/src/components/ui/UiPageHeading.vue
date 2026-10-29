<script setup lang="ts">
/**
 * The heading block a routed page opens with: its single `<h1>`, an optional
 * small accent line above it, the sentence under it, an optional smaller note,
 * and the controls that belong beside the title.
 *
 * The page's title is the document's one `<h1>`: the shell's app bar names the
 * product with a link, not a heading, so the outline reads from this title down.
 *
 * The type lives here so a step change is one edit and a page cannot quietly
 * land a step off the others — the failure the description list produced once,
 * where one screen's label sat a step above the same label on two others and
 * nothing on either screen showed it.
 *
 * The block carries NO outer margin of its own: pages that need one pass
 * `class="mb-4"`, and the one page whose section is a `gap-6` flex column
 * passes nothing. A margin baked in here would have to be undone there.
 *
 * Three rules shape the details:
 *
 * - The title is UPPERCASE display type, and uppercase words must not push the
 *   page sideways: a long Armenian word like «ՀԱՅՏԱՐԱՐՈՒԹՅՈՒՆՆԵՐ» is one
 *   unbreakable run, and Maran ships Armenian. So the text wraps with
 *   `break-words` (`overflow-wrap: break-word`), NOT `wrap-anywhere`. The two
 *   differ in exactly the way that matters here: `anywhere` lets every letter be
 *   a break point when the box's minimum width is computed, so a flex item that
 *   is squeezed collapses to one letter per line («SI / TE / S»); `break-word`
 *   leaves the minimum at the longest whole word and breaks inside a word only
 *   when that single word is wider than the box that holds it.
 * - A `mono` title is machine text — a domain, a path, an identifier — and is
 *   NOT uppercased: a path's case is meaningful, and a domain shown in capitals
 *   is no longer the string the person must type.
 * - The layout answers to the width of the CONTAINER, never the viewport. The
 *   block is a CSS container (`@container`) and the title and the actions sit
 *   side by side only when the block itself is at least `@3xl` (48rem) wide.
 *   Why not `lg:`: a viewport breakpoint cannot see what the screen spends the
 *   viewport on. With a large system font or text-only zoom the rem-sized
 *   sidebar doubles, the content panel shrinks to ~700px, and the viewport still
 *   reports 1280px — the old `lg:flex-row` then put a `shrink-0` actions column
 *   beside a title that was left a hundred pixels, and pushed the buttons off
 *   screen. Container units are rem-based, so `@3xl` grows with the font too:
 *   at 200% it needs 1536px of content width, and the block stacks instead.
 *
 * Layout: stacked by default — title, then the actions beneath it, aligned to
 * the start, so a primary action is never squeezed into a column that makes the
 * title wrap one word per line. From `@3xl` the actions sit beside the title and
 * line up with the BOTTOM of the text block (`items-end`), so a page with a
 * subtitle and one without both put the button on the line of the last text.
 * The row may wrap (`flex-wrap`) and the title keeps a minimum width, so even
 * the wide layout hands the actions the next line rather than starving the
 * title.
 */
import { computed, useSlots, type ComputedRef } from 'vue'

/**
 * What the heading block shows.
 */
const props = withDefaults(
  defineProps<{
    /** The page's title, already translated (or a name the server sent). */
    title: string
    /** The sentence under the title. Omitted on a page that needs none. */
    subtitle?: string
    /**
     * A smaller line under the subtitle for a caveat the user needs before
     * acting — the account prefix a database name will be given, the SSH port
     * a firewall preset leaves open. Muted, because it is read once and then
     * ignored.
     */
    note?: string
    /**
     * Whether the title is machine text — a domain, a path, an identifier —
     * and must be set in the monospace face, in its own case. Same meaning and
     * same default as {@link UiDescriptionItem}'s `mono`: a panel that sets a
     * domain in mono in every table and every detail list must not set it in
     * the display face the one time it is the title of the screen.
     */
    mono?: boolean
    /**
     * The small accent line above the title — the section of the panel the
     * page belongs to. Optional: a page that passes none draws just the title.
     */
    eyebrow?: string
  }>(),
  { subtitle: undefined, note: undefined, mono: false, eyebrow: undefined },
)

/**
 * The caller's slots, read to decide whether the controls column is drawn at
 * all: "are there controls" is answered by whether the `actions` slot was
 * filled, not by a prop. That slot holds the controls that belong beside the
 * title, typically the page's one primary action.
 */
const slots = useSlots()

/**
 * The typography classes for the `<h1>`.
 *
 * The display title is `text-2xl` and steps up to `text-3xl` only when the
 * container is wide (`@3xl`), so a long word has room to fit at a sensible length.
 */
const titleClass: ComputedRef<string> = computed(() => {
  return props.mono
    ? 'font-mono text-2xl font-semibold text-text-primary break-words @3xl:text-3xl'
    : 'font-display text-2xl leading-tight font-black tracking-title text-text-primary uppercase break-words @3xl:text-3xl'
})
</script>

<template>
  <!-- The outer block is the container; the row inside it reads that container's width. A
       container cannot restyle itself by its own size, hence two elements. -->
  <div class="@container">
    <div class="flex flex-col items-start gap-4 @3xl:flex-row @3xl:flex-wrap @3xl:items-end @3xl:justify-between @3xl:gap-6">
      <!-- `@3xl:min-w-64`: in the side-by-side layout the title never gets narrower than 16rem; if
           the actions would take it below that, the row wraps and they drop under the title. -->
      <div class="min-w-0 @3xl:min-w-64 @3xl:flex-1">
        <p
          v-if="eyebrow !== undefined"
          class="mb-1.5 font-display text-xs font-extrabold tracking-caps text-accent-text uppercase break-words"
        >
          {{ eyebrow }}
        </p>
        <h1 :class="titleClass">{{ title }}</h1>
        <p v-if="subtitle !== undefined" class="mt-2 text-base text-text-secondary break-words">
          {{ subtitle }}
        </p>
        <p v-if="note !== undefined" class="mt-1 text-sm text-text-muted break-words">{{ note }}</p>
      </div>
      <div v-if="slots.actions" class="flex flex-wrap items-center gap-2">
        <slot name="actions" />
      </div>
    </div>
  </div>
</template>
