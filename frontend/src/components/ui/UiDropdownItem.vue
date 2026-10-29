<script setup lang="ts">
/**
 * One command inside a `UiDropdown` panel. Renders a real `<button>` with
 * `role="menuitem"` and `tabindex="-1"`: in the ARIA menu pattern the panel
 * has a single tab stop and the owning dropdown moves real focus between
 * items with the arrow keys, so an item must be focusable programmatically
 * but not by Tab.
 *
 * Emits `select` and lets the dropdown close itself — an item never reaches
 * into its parent's state. Only `UiDropdown` renders it.
 *
 * The row is a `rounded-row` button inside the panel's 6px padding, which makes
 * its corners concentric with the panel's 12px ones. The label is pushed apart
 * from whatever trails it (a tick, a shortcut, a count) by 12px. A destructive
 * command is the only one that changes colour.
 *
 * **Contrast, measured (dark / light theme).** The hover and focus fill is
 * `bg-surface-2` over the `bg-surface-1` panel. Primary text on it is 14.75 /
 * 16.31:1. `text-danger` on it is 4.50 / 5.10:1 — in the dark theme exactly at
 * the 4.5:1 line, which is why a destructive row must never be moved to
 * `bg-surface-3` (4.13:1, a failure); re-measure if either token ever moves.
 * The chosen item's tick, `text-accent-text`, is 5.70 / 6.16:1 on that fill.
 * A disabled row's `text-text-muted` is 5.03 / 4.91:1.
 *
 * **Focus** is `shadow-focus` (`shadow-focus-danger` on a destructive row) plus
 * the hover fill, so a keyboard user sees both where they are and which row it
 * is.
 */
import UiIcon from './UiIcon.vue'

/** Props accepted by {@link UiDropdownItem}. */
const props = withDefaults(
  defineProps<{
    /** Disables the command and marks it non-interactive for assistive tech. */
    disabled?: boolean
    /** Marks the command as destructive, which is rendered in the danger color. */
    destructive?: boolean
    /**
     * Whether this item is the chosen one in a menu that picks one of a set — a
     * language, a theme, a sort order.
     *
     * Three-valued on purpose. `undefined` means the item is an ordinary
     * command and stays `role="menuitem"`. `true` or `false` means it is one of
     * a set: EVERY item in that set must pass the prop, because the role and
     * `aria-checked` are what tell a screen-reader user these are alternatives.
     * Giving the role only to the chosen item leaves the others announced as
     * plain commands, so the user never learns a choice was on offer.
     */
    checked?: boolean
  }>(),
  { disabled: false, destructive: false, checked: undefined },
)

/** Events emitted by {@link UiDropdownItem}. */
const emit = defineEmits<{
  /** Fired when the command is activated by pointer or keyboard. */
  (e: 'select'): void
}>()

/**
 * Reports the command as chosen, unless it is disabled.
 * @returns Nothing; emits synchronously.
 */
const onClick = (): void => {
  if (props.disabled) {
    return
  }
  emit('select')
}
</script>

<template>
  <li role="none">
    <!-- The colour and the ring are chosen together per row kind, in one
         conditional class, so a destructive row can never end up with the blue
         ring on a red label. -->
    <button
      type="button"
      :role="checked === undefined ? 'menuitem' : 'menuitemradio'"
      :aria-checked="checked"
      tabindex="-1"
      :disabled="disabled"
      class="flex min-h-9 w-full items-center justify-between gap-3 rounded-row px-3 py-2 text-left text-sm transition-colors enabled:hover:bg-surface-2 focus-visible:bg-surface-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted"
      :class="destructive ? 'text-danger focus-visible:shadow-focus-danger' : 'text-text-primary focus-visible:shadow-focus'"
      @click="onClick"
    >
      <slot />
      <!-- The tick repeats what `aria-checked` already says, for everyone who
           reads the screen rather than hears it. -->
      <UiIcon v-if="checked === true" name="check" size="sm" class="text-accent-text" />
    </button>
  </li>
</template>
