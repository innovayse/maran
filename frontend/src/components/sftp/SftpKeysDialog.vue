<script setup lang="ts">
/**
 * The keys panel of one SFTP login, in a dialog: the login's public keys (searched by label and paged
 * by the server), the form that adds one, and the confirmation that removes one. The login and every
 * list figure come from the keys store, which the logins page opens; this component never reaches
 * the API layer (rules/vue.md).
 *
 * **Removing a key ends the login's open sessions**, so the removal is always confirmed and the
 * confirmation says so. Adding a key never ends a session, and says nothing of the kind.
 */
import { computed, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiConfirm from '../ui/UiConfirm.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiLoadingRegion from '../ui/UiLoadingRegion.vue'
import UiModal from '../ui/UiModal.vue'
import UiPagination from '../ui/UiPagination.vue'
import UiSearchInput from '../ui/UiSearchInput.vue'
import SftpKeyAddForm from './SftpKeyAddForm.vue'
import SftpKeyList from './SftpKeyList.vue'
import { useDebouncedSearch } from '../../composables/useDebouncedSearch'
import { useListLabels } from '../../composables/useListLabels'
import { useSftpKeysStore } from '../../stores/sftpKeys'
import { SEARCH_TERM_MAX_LENGTH } from '../../utils/searchTermLimit'
import type { AddSftpUserKeyRequest, SftpUser, SftpUserKey } from '../../types/sftpUser'

/** How long typing must pause before the label search is sent, in milliseconds. */
const SEARCH_DELAY_MS = 300

/** Props accepted by {@link SftpKeysDialog}; the page mounts it only while a login's keys are open. */
defineProps<{
  /** The login whose keys are shown; the keys store holds the same login and its keys. */
  login: SftpUser
}>()

/** Events emitted by {@link SftpKeysDialog}. */
const emit = defineEmits<{
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const store = useSftpKeysStore()
const labels = useListLabels()

/** The add form, so an accepted add can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The dialog's content region, where focus returns when the control that opened the confirmation is gone. */
const region = useTemplateRef<HTMLElement>('region')

/** The key awaiting a removal confirmation, or `null` when none is. */
const pendingRemoval: Ref<SftpUserKey | null> = ref(null)

/** The label search box: its text is sent to the server once typing pauses, unless it is too long. */
const search = useDebouncedSearch(
  (term: string): void => {
    void store.searchKeys(term)
  },
  SEARCH_DELAY_MS,
  true,
)

/** The message under the search box while its text is longer than the list accepts. */
const searchError: ComputedRef<string | null> = computed(() => {
  return search.tooLong.value ? t('common.list.searchTooLong', { max: SEARCH_TERM_MAX_LENGTH }) : null
})

/**
 * Names where focus goes when the removal confirmation closes: the removed key's button no longer
 * exists, and the fallback would be `<main>`, outside this dialog's focus trap.
 * @returns The content region, which is focusable by script only.
 */
const returnFocus = (): HTMLElement | null => {
  return region.value
}

/**
 * Adds the key, and empties the form only when the panel stored it.
 * @param request The label and key line the form collected.
 * @returns Resolves once the request and the re-read settled.
 */
const add = async (request: AddSftpUserKeyRequest): Promise<void> => {
  if (await store.addKey(request)) {
    form.value?.reset()
  }
}

/**
 * Starts a removal, which then waits for confirmation.
 * @param key The key the person acted on.
 * @returns Nothing.
 */
const ask = (key: SftpUserKey): void => {
  pendingRemoval.value = key
}

/**
 * Abandons a pending removal.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingRemoval.value = null
}

/**
 * Carries out the confirmed removal; the confirmation stays until the request settles.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const key = pendingRemoval.value
  if (key === null) {
    return
  }
  await store.removeKey(key.id)
  pendingRemoval.value = null
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('sftp.keys.title', { name: login.fullName })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <div ref="region" tabindex="-1" class="flex flex-col gap-4 focus-visible:outline-none">
      <p>{{ t('sftp.keys.intro') }}</p>

      <UiAlert v-if="store.removeErrorMessage !== null" variant="error">{{ store.removeErrorMessage }}</UiAlert>

      <UiSearchInput
        :model-value="search.query.value"
        :label="t('sftp.keys.searchLabel')"
        :placeholder="t('sftp.keys.searchPlaceholder')"
        :clear-label="t('sftp.keys.searchClear')"
        :error="searchError"
        @update:model-value="search.type"
      />

      <UiLoadingRegion :state="store.loadState">
        <template #error>
          <UiAlert variant="error">{{ store.loadErrorMessage ?? t('common.list.unreachable') }}</UiAlert>
        </template>

        <UiEmptyState
          v-if="store.total === 0 && store.search === ''"
          :title="t('sftp.keys.emptyTitle')"
          :description="t('sftp.keys.emptyDescription')"
        />
        <UiEmptyState
          v-else-if="store.total === 0"
          :title="t('sftp.keys.noResultsTitle')"
          :description="t('sftp.keys.noResultsDescription')"
        />
        <div v-else class="flex flex-col gap-3">
          <SftpKeyList :keys="store.keys" :busy="store.removing" @remove="ask" />
          <UiPagination
            :page="store.page"
            :page-size="store.pageSize"
            :total="store.total"
            :page-sizes="store.pageSizes"
            :labels="labels.pagination.value"
            @update:page="store.setPage"
            @update:page-size="store.setPageSize"
          />
        </div>
      </UiLoadingRegion>

      <section class="flex flex-col gap-3 border-t border-border-subtle pt-4">
        <h3 class="font-display text-base font-extrabold text-text-primary">{{ t('sftp.keys.form.heading') }}</h3>
        <UiAlert v-if="store.addErrorMessage !== null" variant="error">{{ store.addErrorMessage }}</UiAlert>
        <SftpKeyAddForm ref="form" :submitting="store.adding" @submit="add" />
      </section>
    </div>
  </UiModal>

  <UiConfirm
    v-if="pendingRemoval !== null"
    :open="true"
    :title="t('sftp.keys.confirmRemoveTitle', { label: pendingRemoval.label })"
    :question="t('sftp.keys.confirmRemove', { name: login.fullName })"
    :confirm-label="t('sftp.keys.confirmRemoveAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="store.removing"
    :acting-label="t('sftp.keys.working')"
    :return-focus-to="returnFocus"
    destructive
    @close="cancel"
    @confirm="confirm"
  />
</template>
