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
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from './UiButton.vue'
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
  }>(),
  {
    placeholder: undefined,
    required: false,
    error: null,
    autocomplete: 'current-password',
    generate: false,
  },
)

/** Events emitted by {@link UiPasswordInput}. */
const emit = defineEmits<{
  /** Fired on every input, carrying the field's new value. */
  (e: 'update:modelValue', value: string): void
}>()

const { t } = useI18n()

/**
 * Whether the value is currently shown as text. Always starts hidden: a page
 * that reloads with a filled password must not put it on screen for whoever is
 * standing behind the person typing.
 */
const isRevealed: Ref<boolean> = ref(false)

/**
 * The accessible name of the toggle, which names the action it performs, not
 * the current state. It is no longer rendered as visible text — the icon holds
 * that space now — but it is still the control's only name, so it stays
 * translated and stays required reading for a screen-reader user.
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
    @update:model-value="onUpdate"
  >
    <template #trailing="{ inputId }">
      <!-- Named by `aria-label` for the same reason as the toggle beside it:
           the icon announces nothing, so the button would otherwise reach a
           screen reader unnamed. -->
      <UiButton
        v-if="props.generate"
        variant="ghost"
        :aria-label="t('app.auth.generatePassword')"
        :aria-controls="inputId"
        class="px-2.5 py-1"
        @click="onGenerate"
      >
        <UiIcon name="dices" size="md" />
      </UiButton>
      <!-- `aria-label` is the control's ONLY name now that the words are not
           rendered: the icon is decorative and announces nothing. `aria-controls`
           ties the toggle to the field it changes, and `aria-pressed` reports the
           current state — the two things the name deliberately does not say. -->
      <UiButton
        variant="ghost"
        :aria-label="toggleLabel"
        :aria-controls="inputId"
        :aria-pressed="isRevealed"
        class="px-2.5 py-1"
        @click="onToggle"
      >
        <UiIcon :name="toggleIcon" size="md" />
      </UiButton>
    </template>
  </UiInput>
</template>
