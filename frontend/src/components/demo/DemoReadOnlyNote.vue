<script setup lang="ts">
/**
 * The notice above a screen whose writes the demo refuses (the panel's security policy, mail
 * settings, certificate, licence, two-factor and API keys). It states the reason the BACKEND gives
 * for that area, verbatim, so the sentence a visitor reads here is the one the refused action would
 * also answer with.
 *
 * The area is chosen from the current route by the table below, which is demo-only knowledge and
 * therefore lives in this component instead of in the pages: no page of the normal bundle knows the
 * demo exists. The notice is advice for the reader; the refusal itself is enforced by the backend on
 * every request, so a route missing from the table costs a missing sentence, never a permission.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiAlert from '../ui/UiAlert.vue'
import { useDemoStore } from '../../stores/demo'

/** Route name to the refusal area the backend reports a reason under. */
const AREA_BY_ROUTE: Readonly<Record<string, string>> = {
  'security-policy': 'securityPolicy',
  'smtp-settings': 'smtp',
  'panel-certificate': 'panelCertificate',
  'panel-update': 'panelUpdate',
  'two-factor': 'twoFactor',
  'two-factor-setup': 'twoFactor',
  'api-keys': 'apiKeys',
  'reseller-api-keys': 'apiKeys',
  upgrade: 'licence',
}

const { t } = useI18n()
const route = useRoute()
const demoStore = useDemoStore()

/** The area of the current route, or `null` when the route is not read-only in the demo. */
const area: ComputedRef<string | null> = computed((): string | null => {
  const name = route.name
  return typeof name === 'string' ? (AREA_BY_ROUTE[name] ?? null) : null
})

/** The backend's sentence for that area, or the generic one when the status holds none. */
const reason: ComputedRef<string> = computed((): string => {
  return (area.value === null ? null : demoStore.reasonFor(area.value)) ?? t('demo.readOnly.generic')
})
</script>

<template>
  <section v-if="area !== null" :aria-label="t('demo.readOnly.region')" data-testid="demo-read-only-note">
    <UiAlert variant="warning">
      <strong>{{ t('demo.readOnly.title') }}</strong>
      {{ reason }}
    </UiAlert>
  </section>
</template>
