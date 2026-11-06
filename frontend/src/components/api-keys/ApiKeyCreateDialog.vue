<script setup lang="ts">
/**
 * The create-an-API-key dialog: name, scopes (checkboxes from the list the panel offers), an
 * optional list of allowed ranges, one per line, and an optional expiry day. It holds the draft and
 * the client-side mirror of the server's validator (advice that saves a round trip; the server
 * re-validates and its already-localized message is shown above the form). Dumb by the usual
 * contract: it never touches a store or the API layer, and emits only when its own rules pass.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiDateInput from '../ui/UiDateInput.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { isUsableCidr } from '../../utils/isUsableCidr'
import { localDayBoundary } from '../../utils/localDayBoundary'
import type { ApiKeyChoice, CreateApiKeyRequest } from '../../types/apiKey'

/** Props accepted by {@link ApiKeyCreateDialog}. */
defineProps<{
  /** The scopes the panel offers, under its names. */
  scopes: ApiKeyChoice[]
  /** Whether a create request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed create, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link ApiKeyCreateDialog}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateApiKeyRequest): void
  /** The administrator closed the dialog. */
  (e: 'close'): void
}>()

/** The longest name the server accepts. */
const MAX_NAME_LENGTH = 64

/** The most allowed ranges the server accepts on one key. */
const MAX_CIDRS = 16

const { t } = useI18n()

/**
 * The form's id, which the footer's submit button names through its `form` attribute. The button
 * lives in the modal's footer, outside the `<form>`, and a form whose only submit button is absent
 * does not submit on Enter when it holds two text-like fields (the name and the date).
 */
const formId: string = useId()

/** The label the administrator is typing. */
const name: Ref<string> = ref('')

/** The scope values ticked so far. */
const chosenScopes: Ref<string[]> = ref([])

/** The allowed ranges as typed, one per line. */
const cidrText: Ref<string> = ref('')

/** The expiry day as `YYYY-MM-DD`, or empty for never. */
const expiryDay: Ref<string> = ref('')

/** Whether a submit was attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The ranges typed, trimmed, with blank lines dropped. */
const cidrs: ComputedRef<string[]> = computed(() => {
  return cidrText.value
    .split('\n')
    .map((line) => {
      return line.trim()
    })
    .filter((line) => {
      return line !== ''
    })
})

/** Validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const trimmed = name.value.trim()
  if (trimmed === '') {
    return t('apiKeys.form.errors.nameRequired')
  }
  return trimmed.length > MAX_NAME_LENGTH ? t('apiKeys.form.errors.nameTooLong') : null
})

/** Validation message for the ranges, or `null`. */
const cidrError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (cidrs.value.length > MAX_CIDRS) {
    return t('apiKeys.form.errors.cidrTooMany')
  }
  return cidrs.value.every(isUsableCidr) ? null : t('apiKeys.form.errors.cidrInvalid')
})

/** Validation message for the expiry, or `null`. A day already over cannot be an expiry. */
const expiryError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || expiryDay.value === '') {
    return null
  }
  return new Date(localDayBoundary(expiryDay.value, 'end')).getTime() <= Date.now()
    ? t('apiKeys.form.errors.expiryPast')
    : null
})

/**
 * Ticks or clears one scope.
 * @param value The scope value.
 * @param checked Whether it is now ticked.
 * @returns Nothing.
 */
const toggleScope = (value: string, checked: boolean): void => {
  chosenScopes.value = checked
    ? [...chosenScopes.value, value]
    : chosenScopes.value.filter((scope) => {
        return scope !== value
      })
}

/**
 * Validates, and emits only a request the server has a chance of accepting. The expiry day becomes
 * the last instant of that day in the reader's time zone, here, because only here is it known.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (nameError.value !== null || cidrError.value !== null || expiryError.value !== null) {
    return
  }
  emit('submit', {
    name: name.value.trim(),
    scopes: chosenScopes.value,
    allowedCidrs: cidrs.value.length === 0 ? null : cidrs.value,
    expiresAt: expiryDay.value === '' ? null : localDayBoundary(expiryDay.value, 'end'),
  })
}

/**
 * Forwards a close.
 * @returns Nothing; emits synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiModal :open="true" :title="t('apiKeys.form.heading')" :close-label="t('common.close')" @close="close">
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>

    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <UiInput
        v-model="name"
        :label="t('apiKeys.form.fields.name')"
        :placeholder="t('apiKeys.form.placeholders.name')"
        :error="nameError"
        required
      />

      <div role="group" :aria-label="t('apiKeys.form.fields.scopes')" class="flex flex-col gap-1">
        <p class="text-sm text-text-primary">{{ t('apiKeys.form.fields.scopes') }}</p>
        <UiCheckbox
          v-for="scope in scopes"
          :key="scope.value"
          :model-value="chosenScopes.includes(scope.value)"
          :label="scope.name"
          @update:model-value="toggleScope(scope.value, $event)"
        />
        <UiFieldHint>{{ t('apiKeys.form.hints.scopes') }}</UiFieldHint>
      </div>

      <div class="flex flex-col gap-1">
        <UiTextarea
          v-model="cidrText"
          :label="t('apiKeys.form.fields.cidrs')"
          :placeholder="t('apiKeys.form.placeholders.cidrs')"
          :rows="3"
          :error="cidrError"
        />
        <UiFieldHint>{{ t('apiKeys.form.hints.cidrs') }}</UiFieldHint>
      </div>

      <div class="flex flex-col gap-1">
        <UiDateInput v-model="expiryDay" :label="t('apiKeys.form.fields.expires')" :error="expiryError" />
        <UiFieldHint>{{ t('apiKeys.form.hints.expires') }}</UiFieldHint>
      </div>
    </UiForm>

    <template #footer>
      <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('apiKeys.form.submit') }}</UiButton>
    </template>
  </UiModal>
</template>
