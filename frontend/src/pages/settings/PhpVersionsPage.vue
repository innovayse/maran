<script setup lang="ts">
/**
 * The PHP versions this server has, and the one control that adds to them.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under.
 *
 * **Why a screen existed for choosing a version and none for adding one.** The
 * agent has been able to install a version on demand since plan 3; nothing drove
 * it. An operator whose customer needed 8.4 could see that 8.4 was absent from
 * every site form on the panel and had no way to say so — the only remedy was a
 * shell on the server, which is the thing a control panel exists to avoid.
 *
 * **Administrator-only, and the server is what says so.** No route guard
 * duplicates that decision, for the reason the audit journal's route records:
 * a second copy of an authorization rule is a second place for it to be wrong,
 * and the copy in the browser is the one that cannot be trusted. A customer who
 * types this URL sees the panel's own refusal rendered on the page.
 *
 * **The install does not resolve quickly and the screen says so.** It is the
 * host's package manager fetching over whatever mirror it has. The button holds
 * its pending state for the whole run, and the tasks screen carries the progress
 * the panel records against it.
 *
 * **Layout (redesign 2026-10).** An accent eyebrow over the title, then two panels: the
 * installed versions (a table with the version in the mono face, the default marked with a badge
 * so the word is never the only carrier) and the install control beside it from 1024px, below it
 * on a narrower screen. The table scrolls inside its own container, so nothing scrolls
 * horizontally at 390px.
 *
 * **A refusal to list is shown, and an empty list is only announced once the load has settled.**
 * The store records the server's refusal in `errorMessage`; the page prints it in place of the
 * table, instead of leaving a customer who typed the URL with an empty one. And
 * "No PHP version is installed" is a claim about the host, so it waits for the answer: drawn
 * before the request returned it would state, for a moment, a fact nobody had observed.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useSitesStore } from '../../stores/sites'

const { t } = useI18n()
const sitesStore = useSitesStore()

/** The version typed into the install field, e.g. `8.4`. */
const version: Ref<string> = ref('')


/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'version', label: t('php.table.version'), kind: 'identifier' },
    { key: 'default', label: t('php.table.default'), kind: 'badge' },
  ]
})

/**
 * Names whether a version is the host's default CLI PHP.
 *
 * Three answers, not two. `null` means the agent could not establish it, which is
 * not the same as "no" — printing "no" for it would state a fact nobody observed
 * (the backend keeps the two apart for exactly this reason).
 * @param isDefault What the panel reported for this version.
 * @returns The word an operator reads.
 */
const defaultLabel = (isDefault: boolean | null): string => {
  if (isDefault === null) {
    return t('php.table.defaultUnknown')
  }

  return isDefault ? t('php.table.defaultYes') : t('php.table.defaultNo')
}

/**
 * Asks the server to install the typed version.
 *
 * The field is cleared only when the server reports the version among those
 * installed afterwards. A field cleared on "the request did not throw" would
 * read as done for an install that failed.
 * @returns Resolves once the attempt has settled.
 */
const install = async (): Promise<void> => {
  const wanted = version.value.trim()
  if (wanted.length === 0) {
    return
  }

  const installed = await sitesStore.installPhpVersion(wanted)
  if (installed) {
    version.value = ''
  }
}

onMounted(async () => {
  await sitesStore.loadPhpVersions()
})
</script>

<template>
  <section class="flex flex-col gap-4">
    <UiPageHeading
      :title="t('php.title')"
      :subtitle="t('php.description')"
    />

    <div class="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)] lg:items-start">
      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('php.table.heading')" />

        <UiLoadingRegion :state="sitesStore.phpVersionsState">
          <!-- The server's own refusal or failure to list: the backend's localized sentence, so
               the SPA holds no copy of it. -->
          <template #error>
            <UiAlert variant="error">{{ sitesStore.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="sitesStore.phpVersionsState === 'loaded' && sitesStore.phpVersions.length === 0"
            :title="t('php.empty.title')"
            :description="t('php.empty.description')"
          />

          <UiTable
            v-else
            :caption="t('php.table.caption')"
            :columns="columns"
            :rows="sitesStore.phpVersions"
            row-key="version"
            :loading="sitesStore.phpVersionsState === 'loading'"
          >
            <template #cell-version="{ row }">{{ row.version }}</template>
            <template #cell-default="{ row }">
              <UiBadge v-if="row.isDefault === true" variant="success">{{ defaultLabel(row.isDefault) }}</UiBadge>
              <UiBadge v-else-if="row.isDefault === null" variant="warning">{{ defaultLabel(row.isDefault) }}</UiBadge>
              <template v-else>{{ defaultLabel(row.isDefault) }}</template>
            </template>
          </UiTable>
        </UiLoadingRegion>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('php.install.heading')" />
        <div class="flex flex-col gap-3">
          <UiInput
            v-model="version"
            :label="t('php.install.label')"
            placeholder="8.4"
            :disabled="sitesStore.installingPhpVersion !== null"
          />
          <p class="text-sm text-text-secondary">{{ t('php.install.hint') }}</p>

          <UiAlert v-if="sitesStore.phpInstallErrorMessage" variant="error">
            {{ sitesStore.phpInstallErrorMessage }}
          </UiAlert>

          <div>
            <UiButton
              :disabled="version.trim().length === 0 || sitesStore.installingPhpVersion !== null"
              @click="install"
            >
              {{
                sitesStore.installingPhpVersion === null
                  ? t('php.install.submit')
                  : t('php.install.running', { version: sitesStore.installingPhpVersion })
              }}
            </UiButton>
          </div>
        </div>
      </UiCard>
    </div>
  </section>
</template>
