<script setup lang="ts">
/**
 * The choice of WHAT a restore replaces (issue #160): the whole account, one of its databases, or
 * one of its sites.
 *
 * The names to choose from come from the panel (`RestoreChoices`) and are never typed: a screen that
 * asked a person to spell a database name would be a screen whose contract was missing an endpoint
 * (rules/vue.md "Data comes from the backend"). Only the kinds the account can actually offer are
 * shown — an account with no database is not offered "One database" — and when the lists could not
 * be fetched the whole account is the only choice, with a line saying why, because the whole-account
 * restore needs neither list and must stay possible.
 *
 * The lists are the account's CURRENT databases and sites, not what the copy holds (the panel does
 * not read inside an archive), so a name chosen here can still be refused by the server as not being
 * in the copy; the note under the choice says so, and the server's refusal is the authority.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiRadioGroup, { type RadioOption } from '../ui/UiRadioGroup.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { RestoreChoicesLoad, RestoreScopeKind } from '../../types/backup'

/** What the component needs from its parent. */
const props = defineProps<{
  /** The account's lists, as the store holds them. */
  choices: RestoreChoicesLoad
  /** Whether the controls are out of reach, because a restore is in flight. */
  disabled: boolean
}>()

/** The kind of restore chosen (`v-model:kind`). */
const kind = defineModel<RestoreScopeKind>('kind', { required: true })

/** The database chosen, or an empty string (`v-model:database`). */
const database = defineModel<string>('database', { required: true })

/** The site chosen, or an empty string (`v-model:site`). */
const site = defineModel<string>('site', { required: true })

const { t } = useI18n()

/** The databases on offer; none while the lists are not in hand. */
const databases: ComputedRef<readonly string[]> = computed(() => {
  return props.choices.state === 'loaded' ? props.choices.choices.databases : []
})

/** The sites on offer; none while the lists are not in hand. */
const sites: ComputedRef<readonly string[]> = computed(() => {
  return props.choices.state === 'loaded' ? props.choices.choices.sites : []
})

/** The kinds the account can offer, labelled in the interface language. The whole account is always one. */
const kinds: ComputedRef<RadioOption[]> = computed(() => {
  const offered = [{ value: 'whole', label: t('backups.restore.scopeWhole') }]
  if (databases.value.length > 0) {
    offered.push({ value: 'database', label: t('backups.restore.scopeDatabase') })
  }
  if (sites.value.length > 0) {
    offered.push({ value: 'site', label: t('backups.restore.scopeSite') })
  }
  return offered
})

/** The databases as select options; the name is the value and the label alike. */
const databaseOptions: ComputedRef<SelectOption[]> = computed(() => {
  return databases.value.map((name) => {
    return { value: name, label: name }
  })
})

/** The sites as select options; the domain is the value and the label alike. */
const siteOptions: ComputedRef<SelectOption[]> = computed(() => {
  return sites.value.map((name) => {
    return { value: name, label: name }
  })
})

/**
 * Takes the radio's value as a kind of restore and forgets the name chosen under the other kinds, so
 * a database picked and then abandoned is never sent with a site restore.
 * @param value The option picked; always one of {@link kinds}.
 */
const onKindChange = (value: string): void => {
  kind.value = value === 'database' || value === 'site' ? value : 'whole'
  database.value = ''
  site.value = ''
}
</script>

<template>
  <div class="flex flex-col gap-3">
    <UiAlert v-if="choices.state === 'unavailable'" variant="info">
      {{ t('backups.restore.scopeUnavailable') }}
    </UiAlert>
    <template v-else-if="kinds.length > 1">
      <UiRadioGroup
        :model-value="kind"
        :legend="t('backups.restore.scopeLegend')"
        :options="kinds"
        :disabled="disabled"
        @update:model-value="onKindChange"
      />
      <UiSelect
        v-if="kind === 'database'"
        v-model="database"
        :label="t('backups.restore.databaseLabel')"
        :options="databaseOptions"
        :placeholder="t('backups.restore.databasePlaceholder')"
        :disabled="disabled"
        :required="true"
      />
      <UiSelect
        v-else-if="kind === 'site'"
        v-model="site"
        :label="t('backups.restore.siteLabel')"
        :options="siteOptions"
        :placeholder="t('backups.restore.sitePlaceholder')"
        :disabled="disabled"
        :required="true"
      />
      <p v-if="kind !== 'whole'" class="text-text-muted">{{ t('backups.restore.scopeNote') }}</p>
    </template>
  </div>
</template>
