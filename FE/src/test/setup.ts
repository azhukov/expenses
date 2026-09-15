import '@testing-library/jest-dom/vitest'

// What `/config.js` would have set in a served page. No test reaches it: fetch is always stubbed.
window.__EXPENSES_CONFIG__ = { apiUrl: 'http://ledger.test:9000' }
