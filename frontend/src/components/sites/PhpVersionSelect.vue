<script setup lang="ts">
/**
 * Picker over the PHP versions actually installed on this server.
 *
 * There is no list of PHP versions anywhere in this bundle: the options are whatever
 * `GET /api/v1/sites/php-versions` reported, passed in by the page that loaded them
 * (rules/vue.md: "the frontend never invents domain data"). When the host has none, the
 * picker is replaced by a statement of that fact rather than an empty dropdown that looks
 * broken.
 *
 * `isDefault` is three-valued on purpose — `null` means the agent could not establish which
 * runtime is the host default, which is not the same answer as "no". Only a definite `true`
 * earns the "server default" label; an unknown is left unlabelled rather than shown as a
 * plain version that quietly asserts it is not the default.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { PhpVersion } from '../../types/phpVersion'

/** Props accepted by {@link PhpVersionSelect}. */
const props = withDefaults(
  defineProps<{
    /** The currently chosen version, or the empty string when none is chosen. */
    modelValue: string
    /** The versions the panel reported as installed on this host. */
    versions: readonly PhpVersion[]
    /** Validation message to show under the field, or `null` when the field is fine. */
    error?: string | null
    /** Whether the picker is inert, e.g. while a change is being applied. */
    disabled?: boolean
  }>(),
  { error: null, disabled: false },
)

/** Events emitted by {@link PhpVersionSelect}. */
const emit = defineEmits<{
  /**
   * The operator chose a different version.
   * @param e Event name.
   * @param value The version chosen.
   */
  (e: 'update:modelValue', value: string): void
}>()

const { t } = useI18n()

/** The installed versions as picker options, the host default named as such. */
const options: ComputedRef<SelectOption[]> = computed(() => {
  return props.versions.map((version) => {
    return {
      value: version.version,
      label:
        version.isDefault === true
          ? t('sites.form.phpVersionDefaultOption', { version: version.version })
          : t('sites.form.phpVersionOption', { version: version.version }),
    }
  })
})

/**
 * Forwards a chosen version to the parent.
 * @param value The version the picker reported.
 * @returns Nothing; the parent owns the value.
 */
const onUpdate = (value: string): void => {
  emit('update:modelValue', value)
}
</script>

<template>
  <UiAlert v-if="versions.length === 0" variant="error">
    {{ t('sites.form.noPhpVersions') }}
  </UiAlert>

  <div v-else>
    <UiSelect
      :model-value="modelValue"
      :label="t('sites.form.fields.phpVersion')"
      :options="options"
      :error="error"
      :disabled="disabled"
      required
      @update:model-value="onUpdate"
    />
    <p class="mt-1 text-sm text-text-muted">{{ t('sites.form.hints.phpVersion') }}</p>
  </div>
</template>
