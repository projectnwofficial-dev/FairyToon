// Simulates lilToon's accumulating clipboard against FairyToon's membership table.
// Copy adds/overwrites and does not clear. Paste writes only that block's names already in the dictionary,
// skipping textures unless Paste (with texture).
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const blocksCs = fs.readFileSync(path.join(root, 'Editor/FairyToonPropertyBlocks.cs'), 'utf8');
const clipCs = fs.readFileSync(path.join(root, 'Editor/FairyToonClipboard.cs'), 'utf8');
const errors = [];

if (!/Dictionary<string, MaterialProperty> CopiedProperties/.test(clipCs))
  errors.push('clipboard must be a static Dictionary<string, MaterialProperty> keyed by property name');
if (!/Dictionary<string, Vector4> CopiedVectorProperties/.test(clipCs))
  errors.push('texture _ST must be stored in Dictionary<string, Vector4> CopiedVectorProperties');
const copyFn = clipCs.slice(clipCs.indexOf('void CopyProperties'), clipCs.indexOf('void PasteProperties'));
if (copyFn.includes('.Clear(')) errors.push('CopyProperties must not clear the clipboard');
if (!copyFn.includes('CopiedProperties[entry.Name] = prop')) errors.push('Copy must overwrite by property name');
const pasteFn = clipCs.slice(clipCs.indexOf('void PasteProperties'), clipCs.indexOf('void ResetProperties'));
if (!pasteFn.includes('!shouldCopyTex && entry.IsTexture')) errors.push('Paste must skip textures unless Paste (with texture)');
if (!pasteFn.includes('CopiedVectorProperties')) errors.push('Paste with texture must apply stored _ST');
if (/GetMaterialProperties/.test(copyFn) || /GetMaterialProperties/.test(pasteFn))
  errors.push('Copy and Paste must use the MaterialProperty array from OnGUI, not GetMaterialProperties');
if (clipCs.includes('RevertAllDownToGroup'))
  errors.push('an empty paste must not revert the current undo group');
const resetFn = clipCs.slice(clipCs.indexOf('void ResetProperties'), clipCs.indexOf('void ApplyValue'));
if (/GetMaterialProperties/.test(resetFn))
  errors.push('Reset must use the OnGUI properties');
function outcome(change, callback, animating) {
  if (!change) return 'noop';
  if (callback || animating) return 'write-no-apply';
  return 'write-apply';
}
const expected = [
  [false, false, false, 'noop'],
  [true, false, false, 'write-apply'],
  [true, true, false, 'write-no-apply'],
  [true, false, true, 'write-no-apply'],
  [false, true, true, 'noop'],
];
for (const [change, callback, animating, result] of expected)
  if (outcome(change, callback, animating) !== result) errors.push('paste decision table drifted');
if (pasteFn.indexOf('PasteWouldChange') < 0 || pasteFn.indexOf('PasteWouldChange') > pasteFn.indexOf('BeginUndo'))
  errors.push('Paste must return before recording undo when nothing changes');
const finishFn = clipCs.slice(clipCs.indexOf('void FinishMenuOperation'), clipCs.indexOf('private static bool PasteWouldChange'));
if (!finishFn.includes('callback && !FairyToonGUI.WroteAsset') || finishFn.indexOf('WroteAsset') > finishFn.indexOf('ApplyMaterials'))
  errors.push('a callback that kept the write must return before Apply');
if (!finishFn.includes('Undo.CollapseUndoOperations(group)') || !finishFn.includes('EndMenuOperation'))
  errors.push('menu cleanup and undo collapse must run even when a setter throws');
if (!pasteFn.includes('FinishMenuOperation') || !resetFn.includes('FinishMenuOperation'))
  errors.push('paste and reset must finish the menu scope in finally');
if (pasteFn.includes('AnimationMode.InAnimationMode()'))
  errors.push('animation mode with no callback still writes the asset and must validate');
if (!pasteFn.includes('applyPropertyCallback') && !clipCs.includes('applyPropertyCallback'))
  errors.push('Paste must detect a MaterialProperty apply callback');
if (!clipCs.includes('hasMixedValue') || !clipCs.includes('dest.floatValue != source.floatValue'))
  errors.push('same-value paste must compare the stored value and still run when the selection is mixed');
const writes = [
  'dest.colorValue = source.colorValue',
  'dest.vectorValue = source.vectorValue',
  'dest.floatValue = source.floatValue',
  'dest.intValue = source.intValue',
  'dest.textureValue = source.textureValue',
  'material.SetVector(st, scale)',
];
for (const line of writes)
  if (!clipCs.includes(line)) errors.push('clipboard no longer writes ' + line);
if (!clipCs.includes('Undo.IncrementCurrentGroup()'))
  errors.push('paste undo must start its own group');
if (pasteFn.includes('Undo.IncrementCurrentGroup'))
  errors.push('one paste must not open a new undo group per texture scale');
if (!pasteFn.includes('BeginUndo(editor, undo)'))
  errors.push('paste must record one undo group before it writes');
if (!pasteFn.includes('NoteDirectWrite(material)'))
  errors.push('a texture scale write must count as an asset write');
if (!pasteFn.includes('ApplyValue(dest, source)'))
  errors.push('Paste must call ApplyValue');
if (!resetFn.includes('ApplyDefault(prop, shader, index)'))
  errors.push('Reset must call ApplyDefault');
if (!clipCs.includes('Undo.RecordObjects(editor.targets, undo)'))
  errors.push('direct paste must record the selected materials');
const would = clipCs.slice(clipCs.indexOf('bool PasteWouldChange'), clipCs.indexOf('bool ResetWouldChange'));
if (!/if \(dest == null\) continue;/.test(would))
  errors.push('texture scale must not paste when the target descriptor is missing');
const pasteLoop = pasteFn.slice(pasteFn.indexOf('FairyToonGUI.BeginMenuOperation'));
if (!/if \(dest == null\) continue;/.test(pasteLoop))
  errors.push('the paste loop must skip _ST when the target descriptor is missing');
if (resetFn.indexOf('ResetWouldChange') < 0 || resetFn.indexOf('ResetWouldChange') > resetFn.indexOf('BeginUndo'))
  errors.push('Reset must return before recording undo when every value is already the default');

const table = {};
const re = /new\("(_[^"]+)", (true|false), ((?:PropertyBlock\.\w+,?\s*)+)\)/g;
let m;
while ((m = re.exec(blocksCs))) {
  table[m[1]] = { texture: m[2] === 'true', blocks: [...m[0].matchAll(/PropertyBlock\.(\w+)/g)].map(x => x[1]) };
}

function names(block) {
  return Object.keys(table).filter(n => table[n].blocks.includes(block));
}

// Value id stands in for the stored MaterialProperty. Later Copy of the same name overwrites it.
const clipboard = new Map();
let stamp = 0;
function copy(block, present) {
  for (const name of names(block)) {
    if (!present.has(name)) continue;
    clipboard.set(name, { texture: table[name].texture, stamp: ++stamp, from: block });
  }
}
function paste(block, present, withTex) {
  const written = [];
  for (const name of names(block)) {
    if (!present.has(name)) continue;
    const item = clipboard.get(name);
    if (!item) continue;
    if (!withTex && item.texture) continue;
    written.push(name);
  }
  return written.sort();
}

const matcap = new Set(names('MatCaps'));
const rim = new Set(names('RimLight'));
if (!matcap.has('_MatCapTex') || !table._MatCapTex.texture) errors.push('MatCap texture flag');
if (!rim.has('_RimColor') || table._RimColor.texture) errors.push('Rim color should be a non-texture');
copy('MatCaps', matcap);
copy('RimLight', rim);
if (!clipboard.has('_MatCapColor') || !clipboard.has('_RimColor') || !clipboard.has('_MatCapTex'))
  errors.push('copying Rim after MatCap must keep MatCap entries');
const pastedMat = paste('MatCaps', matcap, false);
const pastedRim = paste('RimLight', rim, false);
if (pastedMat.includes('_MatCapTex')) errors.push('Paste without texture wrote _MatCapTex');
if (!pastedMat.includes('_MatCapColor')) errors.push('Paste MatCap missed _MatCapColor');
if (pastedMat.some(n => rim.has(n) && !matcap.has(n))) errors.push('Paste MatCap wrote a Rim-only property');
if (!pastedRim.includes('_RimColor')) errors.push('Paste Rim missed _RimColor');
if (pastedRim.some(n => matcap.has(n) && !rim.has(n))) errors.push('Paste Rim wrote a MatCap-only property');
const withTex = paste('MatCaps', matcap, true);
if (!withTex.includes('_MatCapTex')) errors.push('Paste with texture skipped _MatCapTex');

// Multi-block property: copying either block stores it; pasting either block writes it; the other block's unique names stay put.
const both = ['_ShadowEnvStrength', '_Metallic', '_RefractionStrength'];
for (const name of both) {
  if (!table[name] || table[name].blocks.length < 2) errors.push(name + ' should belong to two blocks');
}
clipboard.clear();
copy('Shadow', new Set(names('Shadow')));
copy('Lighting', new Set(names('Lighting')));
if (!clipboard.has('_ShadowEnvStrength')) errors.push('multi-block _ShadowEnvStrength was not stored');
const fromShadow = paste('Shadow', new Set(names('Shadow')), false);
const fromLight = paste('Lighting', new Set(names('Lighting')), false);
if (!fromShadow.includes('_ShadowEnvStrength') || !fromLight.includes('_ShadowEnvStrength'))
  errors.push('_ShadowEnvStrength must paste from both Shadow and Lighting');
if (fromLight.includes('_ShadowColor')) errors.push('Lighting paste must not write shadow-only properties');

copy('Reflection', new Set(names('Reflection')));
copy('Gem', new Set(names('Gem')));
const refl = paste('Reflection', new Set(names('Reflection')), false);
const gem = paste('Gem', new Set(names('Gem')), false);
if (!refl.includes('_Metallic') || !gem.includes('_Metallic')) errors.push('_Metallic must paste from Reflection and Gem');
if (refl.includes('_MetallicGlossMap')) errors.push('Reflection paste without texture wrote _MetallicGlossMap');
if (paste('Reflection', new Set(names('Reflection')), true).includes('_MetallicGlossMap') === false)
  errors.push('Reflection paste with texture missed _MetallicGlossMap');

if (names('Fur').includes('_FurMode')) errors.push('Fur block must not include _FurMode');
if (names('Fur').includes('_FurCull')) errors.push('lilToon tags _FurCull as Rendering, not Fur');
if (!table._RefractionStrength || table._RefractionStrength.blocks.includes('Reflection'))
  errors.push('_RefractionStrength is Refraction and Gem, not Reflection');
if (!table._Metallic || !table._Metallic.blocks.includes('Reflection') || !table._Metallic.blocks.includes('Gem'))
  errors.push('_Metallic is Reflection and Gem');

const gui = fs.readFileSync(path.join(root, 'Editor/FairyToonGUI.cs'), 'utf8');
function method(name) {
  const start = gui.search(new RegExp('private (?:void|bool|float) ' + name + '\\('));
  const open = gui.indexOf('{', start);
  let depth = 0;
  for (let i = open; i < gui.length; i++) {
    if (gui[i] === '{') depth++;
    else if (gui[i] === '}') { depth--; if (depth === 0) return gui.slice(open, i + 1); }
  }
  return '';
}
const advanced = method('DrawAdvanced');
if (advanced.includes('Anisotropy();')) errors.push('Anisotropy must be inside the Normal Map parent, not a sibling in DrawAdvanced');
if (!method('NormalGroup').includes('Anisotropy();')) errors.push('Normal Map parent must draw Anisotropy');
if (!method('MainColorFirst').includes('PropertyBlock.MainColor1st')) errors.push('Main Color 1st needs its own gear');
if (!method('AlphaMask').includes('PropertyBlock.AlphaMask')) errors.push('Alpha Mask needs its own gear');
if (gui.includes('m_SearchRelax') || gui.includes('BlockHit')) errors.push('search must not relax on a title hit or use clipboard membership');
if (method('Fur').includes('_FurCull') || method('Fur').includes('_FurStencilRef'))
  errors.push('Fur render-state and stencil rows must leave the Fur section');
if (!method('Rendering').includes('_FurCull') || !method('Stencil').includes('_FurStencilRef'))
  errors.push('Fur render-state rows belong in Rendering and Fur stencil rows in Stencil');
if (!method('Fur').includes('cutoutLength')) errors.push('Fur cutout length must follow Two Pass or a mixed Fur Mode');
if (!gui.includes('() => m_Props')) errors.push('the gear menu must pass the OnGUI MaterialProperty array');
if (!method('Fur').includes('_FurVectorScale')) errors.push('Fur search must include _FurVectorScale, which is drawn on the vector texture row');
if (!method('DissolvePosition').includes('RowVisible(name, label)')) errors.push('DissolvePosition must be filtered like other rows');
if (!method('MainColor').includes('PrefixMatches("_FT_Main2nd")')) errors.push('layer dissolve noise names must open the main layer');
const normals = method('NormalMaps');
if (normals.indexOf('secondNormal && BeginSection') < 0 || normals.indexOf('firstNormal && BeginSection') < 0)
  errors.push('normal 1st and 2nd need separate search gates');
if (method('MainColorFirst').includes('BeginSection(')) errors.push('Main Color 1st is a label and gear, not an extra foldout');
if (!method('DrawLocalizedEnum').includes('BeginScope') || !method('DrawLocalizedEnum').includes('EndScope'))
  errors.push('localized enums must keep the material property and animation context');

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('clipboard scenario ok', clipboard.size);
