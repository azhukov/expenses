import type { FiscalDiagnosis, FiscalParameter } from '../api/types'

/** What the capture screen says when a fiscal capture fetched no invoice. */
export interface FiscalNotice {
  message: string
}

const GENERAL = 'We could not fetch the invoice for this receipt. Photograph the receipt instead.'

/** The verification parameters as a person would name them, never as the code spells them. */
const PARAMETER_NAMES: Record<FiscalParameter, string> = {
  iic: 'the invoice code',
  tin: 'the issuer tax number',
  crtd: 'the creation time',
}

/** "a", "a and b", "a, b and c". */
function listed(names: string[]): string {
  return names.length < 2
    ? names.join('')
    : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

/**
 * The capture screen's notice for a fiscal capture that came back Failed, chosen by the kind of
 * diagnosis rather than by matching its English detail (D47). A missing diagnosis, from a server
 * that predates it, gets the general statement. So do the two kinds a Failed fiscal capture does
 * not expect: `NoPayload` (it had a payload) and `Retrieved` (it would not have failed).
 */
export function fiscalNoticeOf(diagnosis: FiscalDiagnosis | null | undefined): FiscalNotice {
  switch (diagnosis?.kind) {
    case 'PortalUnreachable':
      return {
        message:
          'The fiscal service could not be reached. Photograph the receipt, or try again later.',
      }

    case 'PortalRejected':
      return {
        message:
          diagnosis.portalStatus === null
            ? 'The fiscal service refused the request. Photograph the receipt instead.'
            : `The fiscal service refused the request (HTTP ${diagnosis.portalStatus}). Photograph the receipt instead.`,
      }

    case 'NoRecord':
      return {
        message:
          'The fiscal service has no record of this invoice yet. Photograph the receipt, or try again later.',
      }

    case 'UnparseableResponse':
      return {
        message:
          'The fiscal service answered, but its response could not be read. Photograph the receipt instead.',
      }

    case 'NotFiscal':
      return {
        message:
          "This code is not a fiscal receipt code. Scan the receipt's own QR code, or photograph the receipt.",
      }

    case 'IncompleteIdentifiers': {
      const missing = listed(
        (diagnosis.missingParameters ?? []).map(parameter => PARAMETER_NAMES[parameter]),
      )

      return {
        message:
          missing === ''
            ? 'The code is missing details the fiscal service needs. Photograph the receipt instead.'
            : `The code is missing ${missing}. Photograph the receipt instead.`,
      }
    }

    default:
      return { message: GENERAL }
  }
}
