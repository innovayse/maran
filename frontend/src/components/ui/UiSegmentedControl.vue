<script setup lang="ts">
/**
 * A row of mutually exclusive options where the chosen one stays visible —
 * language, theme, a density or range picker.
 *
 * Not a dropdown and not a toggle, on purpose. A dropdown hides the
 * alternatives behind a click; a toggle shows only what will happen next, so
 * the user has to reason backwards to learn the current state. A segment shows
 * both at once, which is why it suits settings with two or three options and
 * suits nothing with ten.
 *
 * The control owns no copy: every label arrives already translated.
 *
 * The segment face is the design's `segBtn` verbatim: a 4px radius inside the
 * 8px well, and the mono family — the design sets every segment in Azeret Mono
 * because the things it segments are machine values (ranges, densities, locale
 * codes), and mono is what tells the eye they are options, not prose.
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
 * A radio group's own keyboard behaviour: the arrow keys move between options
 * rather than Tab, so the whole control is a single tab stop and a keyboard
 * user does not have to step through every language to leave the header.
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
    class="flex items-center gap-0.5 rounded-lg border border-border-subtle bg-surface-2 p-0.5"
    role="group"
    :aria-label="label"
  >
    <button
      v-for="(option, index) in options"
      :key="option.value"
      ref="optionButtons"
      type="button"
      class="flex items-center gap-1.5 rounded-sm px-2 py-0.5 font-mono text-xs transition-colors focus-visible:shadow-focus focus-visible:outline-none"
      :class="
        modelValue === option.value
          ? 'bg-surface-1 font-semibold text-text-primary'
          : 'text-text-muted hover:text-text-secondary'
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
