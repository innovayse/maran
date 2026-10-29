<script setup lang="ts">
/**
 * The panel's only text-input primitive. Always binds a real `<label>` to
 * its `<input>` via `for`/`id` so the field is accessible even when the
 * caller forgets — a label is a required prop, not an optional slot — and
 * exposes an error state wired to `aria-invalid`/`aria-describedby` so
 * assistive tech announces validation problems (rules/vue.md: "UI comes
 * from components/ui"). A `trailing` slot places one group of controls inside
 * the field's right edge — the reveal toggle of {@link UiPasswordInput} is its
 * only caller — so those controls share the field's box instead of sitting
 * beside it, and the input reserves room for them rather than running underneath.
 *
 * HOW THE FIELD IS FOUND. The decorative borders are 1.2 to 1.5:1 against a
 * panel (`--b1` 1.24, `--b2` 1.47), and the field's fill (`bg-surface-2`) is only
 * 1.06:1 against the panel it sits on — so with either of them the dark theme
 * showed nothing at all where a field is, below the 3:1 WCAG 1.4.11 asks of the
 * visual information that identifies a control. The boundary is therefore its own
 * token, `border-border-control` (`--bc`): 3.57:1 on `--s1`, 3.36:1 on `--s2` and
 * 3.08:1 on `--s3` in dark, 3.63 / 3.30 / 3.07 in light. It is a token rather than
 * a reuse of `text-text-muted`, because a text colour that happens to pass would
 * drift the day the text tier is retuned. The label always visible above the
 * field and the 2px focus ring (`shadow-focus`, 4.8:1) remain on top of it.
 *
 * DISABLED. A disabled field keeps its fill and drops everything that invites a click: the
 * boundary falls back to the decorative `border-border-subtle` (WCAG 1.4.11 exempts inactive
 * components from the 3:1 a live control owes), the pointer becomes `not-allowed`, the hover
 * border is `enabled:`-only, and the typed value and placeholder are `text-text-muted`. That
 * text is NOT dimmed with `opacity`: a half-transparent value is the one a person has to read
 * to learn what is locked, and `text-text-muted` on `bg-surface-2` measures 5.03:1 in dark and
 * 4.91:1 in light — over the 4.5:1 that WCAG 1.4.3 asks of text even when it is inactive. The
 * label above stays `text-text-secondary` (6.65:1), so the field is still named in full.
 * Mobile Safari pins a disabled input's glyphs to its own grey through
 * `-webkit-text-fill-color`, which ignores `color`, so that property is set to the same token.
 *
 * Error text is `text-danger`: 4.79:1 on a panel, 4.502:1 on `bg-surface-2` (at
 * the line) and 4.13:1 on `bg-surface-3` (fails) — a form must not sit on the
 * pressed surface.
 */
import { computed, useId, type ComputedRef } from 'vue'
import UiIcon, { type UiIconName } from './UiIcon.vue'

/** Props accepted by {@link UiInput}. */
const props = withDefaults(
  defineProps<{
    /** Current field value (`v-model` target). */
    modelValue: string
    /** Visible label text, always rendered — never omitted for a placeholder instead. */
    label: string
    /** Native `type` attribute. */
    type?: 'text' | 'email' | 'password'
    /** Placeholder text shown when the field is empty. */
    placeholder?: string
    /**
     * Marks the field required for assistive technology. Rendered as `aria-required`, not the
     * native `required` attribute: forms are `novalidate` (rules/vue.md), so the browser's own
     * bubbles must never appear — they are unstyled, untranslatable, and would compete with the
     * field's own error message.
     */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
    /**
     * Native `autocomplete` token. A real prop rather than a fallthrough attribute: this
     * component renders a wrapper `div`, so an attribute written on the tag would land on the
     * wrapper and never reach the input. Password managers rely on it, and a sign-in form that
     * cannot be filled by one pushes people towards passwords they can retype from memory.
     */
    autocomplete?: string
    /**
     * Sets the typed value in the mono face, for machine text: a command, a path, a cron
     * field, an address. A prop rather than a class on the tag, because a class written on
     * the tag lands on the wrapper and sets the LABEL in mono too — and the label is prose.
     */
    mono?: boolean
    /**
     * Native `inputmode`: which on-screen keyboard a phone raises (`numeric` for a one-time
     * code, `email`, `tel`, ...). A real prop for the same reason as `autocomplete` — written
     * on the tag it would land on the wrapper `div` and the keyboard would never change. It
     * only picks the keyboard; it does not validate, so a field that takes letters too (a
     * recovery code) must not claim `numeric`.
     */
    inputmode?: 'text' | 'numeric' | 'email' | 'url' | 'search' | 'tel'
    /**
     * Disables the native input: it cannot be focused, edited or submitted. A real prop, not a
     * fallthrough `disabled` attribute, because that would land on the wrapper `div` and do
     * nothing at all — the field would look live and accept typing while the caller believed it
     * locked. See "DISABLED" above for how it is drawn.
     */
    disabled?: boolean
    /**
     * A decorative glyph inside the field's left edge that says what kind of value goes in —
     * an envelope for an address, a padlock for a password — so a form is recognisable at a glance
     * before its labels are read. Decorative only: the visible label is still what names the field,
     * so the icon is hidden from assistive technology and never stands in for the label.
     */
    icon?: UiIconName
  }>(),
  {
    type: 'text',
    placeholder: undefined,
    required: false,
    error: null,
    autocomplete: undefined,
    mono: false,
    inputmode: undefined,
    disabled: false,
    icon: undefined,
  },
)

/** Events emitted by {@link UiInput}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

/** Stable, unique id pair for this instance's `<label for>`/`<input id>` and error association. */
const inputId: string = useId()
const errorId: string = `${inputId}-error`

/** Whether the field is currently in an error state. */
const hasError: ComputedRef<boolean> = computed(() => {
  return props.error !== null && props.error !== undefined
})

/**
 * Forwards the native input value to the `update:modelValue` emit.
 * @param event The native input event.
 * @returns Nothing; re-emits synchronously.
 */
const onInput = (event: Event): void => {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <!-- The label turns red with the field: an invalid field is readable as
         invalid from the label down, not from the border (which is too faint to
         carry it). The message below says WHY, so colour is never the only cue. -->
    <label
      :for="inputId"
      class="text-sm font-medium wrap-anywhere"
      :class="hasError ? 'text-danger' : 'text-text-secondary'"
      >{{ label }}</label
    >
    <!-- `relative` only so a trailing control can be positioned over the field's
         right edge; with no `trailing` slot the wrapper adds nothing. -->
    <div class="relative">
      <!-- 16px below `sm`, 14px from `sm` up: a phone browser zooms the page when
           it focuses a field set in anything under 16px, and the zoom is not undone
           on blur. -->
      <input
        :id="inputId"
        :type="type"
        :value="modelValue"
        :placeholder="placeholder"
        :autocomplete="autocomplete"
        :inputmode="inputmode"
        :disabled="disabled"
        :aria-required="required ? 'true' : undefined"
        :aria-invalid="hasError"
        :aria-describedby="hasError ? errorId : undefined"
        class="ui-input-field min-h-10 w-full rounded-control border bg-surface-2 py-2 text-base text-text-primary transition-colors placeholder:text-text-muted focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:opacity-100 disabled:[-webkit-text-fill-color:var(--t3)] sm:text-sm"
        :class="[
          hasError
            ? 'border-danger focus-visible:shadow-focus-danger'
            : 'border-border-control enabled:hover:border-text-muted focus-visible:border-accent focus-visible:shadow-focus disabled:border-border-subtle',
          $slots.trailing ? 'pr-18' : 'pr-3',
          icon ? 'pl-10' : 'pl-3',
          mono ? 'font-mono' : '',
        ]"
        @input="onInput"
      />
      <!-- Decorative: UiIcon hides itself from assistive technology, and the label above
           already names the field. `pointer-events-none` so a click on the glyph still
           lands in the input underneath. -->
      <span
        v-if="icon"
        class="pointer-events-none absolute inset-y-0 left-3.5 flex items-center"
        :class="hasError ? 'text-danger' : 'text-text-muted'"
      >
        <UiIcon :name="icon" size="sm" />
      </span>
      <span v-if="$slots.trailing" class="absolute inset-y-0 right-1 flex items-center">
        <slot name="trailing" :input-id="inputId" />
      </span>
    </div>
    <p v-if="hasError" :id="errorId" class="text-sm text-danger wrap-anywhere">{{ error }}</p>
  </div>
</template>

<style scoped>
/*
 * A browser's autofill paints its own pale background and dark text over the
 * field, which on the dark theme leaves light text on a light box. Painting
 * over it with a box-shadow would also erase the focus ring (it is a
 * box-shadow), so the browser's background is instead held back indefinitely by
 * a transition that never finishes, and the text colour is pinned to the token.
 */
.ui-input-field:-webkit-autofill {
  -webkit-text-fill-color: var(--t1);
  caret-color: var(--t1);
  transition: background-color 600000s 0s;
}
</style>
