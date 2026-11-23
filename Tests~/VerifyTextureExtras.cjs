// A texture row's extra slider must be handed to TexturePropertySingleLine.
// Colours are drawn afterwards; dropping every non-colour extra removes Mask Blend,
// Smoothness, Mask & Strength, Fur vector scale, and Outline width.
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..');
const gui = fs.readFileSync(path.join(root, 'Editor/FairyToonGUI.cs'), 'utf8');
const errors = [];

function methodBody(source, signature) {
  const start = source.indexOf(signature);
  if (start < 0) return null;
  const open = source.indexOf('{', start);
  let depth = 0;
  for (let i = open; i < source.length; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}') {
      depth--;
      if (depth === 0) return source.slice(open, i + 1);
    }
  }
  return null;
}

const tex = methodBody(gui, 'private bool Tex(');
if (!tex) errors.push('Tex() was not found');
else {
  const call = tex.match(/TexturePropertySingleLine\s*\(([^;]*)\)/);
  if (!call) errors.push('Tex() does not call TexturePropertySingleLine');
  else if (!/hdr\s*\?\s*null\s*:\s*other/.test(call[1]))
    errors.push('Tex() must pass scalars and ordinary colours to TexturePropertySingleLine, and keep HDR for the custom swatch');
  const callAt = tex.indexOf('TexturePropertySingleLine');
  const early = tex.indexOf('if (!hdr) return');
  if (early >= 0 && early < callAt)
    errors.push('Tex() returns before the texture row can draw its extra property');
}

const required = [
  'Tex(n + "BlendMask", n + "Blend", "sMask")',
  'Tex("_SmoothnessTex", "_Smoothness", "sSmoothness")',
  'Tex("_ShadowStrengthMask", "_ShadowStrength", "sMaskStrength")',
  'Tex("_FurVectorTex", "_FurVectorScale", "sNormalMap")',
  'Tex("_OutlineWidthMask", "_OutlinePixelWidth", "sMaskWidth")',
  'Tex("_OutlineWidthMask", "_OutlineWidth", "sMaskWidth")'
];
for (const row of required) {
  if (!gui.includes(row)) errors.push('missing texture row ' + row);
}

if (errors.length) {
  console.error(errors.join('\n'));
  process.exit(1);
}
console.log('texture extras ok ' + required.length);
