// Every non-hidden shader property must resolve in English, Japanese, and Simplified Chinese.
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const catalog = fs.readFileSync(path.join(root, 'Editor/FairyToonL10n.Catalog.cs'), 'utf8');
const latinJa = new Set(JSON.parse(fs.readFileSync(path.join(__dirname, 'liltoon-latin-ja.json'), 'utf8')));
// Official lilToon Japanese strings that are intentionally Latin live in the catalog comments' source.
// Accept a ja string that equals en when it is also the zh string only if it has no 4-letter lowercase word
// OR it appears as a msgstr in the catalog next to a kana/CJK sibling. Simpler rule below.

const labels = {};
const addRe = /Add\("(p:[^"]+)", "((?:\\.|[^"\\])*)", "((?:\\.|[^"\\])*)", "((?:\\.|[^"\\])*)"\);/g;
let m;
while ((m = addRe.exec(catalog))) {
  const un = s => s.replace(/\\"/g, '"').replace(/\\\\/g, '\\');
  labels[m[1]] = { en: un(m[2]), ja: un(m[3]), zh: un(m[4]) };
}

const shaderText = fs.readdirSync(path.join(root, 'Shaders'))
  .filter(f => f.endsWith('.shader'))
  .map(f => fs.readFileSync(path.join(root, 'Shaders', f), 'utf8'))
  .join('\n');
const hidden = new Set();
const props = [];
for (const line of shaderText.split('\n')) {
  const found = line.match(/(?<![A-Za-z0-9_])(_[A-Za-z0-9_]+)\s*\(\s*"/);
  if (!found) continue;
  if (line.includes('[HideInInspector]')) hidden.add(found[1]);
  else if (!props.includes(found[1])) props.push(found[1]);
}

const raw = /^s[A-Z][A-Za-z0-9]*$/;
const errors = [];
for (const name of props) {
  const row = labels['p:' + name];
  if (!row) {
    errors.push(name + ' has no p: translation');
    continue;
  }
  for (const lang of ['en', 'ja', 'zh']) {
    if (!row[lang]) errors.push(name + ' missing ' + lang);
    else if (raw.test(row[lang])) errors.push(name + ' ' + lang + ' is a raw key ' + row[lang]);
  }
  // A copied English sentence is a fallback even when it has no four-letter lowercase word ("Toon Edge AA").
  // All-caps tokens (ZTest, GSAA) and strings lilToon itself leaves in Latin are allowed.
  const acronym = /^[A-Z0-9][A-Z0-9 .+\-/]*$/;
  for (const lang of ['ja', 'zh']) {
    const value = row[lang];
    if (value && value === row.en && /[a-z]/.test(row.en) && !latinJa.has(row.en) && !acronym.test(row.en))
      errors.push(name + ' ' + lang + ' falls back to English "' + row.en + '"');
  }
}
if (hidden.has('_SeeThroughHead') === false) errors.push('expected _SeeThroughHead to be hidden');

if (errors.length) {
  console.error(errors.slice(0, 40).join('\n'));
  console.error(errors.length + ' localization errors');
  process.exit(1);
}
console.log('inspector localization ok', props.length);
