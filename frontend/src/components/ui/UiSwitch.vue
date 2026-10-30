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
 *
 * LOOK AND MEASUREMENTS. The state is carried by the knob's POSITION and the
 * track's fill together, never by colour alone. On: a `bg-accent-fill` track
 * (3.65:1 against a panel) with a white knob (5.14:1 on the fill; `bg-accent`
 * would be 3.89:1). Off: a `bg-surface-3` track outlined in `border-border-control`
 * (`--bc`, the control-edge token; the `--b2` border would be 1.47:1, and the track is
 * the only thing that says "this is a switch"). The outline against the surface the
 * switch sits on: dark 3.82 on the page, 3.57 on `--s1`, 3.36 on `--s2`, 3.08 on
 * `--s3`; light 3.39, 3.63, 3.30, 3.07 — over the 3:1 of WCAG 1.4.11. The off knob is
 * `bg-text-secondary` (6.11:1 against the track in dark, 5.06 in light). The knob is
 * a fixed colour pair per state because
 * a white knob is invisible on the off track in the light theme, where
 * `surface-3` is near-white. The track is 36 by 20px and the whole row,
 * including the label, is the click target.
 *
 * LOADING. Inside a {@link UiForm} with `loading`, the label stays (it is the form's real text),
 * the track is replaced by a {@link UiSkeleton} of its footprint, and the label does not toggle:
 * there is no value to flip yet.
 */
import { computed, useId, type ComputedRef } from 'vue'
import { useFormLoading } from '../../composables/useFormLoading'
import UiSkeleton from './UiSkeleton.vue'

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

/** Whether the enclosing form is still loading, so the track is drawn as a placeholder. */
const formLoading = useFormLoading()

/** Tailwind utility classes for the track, which carries the on/off fill and outline. */
const trackClasses: ComputedRef<string> = computed(() => {
  return props.modelValue
    ? 'border-accent-fill bg-accent-fill'
    : 'border-border-control bg-surface-3'
})

/**
 * Tailwind utility classes positioning and colouring the knob. Track inner width
 * is 36px minus the 1px border and 2px inset on each side = 30px; a 14px knob
 * leaves 16px of travel, which is `translate-x-4`.
 */
const knobClasses: ComputedRef<string> = computed(() => {
  return props.modelValue ? 'translate-x-4 bg-white' : 'translate-x-0 bg-text-secondary'
})

/**
 * Flips the switch, unless it is disabled.
 * @returns Nothing; emits synchronously.
 */
const onToggle = (): void => {
  if (props.disabled || formLoading.value) {
    return
  }
  emit('update:modelValue', !props.modelValue)
}
</script>

<template>
  <div class="flex min-h-choice items-center gap-2">
    <UiSkeleton v-if="formLoading" shape="block" control="switch" />
    <button
      v-else
      type="button"
      role="switch"
      :aria-checked="modelValue"
      :aria-labelledby="labelId"
      :disabled="disabled"
      class="inline-flex h-5 w-9 shrink-0 items-center rounded-full border p-0.5 transition-colors focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-65"
      :class="trackClasses"
      @click="onToggle"
    >
      <span
        class="ui-switch-knob size-3.5 rounded-full transition-transform duration-150 ease-out"
        :class="knobClasses"
        aria-hidden="true"
      />
    </button>
    <!-- The label toggles too, so the target is the whole row and not only the
         44px track. It is a span, not a `<label for>`: the button is named by
         `aria-labelledby`, and a label for a button would announce twice. -->
    <span
      :id="labelId"
      class="min-w-0 text-sm text-text-primary wrap-anywhere"
      :class="disabled ? 'cursor-not-allowed opacity-65' : 'cursor-pointer'"
      @click="onToggle"
      >{{ label }}</span
    >
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
