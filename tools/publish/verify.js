// Verifies what actually landed on GitHub, by reading the repository back through the API
// rather than trusting the publisher's own success message.
'use strict';
const { execFileSync } = require('child_process');

const OWNER = 'WindowsModern';
const REPO = 'baml-longhorn';

function token() {
  const out = execFileSync('git', ['credential', 'fill'], {
    input: 'protocol=https\nhost=github.com\n\n',
    encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'],
  });
  for (const line of out.split(/\r?\n/)) {
    const i = line.indexOf('=');
    if (i > 0 && line.slice(0, i) === 'password') return line.slice(i + 1);
  }
  return null;
}

async function api(p, t) {
  const res = await fetch('https://api.github.com' + p, {
    headers: { 'User-Agent': 'verify', Accept: 'application/vnd.github+json', Authorization: 'Bearer ' + t },
  });
  return { status: res.status, body: await res.json() };
}

(async () => {
  const t = token();

  const repo = await api(`/repos/${OWNER}/${REPO}`, t);
  console.log('repo:      %s   private=%s  default=%s', repo.body.full_name, repo.body.private, repo.body.default_branch);
  console.log('pushed_at: %s', repo.body.pushed_at);
  console.log('size:      %d KB', repo.body.size);

  const commits = await api(`/repos/${OWNER}/${REPO}/commits?per_page=10`, t);
  console.log('\ncommits (%d):', commits.body.length);
  for (const c of commits.body.reverse()) {
    console.log('  %s  %s', c.sha.slice(0, 8), c.commit.message.split('\n')[0]);
  }

  const tree = await api(`/repos/${OWNER}/${REPO}/git/trees/main?recursive=1`, t);
  const blobs = tree.body.tree.filter((x) => x.type === 'blob');
  console.log('\ntracked files: %d   truncated=%s', blobs.length, tree.body.truncated);

  const byTop = {};
  for (const b of blobs) {
    const top = b.path.includes('/') ? b.path.split('/')[0] : '(root)';
    byTop[top] = (byTop[top] || 0) + 1;
  }
  for (const [k, v] of Object.entries(byTop).sort()) console.log('  %-14s %4d', k, v);

  console.log('\ndocs/zh-CN (%d):', blobs.filter((b) => b.path.startsWith('docs/zh-CN/')).length);
  for (const b of blobs.filter((x) => x.path.startsWith('docs/zh-CN/')).sort((a, c) => a.path.localeCompare(c.path))) {
    console.log('  %-46s %s B', b.path, b.size);
  }

  console.log('\nroot:');
  for (const b of blobs.filter((x) => !x.path.includes('/'))) {
    console.log('  %-24s %s B', b.path, b.size);
  }

  // spot-check that a Chinese file really arrived as UTF-8 with its content intact
  const one = await api(`/repos/${OWNER}/${REPO}/contents/docs/zh-CN/README.md`, t);
  const text = Buffer.from(one.body.content, 'base64').toString('utf8');
  console.log('\nspot check docs/zh-CN/README.md: %d bytes, first heading: %s',
    Buffer.from(one.body.content, 'base64').length,
    text.split('\n').find((l) => l.startsWith('#')));
  console.log('  contains AI authorship line: %s', text.includes('本项目由 AI 智能体完成'));
})();
