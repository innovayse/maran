<script setup lang="ts">
/**
 * The heading block a routed page opens with: its single `<h1>`, an optional
 * small accent line above it (the navigation group, from the shell), the sentence under it, an optional smaller note,
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
 * - The title is display type in sentence case, and long words must not push the
 *   page sideways: a long Armenian word like «ՀԱՅՏԱՐԱՐՈՒԹՅՈՒՆՆԵՐ» is one
 *   unbreakable run, and Maran ships Armenian. So the text wraps with
 *   `break-words` (`overflow-wrap: break-word`), NOT `wrap-anywhere`. The two
 *   differ in exactly the way that matters here: `anywhere` lets every letter be
 *   a break point when the box's minimum width is computed, so a flex item that
 *   is squeezed collapses to one letter per line («SI / TE / S»); `break-word`
 *   leaves the minimum at the longest whole word and breaks inside a word only
 *   when that single word is wider than the box that holds it.
 * - A `mono` title is machine text — a domain, a path, an identifier — set in
 *   the mono face, because a path's case is meaningful and the title must be
 *   the exact string the person types.
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
 *
 * **`loading`: the heading of a record still on its way.** A detail page's title is the record's
 * own name, which is not known until the record arrives. With `loading` the eyebrow stays (it is
 * the panel's own word), and the title and the subtitle each draw a placeholder line inside their
 * own element, so each keeps the line height it will have and the loaded heading lands without a
 * shift. The placeholders are `aria-hidden`; the {@link UiLoadingRegion} around the page says
 * that it is loading.
 *
 * **`subtitleLoading`: a verdict still on its way under a fixed title.** Where the title is the
 * panel's own word and only the subtitle comes from the server (the health verdict on the status
 * screen), the title stays and the subtitle alone draws its placeholder line.
 */
import { computed, inject, onBeforeUnmount, useSlots, watch, type ComputedRef, type Ref } from 'vue'
import UiSkeleton from './UiSkeleton.vue'
import { PAGE_EYEBROW_KEY } from '../../utils/pageEyebrowKey'
import { PAGE_HEADING_KEY } from '../../utils/pageHeadingKey'
import type { PageHeading } from '../../types/pageHeading'

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
     * Whether the record the title names is still loading: the title, and the subtitle when one
     * is given, draw a placeholder line in place of their text.
     */
    loading?: boolean
    /**
     * Whether only the subtitle is still loading: it draws a placeholder line in place of its text
     * while the title stays. Implied by {@link loading}.
     */
    subtitleLoading?: boolean
  }>(),
  { subtitle: undefined, note: undefined, mono: false, loading: false, subtitleLoading: false },
)

/**
 * The caller's slots, read to decide whether the controls column is drawn at
 * all: "are there controls" is answered by whether the `actions` slot was
 * filled, not by a prop. That slot holds the controls that belong beside the
 * title, typically the page's one primary action.
 */
const slots = useSlots()

/**
 * The small accent line above the title: the navigation group the page sits in, provided by the
 * shell (`utils/pageEyebrowKey.ts`). Not a prop, so no page writes its own and none can name a
 * group the sidebar does not draw it under (issue #209). Absent outside the shell.
 */
const eyebrow: Readonly<Ref<string>> | null = inject(PAGE_EYEBROW_KEY, null)

/**
 * Where the shell keeps the heading on screen (`utils/pageHeadingKey.ts`), which its breadcrumb
 * ends a record screen's trail on. Absent outside the shell.
 */
const onScreen: Ref<PageHeading | null> | null = inject(PAGE_HEADING_KEY, null)

/** What this heading last wrote there, so leaving clears only its own report and never a successor's. */
let reported: PageHeading | null = null

/**
 * The typography classes for the `<h1>`.
 *
 * The display title is `text-xl` and steps up to `text-2xl` only when the
 * container is wide (`@3xl`), so a long word has room to fit at a sensible length.
 */
const titleClass: ComputedRef<string> = computed(() => {
  return props.mono
    ? 'font-mono text-xl font-semibold text-text-primary break-words @3xl:text-2xl'
    : 'font-display text-xl leading-tight font-black tracking-title text-text-primary break-words @3xl:text-2xl'
})

// The shell's breadcrumb reads the title and its loading state from here, so the trail's last
// crumb on a record screen is exactly the record's name this heading shows.
watch(
  (): PageHeading => {
    return { title: props.title, loading: props.loading }
  },
  (heading): void => {
    if (onScreen !== null) {
      reported = heading
      onScreen.value = heading
    }
  },
  { immediate: true },
)

// One screen's heading leaving and the next one's arriving can come in either order, so only the
// report this heading made is withdrawn.
onBeforeUnmount((): void => {
  if (onScreen !== null && onScreen.value === reported) {
    onScreen.value = null
  }
})
</script>

<template>
  <!-- The outer block is the container; the row inside it reads that container's width. A
       container cannot restyle itself by its own size, hence two elements. -->
  <div class="@container">
    <div
      class="flex flex-col items-start gap-3 @3xl:flex-row @3xl:flex-wrap @3xl:items-end @3xl:justify-between @3xl:gap-4"
    >
      <!-- `@3xl:min-w-64`: in the side-by-side layout the title never gets narrower than 16rem; if
           the actions would take it below that, the row wraps and they drop under the title. -->
      <div class="min-w-0 @3xl:min-w-64 @3xl:flex-1">
        <p
          v-if="eyebrow !== null"
          data-ui="eyebrow"
          class="mb-1 font-display text-xs font-extrabold text-accent-text break-words"
        >
          {{ eyebrow }}
        </p>
        <!-- While loading, each placeholder sits inline in the element its text will fill, so the
             element's own line box gives it the height the text will have. -->
        <h1 :class="titleClass">
          <span v-if="loading" class="inline-flex w-48 align-middle"><UiSkeleton shape="line" size="lg" /></span>
          <template v-else>{{ title }}</template>
        </h1>
        <p v-if="subtitle !== undefined" class="mt-1 text-sm text-text-secondary break-words">
          <span v-if="loading || subtitleLoading" class="inline-flex w-40 align-middle"><UiSkeleton shape="line" /></span>
          <template v-else>{{ subtitle }}</template>
        </p>
        <p v-if="note !== undefined" class="mt-0.5 text-sm text-text-muted break-words">
          {{ note }}
        </p>
      </div>
      <div v-if="slots.actions" class="flex flex-wrap items-center gap-2">
        <slot name="actions" />
      </div>
    </div>
  </div>
</template>
