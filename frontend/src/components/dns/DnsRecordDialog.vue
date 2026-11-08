<script setup lang="ts">
/**
 * The add-or-change dialog of one DNS record: the owner name, the type, the TTL, the value and
 * whatever else the chosen type takes. The type list, each type's extra inputs, the TTL bounds and
 * the CAA tags are the panel's catalogue; this dialog decides none of them. Its checks are
 * advice that saves a round trip (rules/vue.md: client rules mirror the server's validator), and
 * the server's already-localized refusal is shown above the form as it was sent.
 *
 * Dumb by the usual contract: it never touches a store or the API layer, and emits only a record
 * that passes its own reading.
 *
 * Mounted by its parent only while open, like the other dialogs of the panel, so a fresh mount is
 * a fresh form.
 */
import { computed, useId, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import DnsRecordTypeFields from './DnsRecordTypeFields.vue'
import { useDnsRecordForm } from '../../composables/useDnsRecordForm'
import type { DnsRecordCatalog } from '../../types/dns'
import type { DnsRecordRequest } from '../../types/dnsRecord'

/** Props accepted by {@link DnsRecordDialog}. */
const props = defineProps<{
  /** The record being changed, or `null` for a new one. */
  record: DnsRecordRequest | null
  /** The panel's record type catalogue, or `null` while it has not arrived. */
  catalog: DnsRecordCatalog | null
  /** Whether the save request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last refused save, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link DnsRecordDialog}. */
const emit = defineEmits<{
  /** Fired only when the inputs pass the client-side reading, carrying the record. */
  (e: 'submit', record: DnsRecordRequest): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The record form's state and its mirror of the server's rules. */
const form = useDnsRecordForm(
  computed((): DnsRecordCatalog | null => {
    return props.catalog
  }),
)
const { name, type, ttl, value, priority, weight, port, flags, tag, typeInfo, fields, errors } = form
form.reset(props.record)

/** The record types the panel serves, as select options. */
const typeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return (props.catalog?.types ?? []).map((info) => {
    return { value: info.type, label: info.label }
  })
})

/**
 * Emits the record when it passes the client-side reading; otherwise shows what is wrong.
 * @returns Nothing.
 */
const submit = (): void => {
  form.submitted.value = true
  const request = form.toRequest()
  if (form.isValid.value && request !== null) {
    emit('submit', request)
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="record === null ? t('dns.record.addTitle') : t('dns.record.editTitle')"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiInput
          v-model="name"
          :label="t('dns.record.fields.name')"
          :placeholder="t('dns.record.placeholders.name')"
          :error="errors.name"
          mono
          required
        />
        <UiSelect
          v-model="type"
          :label="t('dns.record.fields.type')"
          :options="typeOptions"
          :error="errors.type"
          required
        />
      </div>
      <UiFieldHint v-if="typeInfo !== null">{{ typeInfo.description }}</UiFieldHint>
      <DnsRecordTypeFields
        v-model:priority="priority"
        v-model:weight="weight"
        v-model:port="port"
        v-model:flags="flags"
        v-model:tag="tag"
        :fields="fields"
        :caa-tags="catalog?.caaTags ?? []"
        :errors="errors"
      />
      <UiInput v-model="value" :label="t('dns.record.fields.value')" :error="errors.value" mono required />
      <UiInput v-model="ttl" :label="t('dns.record.fields.ttl')" :error="errors.ttl" inputmode="numeric" mono required />
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('dns.record.save') }}</UiButton>
    </template>
  </UiModal>
</template>
