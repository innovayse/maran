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
 * The row is the design's `menuRow` helper: 6px/9px padding, a 6px radius, a
 * 12.5px face, and the label pushed apart from whatever trails it (a shortcut,
 * a count) by 12px. A destructive command is the only one that changes colour.
 */

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
    <button
      type="button"
      :role="checked === undefined ? 'menuitem' : 'menuitemradio'"
      :aria-checked="checked === undefined ? undefined : String(checked)"
      tabindex="-1"
      :disabled="disabled"
      class="flex w-full items-center justify-between gap-3 rounded-md px-2 py-1.5 text-left text-xs transition-colors enabled:hover:bg-surface-3 focus-visible:bg-surface-3 focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-65"
      :class="destructive ? 'text-danger' : 'text-text-primary'"
      @click="onClick"
    >
      <slot />
      <!-- The tick repeats what `aria-checked` already says, for everyone who
           reads the screen rather than hears it. -->
      <svg
        v-if="checked === true"
        class="size-3 shrink-0 text-accent"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2.4"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M5 13l4 4 10-10" />
      </svg>
    </button>
  </li>
</template>
