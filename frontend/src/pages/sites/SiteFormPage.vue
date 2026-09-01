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
 * Renders a `<section>`, not a `<main>` — the layout owns the landmark.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiForm from '../../components/ui/UiForm.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiTextarea from '../../components/ui/UiTextarea.vue'
import SiteBackendFields from '../../components/sites/SiteBackendFields.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useSitesStore } from '../../stores/sites'
import type { SiteBackendType } from '../../types/site'

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

/** The server's maximum length for a domain, an alias and an upstream alike. */
const MAX_HOST_LENGTH = 253

const { t } = useI18n()
const router = useRouter()
const store = useSitesStore()
const accountsStore = useAccountsStore()

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

/** The upstream to forward to, when the backend is a reverse proxy. */
const proxyUpstream: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** The accounts the picker offers, as the panel reported them. */
const accountOptions: ComputedRef<SelectOption[]> = computed(() => {
  return accountsStore.accounts.map((account) => {
    return { value: account.id, label: `${account.name} · ${account.primaryDomain}` }
  })
})

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

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
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
 * Validation message for the alias list, or `null`. Every alias is checked against the same
 * hostname rule as the domain, because the server checks each of them with `RuleForEach`.
 */
const aliasesError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const offending = aliases.value.some((alias) => {
    return alias.length > MAX_HOST_LENGTH || !HOSTNAME_PATTERN.test(alias)
  })
  return offending ? t('sites.form.errors.domainInvalid') : null
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

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return (
    accountError.value === null &&
    domainError.value === null &&
    aliasesError.value === null &&
    phpVersionError.value === null &&
    proxyUpstreamError.value === null
  )
})

/**
 * Loads the two sets of reference data the form selects from: the accounts that can own a site
 * and the PHP versions this host has installed. Neither is written into the SPA.
 * @returns Resolves once both requests have settled.
 */
const loadReferenceData = async (): Promise<void> => {
  await Promise.all([accountsStore.load(), store.loadPhpVersions()])
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
    accountId: accountId.value,
    domain: domain.value.trim(),
    aliases: aliases.value,
    backendType: backendType.value,
    phpVersion: phpVersion.value,
    proxyUpstream: proxyUpstream.value.trim(),
  })

  if (created !== null) {
    await router.push({ name: 'sites' })
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
  <section class="w-full max-w-2xl">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('sites.form.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('sites.form.subtitle') }}</p>
    </div>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <!-- NOT `overflow-hidden`. The footer bar below bleeds to the card's edges and needs its
         bottom corners rounded, and clipping the card was the way that was done — which also
         clipped the select's option list, because that list is positioned inside this box. The
         third plan on this form was rendered, present in the DOM, and unreachable: a hit test at
         its centre returned the card, so an assertion that the option exists passed while an
         operator could not choose it. The footer rounds its own corners instead. -->
    <div class="rounded-xl border border-border-subtle bg-surface-1">
      <UiForm @submit="submit">
        <div class="flex flex-col gap-3.5 p-4.5">
          <UiSelect
            v-model="accountId"
            :label="t('sites.form.fields.accountId')"
            :options="accountOptions"
            :error="accountError"
            required
          />
          <UiInput
            v-model="domain"
            :label="t('sites.form.fields.domain')"
            :placeholder="t('sites.form.placeholders.domain')"
            :error="domainError"
            required
          />
          <div>
            <UiTextarea
              v-model="aliasesText"
              :label="t('sites.form.fields.aliases')"
              :error="aliasesError"
              :rows="3"
            />
            <p class="mt-1 text-sm text-text-muted">{{ t('sites.form.hints.aliases') }}</p>
          </div>
          <SiteBackendFields
            v-model:backend-type="backendType"
            v-model:php-version="phpVersion"
            v-model:proxy-upstream="proxyUpstream"
            :php-versions="store.phpVersions"
            :php-version-error="phpVersionError"
            :proxy-upstream-error="proxyUpstreamError"
          />
        </div>
        <div class="flex justify-end gap-2 rounded-b-xl border-t border-border-subtle bg-surface-2 px-4.5 py-3">
          <UiButton variant="secondary" type="button" @click="cancel">
            {{ t('sites.form.cancel') }}
          </UiButton>
          <UiButton type="submit" :disabled="store.creating">{{ t('sites.form.submit') }}</UiButton>
        </div>
      </UiForm>
    </div>
  </section>
</template>
