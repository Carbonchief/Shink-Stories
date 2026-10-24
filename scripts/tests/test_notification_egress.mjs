import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../../Shink/Components/Layout/MainLayout.razor.js', import.meta.url), 'utf8');
// Execute the production functions with a small DOM boundary; no duplicated
// notification request/state implementation and no real network or user data.
function extract(name) {
  const pattern = new RegExp(`^(?:async )?function ${name}\\(`, 'm');
  const start = source.search(pattern);
  assert.ok(start >= 0, name);
  const rest = source.slice(start);
  const next = rest.slice(1).search(/\n(?:async )?function /);
  return next < 0 ? rest : rest.slice(0, next + 1);
}
function harness() {
  const pending = [];
  const state = {
    requestVersion: 0, refreshQueued: false, isLoaded: false, isLoading: false,
    isMarkingRead: false, isClearing: false, isLoadingMore: false,
    notifications: [], unreadCount: 0, clearingNotificationIds: new Set(), removingNotificationIds: new Set()
  };
  const display = { badge: 0, messages: [] };
  const root = { open: false, classList: { contains: () => root.open } };
  const parts = { centerRoot: root, notificationLoading: { hidden: true } };
  const context = vm.createContext({
    URL, Set, console, window: { location: { origin: 'https://example.test' } },
    OPEN_CLASS: 'is-open', NOTIFICATION_ENDPOINT: '/api/notifications', NOTIFICATION_PAGE_SIZE: 10,
    NOTIFICATION_READ_ALL_ENDPOINT: '/api/notifications/read-all',
    findNotificationCenterParts: () => parts, getNotificationCenterState: () => state,
    setNotificationCount: (_, count) => { display.badge = count; },
    renderNotificationItems: (_, rows) => { display.messages = rows.map(item => item.id); },
    syncNotificationClearButton: () => {}, syncNotificationLoadMoreButton: () => {},
    fetch: (url, options) => new Promise(resolve => pending.push({ url, options, resolve })),
  });
  for (const name of ['buildNotificationRequestUrl', 'getOldestNotificationCreatedAt',
    'refreshNotificationCenter', 'loadNotificationBadge', 'loadNotificationCenter', 'loadMoreNotifications', 'markNotificationCenterRead']) {
    vm.runInContext(extract(name), context);
  }
  const finish = (index, data, status = 200) => pending[index].resolve({ ok: status < 400, status, json: async () => data });
  return { context, state, root, display, pending, finish };
}
const page = id => ({ unreadCount: 0, hasMore: true, hasHistory: true,
  notifications: [{ id, createdAt: '2026-10-23T00:00:00Z', isRead: true }] });

test('closed panel requests only badge data and keeps messages unloaded', async () => {
  const h = harness();
  const read = h.context.refreshNotificationCenter(h.root);
  assert.equal(h.pending[0].url, '/api/notifications?summary=true');
  h.finish(0, { unreadCount: 4 });
  await read;
  assert.equal(h.display.badge, 4);
  assert.equal(h.state.isLoaded, false);
  assert.equal(h.display.messages.length, 0);
});

test('opening requests full page; an older badge cannot overwrite its count', async () => {
  const h = harness();
  const badge = h.context.refreshNotificationCenter(h.root);
  h.root.open = true;
  const full = h.context.loadNotificationCenter(h.root, { force: true });
  assert.equal(h.pending[1].url, '/api/notifications?limit=10');
  h.finish(1, page('new'));
  await full;
  h.finish(0, { unreadCount: 99 });
  await badge;
  assert.equal(h.display.badge, 0);
  assert.equal(h.display.messages.join(','), 'new');
  assert.equal(h.state.hasHistory, true);
  assert.equal(h.state.hasMore, true);
});

test('refresh during a full read queues a fresh read instead of dropping it', async () => {
  const h = harness();
  h.root.open = true;
  const first = h.context.loadNotificationCenter(h.root, { force: true });
  await h.context.refreshNotificationCenter(h.root);
  assert.equal(h.pending.length, 1);
  h.finish(0, page('old'));
  await first;
  assert.equal(h.pending.length, 2);
  h.finish(1, page('fresh'));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.display.messages.join(','), 'fresh');
});

test('optimistic mark-read is not undone by an older badge response', async () => {
  const h = harness();
  h.state.unreadCount = 4;
  h.state.notifications = [{ id: 'one', isRead: false }];
  const badge = h.context.loadNotificationBadge(h.root);
  const marked = h.context.markNotificationCenterRead(h.root);
  assert.equal(h.pending[1].url, '/api/notifications/read-all');
  assert.equal(h.display.badge, 0);
  h.finish(1, {});
  await marked;
  h.finish(0, { unreadCount: 4 });
  await badge;
  assert.equal(h.display.badge, 0);
  assert.equal(h.state.notifications[0].isRead, true);
});

test('failed badge refresh retains the last known count', async () => {
  const h = harness();
  h.display.badge = 3;
  const badge = h.context.loadNotificationBadge(h.root);
  h.finish(0, {}, 503);
  await badge;
  assert.equal(h.display.badge, 3);
});


test('history pagination still loads cleared/older notifications', async () => {
  const h = harness();
  h.root.open = true;
  const full = h.context.loadNotificationCenter(h.root, { force: true });
  h.finish(0, page('current'));
  await full;
  h.state.hasMore = false;
  const history = h.context.loadMoreNotifications(h.root);
  assert.match(h.pending[1].url, /history=true/);
  h.finish(1, { notifications: [{ id: 'cleared', isCleared: true, createdAt: '2026-10-22T00:00:00Z' }], hasMore: false, hasHistory: false });
  await history;
  assert.equal(h.display.messages.join(','), 'current,cleared');
  assert.equal(h.state.isShowingHistory, true);
});

test('an older history response cannot append to a refreshed notification page', async () => {
  const h = harness();
  h.root.open = true;
  const first = h.context.loadNotificationCenter(h.root, { force: true });
  h.finish(0, page('current'));
  await first;
  const oldHistory = h.context.loadMoreNotifications(h.root);
  const refreshed = h.context.refreshNotificationCenter(h.root);
  h.finish(2, page('fresh'));
  await refreshed;
  h.finish(1, page('stale'));
  await oldHistory;
  assert.equal(h.display.messages.join(','), 'fresh');
});
