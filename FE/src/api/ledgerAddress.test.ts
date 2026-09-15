import { describe, expect, it } from 'vitest'

import { parseLedgerAddress } from './ledgerAddress'

describe('A missing address stops the server', () => {
  it.each([undefined, '', '   '])('refuses %j, naming API_URL', value => {
    expect(() => parseLedgerAddress(value)).toThrow(/API_URL/)
    expect(() => parseLedgerAddress(value)).toThrow(/not set/i)
  })
})

describe('A malformed address stops the server', () => {
  it.each([
    ['a relative path', '/api'],
    ['another scheme', 'ftp://ledger.test:9000'],
    ['something that is not a URL', 'not a url'],
  ])('refuses %s, naming API_URL and why', (_, value) => {
    expect(() => parseLedgerAddress(value)).toThrow(/API_URL/)
    expect(() => parseLedgerAddress(value)).toThrow(/absolute http or https URL/i)
  })
})

describe('A trailing slash does not change the requests', () => {
  it('trims the trailing slash from a bare origin', () => {
    expect(parseLedgerAddress('http://ledger.test:9000/')).toBe('http://ledger.test:9000')
  })

  it('leaves an address without one as it is', () => {
    expect(parseLedgerAddress('http://ledger.test:9000')).toBe('http://ledger.test:9000')
  })

  it('keeps a path the API is served under, without its trailing slash', () => {
    expect(parseLedgerAddress('https://host/ledger/')).toBe('https://host/ledger')
  })
})
