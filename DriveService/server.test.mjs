import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { request as httpRequest } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import test from 'node:test';
import { countPairingAttempt, createDriveServer, readConfig } from './server.mjs';

const settings = {
    origin: 'https://dvr.example.test', key: Buffer.alloc(32, 76), clientId: 'mock-client', clientSecret: 'mock-secret',
};
const scope = 'https://www.googleapis.com/auth/drive.readonly';
const refreshToken = 'mock refresh credential should never appear in device responses or on disk';
const video = (id, name) => ({ id, name });
const folderMime = 'application/vnd.google-apps.folder';
const shortcutMime = 'application/vnd.google-apps.shortcut';
const account = { displayName: 'Test <name>', emailAddress: 'viewer@example.test' };
const pilotOrigin = 'http://127.0.0.1:48180';
test('pairing quotas charge accepted attempts atomically and expire after one minute', () => {
    const counters = new Map();
    const charge = (client, now = 0) => countPairingAttempt(counters, 'create', client, 1, 2, now);
    charge('first');
    assert.throws(() => charge('first'), { status: 429 });
    charge('second');
    assert.throws(() => charge('third'), { status: 429 });
    assert.equal(counters.has('create:ip:third'), false);
    charge('first', 60_000);
});
const pilotEnv = { PUBLIC_ORIGIN: pilotOrigin, GOOGLE_CLIENT_ID: 'mock-client', GOOGLE_CLIENT_SECRET: 'mock-secret', STORAGE_KEY: settings.key.toString('base64') };

async function fixture(t, database = ':memory:') {
    const state = { calls: [], failRevocation: false };
    async function google(input, options) {
        const url = new URL(input);
        state.calls.push({ url, options });
        assert.equal(options.redirect, 'manual');
        if (url.pathname === '/token') {
            const params = options.body;
            assert.equal(params.get('client_id'), settings.clientId);
            assert.equal(params.get('client_secret'), settings.clientSecret);
            if (params.get('grant_type') === 'authorization_code') {
                assert.equal(params.get('redirect_uri'), `${settings.origin}/oauth/callback`);
                assert.equal(createHash('sha256').update(params.get('code_verifier')).digest('base64url'), state.challenge);
                state.exchangeStarted?.();
                await state.blockExchange;
                return Response.json({ refresh_token: refreshToken, scope });
            }
            assert.equal(params.get('refresh_token'), refreshToken);
            return state.expired ? Response.json({ error: 'invalid_grant' }, { status: 400 })
                : Response.json({ access_token: 'google-access-token', expires_in: 3600 });
        }
        if (url.pathname === '/revoke') {
            assert.equal(options.body.get('token'), refreshToken);
            return Response.json({}, { status: state.failRevocation ? 503 : 200 });
        }

        assert.equal(options.headers.Authorization, 'Bearer google-access-token');
        if (state.rejectAccess) return Response.json({}, { status: 401 });
        const q = url.searchParams;
        if (url.pathname.endsWith('/about')) {
            assert.equal(q.get('fields'), 'user(displayName,emailAddress)');
            return Response.json({ user: { ...account, permissionId: 'ignored-private-id' }, storageQuota: {} });
        }
        if (url.pathname.endsWith('/files')) {
            assert.equal(q.get('includeItemsFromAllDrives'), 'true');
            assert.equal(q.get('supportsAllDrives'), 'true');
            if (q.get('fields').includes('shortcutDetails')) {
                const filter = q.get('q');
                if (filter.includes("'root' in parents")) {
                    assert.equal(q.get('corpora'), 'user');
                    return Response.json({ files: [{ id: 'nested', name: 'Nested', mimeType: folderMime }], nextPageToken: 'nested-page-2' });
                }
                if (filter.includes("'nested' in parents")) {
                    return Response.json({ files: [{ id: 'movies', name: 'Movies', mimeType: folderMime, resourceKey: 'nested-key' }] });
                }
                if (filter.includes("'movies' in parents")) {
                    assert.equal(options.headers['X-Goog-Drive-Resource-Keys'], 'movies/nested-key');
                    return Response.json({ files: [
                        { id: 'movie-file', name: 'Movie.mp4', mimeType: 'video/mp4' },
                        { id: 'document', name: 'A document', mimeType: 'application/pdf' },
                    ] });
                }
                if (filter.includes("'team-drive' in parents")) {
                    assert.equal(q.get('corpora'), 'drive');
                    assert.equal(q.get('driveId'), 'team-drive');
                    return Response.json({ files: [{ id: 'team-folder', name: 'Team folder', mimeType: folderMime, driveId: 'team-drive' }] });
                }
                if (filter.includes('sharedWithMe = true')) {
                    return Response.json({ files: [{ id: 'shared', name: 'Shared', mimeType: folderMime }] });
                }
                assert.ok(filter.includes("name contains 'Movies'"));
                assert.equal(q.has('orderBy'), false);
                return Response.json({ files: [
                    { id: 'movies', name: 'Movies', mimeType: folderMime, resourceKey: 'nested-key' },
                    { id: 'shortcut', name: 'Movies shortcut', mimeType: shortcutMime, driveId: 'not-target-drive', shortcutDetails: {
                        targetId: 'movies', targetMimeType: folderMime, targetResourceKey: 'nested-key',
                    } },
                ] });
            }
            assert.match(q.get('q'), /trashed = false and mimeType contains 'video\//);
            if (q.get('q').includes('name contains')) {
                assert.ok(q.get('q').endsWith("name contains 'O\\'Brien\\\\Show'"));
                return Response.json({ files: [video('searched', 'O’Brien Show')] });
            }
            if (q.get('corpora') === 'user') {
                return q.get('pageToken') === 'user-page-2' ? Response.json({ files: [video('shared', 'Shared with me')] })
                    : Response.json({ files: [{ ...video('my-video', 'My video'), resourceKey: 'test-resource' }], nextPageToken: 'user-page-2' });
            }
            assert.equal(q.get('driveId'), 'team-drive');
            return Response.json({ files: [video('team-video', 'Team video')] });
        }
        if (url.pathname.endsWith('/drives')) return Response.json({ drives: [{ id: 'team-drive', name: 'Team Drive' }] });

        if (url.pathname.endsWith('/not-video')) return Response.json({ mimeType: 'application/pdf', capabilities: { canDownload: true } });
        assert.ok(url.pathname.endsWith('/my-video'));
        assert.equal(options.headers['X-Goog-Drive-Resource-Keys'], 'my-video/test-resource');
        if (!q.has('alt')) return Response.json({ mimeType: 'video/mp4', size: '8', capabilities: { canDownload: true } });
        assert.equal(options.headers['Accept-Encoding'], 'identity');
        if (state.redirectMedia) return new Response(null, { status: 302, headers: { Location: 'https://other.example.test/file' } });
        if (state.keepStreamOpen) {
            return new Response(new ReadableStream({
                start(controller) {
                    controller.enqueue(Buffer.from('01'));
                    options.signal.addEventListener('abort', () => controller.error(new Error('Stream aborted')), { once: true });
                },
            }), { headers: { 'Content-Length': '8' } });
        }
        if (options.headers.Range === 'bytes=999-') return new Response('', { status: 416, headers: { 'Content-Range': 'bytes */8' } });
        if (options.headers.Range === 'bytes=2-4') {
            return new Response('234', { status: 206, headers: { 'Content-Length': '3', 'Content-Range': 'bytes 2-4/8' } });
        }
        return new Response('01234567', { headers: { 'Content-Length': '8' } });
    }
    let server;
    let root;
    async function start() {
        server = createDriveServer({ ...settings, database }, google).listen(0, '127.0.0.1');
        await once(server, 'listening');
        root = `http://127.0.0.1:${server.address().port}`;
    }
    async function stop() {
        const closed = once(server, 'close');
        server.close();
        server.closeAllConnections();
        await closed;
    }
    await start();
    t.after(stop);
    const request = (path, options) => fetch(root + path, { redirect: 'manual', ...options });
    const api = (path, bearer, options) => request(`/v1/${path}`, { ...options, headers: { Authorization: `Bearer ${bearer}`, ...options?.headers } });
    async function begin() {
        const create = await request('/v1/pairings', { method: 'POST' });
        assert.equal(create.status, 201);
        const pairing = await create.json();
        assert.match(pairing.code, /^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
        assert.equal(pairing.entryUrl, settings.origin + '/pair');
        assert.equal(Buffer.from(pairing.qr, 'base64').subarray(1, 4).toString(), 'PNG');
        const form = await request(`/pair?code=${pairing.code}`);
        assert.match(await form.text(), new RegExp(`value="${pairing.code}"`));

        assert.deepEqual(await (await api('session', pairing.bearer)).json(), { state: 'pending' });
        const resumed = await (await api('session?details=1', pairing.bearer)).json();
        assert.deepEqual(resumed.pairing, { code: pairing.code, entryUrl: pairing.entryUrl, qr: pairing.qr });
        assert.equal((await api('videos', pairing.bearer)).status, 409);
        const consent = await request('/pair', {
            method: 'POST', headers: { Origin: settings.origin }, body: new URLSearchParams({ code: pairing.code.toLowerCase() }),
        });
        assert.equal(consent.status, 303);
        const authorization = new URL(consent.headers.get('location'));
        assert.equal(authorization.origin, 'https://accounts.google.com');
        assert.equal(authorization.searchParams.get('scope'), scope);
        assert.equal(authorization.searchParams.get('access_type'), 'offline');
        assert.equal(authorization.searchParams.get('code_challenge_method'), 'S256');
        state.challenge = authorization.searchParams.get('code_challenge');
        const cookie = consent.headers.get('set-cookie');
        assert.match(cookie, /Secure; HttpOnly; SameSite=Lax/);
        return { ...pairing, cookie: cookie.split(';')[0], state: authorization.searchParams.get('state') };
    }

    const callback = (pair, cookie) => request(`/oauth/callback?code=given-google-code&state=${pair.state}`,
        { headers: cookie ? { Cookie: cookie } : {} });

    return { state, request, api, begin, callback, start, stop };
}

test('pairs with Google consent, survives restart, and browses and streams My Drive and shared videos', async t => {
    const folder = await mkdtemp(join(tmpdir(), 'dvr-drive-test-'));
    const database = join(folder, 'drive.sqlite');
    const drive = await fixture(t, database);
    t.after(() => rm(folder, { force: true, recursive: true }));
    const pair = await drive.begin();
    assert.equal((await drive.callback(pair)).status, 400);
    const connected = await drive.callback(pair, pair.cookie);
    assert.equal(connected.status, 200);
    assert.match(await connected.text(), /Google Drive is connected/);
    assert.equal((await drive.callback(pair, pair.cookie)).status, 400);

    await drive.stop();
    const persisted = await readFile(database);
    assert.equal(persisted.includes(refreshToken), false);
    assert.equal(persisted.includes(pair.bearer), false);
    await drive.start();
    assert.deepEqual(await (await drive.api('session', pair.bearer)).json(), { state: 'connected' });

    const first = await (await drive.api('videos?limit=1', pair.bearer)).json();
    assert.equal(first.files[0].id, 'my-video');
    const page = `videos?limit=1&cursor=${first.nextCursor}`;
    assert.equal((await drive.api(page + 'tampered', pair.bearer)).status, 400);
    assert.equal((await drive.api(page + '&q=other', pair.bearer)).status, 400);
    const second = await (await drive.api(page, pair.bearer)).json();
    assert.equal(second.files[0].id, 'shared');
    const third = await (await drive.api(`videos?limit=1&cursor=${second.nextCursor}`, pair.bearer)).json();
    assert.deepEqual(third, { files: [video('team-video', 'Team video')], nextCursor: '' });
    const searched = await (await drive.api(`videos?q=${encodeURIComponent("O'Brien\\Show")}`, pair.bearer)).json();
    assert.equal(searched.files[0].id, 'searched');

    const path = 'media/my-video?key=test-resource';
    assert.equal((await drive.request('/v1/' + path)).status, 401);
    assert.equal((await drive.api('media/not-video', pair.bearer)).status, 404);
    const head = await drive.api(path, pair.bearer, { method: 'HEAD' });
    assert.equal(head.headers.get('content-length'), '8');
    assert.equal(head.headers.get('cache-control'), 'no-store');
    const all = await drive.api(path, pair.bearer);
    assert.equal(all.status, 200);
    assert.equal(all.headers.get('content-type'), 'video/mp4');
    assert.equal(await all.text(), '01234567');
    const range = await drive.api(path, pair.bearer, { headers: { Range: 'bytes=2-4' } });
    assert.equal(range.status, 206);
    assert.equal(range.headers.get('content-range'), 'bytes 2-4/8');
    assert.equal(range.headers.get('accept-ranges'), 'bytes');
    assert.equal(await range.text(), '234');
    const invalid = await drive.api(path, pair.bearer, { headers: { Range: 'bytes=999-' } });
    assert.equal(invalid.status, 416);
    assert.equal(invalid.headers.get('content-range'), 'bytes */8');
    assert.equal((await drive.api(path, pair.bearer, { headers: { Range: 'bytes=0-1,2-3' } })).status, 416);
    assert.equal((await drive.api(path, pair.bearer, { headers: { Range: 'bytes=1-4' } })).status, 502);
    drive.state.redirectMedia = true;
    assert.equal((await drive.api(path, pair.bearer)).status, 502);
    assert.ok(drive.state.calls.every(call => call.url.hostname !== 'other.example.test'));
    assert.equal(drive.state.calls.filter(call => call.options.body?.get('grant_type') === 'refresh_token').length, 1);
});

test('disconnect blocks playback immediately, can retry Google revocation, and destroys the session', async t => {
    const drive = await fixture(t);
    const pair = await drive.begin();
    assert.equal((await drive.callback(pair, pair.cookie)).status, 200);
    drive.state.keepStreamOpen = true;
    const response = await drive.api('media/my-video?key=test-resource', pair.bearer);
    const reader = response.body.getReader();
    assert.equal(Buffer.from((await reader.read()).value).toString(), '01');
    drive.state.failRevocation = true;
    assert.equal((await drive.api('session', pair.bearer, { method: 'DELETE' })).status, 502);
    await assert.rejects(reader.read());
    assert.deepEqual(await (await drive.api('session', pair.bearer)).json(), { state: 'disconnecting' });
    assert.equal((await drive.api('media/my-video', pair.bearer)).status, 409);
    drive.state.failRevocation = false;
    assert.equal((await drive.api('session', pair.bearer, { method: 'DELETE' })).status, 204);
    assert.equal((await drive.api('session', pair.bearer)).status, 401);
});

test('cancelled pairing rejects late Google callback and revoked grants require a new pairing', async t => {
    const drive = await fixture(t);
    const pending = await drive.begin();
    assert.equal((await drive.api('session', pending.bearer, { method: 'DELETE' })).status, 204);
    assert.equal((await drive.callback(pending, pending.cookie)).status, 400);
    const pair = await drive.begin();
    assert.equal((await drive.callback(pair, pair.cookie)).status, 200);
    assert.equal((await drive.api('videos', pair.bearer)).status, 200);
    drive.state.rejectAccess = true;
    drive.state.expired = true;
    assert.equal((await drive.api('videos', pair.bearer)).status, 401);
    assert.equal((await drive.api('session', pair.bearer)).status, 401);
});

test('cancelling an in-progress consent retains encrypted revocation state until Google responds', async t => {
    const drive = await fixture(t);
    const pair = await drive.begin();
    const started = Promise.withResolvers();
    const unblock = Promise.withResolvers();
    drive.state.exchangeStarted = started.resolve;
    drive.state.blockExchange = unblock.promise;
    const callback = drive.callback(pair, pair.cookie);
    await started.promise;
    assert.equal((await drive.api('session', pair.bearer, { method: 'DELETE' })).status, 409);
    drive.state.failRevocation = true;
    unblock.resolve();
    assert.equal((await callback).status, 502);
    assert.deepEqual(await (await drive.api('session', pair.bearer)).json(), { state: 'disconnecting' });
    assert.equal((await drive.api('videos', pair.bearer)).status, 409);
    drive.state.failRevocation = false;
    assert.equal((await drive.api('session', pair.bearer, { method: 'DELETE' })).status, 204);
});

test('only a paired headset can read account identity and browse nested, shared and searched folders', async t => {
    const drive = await fixture(t);
    assert.equal((await drive.request('/v1/account')).status, 401);
    const pair = await drive.begin();
    assert.equal((await drive.api('account', pair.bearer)).status, 409);
    assert.equal((await drive.callback(pair, pair.cookie)).status, 200);
    const identity = await drive.api('account', pair.bearer);
    assert.equal(identity.status, 200);
    assert.equal(identity.headers.get('cache-control'), 'no-store');
    assert.deepEqual(await identity.json(), account);
    assert.equal((await drive.request('/v1/items?source=folder&parent=root')).status, 401);

    const getItems = async params => {
        const response = await drive.api(`items?${params}`, pair.bearer);
        assert.equal(response.status, 200);
        return response.json();
    };
    const list = await getItems('source=folder&parent=root&limit=1');
    assert.deepEqual(list.files, [{ id: 'nested', kind: 'folder', mimeType: folderMime, name: 'Nested' }]);
    assert.ok(list.nextCursor);
    const cursor = `source=folder&parent=nested&limit=1&cursor=${list.nextCursor}`;
    assert.equal((await drive.api(`items?${cursor}`, pair.bearer)).status, 400);
    const nested = await getItems('source=folder&parent=nested');
    assert.deepEqual(nested.files, [{ id: 'movies', kind: 'folder', mimeType: folderMime, name: 'Movies', resourceKey: 'nested-key' }]);
    const search = await getItems('q=Movies');
    assert.deepEqual(search.files[0], nested.files[0]);
    assert.deepEqual(search.files[1], { ...nested.files[0], name: 'Movies shortcut' });
    const movies = await getItems('source=folder&parent=movies&key=nested-key');
    assert.deepEqual(movies.files, [{ id: 'movie-file', name: 'Movie.mp4', kind: 'video', mimeType: 'video/mp4' }]);
    assert.equal((await getItems('source=shared')).files[0].id, 'shared');
    assert.deepEqual((await getItems('source=drives')).files, [{ id: 'team-drive', name: 'Team Drive', kind: 'drive' }]);
    assert.equal((await getItems('source=folder&parent=team-drive&drive=team-drive')).files[0].driveId, 'team-drive');
    assert.equal((await drive.api('items?source=all&parent=movies', pair.bearer)).status, 400);
    assert.equal((await drive.api('items?source=folder&parent=bad%27value', pair.bearer)).status, 400);
});

test('cleartext is available only in the explicit pilot mode on the fixed loopback origin', () => {
    assert.throws(() => readConfig(pilotEnv), /bare HTTPS origin/);
    for (const override of [
        { PUBLIC_ORIGIN: 'http://localhost:48180' }, { PUBLIC_ORIGIN: 'http://127.0.0.2:48180' },
        { PUBLIC_ORIGIN: 'http://127.0.0.1:48181' }, { PUBLIC_ORIGIN: 'http://127.0.0.1:48180/' },
        { PUBLIC_ORIGIN: 'http://dvr.example.test:48180' }, { PUBLIC_ORIGIN: settings.origin },
        { PORT: '48181' }, { HOST: '0.0.0.0' }, { TRUST_PROXY: '1' },
    ]) assert.throws(() => readConfig({ ...pilotEnv, ...override }, true), /requires http:\/\/127\.0\.0\.1:48180 with no proxy/);
    const pilot = readConfig(pilotEnv, true);
    assert.equal(pilot.origin, pilotOrigin);
    assert.equal(pilot.host, '127.0.0.1');
    assert.equal(pilot.port, 48180);
    assert.equal(pilot.trustProxy, false);
    assert.equal(readConfig({ ...pilotEnv, PUBLIC_ORIGIN: settings.origin }).pilot, false);
});

test('wired loopback rejects foreign sites, uses a PC pairing code, and binds Google consent to the browser', async t => {
    const pilot = readConfig(pilotEnv, true);
    const server = createDriveServer({ ...pilot, database: ':memory:' }, async (url, options) => {
        assert.equal(new URL(url).host, 'oauth2.googleapis.com');
        assert.equal(options.body.get('redirect_uri'), `${pilot.origin}/oauth/callback`);
        return Response.json({ refresh_token: refreshToken, scope });
    }).listen(0, pilot.host);
    await once(server, 'listening');
    t.after(() => server.close());

    const host = new URL(pilotOrigin).host;
    function request(path, { method = 'GET', headers = {}, body = '' } = {}) {
        return new Promise((resolve, reject) => {
            const req = httpRequest({
                host: pilot.host, port: server.address().port, path, method,
                headers: { Host: host, 'Content-Length': Buffer.byteLength(body), ...headers },
            }, async res => {
                const chunks = [];
                for await (const chunk of res) chunks.push(chunk);
                resolve({ status: res.statusCode, headers: res.headers, body: Buffer.concat(chunks).toString() });
            });
            req.once('error', reject);
            req.end(body);
        });
    }

    const health = await request('/health');
    assert.deepEqual(JSON.parse(health.body), { ok: true, mode: 'wired-pilot' });
    assert.equal(health.headers['strict-transport-security'], undefined);
    assert.equal((await request('/health', { headers: { Host: 'dvr.example.test' } })).status, 403);
    assert.equal((await request('/health', { headers: { Origin: 'https://other.example.test' } })).status, 403);
    const start = await request('/v1/pairings', { method: 'POST' });
    assert.equal(start.status, 201);
    const pair = JSON.parse(start.body);
    assert.equal(pair.qr, undefined);
    assert.equal(pair.entryUrl, `${pilot.origin}/pair`);
    assert.equal((await request('/pair')).headers['referrer-policy'], 'same-origin');

    const form = { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: `code=${pair.code}` };
    assert.equal((await request('/pair', form)).status, 403);
    const consent = await request('/pair', { ...form, headers: { ...form.headers, Origin: pilotOrigin } });
    assert.equal(consent.status, 303);
    const auth = new URL(consent.headers.location);
    assert.equal(auth.searchParams.get('redirect_uri'), `${pilot.origin}/oauth/callback`);
    assert.match(consent.headers['set-cookie'][0], /^dvr-pilot-pair=.*; Path=\/; HttpOnly; SameSite=Lax; Max-Age=600$/);
    const callback = `/oauth/callback?code=google&state=${auth.searchParams.get('state')}`;
    assert.equal((await request(callback)).status, 400);
    assert.equal((await request(callback, { headers: { Cookie: consent.headers['set-cookie'][0].split(';')[0] } })).status, 200);
    const session = await request('/v1/session', { headers: { Authorization: `Bearer ${pair.bearer}` } });
    assert.deepEqual(JSON.parse(session.body), { state: 'connected' });
});
