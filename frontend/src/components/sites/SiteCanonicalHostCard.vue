<script setup lang="ts">
/**
 * The canonical-host section of a site's options: which name is the main one, chosen from the panel's
 * list. The server's refusal arrives as {@link error} and is shown under the select.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSelect from '../ui/UiSelect.vue'
import type { SiteChoice } from '../../types/site'

/** Props accepted by {@link SiteCanonicalHostCard}. */
defineProps<{
  /** The canonical-host choices, labelled by the panel. */
  choices: readonly SiteChoice[]
  /** Whether the select is inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The panel's message about the canonical host, or `null` when the last refusal was not about it. */
  error: string | null
}>()

/** The chosen canonical host, as the panel names it. */
const canonicalHost = defineModel<string>({ required: true })

const { t } = useI18n()
</script>

<template>
  <UiCard class="@container flex flex-col gap-4">
    <UiSectionHeading
      :title="t('siteOptions.canonicalHost.heading')"
      :subtitle="t('siteOptions.canonicalHost.subtitle')"
    />
    <UiSelect
      v-model="canonicalHost"
      :label="t('siteOptions.canonicalHost.label')"
      :options="choices"
      :disabled="disabled"
      :error="error"
    />
  </UiCard>
</template>
