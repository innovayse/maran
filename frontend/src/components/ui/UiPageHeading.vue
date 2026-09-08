<script setup lang="ts">
/**
 * The heading block a routed page opens with: its `<h1>`, the sentence under
 * it, an optional smaller note, and the controls that belong beside the title.
 *
 * Twenty pages composed this by hand before this existed, retyping the same
 * `text-3xl font-semibold tracking-title text-text-primary` on the heading and
 * `mt-1 text-base text-text-secondary` on the subtitle every time. The type
 * lives here now, so a step change is one edit rather than twenty, and a page
 * cannot quietly land a step off the others — the failure mode the description
 * list already produced once, where one screen's label sat a step above the
 * same label on two others and nothing on either screen showed it.
 *
 * The block carries NO outer margin of its own: pages that need one pass
 * `class="mb-4"` and the one page whose section is a `gap-6` flex column
 * passes nothing. A margin baked in here would have to be undone there, and a
 * component whose spacing call sites fight is worse than no component.
 *
 * The `actions` slot decides the layout. With it, the title block and the
 * controls sit on one row, the controls aligned to the title's baseline edge;
 * without it the block is plain. That is one arrangement rather than a prop,
 * because "are there controls" is answered by whether the caller filled the
 * slot.
 */

import { useSlots } from 'vue'

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
     * a firewall preset leaves open. Muted and a step down, because it is read
     * once and then ignored.
     */
    note?: string
    /**
     * Whether the title is machine text — a domain, a path, an identifier —
     * and must be set in the monospace face. Same meaning and same default as
     * {@link UiDescriptionItem}'s `mono`: a panel that sets a domain in mono
     * in every table and every detail list must not set it in the body face
     * the one time it is the title of the screen.
     */
    mono?: boolean
  }>(),
  { subtitle: undefined, note: undefined, mono: false },
)

const slots = useSlots()

/**
 * The typography classes for the `<h1>`.
 * @returns The class string for the title element.
 */
const titleClass = (): string => {
  return props.mono
    ? 'font-mono text-3xl font-semibold tracking-title text-text-primary'
    : 'text-3xl font-semibold tracking-title text-text-primary'
}

/**
 * The layout classes for the block's root.
 *
 * `items-end` rather than `items-center`: the controls line up with the
 * BOTTOM of the title block, so a page with a subtitle and one without both
 * put their button on the same line as the last line of text.
 * @returns The class string for the root element, empty when there are no actions.
 */
const rootClass = (): string => {
  return slots.actions ? 'flex flex-wrap items-end justify-between gap-4' : ''
}
</script>

<template>
  <div :class="rootClass()">
    <div>
      <h1 :class="titleClass()">{{ title }}</h1>
      <p v-if="subtitle !== undefined" class="mt-1 text-base text-text-secondary">{{ subtitle }}</p>
      <p v-if="note !== undefined" class="mt-1 text-sm text-text-muted">{{ note }}</p>
    </div>
    <div v-if="slots.actions" class="flex items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
