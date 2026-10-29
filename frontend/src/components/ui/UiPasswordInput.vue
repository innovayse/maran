<script setup lang="ts">
/**
 * A password field with a reveal toggle. Composes {@link UiInput} rather than
 * repeating its markup, so the label binding, the error wiring and the focus
 * treatment stay defined in exactly one place (rules/vue.md: "UI comes from
 * components/ui").
 *
 * The toggle draws an eye rather than the words "Show"/"Hide": the panel's
 * icons now come from `lucide-vue-next` through {@link UiIcon}, so the glyph is
 * one the icon set already defines rather than a shape hand-drawn here.
 *
 * The eye alone would not say WHICH of "is hidden" and "will hide" it means, so
 * the words did not simply go away — they moved into the button's `aria-label`,
 * where they still name the ACTION the control performs. The icon reports the
 * state (an open eye while the value is revealed, a struck-through one while it
 * is masked), `aria-pressed` reports the same state to assistive technology,
 * and `aria-controls` ties the toggle to the field it changes. An icon-only
 * control with no accessible name would be a regression, not a simplification.
 *
 * A second, OPTIONAL control sits beside the toggle: `generate` adds a button
 * that fills the field with a strong random password. It is opt-in rather than
 * always present because it only makes sense where a password is being SET —
 * offering to generate one on a sign-in field would invite a person to lock
 * themselves out of their own account. Generating also reveals the value: a
 * password nobody can read is a password nobody can copy, so hiding it would
 * make the button useless.
 *
 * DISABLED IS FORWARDED, INPUTMODE IS NOT. `disabled` locks the field itself and its two
 * in-field controls too: a reveal toggle that still works on a locked field would show a value
 * the person cannot change, and a generate button would overwrite one they are not allowed to.
 * `inputmode` is deliberately absent: this field is always a password, which is free text, and
 * the toggle flips the input between `password` and `text` — a `numeric` keyboard would lock
 * out the letters and symbols a password manager or a strong password needs. The browser's
 * default keyboard for the type is the right one.
 *
 * PASTING IS ALLOWED. The field is a plain `<input>` with no `onpaste`, no
 * `autocomplete="off"` and nothing that blocks a password manager: WCAG 3.3.8
 * (Accessible Authentication) requires a person to be able to paste or have a
 * manager fill a password rather than recall and retype it.
 *
 * The two controls are raw `<button>`s (allowed here, in the kit), not
 * `UiButton`: they live inside the field's box, 4px from its edge, and
 * need a fixed 32px square that does not depend on another component's padding.
 * 32px clears the 24px minimum target of WCAG 2.5.8. Their icons are
 * `text-text-secondary` (6.65:1 on the field fill, 6.11:1 on the hover fill `bg-surface-3`).
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiIcon, { type UiIconName } from './UiIcon.vue'
import UiInput from './UiInput.vue'
import { generatePassword } from '../../utils/generate'

/** Props accepted by {@link UiPasswordInput}. */
const props = withDefaults(
  defineProps<{
    /** Current field value (`v-model` target). */
    modelValue: string
    /** Visible label text. */
    label: string
    /** Placeholder text shown when the field is empty. */
    placeholder?: string
    /** Marks the field required for assistive technology. */
    required?: boolean
    /** Already-translated validation message; when set, the field renders as invalid. */
    error?: string | null
    /** Native `autocomplete` token — `current-password` when signing in, `new-password` when setting one. */
    autocomplete?: string
    /**
     * Offers a button that fills the field with a generated password. Set it
     * only on a field that SETS a password, never on one that asks for an
     * existing one.
     */
    generate?: boolean
    /** Disables the field and its in-field controls, passed through to {@link UiInput}'s `disabled`. */
    disabled?: boolean
    /** Decorative leading glyph, passed through to {@link UiInput}'s `icon`. */
    icon?: UiIconName
  }>(),
  {
    placeholder: undefined,
    required: false,
    error: null,
    autocomplete: 'current-password',
    generate: false,
    disabled: false,
    icon: undefined,
  },
)

/** Events emitted by {@link UiPasswordInput}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

const { t } = useI18n()

/**
 * Shared look of the two in-field controls: a 32px square (above the 24px target
 * minimum), a radius concentric with the field's (the field's `--r-control` minus
 * the 4px the controls are inset by, which is `rounded-inset`, so the corners keep an even gap), and a
 * `shadow-focus` ring so a keyboard user can see which one they are on.
 */
const CONTROL_CLASSES: string =
  'inline-flex size-8 items-center justify-center rounded-inset text-text-secondary transition-colors hover:bg-surface-3 hover:text-text-primary focus-visible:shadow-focus focus-visible:outline-none disabled:cursor-not-allowed disabled:text-text-muted disabled:hover:bg-transparent'

/**
 * Whether the value is currently shown as text. Always starts hidden: a page
 * that reloads with a filled password must not put it on screen for whoever is
 * standing behind the person typing.
 */
const isRevealed: Ref<boolean> = ref(false)

/**
 * The accessible name of the toggle, which names the action it performs, not
 * the current state. It is not rendered as visible text — the icon fills that
 * space — but it is the control's only accessible name, so it stays translated
 * and is what a screen-reader user hears.
 */
const toggleLabel: ComputedRef<string> = computed(() => {
  return isRevealed.value ? t('app.auth.hidePassword') : t('app.auth.showPassword')
})

/**
 * The glyph the toggle shows, which reports the CURRENT state rather than the
 * action: an open eye while the value is on screen, a struck-through eye while
 * it is masked. The name beside it says what pressing will do, so the two
 * together are unambiguous where either alone would not be.
 */
const toggleIcon: ComputedRef<UiIconName> = computed(() => {
  return isRevealed.value ? 'eye' : 'eyeOff'
})

/**
 * Forwards the composed input's value to this component's own emit.
 * @param value The field's new value.
 * @returns Nothing; re-emits synchronously.
 */
const onUpdate = (value: string): void => {
  emit('update:modelValue', value)
}

/**
 * Flips between the masked and the revealed rendering of the value.
 * @returns Nothing; toggles local state.
 */
const onToggle = (): void => {
  isRevealed.value = !isRevealed.value
}

/**
 * Replaces the field's value with a freshly generated password and puts it on
 * screen. Revealing is deliberate and not a convenience: the value exists
 * nowhere else yet, so a person who cannot see it cannot record it.
 * @returns Nothing; emits the generated value.
 */
const onGenerate = (): void => {
  isRevealed.value = true
  emit('update:modelValue', generatePassword())
}
</script>

<template>
  <UiInput
    :model-value="props.modelValue"
    :label="props.label"
    :type="isRevealed ? 'text' : 'password'"
    :placeholder="props.placeholder"
    :required="props.required"
    :error="props.error"
    :autocomplete="props.autocomplete"
    :icon="props.icon"
    :disabled="props.disabled"
    @update:model-value="onUpdate"
  >
    <template #trailing="{ inputId }">
      <!-- Named by `aria-label` for the same reason as the toggle beside it:
           the icon announces nothing, so the button would otherwise reach a
           screen reader unnamed. -->
      <button
        v-if="props.generate"
        type="button"
        :disabled="props.disabled"
        :class="CONTROL_CLASSES"
        :aria-label="t('app.auth.generatePassword')"
        :aria-controls="inputId"
        @click="onGenerate"
      >
        <UiIcon name="dices" size="md" />
      </button>
      <!-- `aria-label` is the control's ONLY name now that the words are not
           rendered: the icon is decorative and announces nothing. `aria-controls`
           ties the toggle to the field it changes, and `aria-pressed` reports the
           current state — the two things the name deliberately does not say. -->
      <button
        type="button"
        :class="CONTROL_CLASSES"
        :aria-label="toggleLabel"
        :aria-controls="inputId"
        :aria-pressed="isRevealed"
        :disabled="props.disabled"
        @click="onToggle"
      >
        <UiIcon :name="toggleIcon" size="md" />
      </button>
    </template>
  </UiInput>
</template>
