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
  <div class="flex items-center gap-2">
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
      class="inline-flex size-3.5 shrink-0 items-center justify-center rounded-full border bg-surface-2 transition-colors peer-focus-visible:border-accent peer-focus-visible:shadow-focus peer-disabled:opacity-65"
      :class="checked ? 'border-accent' : 'border-border-strong'"
      aria-hidden="true"
    >
      <span v-if="checked" class="size-2 rounded-full bg-accent" />
    </span>
    <label
      :for="fieldId"
      class="text-xs text-text-primary"
      :class="disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'"
      >{{ label }}</label
    >
  </div>
</template>
