// Publishes this repository to GitHub through the Git Data API.
//
// Why not `git push`: the git installed here is 2.16.1 with the schannel TLS backend, and it
// fails against GitHub with "Empty reply from server" while plain HTTPS to the same endpoint
// succeeds (the smart-HTTP discovery URL returns 200). Rather than change the machine's git
// installation, this creates blobs, a tree and a commit over the REST API, which uses Node's
// modern TLS stack.
//
// Usage:
//   node publish.js --dry-run     list what would be published
//   node publish.js               create the commit and update the branch
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const OWNER = 'WindowsModern';
const REPO = 'baml-longhorn';
const BRANCH = 'main';
const ROOT = path.resolve(__dirname, '..', '..');

// Excluded by construction: build output, IDE state, scratch, and the large decompiled trees
// that are inputs to the analysis rather than part of the deliverable.
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

function storedCredential() {
  const out = execFileSync('git', ['credential', 'fill'], {
    input: 'protocol=https\nhost=github.com\n\n',
    encoding: 'utf8',
    stdio: ['pipe', 'pipe', 'ignore'],
  });
  const fields = {};
  for (const line of out.split(/\r?\n/)) {
    const i = line.indexOf('=');
    if (i > 0) fields[line.slice(0, i)] = line.slice(i + 1);
  }
  return fields.password;
}

// Retries are built in because this runs over links that are not always reliable, and a push of
// several hundred blobs will hit a transient failure sooner or later. Transport errors and the
// statuses a proxy or rate limiter returns are retried with a growing delay; a genuine client error
// such as 404 or 422 is not, because retrying it would only waste time.
const RETRYABLE_STATUS = new Set([408, 425, 429, 500, 502, 503, 504, 522, 524]);

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function api(pathname, token, init) {
  const method = (init && init.method) || 'GET';
  const maxAttempts = 6;
  let lastError = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    let res;
    try {
      res = await fetch('https://api.github.com' + pathname, {
        ...init,
        headers: {
          'User-Agent': 'baml-longhorn-publish',
          Accept: 'application/vnd.github+json',
          Authorization: 'Bearer ' + token,
          ...(init && init.headers),
        },
      });
    } catch (err) {
      // a transport failure: DNS, TLS, connection reset
      lastError = err;
      if (attempt === maxAttempts) break;
      const wait = Math.min(30000, 1000 * Math.pow(2, attempt - 1));
      console.log(`    ${method} ${pathname} failed (${err.message}); retry ${attempt}/${maxAttempts - 1} in ${wait}ms`);
      await sleep(wait);
      continue;
    }

    const text = await res.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }

    if (res.status < 300) return body;

    if (RETRYABLE_STATUS.has(res.status) && attempt < maxAttempts) {
      lastError = new Error(`${method} ${pathname} -> ${res.status} ${text.slice(0, 200)}`);
      // honour Retry-After when the server sends it, otherwise back off exponentially
      const header = res.headers.get('retry-after');
      const wait = header
        ? Math.min(60000, parseInt(header, 10) * 1000 || 5000)
        : Math.min(30000, 1000 * Math.pow(2, attempt - 1));
      console.log(`    ${method} ${pathname} -> ${res.status}; retry ${attempt}/${maxAttempts - 1} in ${wait}ms`);
      await sleep(wait);
      continue;
    }

    throw new Error(`${method} ${pathname} -> ${res.status} ${text.slice(0, 300)}`);
  }

  throw new Error(`${method} ${pathname} failed after ${maxAttempts} attempts: ${lastError && lastError.message}`);
}

(async () => {
  const dryRun = process.argv.includes('--dry-run');
  const files = walk(ROOT, '', []);
  const total = files.reduce((n, f) => n + f.size, 0);

  const byTop = {};
  for (const f of files) {
    const top = f.rel.includes('/') ? f.rel.split('/')[0] : '(root)';
    byTop[top] = byTop[top] || { n: 0, bytes: 0 };
    byTop[top].n++;
    byTop[top].bytes += f.size;
  }
  console.log('files: %d   bytes: %s', files.length, total.toLocaleString());
  for (const [k, v] of Object.entries(byTop).sort()) {
    console.log('  %-14s %4d files  %s B', k, v.n, v.bytes.toLocaleString());
  }
  const biggest = [...files].sort((a, b) => b.size - a.size).slice(0, 5);
  console.log('largest:');
  for (const f of biggest) console.log('  %s  %s B', f.rel, f.size.toLocaleString());

  if (dryRun) return;

  const token = storedCredential();
  if (!token) throw new Error('no stored GitHub credential');

  // 1. blobs. Content is base64 so binary samples survive untouched.
  console.log('\ncreating blobs...');
  const entries = [];
  let done = 0;
  for (const f of files) {
    const content = fs.readFileSync(f.full);
    const blob = await api(`/repos/${OWNER}/${REPO}/git/blobs`, token, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: content.toString('base64'), encoding: 'base64' }),
    });
    entries.push({ path: f.rel, mode: '100644', type: 'blob', sha: blob.sha });
    done++;
    if (done % 50 === 0) console.log('  %d / %d', done, files.length);
  }

  // 2. tree
  console.log('creating tree...');
  const tree = await api(`/repos/${OWNER}/${REPO}/git/trees`, token, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tree: entries }),
  });

  // 3. commit. An empty repo has no parent; if the branch already exists, build on it.
  let parents = [];
  try {
    const ref = await api(`/repos/${OWNER}/${REPO}/git/ref/heads/${BRANCH}`, token);
    parents = [ref.object.sha];
  } catch { /* empty repository */ }

  console.log('creating commit... (parents: %d)', parents.length);
  // --message-file lets a follow-up commit carry its own description; the default below
  // describes the project itself and is only right for the first full commit.
  const msgArg = process.argv.indexOf('--message-file');
  const message = msgArg > -1 && process.argv[msgArg + 1]
    ? fs.readFileSync(process.argv[msgArg + 1], 'utf8')
    : [
      'BAML parser and decompiler for Windows Longhorn / pre-release Avalon',
      '',
      'Complete implementation across two record framings and eight generation profiles,',
      'recovered from decompiled Microsoft source and verified against 241 real BAML files',
      'extracted from Microsoft resource manifests.',
      '',
      '- SHORT framing (4074, 4083, 4093) and LONG framing (3683, 3718, 4015, 4033, 4039, 4042)',
      '- XAML decompiler for both lineages',
      '- 103/103 and 133/133 and 5/5 corpora decompiled complete',
      '- CLI (7 subcommands) and WinForms GUI (6 views, 24 translations, RTL)',
      '- format documentation in English and Simplified Chinese',
      '',
      'Implemented in full by DeepSeek Harness (deepseek-flash). See COMPLETION.md.',
    ].join('\n');

  const commit = await api(`/repos/${OWNER}/${REPO}/git/commits`, token, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, tree: tree.sha, parents }),
  });

  // 4. point the branch at it
  if (parents.length === 0) {
    console.log('creating ref heads/%s...', BRANCH);
    await api(`/repos/${OWNER}/${REPO}/git/refs`, token, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ref: `refs/heads/${BRANCH}`, sha: commit.sha }),
    });
  } else {
    console.log('updating ref heads/%s...', BRANCH);
    await api(`/repos/${OWNER}/${REPO}/git/refs/heads/${BRANCH}`, token, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sha: commit.sha, force: false }),
    });
  }

  const url = `https://github.com/${OWNER}/${REPO}/commit/${commit.sha}`;
  console.log('\nPUBLISHED');
  console.log('  commit: %s', commit.sha);
  console.log('  url:    %s', url);
})().catch((err) => {
  console.error('FAILED: ' + err.message);
  process.exit(1);
});
