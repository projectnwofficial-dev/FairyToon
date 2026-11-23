// Diff FairyToon's block table against lilToon 2.4.1 (commit e698a2f) for every shared property name.
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const cs = fs.readFileSync(path.join(root, 'Editor/FairyToonPropertyBlocks.cs'), 'utf8');
const lil = JSON.parse(fs.readFileSync(path.join(__dirname, 'liltoon-property-blocks.json'), 'utf8'));
const drop = new Set(['AudioLink', 'DistanceFade', 'IDMask', 'Parallax', 'Tessellation', 'UDIMDiscard']);
const shaders = fs.readdirSync(path.join(root, 'Shaders')).filter(f => f.endsWith('.shader'));
const shaderText = shaders.map(f => fs.readFileSync(path.join(root, 'Shaders', f), 'utf8')).join('\n');
const fairyNames = new Set();
const hidden = new Set();
const shaderType = {};
for (const line of shaderText.split('\n')) {
  const m = line.match(/(?<![A-Za-z0-9_])(_[A-Za-z0-9_]+)\s*\(\s*"[^"]*"\s*,\s*(\w+)/);
  if (!m) continue;
  fairyNames.add(m[1]);
  if (!shaderType[m[1]]) shaderType[m[1]] = m[2];
  if (line.includes('[HideInInspector]')) hidden.add(m[1]);
}

const fairy = {};
const re = /new\("(_[^"]+)", (true|false), ((?:PropertyBlock\.\w+,?\s*)+)\)/g;
let match;
while ((match = re.exec(cs))) {
  const name = match[1];
  const blocks = [...match[2] && match[0].matchAll(/PropertyBlock\.(\w+)/g)].map(x => x[1]);
  if (fairy[name]) throw new Error('duplicate membership for ' + name);
  fairy[name] = { texture: match[2] === 'true', blocks };
}

const errors = [];
for (const name of Object.keys(lil)) {
  if (!fairyNames.has(name)) continue;
  const kept = lil[name].blocks.filter(b => !drop.has(b));
  const got = fairy[name];
  if (!got) {
    errors.push(name + ' is shared with lilToon but missing from FairyToonPropertyBlocks');
    continue;
  }
  const a = [...got.blocks].sort().join(',');
  const b = [...kept].sort().join(',');
  if (a !== b) errors.push(name + ' blocks FairyToon=[' + a + '] lilToon=[' + b + ']');
  if (got.texture !== lil[name].texture) errors.push(name + ' isTexture FairyToon=' + got.texture + ' lilToon=' + lil[name].texture);
}
const textureTypes = new Set(['2D', '3D', 'Cube', 'CubeArray']);
let shared = 0;
for (const name of Object.keys(lil)) if (fairyNames.has(name) && fairy[name]) shared++;
for (const name of Object.keys(fairy)) {
  if (lil[name] && fairyNames.has(name)) continue;
  const type = shaderType[name];
  if (!type) continue;
  const isTexture = textureTypes.has(type);
  if (fairy[name].texture !== isTexture)
    errors.push(name + ' FairyToon-only isTexture=' + fairy[name].texture + ' but ShaderLab type is ' + type);
}
if (!fairy._EFIrisFrame || fairy._EFIrisFrame.texture !== true || shaderType._EFIrisFrame !== '2D')
  errors.push('_EFIrisFrame is a 2D texture and must be isTexture true');
if (!lil._GlitterColorTex_UVMode || lil._GlitterColorTex_UVMode.texture !== true)
  errors.push('lilToon snapshot should keep the _GlitterColorTex_UVMode isTexture quirk');
if (!fairy._GlitterColorTex_UVMode || fairy._GlitterColorTex_UVMode.texture !== true)
  errors.push('shared _GlitterColorTex_UVMode must keep lilToon isTexture true even though the shader declares an Int');
if (shaderType._GlitterColorTex_UVMode === '2D')
  errors.push('_GlitterColorTex_UVMode is not a texture in ShaderLab; the kept flag is the lilToon quirk');
for (const name of Object.keys(fairy)) {
  for (const block of fairy[name].blocks) {
    if (drop.has(block)) errors.push(name + ' uses omitted block ' + block);
  }
}
if (fairy._FurMode) errors.push('_FurMode must not be in the table');
if ((fairy._FurTouchStrength || {}).blocks && fairy._FurTouchStrength.blocks.includes('Fur') === false)
  errors.push('_FurTouchStrength should stay in Fur, matching lilToon, even though the row is hidden');
if (!lil._FurMode && fairyNames.has('_FurMode') && fairy._FurMode) errors.push('unexpected');
const furBlocks = (lil._FurLayerNum && lil._FurLayerNum.blocks) || [];
if (furBlocks.includes('Fur') && fairy._FurMode) errors.push('_FurMode must not be a Fur member');
if (fairy._SeeThroughHead || fairy._FaceForward || fairy._FaceRight) errors.push('runtime HideInInspector fields must not be members');
if (lil._BeforeExposureLimit) {
  const b = [...lil._BeforeExposureLimit.blocks].sort().join(',');
  if (b !== 'Lighting,Rendering') errors.push('snapshot lost _BeforeExposureLimit Lighting+Rendering');
  if (fairy._BeforeExposureLimit) errors.push('FairyToon does not have _BeforeExposureLimit');
}

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('property blocks ok', Object.keys(fairy).length, 'entries,', shared, 'shared names checked');
