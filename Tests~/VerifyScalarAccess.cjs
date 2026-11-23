// ShaderLab Int/Float/Range are float-backed. ShaderLab Integer is ShaderPropertyType.Int.
// The inspector must read those through Number/SetNumber, which branches on propertyType.
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const gui = fs.readFileSync(path.join(root, 'Editor/FairyToonGUI.cs'), 'utf8');
const clip = fs.readFileSync(path.join(root, 'Editor/FairyToonClipboard.cs'), 'utf8');
const catalog = fs.readFileSync(path.join(root, 'Editor/FairyToonL10n.Catalog.cs'), 'utf8');
const errors = [];

const shaderText = fs.readdirSync(path.join(root, 'Shaders'))
  .filter(f => f.endsWith('.shader'))
  .map(f => fs.readFileSync(path.join(root, 'Shaders', f), 'utf8'))
  .join('\n');

const declared = {};
const enumNames = new Set();
for (const line of shaderText.split('\n')) {
  const prop = line.match(/(?<![A-Za-z0-9_])(_[A-Za-z0-9_]+)\s*\(\s*"[^"]*"\s*,\s*(\w+)/);
  if (prop && !declared[prop[1]]) declared[prop[1]] = prop[2];
  const attr = line.match(/\[Enum\(([^)]+)\)\]/);
  if (!attr) continue;
  const raw = attr[1];
  if (!raw.includes(',')) continue;
  const parts = raw.split(',').map(s => s.trim());
  for (let i = 0; i < parts.length; i += 2) if (parts[i]) enumNames.add(parts[i]);
}
for (const name of ['Off', 'Front', 'Back', 'Zero', 'One', 'DstColor', 'SrcColor', 'OneMinusDstColor', 'SrcAlpha', 'OneMinusSrcColor', 'DstAlpha', 'OneMinusDstAlpha', 'SrcAlphaSaturate', 'OneMinusSrcAlpha', 'Add', 'Subtract', 'ReverseSubtract', 'Min', 'Max', 'LogicalClear', 'LogicalSet', 'LogicalCopy', 'LogicalCopyInverted', 'LogicalNoop', 'LogicalInvert', 'LogicalAnd', 'LogicalNand', 'LogicalOr', 'LogicalNor', 'LogicalXor', 'LogicalEquivalence', 'LogicalAndReverse', 'LogicalAndInverted', 'LogicalOrReverse', 'LogicalOrInverted', 'Multiply', 'Screen', 'Overlay', 'Darken', 'Lighten', 'ColorDodge', 'ColorBurn', 'HardLight', 'SoftLight', 'Difference', 'Exclusion', 'HSLHue', 'HSLSaturation', 'HSLColor', 'HSLLuminosity', 'Disabled', 'Never', 'Less', 'Equal', 'LessEqual', 'Greater', 'NotEqual', 'GreaterEqual', 'Always', 'Keep', 'Replace', 'IncrementSaturate', 'DecrementSaturate', 'Invert', 'IncrementWrap', 'DecrementWrap'])
  enumNames.add(name);

let integers = 0;
for (const [name, type] of Object.entries(declared)) {
  if (type === 'Integer') integers++;
  else if (type !== 'Int' && type !== 'Float' && type !== 'Range' && type !== 'Color' && type !== 'Vector' && !['2D', '3D', 'Cube', 'CubeArray'].includes(type))
    errors.push(name + ' has unhandled ShaderLab type ' + type);
}
if (!gui.includes('prop.propertyType == ShaderPropertyType.Int ? prop.intValue : prop.floatValue'))
  errors.push('Number() must use intValue only for ShaderPropertyType.Int (ShaderLab Integer)');
if (!gui.includes('prop.intValue = Mathf.RoundToInt(value)') || !gui.includes('prop.floatValue = value'))
  errors.push('SetNumber() must write intValue for Integer and floatValue for Float/Range/legacy Int');

const helper = gui.slice(gui.indexOf('private static float Number'), gui.indexOf('private static bool TryEnumOptions'));
const outside = gui.slice(0, gui.indexOf('private static float Number')) + gui.slice(gui.indexOf('private static bool TryEnumOptions'));
if (/\.floatValue|\.intValue/.test(outside))
  errors.push('FairyToonGUI scalar access outside Number/SetNumber must not use floatValue or intValue');
if (!/\.floatValue/.test(helper) || !/\.intValue/.test(helper))
  errors.push('the scalar helper lost a type branch');

const paste = clip.slice(clip.indexOf('void ApplyValue'), clip.indexOf('void SnapshotCoverage'));
if (!/case ShaderPropertyType\.Float:/.test(paste) || !/case ShaderPropertyType\.Int:/.test(paste))
  errors.push('clipboard ApplyValue must branch Float/Range and Int separately');
if (/case ShaderPropertyType\.Int:[\s\S]*?floatValue/.test(paste))
  errors.push('clipboard Int case must not write floatValue');
if (integers === 0 && /Integer\)/.test(shaderText))
  errors.push('an Integer property was not counted');

const keys = new Set();
for (const match of catalog.matchAll(/Add\("((?:\\.|[^"\\])*)"/g)) keys.add(match[1]);
for (const name of enumNames) {
  if (!keys.has(name)) errors.push('enum option "' + name + '" has no catalog entry');
}

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('scalar access ok', Object.keys(declared).length, 'properties,', integers, 'Integer,', enumNames.size, 'enum options');
