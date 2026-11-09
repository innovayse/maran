import { useApi } from '../useApi'
import type { PhpSettingsApi, PhpSettingsDto, SavePhpSettingsRequest } from '../../types/phpSetting'

/** The single endpoint the customer's PHP settings are read from and written to; it names no account. */
const PHP_SETTINGS_PATH = '/api/v1/php-settings'

/**
 * Builds the customer's PHP-settings API on top of the shared low-level client.
 *
 * Neither call carries an account: the server takes it from the signed-in login, so nothing here
 * could aim a request at somebody else's settings.
 * @returns The {@link PhpSettingsApi} bound to the PHP-settings endpoint.
 */
export const usePhpSettingsApi = (): PhpSettingsApi => {
  const api = useApi()

  /**
   * Reads the caller's account's PHP settings.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings with the catalogue and the plan's bounds.
   */
  const get = (signal?: AbortSignal): Promise<PhpSettingsDto> => {
    return api.get<PhpSettingsDto>(PHP_SETTINGS_PATH, signal)
  }

  /**
   * Replaces the caller's account's stored settings.
   * @param request The settings to keep.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings as they now stand.
   */
  const save = (request: SavePhpSettingsRequest, signal?: AbortSignal): Promise<PhpSettingsDto> => {
    return api.put<PhpSettingsDto>(PHP_SETTINGS_PATH, request, signal)
  }

  return { get, save }
}
