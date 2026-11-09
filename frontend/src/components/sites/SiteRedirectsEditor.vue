<script setup lang="ts">
/**
 * The list of a site's redirect rules: a path, how it is compared, where it goes and with which status.
 *
 * Every choice is a select over the list the panel supplied with the options, so a value outside what
 * the server accepts cannot be written; the paths and targets are typed, held to the server's rules
 * (its refusal arrives as {@link error} and is shown under the list). The list only edits the rules
 * the parent owns through `v-model`: it adds, changes and removes rows, and sends nothing itself.
 *
 * A removed rule is a deleted row: the parent sends the whole list, and what is not in it is gone.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect from '../ui/UiSelect.vue'
import type { SiteChoice } from '../../types/site'
import type { RedirectMatchWire, RedirectStatusWire, SiteRedirect } from '../../types/siteOptions'

/** Props accepted by {@link SiteRedirectsEditor}. */
const props = defineProps<{
  /** How a rule's path may be compared, labelled by the panel. */
  matches: readonly SiteChoice[]
  /** The statuses a rule may answer with, labelled by the panel. */
  statuses: readonly SiteChoice[]
  /** The most rules a site may carry, from the panel's limits. */
  maximum: number
  /** Whether the editor is inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The panel's message about the list, or `null` when the last refusal was not about it. */
  error: string | null
}>()

/** The rules, in the order they are rendered. */
const rows = defineModel<SiteRedirect[]>({ required: true })

const { t } = useI18n()

/**
 * Replaces the rule at one position with a changed copy.
 * @param index The rule's position.
 * @param patch The fields to change.
 * @returns Nothing; the model is updated.
 */
const change = (index: number, patch: Partial<SiteRedirect>): void => {
  rows.value = rows.value.map((row, position) => {
    return position === index ? { ...row, ...patch } : row
  })
}

/**
 * Appends an empty rule, using the first choice of each list as its starting value.
 * @returns Nothing; the model is updated.
 */
const add = (): void => {
  // The starting values come from the server's lists, never from constants here: the first entry of
  // each list is the panel's own default for a new rule.
  const match = (props.matches[0]?.value ?? '') as RedirectMatchWire
  const status = (props.statuses[0]?.value ?? '') as RedirectStatusWire
  rows.value = [...rows.value, { path: '', match, target: '', status }]
}

/**
 * Removes the rule at one position.
 * @param index The rule's position.
 * @returns Nothing; the model is updated.
 */
const remove = (index: number): void => {
  rows.value = rows.value.filter((_row, position) => {
    return position !== index
  })
}
</script>

<template>
  <div class="@container flex min-w-0 flex-col gap-4">
    <p v-if="rows.length === 0" class="text-sm text-text-muted">{{ t('siteOptions.redirects.empty') }}</p>

    <ul v-else class="flex min-w-0 flex-col gap-4">
      <li
        v-for="(row, index) in rows"
        :key="index"
        class="flex min-w-0 flex-col gap-3 rounded-control border border-border-subtle bg-surface-2 p-4"
      >
        <div class="grid min-w-0 gap-3 @lg:grid-cols-2">
          <UiInput
            :model-value="row.path"
            :label="t('siteOptions.redirects.pathLabel', { number: index + 1 })"
            :placeholder="t('siteOptions.redirects.pathPlaceholder')"
            :disabled="disabled"
            mono
            @update:model-value="change(index, { path: $event })"
          />
          <UiSelect
            :model-value="row.match"
            :label="t('siteOptions.redirects.matchLabel', { number: index + 1 })"
            :options="matches"
            :disabled="disabled"
            @update:model-value="change(index, { match: $event as RedirectMatchWire })"
          />
        </div>
        <div class="grid min-w-0 gap-3 @lg:grid-cols-2">
          <UiInput
            :model-value="row.target"
            :label="t('siteOptions.redirects.targetLabel', { number: index + 1 })"
            :placeholder="t('siteOptions.redirects.targetPlaceholder')"
            :disabled="disabled"
            mono
            inputmode="url"
            @update:model-value="change(index, { target: $event })"
          />
          <UiSelect
            :model-value="row.status"
            :label="t('siteOptions.redirects.statusLabel', { number: index + 1 })"
            :options="statuses"
            :disabled="disabled"
            @update:model-value="change(index, { status: $event as RedirectStatusWire })"
          />
        </div>
        <div>
          <UiButton variant="secondary" :disabled="disabled" @click="remove(index)">
            {{ t('siteOptions.redirects.remove', { number: index + 1 }) }}
          </UiButton>
        </div>
      </li>
    </ul>

    <p v-if="error !== null" role="alert" class="text-sm text-danger wrap-anywhere">{{ error }}</p>

    <div class="flex flex-wrap items-center gap-3">
      <UiButton variant="secondary" :disabled="disabled || rows.length >= maximum" @click="add">
        {{ t('siteOptions.redirects.add') }}
      </UiButton>
      <UiFieldHint>{{ t('siteOptions.redirects.limit', { count: rows.length, maximum }) }}</UiFieldHint>
    </div>
  </div>
</template>
