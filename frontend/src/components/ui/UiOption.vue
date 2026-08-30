<script setup lang="ts">
/**
 * One row inside `UiSelect`'s listbox. Split out from the select so the row's
 * ARIA contract lives in one place: it is an `<li role="option">` carrying
 * `aria-selected` and a caller-supplied id, which the owning combobox points
 * at with `aria-activedescendant` while keyboard focus stays on the trigger.
 *
 * It is deliberately not focusable and not a button: in the listbox pattern
 * the option is never the focused element, so giving it a tab stop would
 * break the very keyboard model it exists to implement. Only `UiSelect`
 * renders it.
 */

/** Props accepted by {@link UiOption}. */
const props = withDefaults(
  defineProps<{
    /** Value reported when this row is chosen. */
    value: string
    /** Ready-to-render text for the row; the primitive holds no copy of its own. */
    label: string
    /** DOM id, assigned by the owning select so it can reference the row as the active descendant. */
    id: string
    /** Whether this row is the select's current value. */
    selected: boolean
    /** Whether this row is the keyboard's current position within the open list. */
    active: boolean
    /** Whether the row is visible but not choosable. */
    disabled?: boolean
  }>(),
  { disabled: false },
)

/** Events emitted by {@link UiOption}. */
const emit = defineEmits<{
  /** Fired when the row is chosen with the pointer, carrying its value. */
  (e: 'select', value: string): void
  /** Fired when the pointer moves onto the row, so the select can follow it with the active position. */
  (e: 'activate', value: string): void
}>()

/**
 * Reports the row as chosen, unless it is disabled.
 * @returns Nothing; emits synchronously.
 */
const onClick = (): void => {
  if (props.disabled) {
    return
  }
  emit('select', props.value)
}

/**
 * Moves the active position onto this row as the pointer enters it, keeping the
 * mouse and keyboard highlights from disagreeing.
 * @returns Nothing; emits synchronously.
 */
const onPointerEnter = (): void => {
  if (props.disabled) {
    return
  }
  emit('activate', props.value)
}
</script>

<template>
  <li
    :id="id"
    role="option"
    :aria-selected="selected"
    :aria-disabled="disabled ? 'true' : undefined"
    class="flex cursor-pointer items-center justify-between gap-2.5 rounded-md px-2 py-1.5 text-base text-text-primary"
    :class="[
      active ? 'bg-surface-3' : '',
      selected ? 'font-medium' : '',
      disabled ? 'cursor-not-allowed opacity-65' : '',
    ]"
    @click="onClick"
    @pointerenter="onPointerEnter"
  >
    <span>{{ label }}</span>
    <svg
      v-if="selected"
      class="size-3 shrink-0 text-accent"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      aria-hidden="true"
    >
      <path d="M20 6L9 17l-5-5" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  </li>
</template>
