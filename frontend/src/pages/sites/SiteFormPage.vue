<script setup lang="ts">
/**
 * Create-site screen. `UiForm` renders `<form novalidate>`, so the browser validates nothing
 * and every message the operator reads comes either from this page or from the server
 * (rules/vue.md: "Forms: the browser never validates").
 *
 * The client-side rules MIRROR `CreateSiteCommandValidator` — the same hostname pattern, the
 * same 253-character limit, the same host-or-host:port upstream shape, and the same
 * conditionality (a PHP version is only required for a PHP backend, an upstream only for a
 * reverse proxy). They are advice that saves a round trip; the server re-validates everything
 * and its already-localized rejection is rendered verbatim when it disagrees.
 *
 * A customer owns exactly one account, so they get no account picker: their own account id goes on
 * the request from their token, and the administrator-only accounts list is never read
 * (useAccountChoice).
 *
 * Renders a `<section>`, not a `<main>` — the layout owns the landmark.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiFieldHint from '../../components/ui/UiFieldHint.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiTextarea from '../../components/ui/UiTextarea.vue'
import SiteBackendFields from '../../components/sites/SiteBackendFields.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { usePhpVersionsStore } from '../../stores/phpVersions'
import { useSitesStore } from '../../stores/sites'
import type { SiteBackendType } from '../../types/site'
import { flashNoticeState } from '../../utils/flashNotice'

/**
 * The server's hostname rule, character for character: two or more dot-separated labels of
 * 1–63 alphanumerics or hyphens, never starting or ending with a hyphen.
 *
 * JavaScript's `$` does not have .NET's trailing-newline quirk, but the pattern is anchored
 * with an explicit `^`/`$` over a value that has had its whitespace trimmed first, so a pasted
 * `example.com\n` fails here exactly as it fails at the boundary that matters.
 */
const HOSTNAME_PATTERN = /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))+$/

/**
 * The server's upstream rule: a host, optionally with a port. Deliberately no scheme, no path
 * and no query — the value is written into an nginx `proxy_pass` target.
 */
const PROXY_UPSTREAM_PATTERN = /^[A-Za-z0-9.-]{1,253}(:[0-9]{1,5})?$/

/**
 * The lowest and highest loopback port a Node.js app may listen on: below 1024 are the privileged
 * ports, which the site's unprivileged service could not bind.
 */
const MIN_PORT = 1024
const MAX_PORT = 65535

/** The server's maximum length for a domain, an alias and an upstream alike. */
const MAX_HOST_LENGTH = 253

const { t } = useI18n()
const router = useRouter()
const store = useSitesStore()
const phpVersionsStore = usePhpVersionsStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()

/** The account that will own the site. */
const accountId: Ref<string> = ref('')

/** The primary domain the site serves. */
const domain: Ref<string> = ref('')

/** Alias hostnames, one per line as typed; split and trimmed on submit. */
const aliasesText: Ref<string> = ref('')

/** Which backend will serve the site. Static is the default: it needs nothing else chosen. */
const backendType: Ref<SiteBackendType> = ref('static')

/** The PHP runtime to bind, when the backend is PHP. */
const phpVersion: Ref<string> = ref('')

/** The Node.js major to run under, when the backend is Node.js. */
const nodeVersion: Ref<string> = ref('')

/** The port the Node.js app listens on as typed, when the backend is Node.js. */
const appPort: Ref<string> = ref('')

/** The command that starts the Node.js app, when the backend is Node.js. */
const startCommand: Ref<string> = ref('')

/** The upstream to forward to, when the backend is a reverse proxy. */
const proxyUpstream: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The aliases as they will be sent: split on lines, trimmed, blank lines dropped. */
const aliases: ComputedRef<string[]> = computed(() => {
  return aliasesText.value
    .split('\n')
    .map((alias) => {
      return alias.trim()
    })
    .filter((alias) => {
      return alias.length > 0
    })
})

/** The most aliases the server accepts, or `null` while it has not said (the form then stays permissive). */
const maximumAliases: ComputedRef<number | null> = computed(() => {
  return store.limits === null ? null : store.limits.maximumAliases
})

/**
 * Whether the list already holds more aliases than the server accepts. Shown as soon as it is
 * true, not only after a submit: the operator is typing past a hard limit, and finding out on
 * submit would mean deleting lines they were never going to be allowed to keep.
 */
const aliasLimitExceeded: ComputedRef<boolean> = computed(() => {
  return maximumAliases.value !== null && aliases.value.length > maximumAliases.value
})

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || fixedAccountId.value !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('sites.form.errors.accountRequired') : null
})

/** Validation message for the domain field, or `null`. */
const domainError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const value = domain.value.trim()
  if (value.length === 0) {
    return t('sites.form.errors.domainRequired')
  }
  if (value.length > MAX_HOST_LENGTH || !HOSTNAME_PATTERN.test(value)) {
    return t('sites.form.errors.domainInvalid')
  }
  return null
})

/**
 * Validation message for the alias list, or `null`. The count is checked against the server's own
 * limit, and every alias against the same hostname rule as the domain, because the server checks
 * each of them with `RuleForEach`.
 */
const aliasesError: ComputedRef<string | null> = computed(() => {
  if (aliasLimitExceeded.value) {
    return t('sites.form.errors.aliasesLimit', { max: maximumAliases.value })
  }
  if (!submitted.value) {
    return null
  }
  // The first bad line is named, so a long list does not leave the person hunting for it.
  const offending = aliases.value.find((alias) => {
    return alias.length > MAX_HOST_LENGTH || !HOSTNAME_PATTERN.test(alias)
  })
  return offending === undefined ? null : t('sites.form.errors.aliasInvalid', { alias: offending })
})

/** Validation message for the PHP version, or `null` when the backend does not need one. */
const phpVersionError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || backendType.value !== 'php') {
    return null
  }
  return phpVersion.value.length === 0 ? t('sites.form.errors.phpVersionRequired') : null
})

/** Validation message for the upstream, or `null` when the backend does not need one. */
const proxyUpstreamError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || backendType.value !== 'reverseProxy') {
    return null
  }
  const value = proxyUpstream.value.trim()
  if (value.length === 0) {
    return t('sites.form.errors.proxyUpstreamRequired')
  }
  if (value.length > MAX_HOST_LENGTH || !PROXY_UPSTREAM_PATTERN.test(value)) {
    return t('sites.form.errors.proxyUpstreamInvalid')
  }
  return null
})

/** The app port as a whole number, or `null` when what was typed is not a port. */
const parsedAppPort: ComputedRef<number | null> = computed(() => {
  const text = appPort.value.trim()
  if (!/^[0-9]{1,5}$/.test(text)) {
    return null
  }
  const port = Number(text)
  return port >= MIN_PORT && port <= MAX_PORT ? port : null
})

/** Validation message for the Node.js version, or `null` when the backend does not need one. */
const nodeVersionError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || backendType.value !== 'node') {
    return null
  }
  return nodeVersion.value.length === 0 ? t('sites.form.errors.nodeVersionRequired') : null
})

/** Validation message for the app port, or `null` when the backend does not need one. */
const appPortError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || backendType.value !== 'node') {
    return null
  }
  return parsedAppPort.value === null ? t('sites.form.errors.appPortInvalid') : null
})

/** Validation message for the start command, or `null` when the backend does not need one. */
const startCommandError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || backendType.value !== 'node') {
    return null
  }
  return startCommand.value.trim().length === 0 ? t('sites.form.errors.startCommandRequired') : null
})

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return (
    accountError.value === null &&
    domainError.value === null &&
    aliasesError.value === null &&
    phpVersionError.value === null &&
    nodeVersionError.value === null &&
    appPortError.value === null &&
    startCommandError.value === null &&
    proxyUpstreamError.value === null
  )
})

/**
 * Loads the PHP versions this host has installed and the backends the panel offers, which the form
 * selects from, and the limits the form shows. The account picker reads its own choices, one search at a time, when it is opened.
 * @returns Resolves once both requests have settled.
 */
const loadReferenceData = async (): Promise<void> => {
  await Promise.all([phpVersionsStore.loadPhpVersions(), store.loadLimits(), store.loadChoices()])
}

/**
 * Validates, then submits. On success the list page is where the new site is visible; on
 * failure the store holds the server's own message and the template renders it verbatim.
 * @returns Resolves once the attempt has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (!isValid.value) {
    return
  }

  const created = await store.create({
    accountId: fixedAccountId.value ?? accountId.value,
    domain: domain.value.trim(),
    aliases: aliases.value,
    backendType: backendType.value,
    phpVersion: phpVersion.value,
    proxyUpstream: proxyUpstream.value.trim(),
    nodeVersion: nodeVersion.value,
    appPort: backendType.value === 'node' ? (parsedAppPort.value ?? 0) : 0,
    startCommand: startCommand.value.trim(),
  })

  if (created !== null) {
    await router.push({
      name: 'sites',
      state: flashNoticeState(t('sites.form.created', { domain: created.domain })),
    })
  }
}

/**
 * Leaves the form without submitting.
 * @returns Resolves once navigation has been dispatched.
 */
const cancel = async (): Promise<void> => {
  await router.push({ name: 'sites' })
}

onMounted(loadReferenceData)
</script>

<template>
  <section class="flex w-full max-w-3xl min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('sites.form.heading')"
      :subtitle="t('sites.form.subtitle')"
    />

    <!-- The one thing that needs attention goes first: the server's own refusal, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <!-- `UiCard` does not clip its content, and that is load-bearing. An earlier version of this
         card was `overflow-hidden` so the action bar could bleed to its rounded corners, which
         also clipped the select's option list, because that list is positioned inside this box.
         The third plan on this form was rendered, present in the DOM, and unreachable: a hit test
         at its centre returned the card, so an assertion that the option exists passed while an
         operator could not choose it. The action bar below is therefore an ordinary row, never a
         strip that needs the card clipped. -->
    <UiCard>
      <UiForm @submit="submit">
        <div class="flex flex-col gap-5">
          <div class="grid gap-4 md:grid-cols-2">
            <UiCombobox
              v-if="fixedAccountId === null"
              v-model="accountId"
              v-bind="accountPicker"
              :label="t('sites.form.fields.account')"
              :placeholder="t('sites.form.placeholders.account')"
              :error="accountError"
              required
              @search="searchAccounts"
            />
            <UiInput
              v-model="domain"
              :label="t('sites.form.fields.domain')"
              :placeholder="t('sites.form.placeholders.domain')"
              :error="domainError"
              required
            />
          </div>
          <div>
            <UiTextarea
              v-model="aliasesText"
              :label="t('sites.form.fields.aliases')"
              :error="aliasesError"
              :rows="3"
            />
            <UiFieldHint class="mt-1.5">{{ t('sites.form.hints.aliases') }}</UiFieldHint>
            <UiFieldHint v-if="maximumAliases !== null" class="mt-1">
              {{ t('sites.form.hints.aliasesCount', { count: aliases.length, max: maximumAliases }) }}
            </UiFieldHint>
          </div>
          <div v-if="store.choices?.webServer" class="border-t border-border-subtle pt-5">
            <UiDescriptionList>
              <UiDescriptionItem :term="t('sites.form.fields.webServer')">{{ store.choices.webServer.label }}</UiDescriptionItem>
            </UiDescriptionList>
            <UiFieldHint class="mt-1.5">{{ t('sites.form.hints.webServer') }}</UiFieldHint>
          </div>
          <div class="border-t border-border-subtle pt-5">
            <SiteBackendFields
              v-model:backend-type="backendType"
              v-model:php-version="phpVersion"
              v-model:node-version="nodeVersion"
              v-model:app-port="appPort"
              v-model:start-command="startCommand"
              v-model:proxy-upstream="proxyUpstream"
              :backend-choices="store.choices?.backendTypes ?? []"
              :php-versions="phpVersionsStore.phpVersions"
              :node-versions="store.choices?.nodeVersions ?? []"
              :php-version-error="phpVersionError"
              :node-version-error="nodeVersionError"
              :app-port-error="appPortError"
              :start-command-error="startCommandError"
              :proxy-upstream-error="proxyUpstreamError"
            />
          </div>
          <div class="flex flex-col-reverse gap-2 border-t border-border-subtle pt-5 sm:flex-row sm:justify-end">
            <UiButton variant="secondary" type="button" @click="cancel">
              {{ t('common.cancel') }}
            </UiButton>
            <UiButton type="submit" :disabled="store.creating || aliasLimitExceeded">
              {{ t('sites.form.submit') }}
            </UiButton>
          </div>
        </div>
      </UiForm>
    </UiCard>
  </section>
</template>
