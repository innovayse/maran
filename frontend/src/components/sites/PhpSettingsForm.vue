<script setup lang="ts">
/**
 * The customer's PHP settings (issue #138): one field per setting the panel lists, chosen by the
 * setting's `kind`. A size, a number of seconds and a count are number fields with the unit the
 * panel names beside the label; a timezone is a select over the zones the panel knows. Under each
 * field: the largest value the plan allows, a notice (an icon and words, never colour alone) when
 * the stored value is above it so the plan's maximum applies instead, and the value the pool
 * really carries when that differs from the stored one.
 *
 * A setting left blank is not stored: PHP's own default applies. "Reset to default" blanks one
 * field, and saving sends only the filled ones, so a blank setting is removed from the account.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer; it
 * reports the settings to keep and the page decides what to do with them.
 *
 * **The rules are advice.** A number field must hold digits only; everything else (the range, the
 * plan's maximum, the consistency between upload size and post size) is the server's, and its
 * already-localized refusal is what the page shows. Numbers are typed as text for the reason
 * `PlanForm` gives: no browser spinner, no browser validation bubble (rules/vue.md).
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { PhpSettingItem, PhpSettingValue } from '../../types/phpSetting'

/** Props accepted by {@link PhpSettingsForm}. */
const props = defineProps<{
  /** The settings the panel lists, with the account's stored values and the plan's bounds. */
  items: PhpSettingItem[]
  /** Whether a save is in flight, which disables the submit control. */
  saving: boolean
  /** Whether the settings are still loading: the fields keep their places and draw placeholders. */
  loading?: boolean
}>()

/** Events emitted by {@link PhpSettingsForm}. */
const emit = defineEmits<{
  /** Fired only when every field passes, carrying the settings to keep (a blank one is left out). */
  (e: 'submit', settings: PhpSettingValue[]): void
}>()

const { t } = useI18n()

/** Each setting's value as typed, by name; the empty string means "not stored, PHP's default". */
const draft: Ref<Record<string, string>> = ref({})

/** Whether a submit has been attempted, so nothing turns red before the customer has tried. */
const submitted: Ref<boolean> = ref(false)

/**
 * Says whether a value is the whole number the number fields hold.
 * @param text What was typed.
 * @returns True for digits only.
 */
const isWhole = (text: string): boolean => {
  return /^\d+$/.test(text)
}

/**
 * Writes a value with its unit when the panel names one.
 * @param item The setting.
 * @param value The value in the wire spelling.
 * @returns The text to show.
 */
const withUnit = (item: PhpSettingItem, value: string): string => {
  return item.unit === null ? value : `${value} ${item.unit}`
}

/**
 * The label a field is drawn with: the panel's label, and its unit when it has one.
 * @param item The setting.
 * @returns The label text.
 */
const labelOf = (item: PhpSettingItem): string => {
  return item.unit === null ? item.label : `${item.label} (${item.unit})`
}

/**
 * The zones a timezone field offers, as select options.
 * @param item The timezone setting.
 * @returns One option per zone the panel listed.
 */
const optionsOf = (item: PhpSettingItem): SelectOption[] => {
  return (item.choices ?? []).map((zone) => {
    return { value: zone, label: zone }
  })
}

/**
 * Whether the value the pool carries differs from the one the customer stored, which is when it is
 * worth saying.
 * @param item The setting.
 * @returns True when there is an effective value and it is not the stored one.
 */
const showsEffective = (item: PhpSettingItem): boolean => {
  return item.effectiveValue !== null && item.effectiveValue !== item.value
}

/**
 * Validates one field the client's way: a number field that is not blank must be digits.
 * @param item The setting.
 * @returns The message to show, or `null` when the field is acceptable (or nothing was tried yet).
 */
const errorOf = (item: PhpSettingItem): string | null => {
  if (!submitted.value || item.kind === 'timezone') {
    return null
  }
  const text = (draft.value[item.name] ?? '').trim()
  return text === '' || isWhole(text) ? null : t('phpSettings.form.errors.whole')
}

/**
 * Records what was typed or chosen in one field.
 * @param item The setting the field belongs to.
 * @param value The new text.
 * @returns Nothing; the draft is updated.
 */
const change = (item: PhpSettingItem, value: string): void => {
  draft.value[item.name] = value
}

/**
 * Blanks one field, which removes the setting from the account when saved.
 * @param item The setting to put back to PHP's default.
 * @returns Nothing; the field is blank.
 */
const reset = (item: PhpSettingItem): void => {
  draft.value[item.name] = ''
}

/**
 * Validates, and emits only the settings that are filled.
 * @returns Nothing; emits synchronously when every field passes.
 */
const submit = (): void => {
  submitted.value = true
  const invalid = props.items.some((item) => {
    return errorOf(item) !== null
  })
  if (invalid) {
    return
  }
  const settings: PhpSettingValue[] = []
  for (const item of props.items) {
    const text = (draft.value[item.name] ?? '').trim()
    if (text !== '') {
      settings.push({ name: item.name, value: text })
    }
  }
  emit('submit', settings)
}

// The fields follow what the panel last reported, including after a save and a language switch.
watch(
  () => {
    return props.items
  },
  (items) => {
    const next: Record<string, string> = {}
    for (const item of items) {
      next[item.name] = item.value ?? ''
    }
    draft.value = next
    submitted.value = false
  },
  { immediate: true },
)
</script>

<template>
  <UiForm :loading="loading" @submit="submit">
    <div class="grid gap-6 sm:grid-cols-2">
      <div v-for="item in items" :key="item.name" class="flex min-w-0 flex-col gap-2">
        <UiSelect
          v-if="item.kind === 'timezone'"
          :model-value="draft[item.name] ?? ''"
          :label="labelOf(item)"
          :options="optionsOf(item)"
          :placeholder="t('phpSettings.form.useDefault')"
          @update:model-value="change(item, $event)"
        />
        <UiInput
          v-else
          :model-value="draft[item.name] ?? ''"
          :label="labelOf(item)"
          :placeholder="t('phpSettings.form.useDefault')"
          :error="errorOf(item)"
          inputmode="numeric"
          mono
          @update:model-value="change(item, $event)"
        />

        <UiFieldHint v-if="item.planMaximum !== null">
          {{ t('phpSettings.form.planMaximum', { max: withUnit(item, String(item.planMaximum)) }) }}
        </UiFieldHint>

        <!-- An icon and words (the alert's triangle and this sentence), so the state never rests on colour. -->
        <UiAlert v-if="item.exceedsPlan && item.planMaximum !== null" variant="warning">
          {{ t('phpSettings.form.exceedsPlan', { max: withUnit(item, String(item.planMaximum)) }) }}
        </UiAlert>

        <UiFieldHint v-if="showsEffective(item) && item.effectiveValue !== null">
          {{ t('phpSettings.form.effective', { value: withUnit(item, item.effectiveValue) }) }}
        </UiFieldHint>

        <div>
          <UiButton
            variant="quiet"
            type="button"
            :disabled="(draft[item.name] ?? '') === ''"
            :aria-label="t('phpSettings.form.resetNamed', { label: item.label })"
            @click="reset(item)"
          >
            {{ t('phpSettings.form.reset') }}
          </UiButton>
        </div>
      </div>
    </div>

    <div class="mt-5 flex flex-wrap justify-end gap-2 border-t border-border-subtle pt-4">
      <UiButton type="submit" :disabled="saving">{{ t('phpSettings.form.save') }}</UiButton>
    </div>
  </UiForm>
</template>
