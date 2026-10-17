import assert from 'node:assert/strict'
import { test } from 'node:test'
import { readAuthClaims } from './authClaims.ts'

const nameClaim = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'
const roleClaim = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'
const token = claims => `header.${Buffer.from(JSON.stringify(claims)).toString('base64url')}.signature`

test('reads legacy backend name and role claims rather than the user ID', () => {
  assert.deepEqual(readAuthClaims(token({
    sub: 'user-guid',
    [nameClaim]: 'admin',
    [roleClaim]: 'Admin',
  })), { username: 'admin', displayName: 'admin', roles: ['Admin'] })
})

test('decodes UTF-8 display names without replacing the login username', () => {
  assert.deepEqual(readAuthClaims(token({
    sub: 'user-guid',
    [nameClaim]: 'admin',
    display_name: '\u9673\u5927\u6587',
    [roleClaim]: ['Admin', 'Accountant'],
  })), { username: 'admin', displayName: '\u9673\u5927\u6587', roles: ['Admin', 'Accountant'] })
})

test('supports short name claims and ignores invalid claim types', () => {
  assert.equal(readAuthClaims(token({ name: 'alice' })).username, 'alice')
  assert.deepEqual(readAuthClaims(token({
    name: 123,
    unique_name: 'alice',
    display_name: ' ',
    role: ['Admin', 123],
  })), { username: 'alice', displayName: 'alice', roles: ['Admin'] })
})

test('never displays subject IDs as names and clears claims for an empty token', () => {
  const empty = { username: '', displayName: '', roles: [] }
  assert.deepEqual(readAuthClaims(token({ sub: 'user-guid' })), empty)
  assert.deepEqual(readAuthClaims(''), empty)
})

test('reports malformed tokens without throwing or exposing token contents', t => {
  const warn = t.mock.method(console, 'warn', () => {})
  assert.deepEqual(readAuthClaims('invalid'), { username: '', displayName: '', roles: [] })
  assert.equal(warn.mock.callCount(), 1)
  assert.equal(warn.mock.calls[0].arguments[0], 'Unable to decode JWT display claims')
})
