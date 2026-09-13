<script setup lang="ts">
/**
 * The part of the site form that depends on what will serve the site: the backend picker, and
 * the one extra field that backend needs.
 *
 * A PHP site needs a runtime; a reverse-proxied site needs an upstream; a static site needs
 * neither. Showing all three at once would ask the operator to ignore two of them and would
 * submit values the server's validator does not even look at — the backend applies its
 * `PhpVersion` and `ProxyUpstream` rules only `.When()` the backend type calls for them, and
 * this component mirrors that shape rather than inventing its own.
 *
 * It owns no state: every value is a `defineModel`, so the page holds the request being built
 * and this component only decides what is on screen.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import PhpVersionSelect from './PhpVersionSelect.vue'
import type { PhpVersion } from '../../types/phpVersion'
import type { SiteBackendType } from '../../types/site'

/** Props accepted by {@link SiteBackendFields}. */
const props = withDefaults(
  defineProps<{
    /** The PHP versions the panel reported as installed, for the runtime picker. */
    phpVersions: readonly PhpVersion[]
    /** Validation message for the PHP version field, or `null`. */
    phpVersionError?: string | null
    /** Validation message for the upstream field, or `null`. */
    proxyUpstreamError?: string | null
  }>(),
  { phpVersionError: null, proxyUpstreamError: null },
)

/** Which backend serves the site. Two-way bound to the page's request. */
const backendType = defineModel<SiteBackendType>('backendType', { required: true })

/** The PHP runtime to bind, meaningful only for a PHP backend. Two-way bound. */
const phpVersion = defineModel<string>('phpVersion', { required: true })

/** The upstream to forward to, meaningful only for a reverse proxy. Two-way bound. */
const proxyUpstream = defineModel<string>('proxyUpstream', { required: true })

const { t } = useI18n()

/**
 * The three backends the contract defines, labelled from the panel's own chrome.
 *
 * These are UI labels for a closed enum the API contract fixes, not server-side reference
 * data: `SiteBackendType` has exactly these members, and a fourth would be a proto change.
 */
const backendOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: 'static', label: t('sites.backendType.static') },
    { value: 'php', label: t('sites.backendType.php') },
    { value: 'reverseProxy', label: t('sites.backendType.reverseProxy') },
  ]
})

/** Whether the chosen backend needs a PHP runtime. */
const needsPhpVersion: ComputedRef<boolean> = computed(() => {
  return backendType.value === 'php'
})

/** Whether the chosen backend needs an upstream. */
const needsProxyUpstream: ComputedRef<boolean> = computed(() => {
  return backendType.value === 'reverseProxy'
})

/**
 * Applies a chosen backend and clears the field the previous backend owned.
 *
 * Clearing matters: a PHP version left behind after switching to a static site would be sent
 * in the create request, and a value the server ignores today is a value it may reject
 * tomorrow.
 * @param value The backend the picker reported.
 * @returns Nothing.
 */
const onBackendChange = (value: string): void => {
  backendType.value = value as SiteBackendType
  if (value !== 'php') {
    phpVersion.value = ''
  }
  if (value !== 'reverseProxy') {
    proxyUpstream.value = ''
  }
}
</script>

<template>
  <UiSelect
    :model-value="backendType"
    :label="t('sites.form.fields.backendType')"
    :options="backendOptions"
    required
    @update:model-value="onBackendChange"
  />

  <PhpVersionSelect
    v-if="needsPhpVersion"
    v-model="phpVersion"
    :versions="props.phpVersions"
    :error="props.phpVersionError"
  />

  <div v-if="needsProxyUpstream">
    <UiInput
      v-model="proxyUpstream"
      :label="t('sites.form.fields.proxyUpstream')"
      :placeholder="t('sites.form.placeholders.proxyUpstream')"
      :error="props.proxyUpstreamError"
      required
    />
    <p class="mt-1 text-sm text-text-muted">{{ t('sites.form.hints.proxyUpstream') }}</p>
  </div>
</template>
