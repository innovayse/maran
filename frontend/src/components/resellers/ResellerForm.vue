<script setup lang="ts">
/**
 * The reseller form, for both jobs it has: creating a reseller (its name, the address its
 * invitation goes to, the invitation's language and its envelope) and changing the envelope of an
 * existing one, where the identity is fixed and only the envelope is drawn.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer
 * (rules/vue.md); it reports a validated request and the page decides what to do with it, and it
 * refuses to emit until its own rules pass.
 *
 * **The rules are the server's and they are advice.** The envelope's figures, runtime ceilings and
 * feature set are held by `useResellerEnvelopeForm`; the name and the address are checked here.
 * Whatever the form lets through, the server re-validates and its already-localized refusal is what
 * the page shows, so a rule here that is stricter than the server's is a bug and the bounds err wide.
 *
 * **Numbers are typed as text.** The kit's input is a text field, and `type="number"` would bring
 * the browser's own spinner and validation bubble (rules/vue.md: "the browser never validates"). The
 * `numeric` keyboard is requested instead, and the text is parsed by the composable.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import { useResellerEnvelopeForm } from '../../composables/useResellerEnvelopeForm'
import type { AccountFeatureName } from '../../types/accountFeature'
import type { OwnerLanguageChoice } from '../../types/account'
import type { LoadingState } from '../../types/loading'
import type { PlanFeatureChoice } from '../../types/plan'
import type { ChangeResellerEnvelopeRequest, CreateResellerRequest, ResellerDetail } from '../../types/reseller'
import { isPlausibleResellerEmail, MAX_RESELLER_NAME_LENGTH } from '../../utils/resellerIdentityRules'

/** Props accepted by {@link ResellerForm}. */
const props = defineProps<{
  /** The reseller whose envelope is being changed, or `null` when a new one is being created. */
  reseller: ResellerDetail | null
  /** Whether a save request is already in flight, which disables the submit control. */
  submitting: boolean
  /** The languages the panel can write an invitation in, each named in itself. */
  languages: OwnerLanguageChoice[]
  /** The panel's message when the languages could not be read, or `null`. */
  languagesError: string | null
  /** The language the invitation starts in (the operator's own, when the panel lists it). */
  defaultLanguage: string
  /** The features the panel offers, or `null` before they arrived and after the read failed. */
  featureChoices: PlanFeatureChoice[] | null
  /** Where the read of {@link featureChoices} stands. */
  featuresState: LoadingState
}>()

/** Events emitted by {@link ResellerForm}. */
const emit = defineEmits<{
  /** Fired when a NEW reseller passes every client-side rule, carrying the request to send. */
  (e: 'submit', request: CreateResellerRequest): void
  /** Fired when an existing reseller's envelope passes every client-side rule, carrying the request to send. */
  (e: 'submitEnvelope', request: ChangeResellerEnvelopeRequest): void
  /** Fired when the administrator abandons the form. */
  (e: 'cancel'): void
}>()

const { t } = useI18n()

/** The reseller's name as typed. */
const name: Ref<string> = ref('')

/** The contact address as typed. */
const contactEmail: Ref<string> = ref('')

/** The invitation's language. */
const language: Ref<string> = ref(props.defaultLanguage)

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The envelope's figures, ceilings and features. */
const envelope = useResellerEnvelopeForm(props.reseller?.envelope ?? null, submitted, () => {
  return props.featureChoices
})

/** Whether this form changes an existing reseller's envelope. */
const isEditing: ComputedRef<boolean> = computed(() => {
  return props.reseller !== null
})

/** The language choices exactly as the panel lists them. */
const languageOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.languages.map((choice) => {
    return { value: choice.language, label: choice.languageDisplayName }
  })
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || isEditing.value) {
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
  if (!submitted.value || isEditing.value) {
    return null
  }
  if (contactEmail.value.length === 0) {
    return t('resellers.form.errors.contactEmailRequired')
  }
  return isPlausibleResellerEmail(contactEmail.value) ? null : t('resellers.form.errors.contactEmailInvalid')
})

/** Whether the identity fields pass; silent until a submit was attempted, and an edit has none to check. */
const identityValid: ComputedRef<boolean> = computed(() => {
  return nameError.value === null && contactEmailError.value === null
})

/**
 * Reports one feature box being toggled.
 * @param feature The feature's name on the wire.
 * @param ticked Whether the box is now ticked.
 * @returns Nothing.
 */
const toggleFeature = (feature: AccountFeatureName, ticked: boolean): void => {
  envelope.features.toggle(feature, ticked)
}

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!identityValid.value || !envelope.isValid.value) {
    return
  }
  if (props.reseller === null) {
    emit('submit', {
      name: name.value.trim(),
      contactEmail: contactEmail.value,
      language: language.value,
      envelope: envelope.toRequest(),
    })
  } else {
    emit('submitEnvelope', { envelope: envelope.toRequest() })
  }
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
      <template v-if="!isEditing">
        <UiSectionHeading class="mb-4" :title="t('resellers.form.identitySection')" />
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
            <UiFieldHint>{{ t('resellers.form.hints.name') }}</UiFieldHint>
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
              :label="t('resellers.form.fields.language')"
              :options="languageOptions"
              :load-error="languagesError"
              required
            />
            <UiFieldHint>{{ t('resellers.form.hints.language') }}</UiFieldHint>
          </div>
        </div>
      </template>

      <UiSectionHeading
        :class="isEditing ? 'mb-4' : 'mt-6 mb-4'"
        :title="t('resellers.form.limitsSection')"
        :subtitle="t('resellers.form.limitsHint')"
      />
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <div v-for="field in envelope.fields" :key="field.key" class="min-w-0">
          <UiInput
            v-model="envelope.values.value[field.key]"
            :label="t(`resellers.form.fields.${field.key}`)"
            :error="envelope.errorOf(field)"
            inputmode="numeric"
            mono
            required
          />
        </div>
      </div>

      <UiSectionHeading
        class="mt-6 mb-4"
        :title="t('resellers.form.runtimeSection')"
        :subtitle="t('resellers.form.runtimeHint')"
      />
      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <div v-for="field in envelope.runtime.fields" :key="field.key" class="flex min-w-0 flex-col gap-2">
          <UiInput
            v-model="envelope.runtime.ceilings.value[field.key].figure"
            :label="t(`resellers.form.fields.${field.key}`)"
            :error="envelope.runtime.ceilings.value[field.key].unlimited ? null : envelope.runtime.errorOf(field)"
            :disabled="envelope.runtime.ceilings.value[field.key].unlimited"
            inputmode="numeric"
            mono
          />
          <UiCheckbox
            v-model="envelope.runtime.ceilings.value[field.key].unlimited"
            :label="t('resellers.form.noLimit')"
          />
        </div>
      </div>

      <UiSectionHeading
        class="mt-6 mb-4"
        :title="t('resellers.form.featuresSection')"
        :subtitle="t('resellers.form.featuresHint')"
      />
      <div role="group" :aria-label="t('resellers.form.featuresSection')" class="grid gap-x-4 gap-y-1 sm:grid-cols-2 xl:grid-cols-3">
        <template v-if="featureChoices !== null">
          <UiCheckbox
            v-for="choice in featureChoices"
            :key="choice.feature"
            :model-value="envelope.features.selected.value.includes(choice.feature)"
            :label="choice.featureDisplayName"
            @update:model-value="(ticked: boolean) => toggleFeature(choice.feature, ticked)"
          />
        </template>
        <p v-else-if="featuresState === 'loading'" class="text-sm text-text-muted sm:col-span-2 xl:col-span-3">
          {{ t('resellers.form.featuresLoading') }}
        </p>
        <p v-else class="text-sm text-text-muted sm:col-span-2 xl:col-span-3">{{ t('resellers.form.featuresUnavailable') }}</p>
      </div>
      <UiAlert v-if="submitted && !envelope.features.isReady.value" class="mt-4" variant="error">
        {{ t('resellers.form.featuresUnavailable') }}
      </UiAlert>

      <div class="mt-5 flex flex-wrap justify-end gap-2 border-t border-border-subtle pt-4">
        <UiButton variant="secondary" type="button" @click="cancel">{{ t('common.cancel') }}</UiButton>
        <UiButton type="submit" :disabled="submitting">
          {{ isEditing ? t('resellers.form.submitEdit') : t('resellers.form.submitNew') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
