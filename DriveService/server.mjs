import { createCipheriv, createDecipheriv, createHash, createHmac, randomBytes } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { createServer } from 'node:http';
import { dirname, resolve } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { DatabaseSync } from 'node:sqlite';
import { pathToFileURL } from 'node:url';
import QRCode from 'qrcode';

const scope = 'https://www.googleapis.com/auth/drive.readonly';
const authorize = 'https://accounts.google.com/o/oauth2/v2/auth';
const tokenUrl = 'https://oauth2.googleapis.com/token';
const revokeUrl = 'https://oauth2.googleapis.com/revoke';
const driveUrl = 'https://www.googleapis.com/drive/v3/';
const codeAlphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const opaque = /^[A-Za-z0-9_-]{43}$/;
const fileId = /^[A-Za-z0-9_-]{1,256}$/;
const pilotOrigin = 'http://127.0.0.1:48180';
const pairingLife = 10 * 60_000;
const secret = () => randomBytes(32).toString('base64url');
const hash = value => createHash('sha256').update(value).digest('base64url');

class HttpError extends Error {
    constructor(status, message) {
        super(message);
        this.status = status;
    }
}

function json(res, status, data) {
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(data));
}

function page(res, status, body) {
    res.writeHead(status, {
        'Content-Type': 'text/html; charset=utf-8',
        'Content-Security-Policy': "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'self' https://accounts.google.com; frame-ancestors 'none'",
    });
    res.end(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Connect Google Drive to DVR</title>
        <style>body{background:#171820;color:#f5f1e8;font:18px/1.5 system-ui;margin:4rem auto;padding:0 1.2rem;max-width:34rem}input,button{font:inherit;padding:.55rem .8rem;border-radius:.35rem;margin:.4rem 0}button{background:#f7cf87;border:0;color:#171820;cursor:pointer}a{color:#f7cf87}</style>
        <h1>Connect Google Drive to DVR</h1>${body}</html>`);
}

function pairForm(res, code) {
    const digits = code ? `${code.slice(0, 4)}-${code.slice(4)}` : '';
    // A no-referrer policy can serialize the browser's form POST Origin as null.
    res.setHeader('Referrer-Policy', 'same-origin');
    page(res, 200, `<p>Continue only if you are connecting your own headset and the code matches what you see in DVR.</p>
        <form method="post" action="/pair"><label for="code">Code on your headset</label><br>
        <input id="code" name="code" value="${digits}" required maxlength="9" autocomplete="off" autocapitalize="characters" spellcheck="false"><br>
        <button>Continue with Google</button></form>
        <p>DVR uses your approval to find and stream videos from Drive. You can disconnect from your headset.</p>`);
}

async function readForm(req) {
    if (!req.headers['content-type']?.startsWith('application/x-www-form-urlencoded')) {
        throw new HttpError(415, 'Use the form to submit the headset code.');
    }
    let size = 0;
    const chunks = [];
    for await (const chunk of req) {
        size += chunk.length;
        if (size > 1024) throw new HttpError(413, 'The form is too large.');
        chunks.push(chunk);
    }
    return new URLSearchParams(Buffer.concat(chunks).toString('utf8'));
}

function normalizeCode(value) {
    const code = (value ?? '').trim().replace('-', '').toUpperCase();
    return code.length === 8 && [...code].every(char => codeAlphabet.includes(char)) ? code : '';
}

function seal(key, id, value) {
    const iv = randomBytes(12);
    const cipher = createCipheriv('aes-256-gcm', key, iv).setAAD(Buffer.from(id));
    const encrypted = Buffer.concat([cipher.update(value, 'utf8'), cipher.final()]);
    return Buffer.concat([iv, cipher.getAuthTag(), encrypted]);
}

function unseal(key, id, data) {
    const decipher = createDecipheriv('aes-256-gcm', key, data.subarray(0, 12));
    decipher.setAAD(Buffer.from(id)).setAuthTag(data.subarray(12, 28));
    return Buffer.concat([decipher.update(data.subarray(28)), decipher.final()]).toString('utf8');
}

export function readConfig(env, pilot = false) {
    const origin = URL.parse(env.PUBLIC_ORIGIN ?? '');
    if (pilot) {
        if (env.PUBLIC_ORIGIN !== pilotOrigin || (env.HOST && env.HOST !== '127.0.0.1')
            || (env.PORT && env.PORT !== '48180') || (env.TRUST_PROXY && env.TRUST_PROXY !== '0')) {
            throw new Error('The wired pilot requires http://127.0.0.1:48180 with no proxy.');
        }
    } else if (origin?.protocol !== 'https:' || origin.origin !== env.PUBLIC_ORIGIN) {
        throw new Error('PUBLIC_ORIGIN must be a bare HTTPS origin without a trailing slash.');
    }
    const key = Buffer.from(env.STORAGE_KEY ?? '', 'base64');
    if (key.length !== 32 || !env.GOOGLE_CLIENT_ID || !env.GOOGLE_CLIENT_SECRET) {
        throw new Error('Set a 32-byte base64 STORAGE_KEY and both Google OAuth credentials.');
    }
    return {
        origin: origin.origin,
        key,
        clientId: env.GOOGLE_CLIENT_ID,
        clientSecret: env.GOOGLE_CLIENT_SECRET,
        database: resolve(env.DATABASE_PATH || './data/drive.sqlite'),
        host: pilot ? '127.0.0.1' : env.HOST || '127.0.0.1',
        port: pilot ? 48180 : Number(env.PORT || 8080),
        pilot,
        trustProxy: env.TRUST_PROXY === '1',
    };
}

export function createDriveServer(settings, google = fetch) {
    if (settings.database !== ':memory:') mkdirSync(dirname(settings.database), { recursive: true, mode: 0o700 });
    const db = new DatabaseSync(settings.database);
    db.exec(`PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY, code TEXT UNIQUE, expires INTEGER NOT NULL,
            state TEXT UNIQUE, verifier TEXT, refresh BLOB, disconnecting INTEGER NOT NULL DEFAULT 0
        )`);

    const access = new Map();
    const streams = new Map();
    const attempts = new Map();
    const redirect = `${settings.origin}/oauth/callback`;
    const browserCookie = settings.pilot ? 'dvr-pilot-pair' : '__Host-dvr-pair';
    const cookieAttributes = `Path=/; ${settings.pilot ? '' : 'Secure; '}HttpOnly; SameSite=Lax; Max-Age=`;
    const pilotHost = new URL(pilotOrigin).host;
    const encodeCursor = (id, search, position) => {
        const data = Buffer.from(JSON.stringify(position)).toString('base64url');
        return `${data}.${createHmac('sha256', settings.key).update(JSON.stringify([id, search, data])).digest('base64url')}`;
    };

    function limit(req, key, perIp, total) {
        const now = Date.now();
        for (const [bucket, value] of attempts) if (value.until <= now) attempts.delete(bucket);
        const ip = settings.trustProxy ? req.headers['x-real-ip'] || req.socket.remoteAddress : req.socket.remoteAddress;
        function count(bucket, maximum) {
            const value = attempts.get(bucket) ?? { until: now + 60_000, count: 0 };
            if (value.count >= maximum) throw new HttpError(429, 'Too many attempts. Try again in a minute.');
            value.count++;
            attempts.set(bucket, value);
        }
        count(`${key}:global`, total);
        count(`${key}:ip:${ip}`, perIp);
    }

    function session(req) {
        const bearer = req.headers.authorization?.match(/^Bearer ([A-Za-z0-9_-]{43})$/)?.[1];
        const row = bearer && db.prepare('SELECT * FROM sessions WHERE id = ?').get(hash(bearer));
        if (!row) throw new HttpError(401, 'Connect Google Drive again.');
        return row;
    }

    async function googleJson(url, options = {}) {
        const response = await google(url, { signal: AbortSignal.timeout(12_000), redirect: 'manual', ...options });
        return { response, data: await response.json().catch(() => ({})) };
    }

    function oauthToken(params) {
        return googleJson(tokenUrl, {
            method: 'POST',
            body: new URLSearchParams({ client_id: settings.clientId, client_secret: settings.clientSecret, ...params }),
        });
    }

    async function revoke(refresh) {
        const { response, data } = await googleJson(revokeUrl, {
            method: 'POST', body: new URLSearchParams({ token: refresh }),
        });
        if (!response.ok && !(response.status === 400 && data.error === 'invalid_token')) {
            throw new HttpError(502, 'DVR access is blocked. Try disconnecting again to revoke Google access.');
        }
    }

    async function pairingDetails(code) {
        const entryUrl = `${settings.origin}/pair`;
        const formatted = `${code.slice(0, 4)}-${code.slice(4)}`;
        if (settings.pilot) return { code: formatted, entryUrl };
        const qr = await QRCode.toDataURL(`${entryUrl}?code=${code}`, { errorCorrectionLevel: 'M', scale: 4 });
        return { code: formatted, entryUrl, qr: qr.split(',')[1] };
    }

    async function accessFor(row) {
        const now = Date.now();
        for (const [id, token] of access) if (token.until <= now) access.delete(id);
        const cached = access.get(row.id);
        if (cached) return cached.task;
        const token = { until: Infinity };
        token.task = (async () => {
            const { response, data } = await oauthToken({
                grant_type: 'refresh_token', refresh_token: unseal(settings.key, row.id, row.refresh),
            });
            if (response.status === 400 && data.error === 'invalid_grant') {
                db.prepare('DELETE FROM sessions WHERE id = ? AND disconnecting = 0').run(row.id);
                throw new HttpError(401, 'Google Drive access expired. Connect again.');
            }
            if (!response.ok || typeof data.access_token !== 'string') {
                throw new HttpError(502, 'Google Drive is temporarily unavailable.');
            }
            token.until = now + Math.max(0, Number(data.expires_in || 0) - 60) * 1000;
            return data.access_token;
        })().catch(error => {
            if (access.get(row.id) === token) access.delete(row.id);
            throw error;
        });
        access.set(row.id, token);
        return token.task;
    }

    function requireConnected(row) {
        if (!row.refresh || row.disconnecting) throw new HttpError(409, 'Connect Google Drive first.');
    }

    function confirmConnected(id) {
        requireConnected(db.prepare('SELECT * FROM sessions WHERE id = ?').get(id) ?? {});
    }

    async function driveJson(url, row, extra = {}) {
        async function send() {
            confirmConnected(row.id);
            const pending = accessFor(row);
            const cache = access.get(row.id);
            const token = await pending;
            confirmConnected(row.id);
            const result = await googleJson(url, { headers: { Authorization: `Bearer ${token}`, ...extra } });
            if (result.response.status === 401 && access.get(row.id) === cache) access.delete(row.id);
            return result;
        }
        let { response, data } = await send();
        if (response.status === 401) ({ response, data } = await send());
        if (!response.ok) {
            if (response.status === 404 || response.status === 403) throw new HttpError(404, 'Google Drive could not open this item.');
            if (response.status === 400) throw new HttpError(400, 'Refresh the Drive list and try again.');
            throw new HttpError(502, 'Google Drive is temporarily unavailable.');
        }
        return data;
    }

    async function listVideos(res, row, params) {
        requireConnected(row);
        const search = (params.get('q') ?? '').trim();
        const count = Number(params.get('limit') ?? 6);
        const cursor = params.get('cursor');
        if (search.length > 120 || !Number.isInteger(count) || count < 1 || count > 30) {
            throw new HttpError(400, 'Invalid Drive list request.');
        }
        let position = { corpus: 'user' };
        if (cursor) {
            try {
                if (cursor.length > 8192) throw new Error();
                position = JSON.parse(Buffer.from(cursor.split('.')[0], 'base64url').toString('utf8'));
                if (cursor !== encodeCursor(row.id, search, position)) throw new Error();
            } catch {
                throw new HttpError(400, 'Refresh the Drive list and try again.');
            }
        }

        const query = [`trashed = false`, `mimeType contains 'video/'`];
        if (search) query.push(`name contains '${search.replaceAll('\\', '\\\\').replaceAll("'", "\\'")}'`);
        let files = [];
        for (let step = 0; step < 6 && position && files.length === 0; step++) {
            if (position.corpus === 'drives') {
                const list = new URL('drives', driveUrl);
                list.search = new URLSearchParams({ pageSize: '1', fields: 'drives(id),nextPageToken' });
                if (position.nextDrive) list.searchParams.set('pageToken', position.nextDrive);
                const result = await driveJson(list, row);
                const id = result.drives?.[0]?.id;
                position = id ? { corpus: 'drive', id, nextDrive: result.nextPageToken }
                    : result.nextPageToken ? { corpus: 'drives', nextDrive: result.nextPageToken } : null;
                continue;
            }

            const list = new URL('files', driveUrl);
            list.search = new URLSearchParams({
                corpora: position.corpus,
                q: query.join(' and '),
                pageSize: String(count),
                fields: 'files(id,name,resourceKey),nextPageToken',
                orderBy: 'name_natural',
                supportsAllDrives: 'true',
                includeItemsFromAllDrives: 'true',
            });
            if (position.id) list.searchParams.set('driveId', position.id);
            if (position.page) list.searchParams.set('pageToken', position.page);
            const result = await driveJson(list, row);
            files = result.files ?? [];
            if (result.nextPageToken) position.page = result.nextPageToken;
            else if (position.corpus === 'user' || position.nextDrive) {
                position = { corpus: 'drives', nextDrive: position.nextDrive };
            } else position = null;
        }
        confirmConnected(row.id);
        json(res, 200, { files, nextCursor: position ? encodeCursor(row.id, search, position) : '' });
    }

    async function media(req, res, row, id, key) {
        requireConnected(row);
        if (!fileId.test(id) || (key && !fileId.test(key))) throw new HttpError(400, 'Invalid Drive video.');
        if (req.headers.range && !/^bytes=(?:\d{1,20}-\d{0,20}|-\d{1,20})$/.test(req.headers.range)) {
            throw new HttpError(416, 'Unsupported byte range.');
        }
        const headers = key ? { 'X-Goog-Drive-Resource-Keys': `${id}/${key}` } : {};
        const url = new URL(`files/${id}?supportsAllDrives=true`, driveUrl);
        url.searchParams.set('fields', 'mimeType,size,trashed,capabilities(canDownload)');
        const item = await driveJson(url, row, headers);
        if (item.trashed || !item.mimeType?.startsWith('video/') || !item.capabilities?.canDownload) {
            throw new HttpError(404, 'This Drive video cannot be streamed.');
        }
        confirmConnected(row.id);
        if (req.method === 'HEAD') {
            res.setHeader('Content-Type', item.mimeType);
            res.setHeader('Accept-Ranges', 'bytes');
            if (/^\d+$/.test(item.size)) res.setHeader('Content-Length', item.size);
            res.writeHead(200).end();
            return;
        }

        url.searchParams.delete('fields');
        url.searchParams.set('alt', 'media');
        if (req.headers.range) headers.Range = req.headers.range;
        const token = await accessFor(row);
        confirmConnected(row.id);
        const abort = new AbortController();
        const current = streams.get(row.id) ?? new Set();
        streams.set(row.id, current);
        current.add(abort);
        const cancel = () => abort.abort();
        res.on('close', cancel);
        const deadline = setTimeout(cancel, 15_000);
        try {
            const upstream = await google(url, {
                headers: { Authorization: `Bearer ${token}`, 'Accept-Encoding': 'identity', ...headers },
                signal: abort.signal, redirect: 'manual',
            });
            clearTimeout(deadline);
            abort.signal.throwIfAborted();
            if (upstream.status === 416) {
                const size = upstream.headers.get('content-range');
                if (size) res.setHeader('Content-Range', size);
                await upstream.body?.cancel();
                throw new HttpError(416, 'This byte range is unavailable.');
            }
            const rangeAccepted = !headers.Range || headers.Range === 'bytes=0-' || upstream.status === 206;
            const hasContentRange = upstream.status !== 206 || /^bytes \d+-\d+\/(?:\d+|\*)$/.test(upstream.headers.get('content-range'));
            if (![200, 206].includes(upstream.status) || !upstream.body || upstream.headers.has('content-encoding')
                || !rangeAccepted || !hasContentRange) {
                await upstream.body?.cancel();
                throw new HttpError(502, 'Google Drive could not stream this video.');
            }
            res.setHeader('Content-Type', item.mimeType);
            res.setHeader('Accept-Ranges', 'bytes');
            for (const header of ['content-length', 'content-range']) {
                const value = upstream.headers.get(header);
                if (value) res.setHeader(header, value);
            }
            res.writeHead(upstream.status);
            await pipeline(Readable.fromWeb(upstream.body), res);
        } finally {
            clearTimeout(deadline);
            res.off('close', cancel);
            current.delete(abort);
            if (!current.size) streams.delete(row.id);
        }
    }

    async function route(req, res, url) {
        const path = url.pathname;
        const method = req.method;
        if (path === '/health' && method === 'GET') return json(res, 200, settings.pilot ? { ok: true, mode: 'wired-pilot' } : { ok: true });
        if (path === '/v1/pairings' && method === 'POST') {
            limit(req, 'create', 12, 120);
            const bearer = secret();
            const now = Date.now();
            db.prepare('DELETE FROM sessions WHERE refresh IS NULL AND expires < ?').run(now - 60_000);
            let code;
            do {
                code = Array.from(randomBytes(8), value => codeAlphabet[value & 31]).join('');
            } while (db.prepare('SELECT id FROM sessions WHERE code = ?').get(code));
            db.prepare('INSERT INTO sessions (id, code, expires) VALUES (?, ?, ?)').run(hash(bearer), code, now + pairingLife);
            return json(res, 201, { bearer, ...await pairingDetails(code) });
        }
        if (path === '/pair' && method === 'GET') return pairForm(res, normalizeCode(url.searchParams.get('code')));
        if (path === '/pair' && method === 'POST') {
            limit(req, 'code', 10, 150);
            if ((settings.pilot || req.headers.origin) && req.headers.origin !== settings.origin) {
                throw new HttpError(403, 'Open the pairing page from DVR.');
            }
            const code = normalizeCode((await readForm(req)).get('code'));
            const state = secret();
            const verifier = secret();
            const result = db.prepare(`UPDATE sessions SET state = ?, verifier = ?
                WHERE code = ? AND expires > ? AND refresh IS NULL AND (state IS NULL OR verifier IS NOT NULL)`)
                .run(hash(state), verifier, code, Date.now());
            if (!result.changes) throw new HttpError(400, 'This pairing code is unavailable. Start a new pairing in DVR.');
            const authUrl = new URL(authorize);
            authUrl.search = new URLSearchParams({
                client_id: settings.clientId, redirect_uri: redirect, response_type: 'code', scope,
                access_type: 'offline', prompt: 'consent select_account', state,
                code_challenge: hash(verifier), code_challenge_method: 'S256',
            });
            res.setHeader('Set-Cookie', `${browserCookie}=${state}; ${cookieAttributes}600`);
            return res.writeHead(303, { Location: authUrl.href }).end();
        }
        if (path === '/oauth/callback' && method === 'GET') {
            const state = url.searchParams.get('state') ?? '';
            const cookie = req.headers.cookie?.split(';').map(value => value.trim()).find(value => value.startsWith(`${browserCookie}=`));
            const row = opaque.test(state) && cookie === `${browserCookie}=${state}`
                && db.prepare('SELECT * FROM sessions WHERE state = ? AND expires > ?').get(hash(state), Date.now());
            if (!row?.verifier) throw new HttpError(400, 'This Google approval has expired. Start a new pairing in DVR.');
            db.prepare('UPDATE sessions SET verifier = NULL, expires = ? WHERE id = ?').run(Math.min(row.expires, Date.now() + 20_000), row.id);
            res.setHeader('Set-Cookie', `${browserCookie}=; ${cookieAttributes}0`);
            try {
                const code = url.searchParams.get('code');
                if (!code || code.length > 4096 || url.searchParams.has('error')) throw new HttpError(400, 'Google access was declined.');
                const { response, data } = await oauthToken({ grant_type: 'authorization_code', code, code_verifier: row.verifier, redirect_uri: redirect });
                if (!response.ok || typeof data.refresh_token !== 'string' || !data.scope?.split(' ').includes(scope)) {
                    if (typeof data.refresh_token === 'string') await revoke(data.refresh_token);
                    throw new HttpError(400, 'Google did not grant read-only Drive access.');
                }
                const encrypted = seal(settings.key, row.id, data.refresh_token);
                const result = db.prepare('UPDATE sessions SET code = NULL, state = NULL, refresh = ? WHERE id = ? AND expires > ? AND disconnecting = 0')
                    .run(encrypted, row.id, Date.now());
                if (!result.changes) {
                    db.prepare('UPDATE sessions SET code = NULL, state = NULL, refresh = ?, disconnecting = 1 WHERE id = ?').run(encrypted, row.id);
                    await revoke(data.refresh_token);
                    db.prepare('DELETE FROM sessions WHERE id = ?').run(row.id);
                    throw new HttpError(410, 'The headset pairing ended. Start a new pairing in DVR.');
                }
                return page(res, 200, '<p>Google Drive is connected. Return to your headset to choose a video.</p>');
            } catch (error) {
                db.prepare('DELETE FROM sessions WHERE id = ? AND disconnecting = 1 AND refresh IS NULL').run(row.id);
                db.prepare('UPDATE sessions SET state = NULL, expires = ? WHERE id = ? AND refresh IS NULL').run(row.expires, row.id);
                throw error;
            }
        }
        if (path === '/v1/session' && method === 'GET') {
            const row = session(req);
            if (row.disconnecting) return json(res, 200, { state: 'disconnecting' });
            if (row.refresh) return json(res, 200, { state: 'connected' });
            if (row.expires <= Date.now()) throw new HttpError(410, 'Pairing expired. Start a new code.');
            const details = url.searchParams.get('details') === '1' ? { pairing: await pairingDetails(row.code) } : {};
            return json(res, 200, { state: 'pending', ...details });
        }
        if (path === '/v1/session' && method === 'DELETE') {
            const row = session(req);
            if (!row.refresh && row.state && !row.verifier && row.expires > Date.now()) {
                db.prepare('UPDATE sessions SET code = NULL, disconnecting = 1 WHERE id = ?').run(row.id);
                throw new HttpError(409, 'Google approval is finishing. Try disconnecting again.');
            }
            if (row.refresh) {
                db.prepare('UPDATE sessions SET disconnecting = 1 WHERE id = ?').run(row.id);
                access.delete(row.id);
                for (const stream of streams.get(row.id) ?? []) stream.abort();
                try {
                    await revoke(unseal(settings.key, row.id, row.refresh));
                } catch {
                    throw new HttpError(502, 'DVR access is blocked. Try disconnecting again to revoke Google access.');
                }
            }
            db.prepare('DELETE FROM sessions WHERE id = ?').run(row.id);
            return res.writeHead(204).end();
        }
        if (path === '/v1/videos' && method === 'GET') return listVideos(res, session(req), url.searchParams);
        if (path.startsWith('/v1/media/') && (method === 'GET' || method === 'HEAD')) {
            return media(req, res, session(req), path.slice('/v1/media/'.length), url.searchParams.get('key'));
        }
        throw new HttpError(404, 'Page not found.');
    }

    const server = createServer(async (req, res) => {
        res.setHeader('Cache-Control', 'no-store');
        res.setHeader('Referrer-Policy', 'no-referrer');
        res.setHeader('X-Content-Type-Options', 'nosniff');
        if (!settings.pilot) res.setHeader('Strict-Transport-Security', 'max-age=31536000');
        let path = '';
        try {
            if (settings.pilot && (req.headers.host !== pilotHost || req.socket.remoteAddress !== '127.0.0.1'
                || (req.headers.origin && req.headers.origin !== settings.origin))) {
                throw new HttpError(403, 'Use the wired pilot address on this PC.');
            }
            if (!req.url?.startsWith('/') || req.url.startsWith('//')) throw new HttpError(400, 'Invalid request.');
            const url = new URL(req.url, settings.origin);
            if (url.origin !== settings.origin) throw new HttpError(400, 'Invalid request.');
            path = url.pathname;
            await route(req, res, url);
        } catch (error) {
            if (res.headersSent || res.destroyed) return res.destroy();
            const status = error instanceof HttpError ? error.status : 502;
            const message = error instanceof HttpError ? error.message : 'The Drive service could not complete this request. Try again.';
            if (path === '/pair' || path === '/oauth/callback') page(res, status, `<p>${message}</p><p><a href="/pair">Enter a headset code</a></p>`);
            else json(res, status, { error: message });
        }
    });
    server.once('close', () => db.close());
    return server;
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
    const settings = readConfig(process.env, process.argv.includes('--wired-pilot'));
    const server = createDriveServer(settings);
    server.listen(settings.port, settings.host, () => {
        console.log(settings.pilot ? `DVR companion is ready. Enter the headset code at ${settings.origin}/pair` : 'DVR Drive service listening');
    });
}
