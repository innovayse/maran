<script setup lang="ts">
/**
 * A site's IP rules: whether the listed addresses are the only ones let in or the ones kept out, and
 * the addresses themselves, one per line.
 *
 * The mode is a select over the panel's list, and the addresses are free text the server parses and
 * normalises; its refusal arrives as {@link error} and is shown under the field. An empty list means
 * no IP rules at all, whatever the mode says: the parent sends no mode with it.
 */
import { useI18n } from 'vue-i18n'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiSelect from '../ui/UiSelect.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import type { SiteChoice } from '../../types/site'

/** Props accepted by {@link SiteIpRulesEditor}. */
defineProps<{
  /** Whether the listed addresses are allowed or refused, labelled by the panel. */
  modes: readonly SiteChoice[]
  /** The most addresses a site may carry, from the panel's limits. */
  maximum: number
  /** Whether the editor is inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The panel's message about the rules, or `null` when the last refusal was not about them. */
  error: string | null
}>()

/** The chosen mode, as the panel names it. */
const mode = defineModel<string>('mode', { required: true })

/** The addresses as typed: one address or CIDR range per line. */
const text = defineModel<string>('text', { required: true })

const { t } = useI18n()
</script>

<template>
  <div class="@container flex min-w-0 flex-col gap-4">
    <UiSelect
      v-model="mode"
      :label="t('siteOptions.ipRules.modeLabel')"
      :options="modes"
      :disabled="disabled"
    />
    <UiTextarea
      v-model="text"
      :label="t('siteOptions.ipRules.listLabel')"
      :placeholder="t('siteOptions.ipRules.listPlaceholder')"
      :rows="4"
      :error="error"
    />
    <UiFieldHint>{{ t('siteOptions.ipRules.hint', { maximum }) }}</UiFieldHint>
  </div>
</template>
