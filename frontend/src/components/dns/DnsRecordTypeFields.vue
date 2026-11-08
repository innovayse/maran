<script setup lang="ts">
/**
 * The inputs a record type takes beyond its value: priority, weight, port, flags and the CAA tag.
 * Which of them are drawn is the panel's answer for the chosen type (`fields` of the reference
 * data); this component knows none of the types. Numbers are typed as text and checked by the
 * form's mirror of the server's rules, never by the browser (rules/vue.md: forms).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { DnsRecordFormErrors } from '../../composables/useDnsRecordForm'
import type { DnsRecordField } from '../../types/dns'

/** Props accepted by {@link DnsRecordTypeFields}. */
const props = defineProps<{
  /** The extra inputs the chosen type takes, in the order to draw them. */
  fields: readonly DnsRecordField[]
  /** The CAA tags the panel accepts. */
  caaTags: readonly string[]
  /** The message for each input, or `null` while it is fine. */
  errors: DnsRecordFormErrors
}>()

/** The priority as typed. */
const priority = defineModel<string>('priority', { required: true })

/** The weight as typed. */
const weight = defineModel<string>('weight', { required: true })

/** The port as typed. */
const port = defineModel<string>('port', { required: true })

/** The flags as typed. */
const flags = defineModel<string>('flags', { required: true })

/** The chosen CAA tag. */
const tag = defineModel<string>('tag', { required: true })

const { t } = useI18n()

/** The CAA tags as select options. */
const tagOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.caaTags.map((candidate) => {
    return { value: candidate, label: candidate }
  })
})
</script>

<template>
  <div v-if="fields.length > 0" class="grid gap-4 sm:grid-cols-2">
    <UiInput
      v-if="fields.includes('priority')"
      v-model="priority"
      :label="t('dns.record.fields.priority')"
      :error="errors.priority"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-if="fields.includes('weight')"
      v-model="weight"
      :label="t('dns.record.fields.weight')"
      :error="errors.weight"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-if="fields.includes('port')"
      v-model="port"
      :label="t('dns.record.fields.port')"
      :error="errors.port"
      inputmode="numeric"
      mono
      required
    />
    <UiInput
      v-if="fields.includes('flags')"
      v-model="flags"
      :label="t('dns.record.fields.flags')"
      :error="errors.flags"
      inputmode="numeric"
      mono
      required
    />
    <UiSelect
      v-if="fields.includes('tag')"
      v-model="tag"
      :label="t('dns.record.fields.tag')"
      :options="tagOptions"
      :placeholder="t('dns.record.placeholders.tag')"
      :error="errors.tag"
      required
    />
  </div>
</template>
