// Every FairyToonL10n.Text("key") used by the inspector must exist in en, ja, and zh-Hans.
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..');
const gui = fs.readFileSync(path.join(root, 'Editor/FairyToonGUI.cs'), 'utf8');
const catalog = fs.readFileSync(path.join(root, 'Editor/FairyToonL10n.Catalog.cs'), 'utf8');
const errors = [];
const entries = {};
const add = /Add\("((?:\\.|[^"\\])*)", "((?:\\.|[^"\\])*)", "((?:\\.|[^"\\])*)", "((?:\\.|[^"\\])*)"\);/g;
let match;
while ((match = add.exec(catalog))) entries[match[1]] = [match[2], match[3], match[4]];
const used = new Set();
const text = /FairyToonL10n\.Text\("((?:\\.|[^"\\])*)"\)/g;
while ((match = text.exec(gui))) used.add(match[1]);
const quoted = /"((?:s[A-Z][A-Za-z0-9]*|ft\.[A-Za-z0-9]+))"/g;
while ((match = quoted.exec(gui))) used.add(match[1]);
if (!used.has('sMatCap')) errors.push('MatCap texture label must look up sMatCap');
for (const key of used) {
  if (!/^(s|ft\.|p:)/.test(key)) continue;
  const row = entries[key];
  if (!row) {
    errors.push('missing catalog entry for ' + key);
    continue;
  }
  if (row.some(value => value.length === 0)) errors.push(key + ' has an empty language');
  if (row[0] === key && key.startsWith('s')) errors.push(key + ' still shows the raw key in English');
}
if (entries['ft.Tiling'] && entries['ft.Tiling'].some(value => value !== 'Tiling'))
  errors.push('Tiling must stay the English word in every language');
if (entries['ft.Offset'] && entries['ft.Offset'].some(value => value !== 'Offset'))
  errors.push('Offset must stay the English word in every language');
if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('label keys ok ' + used.size);
