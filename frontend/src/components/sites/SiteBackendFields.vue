<script setup lang="ts">
/**
 * The part of the site form that depends on what will serve the site: the backend picker, and
 * the one extra field that backend needs.
 *
 * A PHP site needs a runtime; a Node.js site needs a runtime, the port its app listens on and the
 * command that starts it; a reverse-proxied site needs an upstream; a static site needs none of
 * these. Showing them all at once would ask the operator to ignore most of them and would
 * submit values the server's validator does not even look at — the backend applies its
 * `PhpVersion`, `ProxyUpstream` and Node.js rules only `.When()` the backend type calls for them, and
 * this component mirrors that shape rather than inventing its own.
 *
 * It owns no state: every value is a `defineModel`, so the page holds the request being built
 * and this component only decides what is on screen.
 *
 * It renders ONE root (a two-column grid from 768px, a single column below), so the page can
 * place the whole block as a unit; the earlier fragment of three roots could only be laid out
 * by whatever stack happened to contain it. The upstream hint wraps anywhere, because it quotes
 * an address and an unbroken value must not push a 390px screen sideways.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import PhpVersionSelect from './PhpVersionSelect.vue'
import type { PhpVersion } from '../../types/phpVersion'
import type { SiteBackendType, SiteChoice } from '../../types/site'

/** Props accepted by {@link SiteBackendFields}. */
const props = withDefaults(
  defineProps<{
    /** The backends the panel offers, already labelled in the operator's language. */
    backendChoices: readonly SiteChoice[]
    /** The PHP versions the panel reported as installed, for the runtime picker. */
    phpVersions: readonly PhpVersion[]
    /** The Node.js majors the panel reported as installed, already labelled in the operator's language. */
    nodeVersions?: readonly SiteChoice[]
    /** Validation message for the PHP version field, or `null`. */
    phpVersionError?: string | null
    /** Validation message for the Node.js version field, or `null`. */
    nodeVersionError?: string | null
    /** Validation message for the app port field, or `null`. */
    appPortError?: string | null
    /** Validation message for the start command field, or `null`. */
    startCommandError?: string | null
    /** Validation message for the upstream field, or `null`. */
    proxyUpstreamError?: string | null
  }>(),
  {
    nodeVersions: () => {
      return []
    },
    phpVersionError: null,
    nodeVersionError: null,
    appPortError: null,
    startCommandError: null,
    proxyUpstreamError: null,
  },
)

/** Which backend serves the site. Two-way bound to the page's request. */
const backendType = defineModel<SiteBackendType>('backendType', { required: true })

/** The PHP runtime to bind, meaningful only for a PHP backend. Two-way bound. */
const phpVersion = defineModel<string>('phpVersion', { required: true })

/** The Node.js major to run under, meaningful only for a Node.js backend. Two-way bound. */
const nodeVersion = defineModel<string>('nodeVersion', { required: true })

/** The port the Node.js app listens on as typed, meaningful only for a Node.js backend. Two-way bound. */
const appPort = defineModel<string>('appPort', { required: true })

/** The command that starts the Node.js app, meaningful only for a Node.js backend. Two-way bound. */
const startCommand = defineModel<string>('startCommand', { required: true })

/** The upstream to forward to, meaningful only for a reverse proxy. Two-way bound. */
const proxyUpstream = defineModel<string>('proxyUpstream', { required: true })

const { t } = useI18n()

/** The backends as picker options: the panel's own list and labels, none written into this bundle. */
const backendOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.backendChoices.map((choice) => {
    return { value: choice.value, label: choice.label }
  })
})

/** Whether the chosen backend needs a PHP runtime. */
const needsPhpVersion: ComputedRef<boolean> = computed(() => {
  return backendType.value === 'php'
})

/** Whether the chosen backend needs a Node.js runtime, a port and a start command. */
const needsNode: ComputedRef<boolean> = computed(() => {
  return backendType.value === 'node'
})

/** The installed Node.js majors as picker options. */
const nodeOptions: ComputedRef<SelectOption[]> = computed(() => {
  return props.nodeVersions.map((choice) => {
    return { value: choice.value, label: choice.label }
  })
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
  if (value !== 'node') {
    nodeVersion.value = ''
    appPort.value = ''
    startCommand.value = ''
  }
  if (value !== 'reverseProxy') {
    proxyUpstream.value = ''
  }
}
</script>

<template>
  <div class="grid gap-4 md:grid-cols-2">
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

    <template v-if="needsNode">
      <UiAlert v-if="nodeVersions.length === 0" variant="error">{{ t('sites.form.noNodeVersions') }}</UiAlert>
      <div v-else class="flex min-w-0 flex-col gap-2">
        <UiSelect
          v-model="nodeVersion"
          :label="t('sites.form.fields.nodeVersion')"
          :placeholder="t('sites.form.placeholders.nodeVersion')"
          :options="nodeOptions"
          :error="nodeVersionError"
          required
        />
        <UiFieldHint>{{ t('sites.form.hints.nodeVersion') }}</UiFieldHint>
      </div>
      <div class="min-w-0">
        <UiInput
          v-model="appPort"
          mono
          inputmode="numeric"
          :label="t('sites.form.fields.appPort')"
          :placeholder="t('sites.form.placeholders.appPort')"
          :error="appPortError"
          required
        />
        <UiFieldHint class="mt-1.5">{{ t('sites.form.hints.appPort') }}</UiFieldHint>
      </div>
      <div class="min-w-0 md:col-span-2">
        <UiInput
          v-model="startCommand"
          mono
          :label="t('sites.form.fields.startCommand')"
          :placeholder="t('sites.form.placeholders.startCommand')"
          :error="startCommandError"
          required
        />
        <UiFieldHint class="mt-1.5">{{ t('sites.form.hints.startCommand') }}</UiFieldHint>
      </div>
    </template>

    <div v-if="needsProxyUpstream" class="min-w-0">
      <UiInput
        v-model="proxyUpstream"
        :label="t('sites.form.fields.proxyUpstream')"
        :placeholder="t('sites.form.placeholders.proxyUpstream')"
        :error="props.proxyUpstreamError"
        required
      />
      <UiFieldHint class="mt-1.5">{{ t('sites.form.hints.proxyUpstream') }}</UiFieldHint>
    </div>
  </div>
</template>
