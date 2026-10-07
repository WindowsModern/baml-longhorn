// Publishes incrementally, on top of the existing remote history.
//
// Two problems with uploading the whole tree every time:
//
//   * a push of several hundred blobs fails on an unreliable link, and a failure partway through
//     used to mean starting over
//   * it discards the remote's history, because a fresh root commit has no parent
//
// Git object ids are content-addressed, so the blob hash of a file can be computed locally with no
// network at all. Comparing those against the remote tree identifies exactly which files differ, so
// only those are uploaded, and the new tree is built with the remote's tree as its base. The commit
// then names the remote head as its parent, which keeps the history rather than replacing it.
//
// Usage:
//   node publish3.js --plan                  show what would change (network read only)
//   node publish3.js --message-file <file>   upload the differences and commit
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
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

// The Git blob hash is sha1("blob <byteLength>\0" + content). Computed directly because this
// directory is not a git repository -- the repository exists only remotely, which is why publishing
// goes through the API -- so there is no local object store to ask.
function blobHash(file) {
  const content = fs.readFileSync(file);
  const header = Buffer.from('blob ' + content.length + '\0', 'utf8');
  return crypto.createHash('sha1').update(header).update(content).digest('hex');
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
  const planOnly = process.argv.includes('--plan');

  const files = walk(ROOT, '', []);
  console.log('local files: %d   bytes: %s', files.length,
    files.reduce((n, f) => n + f.size, 0).toLocaleString());

  console.log('computing blob hashes locally...');
  const local = new Map();
  for (const f of files) {
    f.sha = blobHash(f.full);
    local.set(f.rel, f);
  }
  console.log('  %d hashes', files.length);

  const t = token();
  if (!t) throw new Error('no stored credential');

  // remote head and its tree
  let headSha = null;
  try {
    const ref = await api(`/repos/${OWNER}/${REPO}/git/ref/heads/${BRANCH}`, t);
    headSha = ref.object.sha;
  } catch (e) {
    if (e.status !== 404) throw e;
  }
  console.log('  remote head: %s', headSha ? headSha.slice(0, 8) : '(none)');

  let remote = new Map();
  if (headSha) {
    const commit = await api(`/repos/${OWNER}/${REPO}/git/commits/${headSha}`, t);
    const tree = await api(`/repos/${OWNER}/${REPO}/git/trees/${commit.tree.sha}?recursive=1`, t);
    for (const e of tree.tree) {
      if (e.type === 'blob') remote.set(e.path, e.sha);
    }
  }
  console.log('  remote files: %d', remote.size);

  const added = [], changed = [], same = [];
  for (const [rel, f] of local) {
    if (!remote.has(rel)) added.push(f);
    else if (remote.get(rel) !== f.sha) changed.push(f);
    else same.push(f);
  }
  const deleted = [...remote.keys()].filter((r) => !local.has(r));

  console.log();
  console.log('  unchanged : %d', same.length);
  console.log('  added     : %d', added.length);
  console.log('  changed   : %d', changed.length);
  console.log('  deleted   : %d', deleted.length);
  for (const f of added.slice(0, 20)) console.log('      + ' + f.rel);
  if (added.length > 20) console.log('      + ... and %d more', added.length - 20);
  for (const f of changed.slice(0, 20)) console.log('      ~ ' + f.rel);
  if (changed.length > 20) console.log('      ~ ... and %d more', changed.length - 20);
  for (const d of deleted.slice(0, 20)) console.log('      - ' + d);

  if (planOnly) return;

  // upload only what differs
  const toUpload = added.concat(changed);
  let n = 0;
  for (const f of toUpload) {
    const content = fs.readFileSync(f.full);
    await api(`/repos/${OWNER}/${REPO}/git/blobs`, t, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: content.toString('base64'), encoding: 'base64' }),
    });
    n++;
    if (n % 25 === 0) console.log('  uploaded %d/%d', n, toUpload.length);
  }
  console.log('  blobs uploaded: %d', n);

  // build the tree on top of the remote's, so unchanged files are not re-sent
  const entries = [];
  for (const f of toUpload) entries.push({ path: f.rel, mode: '100644', type: 'blob', sha: f.sha });
  for (const d of deleted) entries.push({ path: d, mode: '100644', type: 'blob', sha: null });

  console.log('  creating tree (base %s)...', headSha ? 'remote' : 'none');
  const treeBody = { tree: entries };
  if (headSha) treeBody.base_tree = headSha;
  const tree = await api(`/repos/${OWNER}/${REPO}/git/trees`, t, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(treeBody),
  });

  const msgArg = process.argv.indexOf('--message-file');
  const message = msgArg > -1 ? fs.readFileSync(process.argv[msgArg + 1], 'utf8') : 'Update';

  console.log('  creating commit (parent %s)...', headSha ? headSha.slice(0, 8) : 'none');
  const commit = await api(`/repos/${OWNER}/${REPO}/git/commits`, t, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, tree: tree.sha, parents: headSha ? [headSha] : [] }),
  });

  if (!headSha) {
    await api(`/repos/${OWNER}/${REPO}/git/refs`, t, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ref: `refs/heads/${BRANCH}`, sha: commit.sha }),
    });
  } else {
    await api(`/repos/${OWNER}/${REPO}/git/refs/heads/${BRANCH}`, t, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sha: commit.sha, force: false }),
    });
  }

  console.log();
  console.log('PUBLISHED');
  console.log('  commit: %s', commit.sha);
  console.log('  parent: %s', headSha || '(root)');
  console.log('  url:    https://github.com/%s/%s/commit/%s', OWNER, REPO, commit.sha);
}

main().catch((e) => { console.error('FAILED: ' + e.message); process.exit(1); });
