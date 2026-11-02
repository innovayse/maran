<script setup lang="ts">
/**
 * The team screen of the customer's own account: invite a person, choose which gated features they may
 * use, and remove them. Renders a `<section>`, not a `<main>` — the single landmark lives in the layout.
 * Rows come from the sub-users store; the page never touches the API layer (rules/vue.md).
 *
 * **Only the account's owner can use it.** Every endpoint behind it answers 403 to a team member and to
 * an administrator, and that refusal is rendered verbatim (the backend's sentence, already in the
 * caller's language) in place of the table. Nothing here decides who may see the page: the screen is
 * advice, the endpoints are the boundary.
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search and sort live in the query.
 *
 * **Two kinds of nothing.** No team members at all is the empty state; a search matching none is the
 * no-results state, with the way back to everyone.
 */
import { computed, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import SubUserFeaturesDialog from '../../components/sub-users/SubUserFeaturesDialog.vue'
import SubUserInviteForm from '../../components/sub-users/SubUserInviteForm.vue'
import SubUserRemoveConfirm from '../../components/sub-users/SubUserRemoveConfirm.vue'
import SubUsersTable from '../../components/sub-users/SubUsersTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useSubUsersStore } from '../../stores/subUsers'
import type { ListLoad, ListRequest } from '../../types/list'
import type { InviteSubUserRequest, SubUser, SubUserAction } from '../../types/subUser'

const { t } = useI18n()
const store = useSubUsersStore()
const labels = useListLabels()

const table = useServerTable<SubUser>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<SubUser>> => {
    return store.list(request)
  },
})

/** The invite form, so an accepted invitation can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** The sub-user whose features are being changed, or `null` when no dialog is open. */
const editing: Ref<SubUser | null> = ref(null)

/** The sub-user awaiting removal confirmation, or `null` when none is. */
const removing: Ref<SubUser | null> = ref(null)

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether the panel refused the list (a team member or an administrator opened the page). */
const refused: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'refused'
})

/**
 * Invites a person, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The login name, address and features the form collected.
 * @returns Resolves once the invite and the re-read have settled.
 */
const invite = async (request: InviteSubUserRequest): Promise<void> => {
  if (await store.invite(request)) {
    notice.value = t('subUsers.invited', { name: request.username })
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts an action on one row: the features dialog or the removal confirmation.
 * @param row The row the owner acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (row: SubUser, action: SubUserAction): void => {
  if (action === 'features') {
    editing.value = row
  } else {
    removing.value = row
  }
}

/**
 * Saves the features chosen in the dialog and closes it when the panel accepted them.
 * @param featureIds The complete new set of feature ids.
 * @returns Resolves once the request has settled.
 */
const saveFeatures = async (featureIds: string[]): Promise<void> => {
  const target = editing.value
  if (target === null) {
    return
  }
  if (await store.changeFeatures(target.id, { features: featureIds })) {
    notice.value = t('subUsers.featuresChanged', { name: target.username })
    editing.value = null
  }
}

/**
 * Carries out the confirmed removal and reads the page again so it refills from the next one.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  const target = removing.value
  if (target === null) {
    return
  }
  if (await store.remove(target.id)) {
    notice.value = t('subUsers.removed', { name: target.username })
    await table.reload()
  }
  // Closed after the request settles rather than before it is sent: the dialog holds the spinner.
  removing.value = null
}

/**
 * Closes the features dialog without saving.
 * @returns Nothing.
 */
const closeEditing = (): void => {
  editing.value = null
}

/**
 * Closes the removal confirmation without removing.
 * @returns Nothing.
 */
const closeRemoving = (): void => {
  removing.value = null
}

/**
 * Clears the search, the way back from a question that matched nobody.
 * @returns Nothing; the list reads again after the box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(store.loadFeatures)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('subUsers.heading')" :subtitle="t('subUsers.subtitle')" />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="refused && table.state.value.status === 'refused'" variant="error">
      {{ table.state.value.message }}
    </UiAlert>
    <UiAlert v-if="store.inviteErrorMessage !== null" variant="error">{{ store.inviteErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
    <UiAlert v-if="!refused && store.featuresErrorMessage !== null" variant="error">{{ store.featuresErrorMessage }}</UiAlert>

    <template v-if="!refused">
      <SubUserInviteForm ref="form" :features="store.features" :submitting="store.inviting" @submit="invite" />

      <UiEmptyState
        v-if="answeredNothing && !isNarrowed"
        :title="t('subUsers.emptyTitle')"
        :description="t('subUsers.emptyDescription')"
      >
        <template #icon><UiIcon name="users" size="lg" /></template>
      </UiEmptyState>

      <UiCard v-else>
        <div class="flex flex-col gap-4">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85"
            :label="t('subUsers.searchLabel')"
            :placeholder="t('subUsers.searchPlaceholder')"
            :clear-label="t('subUsers.searchClear')"
          />

          <UiLoadingRegion :state="table.loadingState.value">
            <template #error>
              <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
                {{ table.state.value.message }}
              </UiAlert>
              <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
            </template>

            <UiEmptyState
              v-if="answeredNothing"
              :title="t('subUsers.noResultsTitle')"
              :description="t('subUsers.noResultsDescription')"
            >
              <UiButton variant="secondary" @click="clearSearch">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
            </UiEmptyState>

            <div v-else class="flex flex-col gap-4">
              <SubUsersTable
                :rows="store.subUsers"
                :features="store.features"
                :loading="table.state.value.status === 'loading'"
                :skeleton-rows="table.pageSize.value"
                :sort="table.sort.value"
                @sort="table.toggleSort"
                @act="ask"
              />

              <UiPagination
                v-if="table.state.value.status === 'loaded'"
                :page="table.page.value"
                :page-size="table.pageSize.value"
                :total="table.total.value"
                :page-sizes="table.pageSizes"
                :labels="labels.pagination.value"
                @update:page="table.setPage"
                @update:page-size="table.setPageSize"
              />
            </div>
          </UiLoadingRegion>
        </div>
      </UiCard>
    </template>

    <SubUserFeaturesDialog
      :sub-user="editing"
      :features="store.features"
      :acting="store.acting"
      @close="closeEditing"
      @save="saveFeatures"
    />

    <SubUserRemoveConfirm :sub-user="removing" :acting="store.acting" @close="closeRemoving" @confirm="confirmRemove" />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
