<script setup lang="ts">
/**
 * The form that renames a reseller and changes its contact address and language: the three values
 * that identify it, apart from what it was sold (the envelope has its own form).
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md); it reports a validated request and the page decides what to do with it, and it
 * refuses to emit until its own rules pass. The rules are the server's and they are advice: whatever
 * the form lets through, the server re-validates and its already-localized refusal is what the page
 * shows, so the bounds err wide.
 *
 * It starts from the reseller it was given, once, when it is created: the page creates it only after
 * the reseller has arrived.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { OwnerLanguageChoice } from '../../types/account'
import type { ResellerDetail, UpdateResellerRequest } from '../../types/reseller'
import { isPlausibleResellerEmail, MAX_RESELLER_NAME_LENGTH } from '../../utils/resellerIdentityRules'

/** Props accepted by {@link ResellerProfileForm}. */
const props = defineProps<{
  /** The reseller being changed; the fields start from its current values. */
  reseller: ResellerDetail
  /** Whether a save request is already in flight, which disables the submit control. */
  submitting: boolean
  /** The languages the panel can write to a contact in, each named in itself. */
  languages: OwnerLanguageChoice[]
  /** The panel's message when the languages could not be read, or `null`. */
  languagesError: string | null
}>()

/** Events emitted by {@link ResellerProfileForm}. */
const emit = defineEmits<{
  /** Fired when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: UpdateResellerRequest): void
  /** Fired when the administrator abandons the form. */
  (e: 'cancel'): void
}>()

const { t } = useI18n()

/** The reseller's name as typed. */
const name: Ref<string> = ref(props.reseller.name)

/** The contact address as typed. */
const contactEmail: Ref<string> = ref(props.reseller.contactEmail)

/** The contact's language. */
const language: Ref<string> = ref(props.reseller.language)

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The language choices exactly as the panel lists them. */
const languageOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.languages.map((choice) => {
    return { value: choice.language, label: choice.languageDisplayName }
  })
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const trimmed = name.value.trim()
  if (trimmed.length === 0) {
    return t('resellers.form.errors.nameRequired')
  }
  return trimmed.length > MAX_RESELLER_NAME_LENGTH
    ? t('resellers.form.errors.nameTooLong', { max: MAX_RESELLER_NAME_LENGTH })
    : null
})

/** Validation message for the contact address, or `null`. */
const contactEmailError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (contactEmail.value.length === 0) {
    return t('resellers.form.errors.contactEmailRequired')
  }
  return isPlausibleResellerEmail(contactEmail.value) ? null : t('resellers.form.errors.contactEmailInvalid')
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (nameError.value !== null || contactEmailError.value !== null) {
    return
  }
  emit('submit', { name: name.value.trim(), contactEmail: contactEmail.value, language: language.value })
}

/**
 * Abandons the form.
 * @returns Nothing; emits `cancel`.
 */
const cancel = (): void => {
  emit('cancel')
}
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <div class="grid gap-4 md:grid-cols-2">
        <div class="flex flex-col gap-1">
          <UiInput
            v-model="name"
            :label="t('resellers.form.fields.name')"
            :error="nameError"
            required
            mono
            :placeholder="t('resellers.form.placeholders.name')"
          />
          <UiFieldHint>{{ t('resellers.profile.hints.name') }}</UiFieldHint>
        </div>
        <UiInput
          v-model="contactEmail"
          :label="t('resellers.form.fields.contactEmail')"
          :error="contactEmailError"
          required
          autocomplete="email"
          :placeholder="t('resellers.form.placeholders.contactEmail')"
        />
        <div class="flex flex-col gap-1">
          <UiSelect
            v-model="language"
            :label="t('resellers.profile.fields.language')"
            :options="languageOptions"
            :load-error="languagesError"
            required
          />
          <UiFieldHint>{{ t('resellers.profile.hints.language') }}</UiFieldHint>
        </div>
      </div>

      <div class="mt-5 flex flex-wrap justify-end gap-2 border-t border-border-subtle pt-4">
        <UiButton variant="secondary" type="button" @click="cancel">{{ t('common.cancel') }}</UiButton>
        <UiButton type="submit" :disabled="submitting">{{ t('resellers.profile.submit') }}</UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
