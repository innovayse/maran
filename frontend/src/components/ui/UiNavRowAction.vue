<script setup lang="ts">
/**
 * A small icon button laid over the end of a navigation row (issue #318: the favourites star),
 * the sibling of the toggle `UiNavSection` lays over a section's row. It cannot sit inside the
 * row's link, because a button inside a link is invalid markup and would share the link's
 * accessible name, so the caller wraps the link and this control in a `relative` box, gives the
 * link `trailing` (room at its end) and the class `peer`.
 *
 * **The colour follows the row.** On an unlit row it is `text-text-muted` (5.36:1 on a panel),
 * stepping up to primary text on the `bg-surface-3` hover; once `pressed` it rests at primary text.
 * On the lit row (the link carries `router-link-active`, which the `peer` marker lets this button
 * read) it sits on `--acf`, so it is white (5.14:1) with the `--ach` hover (6.80:1); white never
 * sits on `--ac`.
 *
 * The icon goes in the default slot and is decorative, so the control carries its own translated
 * name. `pressed` is for a control that stays in a state (pinned or not): it becomes `aria-pressed`;
 * leave it unset for a one-shot action. Below 1024px the target grows to 36px with the drawer's rows.
 */

/** Props accepted by {@link UiNavRowAction}. */
defineProps<{
  /** The control's accessible name, already translated. */
  label: string
  /** Whether the control is in its "on" state, or undefined when it is not a toggle. */
  pressed?: boolean
  /** Disables the control and marks it non-interactive for assistive technology. */
  disabled?: boolean
}>()

/** Events emitted by {@link UiNavRowAction}. */
const emit = defineEmits<{
  /** The control was activated by pointer or keyboard. */
  (e: 'press'): void
}>()

/**
 * Reports that the control was activated.
 * @returns Nothing; the event is emitted synchronously.
 */
const press = (): void => {
  emit('press')
}
</script>

<template>
  <button
    type="button"
    class="absolute top-1/2 right-1 flex size-7 -translate-y-1/2 items-center justify-center rounded-inset transition-colors hover:bg-surface-3 focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50 peer-[.router-link-active]:text-white peer-[.router-link-active]:hover:bg-accent-hover max-lg:size-9"
    :class="pressed === true ? 'text-text-primary' : 'text-text-muted hover:text-text-primary'"
    :aria-label="label"
    :aria-pressed="pressed === undefined ? undefined : pressed ? 'true' : 'false'"
    :disabled="disabled"
    @click="press"
  >
    <slot />
  </button>
</template>
