import { describe, expect, it } from 'vitest'

import { parsePreviewHosts } from './previewHosts'

describe('Nothing listed still serves localhost', () => {
  it.each([undefined, '', '  ', ','])('lists no extra host for %j', value => {
    expect(parsePreviewHosts(value)).toEqual([])
  })
})

describe('Several hosts are listed', () => {
  it('splits on commas, trimming whitespace and dropping empty entries', () => {
    expect(parsePreviewHosts(' a.example , b.example,')).toEqual(['a.example', 'b.example'])
  })

  it('lists the Railway domain on its own', () => {
    expect(parsePreviewHosts('expenses-frontend-production-2e52.up.railway.app')).toEqual([
      'expenses-frontend-production-2e52.up.railway.app',
    ])
  })
})

describe('A URL instead of a host name stops the server', () => {
  it.each([
    ['a URL', 'https://expenses-frontend-production-2e52.up.railway.app/'],
    ['a host with a port', 'a.example:4173'],
    ['a value with whitespace inside', 'a.example b.example'],
  ])('refuses %s, naming PREVIEW_ALLOWED_HOSTS and the value', (_, value) => {
    expect(() => parsePreviewHosts(value)).toThrow(/PREVIEW_ALLOWED_HOSTS/)
    expect(() => parsePreviewHosts(value)).toThrow(`'${value}'`)
  })
})
