<script setup lang="ts">
/**
 * The add-or-change dialog of one record of a template: the owner name, the type, the TTL, the
 * value and whatever else the chosen type takes, with the same form logic as the zone's record
 * dialog. Two things differ: the TTL may be left empty (the zone then takes the default TTL of the
 * name server settings), and the value may carry the variables the panel's catalogue lists, which
 * the panel checks and expands. The record type list, the bounds and the CAA tags are the panel's
 * catalogue; this dialog decides none of them. Its checks are advice that saves a round trip, and
 * the panel's refusal on the whole template is shown above the template's form.
 *
 * Dumb by the usual contract: it never touches a store or the API layer. Mounted by its parent only
 * while open, so a fresh mount is a fresh form.
 */
import { computed, useId, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import DnsRecordTypeFields from './DnsRecordTypeFields.vue'
import { useDnsRecordForm } from '../../composables/useDnsRecordForm'
import type { DnsRecordCatalog } from '../../types/dns'
import type { DnsTemplateRecord } from '../../types/dnsTemplate'

/** Props accepted by {@link DnsTemplateRecordDialog}. */
const props = defineProps<{
  /** The record being changed, or `null` for a new one. */
  record: DnsTemplateRecord | null
  /** The panel's record type catalogue, or `null` while it has not arrived. */
  catalog: DnsRecordCatalog | null
}>()

/** Events emitted by {@link DnsTemplateRecordDialog}. */
const emit = defineEmits<{
  /** Fired only when the inputs pass the client-side reading, carrying the record. */
  (e: 'submit', record: DnsTemplateRecord): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The record form's state and its mirror of the server's rules, with the TTL optional. */
const form = useDnsRecordForm(
  computed((): DnsRecordCatalog | null => {
    return props.catalog
  }),
  { ttlOptional: true },
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
 * Emits the record when it passes the client-side reading; otherwise shows what is wrong. An empty
 * TTL is sent as `null`, which is how a template states "the settings' default".
 * @returns Nothing.
 */
const submit = (): void => {
  form.submitted.value = true
  const request = form.toRequest()
  if (form.isValid.value && request !== null) {
    emit('submit', { ...request, ttl: ttl.value.trim() === '' ? null : request.ttl })
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="record === null ? t('dns.templates.records.addTitle') : t('dns.templates.records.editTitle')"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
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
      <div>
        <UiInput v-model="value" :label="t('dns.record.fields.value')" :error="errors.value" mono required />
        <UiFieldHint class="mt-1.5">{{ t('dns.templates.records.valueHint') }}</UiFieldHint>
      </div>
      <div>
        <UiInput v-model="ttl" :label="t('dns.record.fields.ttl')" :error="errors.ttl" inputmode="numeric" mono />
        <UiFieldHint class="mt-1.5">{{ t('dns.templates.records.ttlHint') }}</UiFieldHint>
      </div>
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId">{{ t('dns.templates.records.keep') }}</UiButton>
    </template>
  </UiModal>
</template>
