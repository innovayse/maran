<script setup lang="ts">
/**
 * A password field with a reveal toggle. Composes {@link UiInput} rather than
 * repeating its markup, so the label binding, the error wiring and the focus
 * treatment stay defined in exactly one place (rules/vue.md: "UI comes from
 * components/ui").
 *
 * The toggle is a word, not an eye glyph: `UiIcon` draws only the glyphs the
 * design canvas defines, and inventing one here would put a shape in the panel
 * that the design never approved. A word also states what will happen, which an
 * eye — ambiguous between "is hidden" and "will hide" — does not.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from './UiButton.vue'
import UiInput from './UiInput.vue'

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
  }>(),
  { placeholder: undefined, required: false, error: null, autocomplete: 'current-password' },
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

/** The label of the toggle, which names the action it performs, not the current state. */
const toggleLabel: ComputedRef<string> = computed(() => {
  return isRevealed.value ? t('app.auth.hidePassword') : t('app.auth.showPassword')
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
      <!-- `aria-controls` ties the toggle to the field it changes, and `aria-pressed`
           reports the current state — the two things the label deliberately does not say. -->
      <UiButton
        variant="ghost"
        :aria-controls="inputId"
        :aria-pressed="isRevealed"
        class="px-2 py-0.5"
        @click="onToggle"
        >{{ toggleLabel }}</UiButton
      >
    </template>
  </UiInput>
</template>
