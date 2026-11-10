<script setup lang="ts">
/**
 * One account export: the package's facts, the key fingerprint the receiving administrator compares,
 * the download, and the single-use pull grants (issue #127). This page owns the export's read and the
 * token; the cards are dumb.
 *
 * An export that expired, or never existed, is the not-found state, not an alert. The token of a grant
 * just issued is held by the store until its dialog is closed, and the page drops it when it is left,
 * so a token never outlives the screen that showed it. Renders a `<section>`, not a `<main>`.
 */
import { computed, onBeforeUnmount, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import ExportGrantsPanel from '../../components/migration/ExportGrantsPanel.vue'
import ExportPackageCard from '../../components/migration/ExportPackageCard.vue'
import GrantTokenDialog from '../../components/migration/GrantTokenDialog.vue'
import { useAccountExportsStore } from '../../stores/accountExports'
import type { AccountExport, IssueExportGrantRequest } from '../../types/accountMove'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The export's identity, from `/migration/account-export/:id`. */
  id: string
}>()

const { t } = useI18n()
const store = useAccountExportsStore()

/** The export on screen once the panel has answered with it; an export the store holds from an earlier visit is not drawn. */
const accountExport: ComputedRef<AccountExport | null> = computed(() => {
  return store.current !== null && store.current.id === props.id ? store.current : null
})

/** Whether the export does not exist: the read finished and found nothing. */
const missing: ComputedRef<boolean> = computed(() => {
  return store.currentState === 'loaded' && accountExport.value === null
})

/**
 * Saves the package through the browser.
 * @returns Resolves once the download has settled.
 */
const download = async (): Promise<void> => {
  await store.download()
}

/**
 * Issues a grant; its token opens in the dialog.
 * @param request The address range and lifetime.
 * @returns Resolves once the request has settled.
 */
const issue = async (request: IssueExportGrantRequest): Promise<void> => {
  await store.issueGrant(request)
}

/**
 * Closes the token dialog and forgets the token.
 * @returns Nothing.
 */
const closeToken = (): void => {
  store.dismissToken()
}

onMounted(async () => {
  store.clearErrors()
  await store.loadOne(props.id)
})

// The token is the one copy there will ever be; it must not stay in memory once nobody can see it.
onBeforeUnmount(() => {
  store.dismissToken()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="store.currentState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="missing"
        :title="t('accountMove.export.detail.notFoundTitle')"
        :description="t('accountMove.export.detail.notFoundDescription')"
      >
        <UiLink :to="{ name: 'account-export' }" icon="chevronLeft">{{ t('accountMove.export.detail.backToForm') }}</UiLink>
      </UiEmptyState>

      <div v-else class="flex min-w-0 flex-col gap-4">
        <UiPageHeading
          :title="t('accountMove.export.detail.heading', { name: accountExport?.accountName ?? '' })"
          :subtitle="t('accountMove.export.detail.subtitle')"
          :loading="accountExport === null"
        >
          <template #actions>
            <UiLink :to="{ name: 'account-export' }" icon="chevronLeft">{{ t('accountMove.export.detail.backToForm') }}</UiLink>
          </template>
        </UiPageHeading>

        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

        <template v-if="accountExport !== null">
          <UiCard>
            <ExportPackageCard :account-export="accountExport" :busy="store.busy" @download="download" />
          </UiCard>
          <UiCard>
            <ExportGrantsPanel :account-name="accountExport.accountName" :busy="store.busy" @issue="issue" />
          </UiCard>
        </template>
      </div>
    </UiLoadingRegion>

    <GrantTokenDialog v-if="store.issuedToken !== null" :grant="store.issuedToken" @close="closeToken" />
  </section>
</template>
