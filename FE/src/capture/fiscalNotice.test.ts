import { describe, expect, it } from 'vitest'

import type { FiscalDiagnosis, FiscalDiagnosisKind } from '../api/types'

import { fiscalNoticeOf } from './fiscalNotice'

/**
 * Scenarios from browser-client, "A photograph is taken only when the code produces no invoice":
 * the message chosen for each diagnosis, decidable without rendering (D47). Whether the QR code
 * content is shown is no longer the notice's to decide: it is shown for every failure.
 */
function diagnosis(
  kind: FiscalDiagnosisKind,
  overrides: Partial<FiscalDiagnosis> = {},
): FiscalDiagnosis {
  return {
    kind,
    detail: 'detail',
    missingParameters: null,
    portalStatus: null,
    responseExcerpt: null,
    ...overrides,
  }
}

const generic = /could not fetch the invoice/i

describe('The notice for a fiscal capture that fetched no invoice', () => {
  it('says the fiscal service could not be reached, and suggests trying later', () => {
    const notice = fiscalNoticeOf(diagnosis('PortalUnreachable'))

    expect(notice.message).toMatch(/fiscal service could not be reached/i)
    expect(notice.message).toMatch(/try again later/i)
  })

  it('names the status the fiscal service refused with', () => {
    const notice = fiscalNoticeOf(diagnosis('PortalRejected', { portalStatus: 503 }))

    expect(notice.message).toMatch(/refused/i)
    expect(notice.message).toContain('HTTP 503')
  })

  it('says the fiscal service refused even when no status came with it', () => {
    expect(fiscalNoticeOf(diagnosis('PortalRejected')).message).toMatch(/refused/i)
  })

  it('says the fiscal service has no record of the invoice yet', () => {
    const notice = fiscalNoticeOf(diagnosis('NoRecord'))

    expect(notice.message).toMatch(/no record of this invoice/i)
  })

  it('says the fiscal service answered unreadably', () => {
    const notice = fiscalNoticeOf(diagnosis('UnparseableResponse', { responseExcerpt: '<html>' }))

    expect(notice.message).toMatch(/could not be read/i)
  })

  it('says a code that is not a fiscal code is not one', () => {
    const notice = fiscalNoticeOf(diagnosis('NotFiscal'))

    expect(notice.message).toMatch(/not a fiscal receipt code/i)
  })

  it('names the missing parameters in words', () => {
    const notice = fiscalNoticeOf(
      diagnosis('IncompleteIdentifiers', { missingParameters: ['tin', 'crtd'] }),
    )

    expect(notice.message).toContain('the issuer tax number and the creation time')
    expect(notice.message).not.toMatch(/\btin\b|\bcrtd\b/)
  })

  it('names a single missing parameter', () => {
    const notice = fiscalNoticeOf(
      diagnosis('IncompleteIdentifiers', { missingParameters: ['iic'] }),
    )

    expect(notice.message).toContain('missing the invoice code')
  })

  it.each([null, undefined])(
    'falls back to the general statement when there is no diagnosis (%s)',
    missing => {
      const notice = fiscalNoticeOf(missing)

      expect(notice.message).toMatch(generic)
    },
  )

  it.each<FiscalDiagnosisKind>(['NoPayload', 'Retrieved'])(
    'falls back to the general statement for %s, which a Failed fiscal capture does not expect',
    kind => {
      expect(fiscalNoticeOf(diagnosis(kind)).message).toMatch(generic)
    },
  )
})
