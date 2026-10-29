<script setup lang="ts">
/**
 * One choice inside a `UiRadioGroup`. Like `UiCheckbox`, the visible dot is
 * decoration beside a real, visually hidden `<input type="radio">`: sharing a
 * `name` with its siblings is what gives the group its ARIA radiogroup
 * behaviour — arrow-key roving between choices, one tab stop for the whole
 * set — implemented by the browser rather than by key handling of our own,
 * which is both less code and more correct on every platform.
 *
 * Not intended for standalone use: a radio outside a group has no way to be
 * unselected, so render it through `UiRadioGroup`.
 *
 * The input is `sr-only`, so a test or a pointer cannot click the 1px element
 * itself: the click target is the label, which is why the input sits inside it
 * and the label's text is the one place the choice's name is written (a second
 * copy would make `getByText` ambiguous).
 *
 * LOOK AND MEASUREMENTS. Same family as `UiCheckbox`. Unselected: a
 * `bg-surface-2` disc with a `border-border-control` outline (`--bc`, the control-edge
 * token; the `--b2` border would be 1.47:1). Against the surface it sits on: dark 3.82
 * on the page, 3.57 on `--s1`, 3.36 on `--s2`, 3.08 on `--s3`; light 3.39, 3.63, 3.30,
 * 3.07 — all over the 3:1 of WCAG 1.4.11. Selected: a
 * `bg-accent-fill` disc (3.65:1 against a panel) holding a white dot at 5.14:1,
 * so selection is carried by the dot's presence as well as by colour. The row is
 * at least 32px tall.
 */
import { useId } from 'vue'

/** Props accepted by {@link UiRadio}. */
const props = withDefaults(
  defineProps<{
    /** Value this choice contributes when selected. */
    value: string
    /** Visible label text, always rendered — the dot alone names nothing. */
    label: string
    /** Shared group name; all radios of one group must pass the same value. */
    name: string
    /** Whether this choice is the group's current selection. */
    checked: boolean
    /** Disables this choice and marks it non-interactive for assistive tech. */
    disabled?: boolean
  }>(),
  { disabled: false },
)

/** Events emitted by {@link UiRadio}. */
const emit = defineEmits<{
  /** Fired when this choice becomes the selection, carrying its value. */
  (e: 'select', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>`. */
const fieldId: string = useId()

/**
 * Reports this choice as the new selection. The group owns the value, so the
 * radio never mutates anything itself.
 * @returns Nothing; emits synchronously.
 */
const onChange = (): void => {
  emit('select', props.value)
}
</script>

<template>
  <label
    :for="fieldId"
    class="flex min-h-8 w-fit max-w-full items-center gap-2.5 text-sm text-text-primary"
    :class="disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'"
  >
    <input
      :id="fieldId"
      type="radio"
      class="peer sr-only"
      :name="name"
      :value="value"
      :checked="checked"
      :disabled="disabled"
      @change="onChange"
    />
    <span
      class="inline-flex size-5 shrink-0 items-center justify-center rounded-full border transition-colors peer-focus-visible:shadow-focus"
      :class="checked ? 'border-accent-fill bg-accent-fill' : 'border-border-control bg-surface-2'"
      aria-hidden="true"
    >
      <span v-if="checked" class="size-2 rounded-full bg-white" />
    </span>
    <span class="min-w-0 wrap-anywhere">{{ label }}</span>
  </label>
</template>
