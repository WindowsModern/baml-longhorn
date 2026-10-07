// Publishes by computing blob hashes locally and reusing blobs that are already on the server.
//
// The straightforward approach -- upload every file as a blob, then build a tree -- does not
// survive an unreliable link: a push of several hundred files that fails partway has to start over,
// because the uploaded blobs are unreachable without their SHAs saved somewhere.
//
// Git object hashes are content-addressed, so the SHA of a file's blob can be computed locally with
// `git hash-object` without contacting the server. GitHub stores a blob once and returns the same SHA
// for the same content. So the blob set can be uploaded incrementally and resumed: on a retry, a
// file whose blob already exists needs no upload at all.
//
// Usage:
//   node publish2.js --hashes      print the computed blob hashes (no network)
//   node publish2.js               upload whatever is missing, then commit
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const OWNER = 'WindowsModern';
const REPO = 'baml-longhorn';
const BRANCH = 'main';
const ROOT = path.resolve(__dirname, '..', '..');

const EXCLUDE_DIRS = new Set([
  'bin', 'obj', '.vs', '.git', '__pycache__', 'Temp', '_probe',
  'Decompile\u200Cd', 'For Test', 'node_modules',
]);
const EXCLUDE_EXT = new Set(['.pyc', '.suo', '.user', '.vsidx', '.log']);
const EXCLUDE_NAMES = new Set(['Thumbs.db', 'desktop.ini', '.DS_Store']);

function walk(dir, base, out) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    const rel = base ? base + '/' + entry.name : entry.name;
    if (entry.isDirectory()) {
      if (EXCLUDE_DIRS.has(entry.name)) continue;
      walk(full, rel, out);
    } else {
      if (EXCLUDE_NAMES.has(entry.name)) continue;
      if (EXCLUDE_EXT.has(path.extname(entry.name).toLowerCase())) continue;
      out.push({ rel, full, size: fs.statSync(full).size });
    }
  }
  return out;
}

function token() {
  const out = execFileSync('git', ['credential', 'fill'], {
    input: 'protocol=https\nhost=github.com\n\n',
    encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'],
  });
  for (const l of out.split(/\r?\n/)) {
    const i = l.indexOf('=');
    if (i > 0 && l.slice(0, i) === 'password') return l.slice(i + 1);
  }
  return null;
}

// Local, no network: the Git blob hash is sha1("blob <byteLength>\0" + content).
//
// Computed directly rather than by shelling out to `git hash-object`, because this directory is not
// a git repository -- the repository exists only remotely, which is the whole reason publishing goes
// through the API. The algorithm is fixed and short, so implementing it avoids depending on a git
// installation that is not needed for anything else here.
const crypto = require('crypto');

function blobHash(file) {
  const content = fs.readFileSync(file);
  const header = Buffer.from('blob ' + content.length + '\0', 'utf8');
  return crypto.createHash('sha1').update(header).update(content).digest('hex');
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const RETRYABLE = new Set([408, 425, 429, 500, 502, 503, 504, 522, 524]);

async function api(pathname, t, init, attempts = 6) {
  const method = (init && init.method) || 'GET';
  let last = null;
  for (let a = 1; a <= attempts; a++) {
    let res;
    try {
      res = await fetch('https://api.github.com' + pathname, {
        ...init,
        headers: {
          'User-Agent': 'baml-longhorn-publish',
          Accept: 'application/vnd.github+json',
          Authorization: 'Bearer ' + t,
          ...(init && init.headers),
        },
      });
    } catch (err) {
      last = err;
      if (a === attempts) break;
      const wait = Math.min(20000, 800 * Math.pow(2, a - 1));
      console.log(`    ! ${method} ${pathname}: ${err.message} -- retry ${a} in ${wait}ms`);
      await sleep(wait);
      continue;
    }
    const text = await res.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (res.status < 300) return body;
    if (RETRYABLE.has(res.status) && a < attempts) {
      const wait = Math.min(30000, 800 * Math.pow(2, a - 1));
      console.log(`    ! ${method} ${pathname} -> ${res.status} -- retry ${a} in ${wait}ms`);
      await sleep(wait);
      continue;
    }
    const e = new Error(`${method} ${pathname} -> ${res.status} ${text.slice(0, 200)}`);
    e.status = res.status;
    throw e;
  }
  throw new Error(`${method} ${pathname} failed: ${last && last.message}`);
}

async function main() {
  const files = walk(ROOT, '', []);
  console.log('files: %d   bytes: %s', files.length,
    files.reduce((n, f) => n + f.size, 0).toLocaleString());

  console.log('computing blob hashes locally (no network)...');
  for (const f of files) f.sha = blobHash(f.full);
  console.log('  %d hashes computed', files.length);

  const hashOnly = process.argv.includes('--hashes');
  const cacheFile = path.join(ROOT, 'Temp', 'publish-state.json');
  if (hashOnly) {
    fs.writeFileSync(cacheFile, JSON.stringify(
      files.map((f) => ({ path: f.rel, sha: f.sha, size: f.size })), null, 1));
    console.log('  wrote ' + cacheFile);
    return;
  }

  const t = token();
  if (!t) throw new Error('no stored credential');

  // save progress as we go, so an interrupted run resumes instead of restarting
  let state = { uploaded: {}, tree: null, commit: null };
  if (fs.existsSync(cacheFile)) {
    try {
      const loaded = JSON.parse(fs.readFileSync(cacheFile, 'utf8'));
      // --hashes writes a bare array of hashes; that is not resume state, and treating it as such
      // leaves `uploaded` undefined and the upload loop crashes on the first file
      if (loaded && !Array.isArray(loaded) && typeof loaded === 'object' && loaded.uploaded) {
        state = loaded;
      } else {
        console.log('  (hash listing found, not resume state; starting a fresh upload)');
      }
    } catch { }
  }
  const save = () => {
    try { fs.writeFileSync(cacheFile, JSON.stringify(state, null, 1)); } catch { }
  };

  // Upload only what has not been confirmed uploaded in a previous run. Blob creation is
  // idempotent in effect: the same content yields the same SHA.
  let uploaded = 0, skipped = 0;
  for (const f of files) {
    if (state.uploaded[f.rel] === f.sha) { skipped++; continue; }
    const content = fs.readFileSync(f.full);
    await api(`/repos/${OWNER}/${REPO}/git/blobs`, t, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: content.toString('base64'), encoding: 'base64' }),
    });
    state.uploaded[f.rel] = f.sha;
    uploaded++;
    if (uploaded % 25 === 0) { console.log('  uploaded %d (%d skipped)', uploaded, skipped); save(); }
  }
  save();
  console.log('  blobs: %d uploaded, %d reused from a previous run', uploaded, skipped);

  // tree
  const entries = files.map((f) => ({
    path: f.rel, mode: '100644', type: 'blob', sha: f.sha,
  }));
  let treeSha = state.tree;
  if (!treeSha) {
    console.log('  creating tree...');
    const tree = await api(`/repos/${OWNER}/${REPO}/git/trees`, t, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tree: entries }),
    });
    treeSha = tree.sha;
    state.tree = treeSha;
    save();
  } else {
    console.log('  tree reused from a previous run: %s', treeSha.slice(0, 8));
  }

  // commit
  let parents = [];
  try {
    const ref = await api(`/repos/${OWNER}/${REPO}/git/ref/heads/${BRANCH}`, t);
    parents = [ref.object.sha];
  } catch { }

  const msgArg = process.argv.indexOf('--message-file');
  const message = msgArg > -1
    ? fs.readFileSync(process.argv[msgArg + 1], 'utf8')
    : 'Update';

  console.log('  creating commit (parents: %d)...', parents.length);
  const commit = await api(`/repos/${OWNER}/${REPO}/git/commits`, t, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, tree: treeSha, parents }),
  });

  if (parents.length === 0) {
    await api(`/repos/${OWNER}/${REPO}/git/refs`, t, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ref: `refs/heads/${BRANCH}`, sha: commit.sha }),
    });
  } else {
    await api(`/repos/${OWNER}/${REPO}/git/refs/heads/${BRANCH}`, t, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sha: commit.sha }),
    });
  }

  // only clear the resume state once the ref has moved
  try { fs.unlinkSync(cacheFile); } catch { }

  console.log();
  console.log('PUBLISHED');
  console.log('  commit: %s', commit.sha);
  console.log('  url:    https://github.com/%s/%s/commit/%s', OWNER, REPO, commit.sha);
}

main().catch((e) => { console.error('FAILED: ' + e.message); process.exit(1); });
