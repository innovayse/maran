import type { FileTransferProtocol } from '../types/fileTransferLogin'

/**
 * Narrows a protocol token the panel sent into the union the merged screen renders.
 *
 * Three inputs and three different answers, because they are three different situations and folding
 * any two of them together loses the one that matters:
 *
 * - a token this bundle recognises becomes that protocol, matched case-insensitively because the
 *   server spells its enum members `Sftp`/`Ftps` and its JSON converter may camel-case them;
 * - an ABSENT token means the response predates the field. The caller says what the endpoint
 *   answers by construction — `/api/v1/sftp-users` has only ever returned SFTP logins — which is
 *   the same reading the backend applies to the wire's own absent protocol value;
 * - an UNRECOGNISED token becomes `unknown` and is rendered as absence. It is deliberately NOT
 *   folded into {@link whenAbsent}: a third protocol from a newer panel labelled as SFTP would be a
 *   confident, wrong instruction to a customer, and a blank is the honest answer instead.
 * @param token The protocol as the panel spelled it, or `undefined`/`null` when the response
 * carried no such field.
 * @param whenAbsent What the endpoint answers by construction, used only when the token is absent.
 * @returns The protocol this row is rendered as.
 */
export const fileTransferProtocolOf = (
  token: string | null | undefined,
  whenAbsent: FileTransferProtocol,
): FileTransferProtocol => {
  if (token === null || token === undefined || token.length === 0) {
    return whenAbsent
  }

  const normalised = token.toLowerCase()

  if (normalised === 'sftp') {
    return 'sftp'
  }

  if (normalised === 'ftps') {
    return 'ftps'
  }

  return 'unknown'
}
