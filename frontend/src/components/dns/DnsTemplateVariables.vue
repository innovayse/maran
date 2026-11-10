<script setup lang="ts">
/**
 * The variables a template's record values may carry, as the panel's catalogue lists them: each
 * token with what it stands for and where it may be used. The SPA holds no list of its own; this
 * component only draws what the panel sent, so a variable the panel adds shows here untouched.
 * Props in, nothing out.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { DnsTemplateVariable } from '../../types/dnsTemplate'

/** Props accepted by {@link DnsTemplateVariables}. */
defineProps<{
  /** The catalogue the panel sent; empty until it has answered. */
  variables: DnsTemplateVariable[]
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard v-if="variables.length > 0">
    <div class="flex flex-col gap-4">
      <UiSectionHeading :title="t('dns.templates.variables.heading')" :subtitle="t('dns.templates.variables.subtitle')" />
      <UiDescriptionList>
        <UiDescriptionItem v-for="variable in variables" :key="variable.token" :term="variable.token">{{
          variable.description
        }}</UiDescriptionItem>
      </UiDescriptionList>
    </div>
  </UiCard>
</template>
