<script setup lang="ts">
/**
 * The text editor for one file of an account: `/files/edit?account=<id>&path=<file>`. The path is in
 * the URL so a reload, a bookmark and Back all land on the same file. Renders a `<section>`, not a
 * `<main>`: the landmark lives in the layout.
 *
 * Everything it shows comes from the editor store; it never touches the API layer (rules/vue.md).
 * The text is edited in a code editor (`UiCodeEditor`, CodeMirror loaded lazily), never rendered as markup. A file the server will not open
 * (too large, binary) shows the server's sentence and a download button instead of an editor.
 *
 * **Saving is optimistic.** The store sends the modification instant it loaded; if the file
 * changed on disk since, the server refuses and this page offers the two honest ways out, reload
 * (losing the edit) or save over it.
 *
 * **Leaving with unsaved edits asks first**, for a route change by a dialog and for closing the tab
 * by the browser's own prompt (`useUnsavedChangesGuard`).
 */
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import FileDownloadButton from '../../components/files/FileDownloadButton.vue'
import FileEditorToolbar from '../../components/files/FileEditorToolbar.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCodeEditor from '../../components/ui/UiCodeEditor.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useUnsavedChangesGuard } from '../../composables/useUnsavedChangesGuard'
import { useFileEditorStore } from '../../stores/fileEditor'
import { baseName, parentPath } from '../../utils/filePath'
import type { LoadingState } from '../../types/loading'

const { t } = useI18n()
const route = useRoute()
const store = useFileEditorStore()
const { fixedAccountId } = useAccountChoice()

/** Whether long lines stay on one line instead of wrapping. */
const noWrap: Ref<boolean> = ref(false)

/** Whether the Tab key indents; off by default so Tab never traps keyboard focus. */
const tabIndents: Ref<boolean> = ref(false)

/** The editor, to open its find panel and go-to-line prompt from the toolbar. */
const editor: Ref<InstanceType<typeof UiCodeEditor> | null> = ref(null)

/** The account whose file is edited: a customer's own, otherwise the one the URL names. */
const accountId: ComputedRef<string> = computed(() => {
  const named = route.query.account
  return fixedAccountId.value ?? (typeof named === 'string' ? named : '')
})

/** The file being edited, from the URL; empty when the address names none. */
const filePath: ComputedRef<string> = computed(() => {
  const named = route.query.path
  return typeof named === 'string' ? named : ''
})

/** Where "back" goes: the folder that holds the file, in the same account. */
const folderTarget: ComputedRef<{ name: string; query: Record<string, string> }> = computed(() => {
  return {
    name: 'file-manager',
    query: { account: accountId.value, path: parentPath(filePath.value) },
  }
})

/** The loading region's state for the file read. */
const loadState: ComputedRef<LoadingState> = computed(() => {
  if (store.status === 'failed' || store.status === 'notEditable') {
    return 'error'
  }
  return store.status === 'ready' ? 'loaded' : 'loading'
})

/** Whether the address names a file to open at all. */
const hasTarget: ComputedRef<boolean> = computed(() => {
  return accountId.value !== '' && filePath.value !== ''
})

/** The sentence for a failed read: the server's, or the panel's own when nothing answered. */
const loadErrorText: ComputedRef<string> = computed(() => {
  return store.loadErrorMessage ?? t('filesEditor.unreachable')
})

/** The sentence for a failed save: the server's, or the panel's own when nothing answered. */
const saveErrorText: ComputedRef<string> = computed(() => {
  return store.saveErrorMessage ?? t('filesEditor.saveFailed')
})

const guard = useUnsavedChangesGuard(
  computed(() => {return store.dirty}),
  (): void => {
    store.close()
  },
)

/**
 * Reads the file the address names.
 * @returns Resolves once the read has settled.
 */
const load = async (): Promise<void> => {
  if (hasTarget.value) {
    await store.open(accountId.value, filePath.value)
  }
}

/**
 * Saves the draft.
 * @returns Resolves once the request has settled.
 */
const save = async (): Promise<void> => {
  await store.save()
}

/**
 * Saves over a newer file on disk, the answer to a conflict.
 * @returns Resolves once the request has settled.
 */
const saveAnyway = async (): Promise<void> => {
  await store.save(true)
}

/**
 * Reads the file again, discarding the edits.
 * @returns Resolves once the read has settled.
 */
const reload = async (): Promise<void> => {
  await store.reload()
}

/**
 * Saves from the editor's own shortcut, under the same rules as the Save button.
 * @returns Resolves once the request has settled; nothing is sent when saving is not allowed.
 */
const saveFromShortcut = async (): Promise<void> => {
  if (store.canSave) {
    await save()
  }
}

/**
 * Opens find and replace in the text.
 * @returns Nothing.
 */
const find = (): void => {
  editor.value?.openSearch()
}

/**
 * Opens the go-to-line prompt in the text.
 * @returns Nothing.
 */
const goToLine = (): void => {
  editor.value?.openGoToLine()
}

/**
 * Hands the editor's new text to the store.
 * @param value The editor's new text.
 * @returns Nothing.
 */
const edit = (value: string): void => {
  store.edit(value)
}

onMounted(load)

// The same component serves every file, so a changed address (the guard has already let it
// through) means another file to read.
watch([accountId, filePath], load)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="baseName(filePath)"
      :subtitle="t('filesEditor.subtitle', { path: filePath })"
      mono
      :loading="store.status === 'loading'"
    />

    <div v-if="hasTarget">
      <UiButton variant="ghost" :to="folderTarget">{{ t('filesEditor.back') }}</UiButton>
    </div>

    <UiEmptyState v-if="!hasTarget" :title="t('filesEditor.heading')" :description="t('filesEditor.needsAccount')" />

    <UiLoadingRegion v-else :state="loadState">
      <template #error>
        <UiAlert variant="error">
          {{ loadErrorText }}
          <template v-if="store.status === 'notEditable'" #actions>
            <span class="text-sm">{{ t('filesEditor.notEditableHint') }}</span>
            <FileDownloadButton :account-id="accountId" :path="filePath" :name="baseName(filePath)" />
          </template>
        </UiAlert>
      </template>

      <UiCard>
        <div class="flex flex-col gap-4">
          <UiAlert v-if="store.conflict" variant="warning">
            <strong>{{ t('filesEditor.conflictTitle') }}</strong>
            {{ saveErrorText }} {{ t('filesEditor.conflictHint') }}
            <template #actions>
              <UiButton variant="secondary" @click="reload">{{ t('filesEditor.conflictReload') }}</UiButton>
              <UiButton :disabled="store.saving" @click="saveAnyway">{{ t('filesEditor.conflictOverwrite') }}</UiButton>
            </template>
          </UiAlert>
          <UiAlert v-else-if="store.saveErrorMessage !== null" variant="error">{{
            saveErrorText
          }}</UiAlert>

          <FileEditorToolbar
            v-model:no-wrap="noWrap"
            v-model:tab-indents="tabIndents"
            :account-id="accountId"
            :path="filePath"
            :can-save="store.canSave"
            :saving="store.saving"
            :just-saved="store.justSaved"
            :size-bytes="store.draftBytes"
            :limit-bytes="store.options?.maxEditBytes ?? null"
            :over-limit="store.overLimit"
            :mode="store.mode"
            :editor-ready="store.status === 'ready'"
            @save="save"
            @reload="reload"
            @find="find"
            @go-to-line="goToLine"
          />

          <UiCodeEditor
            ref="editor"
            :model-value="store.draft"
            :label="t('filesEditor.textLabel')"
            :file-name="baseName(filePath)"
            :no-wrap="noWrap"
            :tab-indents="tabIndents"
            :disabled="store.status !== 'ready'"
            @update:model-value="edit"
            @save="saveFromShortcut"
          />
        </div>
      </UiCard>
    </UiLoadingRegion>

    <UiConfirm
      :open="guard.asking.value"
      :title="t('filesEditor.leaveTitle')"
      :question="t('filesEditor.leaveQuestion', { path: filePath })"
      :confirm-label="t('filesEditor.leaveConfirm')"
      :cancel-label="t('filesEditor.leaveCancel')"
      :close-label="t('filesEditor.close')"
      :acting-label="t('filesEditor.leaveActing')"
      @close="guard.stay"
      @confirm="guard.leave"
    />
  </section>
</template>
