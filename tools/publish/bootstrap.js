// Creates the first commit in a completely empty repository.
//
// The Git Data API refuses to create a blob ("Git Repository is empty", HTTP 409) until the
// repository has at least one commit, so a bootstrap commit is required before the tree-based
// publisher can run. The Contents API does not have that restriction.
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const OWNER = 'WindowsModern';
const REPO = 'baml-longhorn';
const BRANCH = 'main';
const ROOT = path.resolve(__dirname, '..', '..');

function storedCredential() {
  const out = execFileSync('git', ['credential', 'fill'], {
    input: 'protocol=https\nhost=github.com\n\n',
    encoding: 'utf8',
    stdio: ['pipe', 'pipe', 'ignore'],
  });
  const f = {};
  for (const line of out.split(/\r?\n/)) {
    const i = line.indexOf('=');
    if (i > 0) f[line.slice(0, i)] = line.slice(i + 1);
  }
  return f.password;
}

async function api(pathname, token, init) {
  const res = await fetch('https://api.github.com' + pathname, {
    ...init,
    headers: {
      'User-Agent': 'baml-longhorn-publish',
      Accept: 'application/vnd.github+json',
      Authorization: 'Bearer ' + token,
      ...(init && init.headers),
    },
  });
  const text = await res.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  return { status: res.status, body };
}

(async () => {
  const token = storedCredential();
  if (!token) throw new Error('no stored credential');

  // A minimal but real first commit. Kept tiny on purpose: the full tree follows immediately,
  // and a bootstrap commit that contained everything would double the uploaded bytes.
  const content = [
    '# baml-longhorn',
    '',
    'A BAML (compiled XAML) parser and decompiler for Windows Longhorn / pre-release Avalon.',
    '',
    'Complete implementation, format documentation and sample corpora follow in the next',
    'commit.',
    '',
  ].join('\n');

  const body = {
    message: 'Initialise repository',
    content: Buffer.from(content, 'utf8').toString('base64'),
    branch: BRANCH,
  };

  const r = await api(`/repos/${OWNER}/${REPO}/contents/README.md`, token, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  if (r.status >= 300) {
    throw new Error(`PUT contents -> ${r.status} ${JSON.stringify(r.body).slice(0, 300)}`);
  }
  console.log('BOOTSTRAPPED');
  console.log('  commit: %s', r.body.commit.sha);
})().catch((e) => {
  console.error('FAILED: ' + e.message);
  process.exit(1);
});
