<script setup lang="ts">
/**
 * On/off control for a setting that takes effect immediately (enable a
 * module, turn a firewall rule on). Use `UiCheckbox` instead when the value
 * is submitted later as part of a form — the difference is what the user
 * expects to happen on click, and it is worth two primitives.
 *
 * Implemented as a real `<button role="switch">` with `aria-checked`: a
 * button is keyboard-activatable and focusable with no scripted handling,
 * and `switch` is the role assistive technology announces as on/off rather
 * than checked/unchecked.
 */
import { computed, useId, type ComputedRef } from 'vue'

/** Props accepted by {@link UiSwitch}. */
const props = withDefaults(
  defineProps<{
    /** Whether the switch is on (`v-model` target). */
    modelValue: boolean
    /** Visible label text, always rendered — the track alone names nothing. */
    label: string
    /** Disables the control and marks it non-interactive for assistive tech. */
    disabled?: boolean
  }>(),
  { disabled: false },
)

/** Events emitted by {@link UiSwitch}. */
const emit = defineEmits<{
  /** Fired when the user flips the switch, carrying the new state. */
  (e: 'update:modelValue', value: boolean): void
}>()

/** Id of the visible label, used to name the switch button via `aria-labelledby`. */
const labelId: string = useId()

/**
 * Tailwind utility classes for the track, which carries the on/off color. The
 * design only draws the "on" track (the accent); "off" is the sunken surface
 * inside a strong border, the same treatment its other empty wells get.
 */
const trackClasses: ComputedRef<string> = computed(() =>
  props.modelValue ? 'border-accent bg-accent' : 'border-border-strong bg-surface-3',
)

/**
 * Tailwind utility classes positioning and colouring the knob.
 *
 * The colour is not constant: on the accent track a white knob is correct in
 * both themes, but the off track is `surface-3`, which is near-white in the
 * light theme — a white knob there is invisible. Off therefore uses the
 * secondary text token, which clears its track in both themes.
 */
const knobClasses: ComputedRef<string> = computed(() =>
  props.modelValue ? 'translate-x-3.5 bg-white' : 'translate-x-0 bg-text-secondary',
)

/**
 * Flips the switch, unless it is disabled.
 * @returns Nothing; emits synchronously.
 */
const onToggle = (): void => {
  if (props.disabled) {
    return
  }
  emit('update:modelValue', !props.modelValue)
}
</script>

<template>
  <div class="flex items-center gap-2">
    <button
      type="button"
      role="switch"
      :aria-checked="modelValue"
      :aria-labelledby="labelId"
      :disabled="disabled"
      class="inline-flex h-4.5 w-8 shrink-0 items-center rounded-full border p-0.5 transition-colors focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-65"
      :class="trackClasses"
      @click="onToggle"
    >
      <span
        class="ui-switch-knob size-3.5 rounded-full transition-transform duration-150 ease-out"
        :class="knobClasses"
        aria-hidden="true"
      />
    </button>
    <span :id="labelId" class="text-xs text-text-primary">{{ label }}</span>
  </div>
</template>

<style scoped>
/* The knob's travel is decoration; a reader who asked for less motion gets the
   end state immediately, and the colour change still reports the new value. */
@media (prefers-reduced-motion: reduce) {
  .ui-switch-knob {
    transition: none;
  }
}
</style>
