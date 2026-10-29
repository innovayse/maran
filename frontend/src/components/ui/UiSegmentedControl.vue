<script setup lang="ts">
/**
 * A row of mutually exclusive options where the chosen one stays visible —
 * language, theme, a density or range picker, the tabs of a detail page.
 *
 * Not a dropdown and not a toggle, on purpose. A dropdown hides the
 * alternatives behind a click; a toggle shows only what will happen next, so
 * the user has to reason backwards to learn the current state. A segment shows
 * both at once, which is why it suits settings with two or three options and
 * suits nothing with ten.
 *
 * The control owns no copy: every label arrives already translated.
 *
 * WHY THE SEGMENTS ARE SET IN THE MONOSPACE FACE. What this control segments
 * is machine values — ranges (`24h`, `7d`), densities, locale codes — and a
 * monospace face is what tells the eye they are options to pick, not prose to
 * read. The panel's monospace is JetBrains Mono. The face has Latin and Cyrillic, not
 * Armenian: an Armenian label
 * falls through to Noto Sans Armenian, per the font stack in `main.css`, rather
 * than to a system face that does not match. Because labels in the three
 * languages differ a lot in length, the segments wrap onto a second row instead
 * of running off the edge of a 390px screen.
 *
 * KEYBOARD. Each segment is a real `<button>` and so a tab stop of its own; the
 * left and right arrows additionally move focus to the neighbour (wrapping at the
 * ends), so a keyboard user can sweep the row without tabbing through it. Moving
 * focus does not choose: Enter or Space does, as a button's does.
 *
 * LOOK AND MEASUREMENTS. The well is `bg-surface-2`; the chosen segment is a
 * solid `bg-accent-fill` with white text (5.14:1 — `bg-accent` would be 3.89:1
 * and fail), 3.43:1 against the well, and `aria-pressed` reports it to assistive
 * technology so colour is not the only cue. Unchosen segments are
 * `text-text-secondary` (6.65:1 on the well) and rise to `text-text-primary` on
 * `bg-surface-3` under the pointer. Each segment is at least 32px tall. The
 * segment radius is the well's minus its 4px padding, so the corners stay
 * concentric.
 */
import { type Ref, ref } from 'vue'

/** One choice offered by a {@link UiSegmentedControl}. */
export interface SegmentOption {
  /** Stable machine value bound through `v-model` when this option is chosen. */
  value: string
  /** Ready-to-render text for the option; the control holds no copy of its own. */
  label: string
}

/** Props accepted by {@link UiSegmentedControl}. */
defineProps<{
  /** The chosen option's value. */
  modelValue: string
  /** The options, in the order they should read. */
  options: readonly SegmentOption[]
  /** Accessible name for the group, already translated by the caller. */
  label: string
}>()

/** Events emitted by {@link UiSegmentedControl}. */
const emit = defineEmits<{
  /** The user chose an option. */
  (event: 'update:modelValue', value: string): void
}>()

/** The rendered option buttons, in document order, for arrow-key navigation. */
const optionButtons: Ref<HTMLButtonElement[]> = ref([])

/**
 * Moves focus to the neighbouring option, wrapping at the ends.
 *
 * The arrow keys move between options in addition to Tab, so a keyboard user
 * does not have to step through every option to cross the row.
 * @param index Position of the option the keyboard is on.
 * @param step 1 to move right, -1 to move left.
 * @returns Nothing; focus moves synchronously.
 */
const moveFocus = (index: number, step: number): void => {
  const buttons = optionButtons.value
  if (buttons.length === 0) {
    return
  }

  const next = (index + step + buttons.length) % buttons.length
  buttons[next]?.focus()
}
</script>

<template>
  <div
    class="flex w-fit max-w-full flex-wrap items-center gap-1 rounded-control border border-border-subtle bg-surface-2 p-1"
    role="group"
    :aria-label="label"
  >
    <button
      v-for="(option, index) in options"
      :key="option.value"
      ref="optionButtons"
      type="button"
      class="flex min-h-8 items-center gap-1.5 rounded-inset px-3 py-1 font-mono text-sm wrap-anywhere transition-colors focus-visible:shadow-focus focus-visible:outline-none"
      :class="
        modelValue === option.value
          ? 'bg-accent-fill font-semibold text-white'
          : 'text-text-secondary hover:bg-surface-3 hover:text-text-primary'
      "
      :aria-pressed="modelValue === option.value"
      @click="emit('update:modelValue', option.value)"
      @keydown.right.prevent="moveFocus(index, 1)"
      @keydown.left.prevent="moveFocus(index, -1)"
    >
      <!-- Optional leading mark: the caller draws it, so the control stays free
           of any icon set. -->
      <slot name="icon" :option="option"></slot>
      {{ option.label }}
    </button>
  </div>
</template>
