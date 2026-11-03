import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'
import { useSitesApi } from '../composables/apis/useSitesApi'
import { ApiError } from '../composables/useApi'
import type { LoadingState } from '../types/loading'
import type { InstallablePhpVersion, PhpVersion } from '../types/phpVersion'

/**
 * Owns the PHP versions installed on the host and the install of a new one. The site form, the
 * site list's filter, the detail page's version picker and the PHP settings page all read them
 * from here.
 *
 * Error text is never generated here: the backend's already-localized message is stored verbatim.
 */
export const usePhpVersionsStore = defineStore('phpVersions', () => {
  const api = useSitesApi()

  /** The PHP versions installed on the host, as last loaded. Never a list written into the SPA. */
  const phpVersions: Ref<PhpVersion[]> = ref([])

  /** Where the read of {@link phpVersions} stands; every failure sets `error`, whatever its cause. */
  const phpVersionsState: Ref<LoadingState> = ref('loading')

  /** The versions the host can install, installed ones flagged, as last loaded. Never written into the SPA. */
  const installablePhpVersions: Ref<InstallablePhpVersion[]> = ref([])

  /** Where the read of {@link installablePhpVersions} stands. */
  const installablePhpVersionsState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from the most recent failed read of the installable versions, or `null`. */
  const installableErrorMessage: Ref<string | null> = ref(null)

  /** The version an install is running for, or `null` when none is. */
  const installingPhpVersion: Ref<string | null> = ref(null)

  /**
   * Why the last install attempt failed, verbatim from the panel, or `null`.
   *
   * Separate from the store's general `errorMessage` for the reason the create flow keeps its own:
   * an install that fails must not wipe the reason a different request failed, and the screen
   * shows this one next to the control that caused it.
   */
  const phpInstallErrorMessage: Ref<string | null> = ref(null)

  /**
   * Backend-localized message from the most recent failed read of {@link phpVersions}, or `null`
   * when the last read succeeded or none has been attempted. Rendered verbatim.
   */
  const errorMessage: Ref<string | null> = ref(null)

  /**
   * Loads the PHP versions installed on the host, which the site form selects from.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadPhpVersions = async (): Promise<void> => {
    phpVersionsState.value = 'loading'
    try {
      phpVersions.value = await api.listPhpVersions()
      errorMessage.value = null
      phpVersionsState.value = 'loaded'
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
      phpVersionsState.value = 'error'
    }
  }

  /**
   * Loads the versions the host can install, which the install picker selects from.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadInstallablePhpVersions = async (): Promise<void> => {
    installablePhpVersionsState.value = 'loading'
    try {
      installablePhpVersions.value = await api.listInstallablePhpVersions()
      installableErrorMessage.value = null
      installablePhpVersionsState.value = 'loaded'
    } catch (error) {
      installableErrorMessage.value = error instanceof ApiError ? error.message : null
      installablePhpVersionsState.value = 'error'
    }
  }

  /**
   * Asks the server to install a PHP version, and replaces the held list with what it answers.
   *
   * The list is REPLACED by the server's answer rather than appended to with the version that was
   * requested: what this host has is the agent's to report, and a list grown from the request
   * would show a version as installed on the strength of having asked for it.
   * @param version The version to install, e.g. `8.4`.
   * @returns `true` when the server reports the version among those installed afterwards.
   */
  const installPhpVersion = async (version: string): Promise<boolean> => {
    installingPhpVersion.value = version
    try {
      phpVersions.value = await api.installPhpVersion(version)
      phpInstallErrorMessage.value = null
      // The installed flags changed, so the picker's options are re-read from the server rather than
      // patched locally with the version that was asked for.
      await loadInstallablePhpVersions()
      return phpVersions.value.some((installed) => {
        return installed.version === version
      })
    } catch (error) {
      phpInstallErrorMessage.value = error instanceof ApiError ? error.message : null
      return false
    } finally {
      installingPhpVersion.value = null
    }
  }

  return {
    phpVersions,
    phpVersionsState,
    installablePhpVersions,
    installablePhpVersionsState,
    installableErrorMessage,
    installingPhpVersion,
    phpInstallErrorMessage,
    errorMessage,
    loadPhpVersions,
    loadInstallablePhpVersions,
    installPhpVersion,
  }
})
