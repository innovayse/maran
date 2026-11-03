<script setup lang="ts">
/**
 * The add-an-existing-login form: one identifier (a login name or an e-mail address), the features to
 * offer, and the client-side mirror of the server's validator (`OfferMembershipCommandValidator`).
 *
 * It offers rather than adds: the person has to accept inside the panel, and the hint says so. It
 * never tells the owner whether the identifier named anybody — the server answers every identifier the
 * same way, and so does this form. Dumb by the usual contract: props in, emits out. It never touches a
 * store or the API layer and refuses to emit until its own rules pass; whatever it lets through, the
 * server re-validates and its localized message is what the owner reads on failure.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import SubUserFeaturePicker from './SubUserFeaturePicker.vue'
import type { AccountFeatureName } from '../../types/accountFeature'
import type { GrantableFeature, OfferMembershipRequest } from '../../types/subUser'

/**
 * The longest identifier the server accepts: an e-mail address is the longest thing it can be, and
 * the standard caps one at 254 characters. Advice that saves a round trip; the server re-validates it.
 */
const MAX_IDENTIFIER_LENGTH = 254

/** Props accepted by {@link SubUserAddExistingForm}. */
defineProps<{
  /** Every feature the owner may give, from the panel. */
  features: GrantableFeature[]
  /** Whether an offer request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link SubUserAddExistingForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: OfferMembershipRequest): void
}>()

const { t } = useI18n()

/** The login name or address the owner typed. */
const identifier: Ref<string> = ref('')

/** The ticked feature ids. */
const selected: Ref<AccountFeatureName[]> = ref([])

/** Whether a submit has been attempted, so nothing turns red before the owner has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the identifier, or `null`. */
const identifierError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (identifier.value.trim().length === 0) {
    return t('subUsers.existing.errors.identifierRequired')
  }
  return identifier.value.trim().length > MAX_IDENTIFIER_LENGTH ? t('subUsers.existing.errors.identifierTooLong') : null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (identifierError.value !== null) {
    return
  }
  emit('submit', { identifier: identifier.value.trim(), features: selected.value })
}

/**
 * Returns every field to its starting value, so a form still showing what it just sent does not
 * invite the same submit twice.
 * @returns Nothing.
 */
const reset = (): void => {
  identifier.value = ''
  selected.value = []
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('subUsers.existing.heading')" />
    <UiForm @submit="submit">
      <UiInput
        v-model="identifier"
        :label="t('subUsers.existing.fields.identifier')"
        :placeholder="t('subUsers.existing.placeholders.identifier')"
        :error="identifierError"
        required
      />
      <p class="mt-5 mb-1 text-sm font-medium text-text-primary">{{ t('subUsers.existing.fields.features') }}</p>
      <SubUserFeaturePicker
        v-model="selected"
        :features="features"
        :group-label="t('subUsers.existing.featuresGroupLabel')"
      />
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('subUsers.existing.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('subUsers.existing.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
