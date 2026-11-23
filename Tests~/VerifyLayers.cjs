// Source contracts and numerical regressions against installed lilToon. Does not start Unity.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { test } = require('node:test');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8').replace(/\r\n/g, '\n');
function properties(text) {
    return new Map(text.split('\n').flatMap(line => {
        const m = line.match(/\b(_\w+)\s*\("[^"]*",\s*(.+)\)\s*=\s*(.*?)(?:\s*\{.*)?$/);
        return m ? [[m[1], { type: m[2].trim(), value: m[3].replace(/\s/g, '') }]] : [];
    }));
}
function hlslBody(text, name) {
    const pattern = new RegExp('\\b' + name + '\\s*\\(', 'g');
    let match, opening = -1;
    while ((match = pattern.exec(text))) {
        let cursor = match.index + match[0].length, depth = 1;
        for (; depth && cursor < text.length; cursor++) {
            if (text[cursor] === '(') depth++;
            if (text[cursor] === ')') depth--;
        }
        // Skip calls/macros encountered before the definition; support nested TEXTURE2D parameters.
        const declaration = text.slice(cursor).match(/^\s*(?::[^;{}]+)?\{/);
        if (declaration) { opening = cursor + declaration[0].lastIndexOf('{'); break; }
    }
    assert.ok(opening >= 0, 'missing HLSL helper ' + name);
    let depth = 1, cursor = opening + 1;
    for (; depth && cursor < text.length; cursor++) {
        if (text[cursor] === '{') depth++;
        if (text[cursor] === '}') depth--;
    }
    assert.equal(depth, 0, 'complete HLSL body ' + name);
    return text.slice(opening + 1, cursor - 1);
}
function scalarHlsl(body, defines, bindings) {
    const stack = [];
    let active = true;
    const evaluateCondition = expression => new Function('return ' + expression
        .replace(/defined\((\w+)\)/g, (_, name) => defines.includes(name) ? '1' : '0')
        .replace(/\b[A-Za-z_]\w*\b/g, name => defines.includes(name) ? '1' : '0'))();
    const source = body.split('\n').flatMap(line => {
        const condition = line.match(/^\s*#if\s+(.+)/);
        if (condition) {
            const enabled = evaluateCondition(condition[1]);
            stack.push({ parent: active, enabled });
            active = active && enabled;
            return [];
        }
        const alternate = line.match(/^\s*#elif\s+(.+)/);
        if (alternate) {
            const top = stack.at(-1), enabled = !top.enabled && evaluateCondition(alternate[1]);
            active = top.parent && enabled;
            top.enabled ||= enabled;
            return [];
        }
        if (/^\s*#else/.test(line)) { const top = stack.at(-1); active = top.parent && !top.enabled; return []; }
        if (/^\s*#endif/.test(line)) { active = stack.pop().parent; return []; }
        return active ? [line] : [];
    }).join('\n').replace(/\/\/[^\n]*/g, '')
        .replace(/FairyToonLight l(?: = \(FairyToonLight\)0)?;/g, 'let l = {};')
        .replace(/l = \(FairyToonLight\)0;/g, 'l = {};')
        .replace(/\((?:half|float)[234]?\)/g, '')
        .replace(/\b(?:half|float|int|bool)[234]?\s+/g, 'let ')
        .replace(/TEXTURE2D_ARGS\(([^,]+),\s*[^)]+\)/g, '$1');
    const uniforms = Object.fromEntries([...new Set(source.match(/\b_[A-Za-z]\w*/g) || [])].map(name => [name, 0]));
    // Unrelated scalar fixtures leave the new optional edge layers neutral.
    const values = { ...uniforms, _Main2ndDissolveColor: {rgb: 0}, _Main3rdDissolveColor: {rgb: 0}, ...bindings };
    if (values.s && !values.s.layerDissolveEdges) values.s.layerDissolveEdges = {x: 0, y: 0};
    return new Function(...Object.keys(values), source)(...Object.values(values));
}
const shader = read('Shaders/FairyToon.shader');
const input = read('Shaders/FairyToonInput.hlsl');
const setup = read('Editor/FairyToonMaterialSetup.cs');
const converter = read('Editor/FairyToonConverter.cs');
const current = properties(shader);
const reference = properties(read('../jp.lilxyzw.liltoon/CustomShaderResources/Properties/Default.lilblock'));
const names = [...current.keys()].filter(n => /^_(?:UseMain[23]|Main[23]|Color[23]|UseEmission|Emission)/.test(n));

const specialVariants = [
    ['Refraction', 'lts_ref.shader', /^_Refraction/],
    ['Fur', 'lts_fur.shader', /^_(?:Fur|VertexColor2FurVector)/],
    ['Gem', 'lts_gem.shader', /^_(?:Gem|Refraction)/],
];
for (const [variant, sourceName, pattern] of specialVariants) {
    test(variant + ' is a separate shader with the complete installed lilToon property contract', () => {
        const file = path.join(root, 'Shaders/FairyToon' + variant + '.shader');
        assert.ok(fs.existsSync(file), 'missing separate FairyToon/' + variant + ' shader');
        const text = read('Shaders/FairyToon' + variant + '.shader');
        assert.match(text, new RegExp('Shader\\s+"FairyToon/' + variant + '"'));
        const actual = properties(text);
        const expected = properties(read('../jp.lilxyzw.liltoon/Shader/' + sourceName));
        const selected = [...expected].filter(([name]) => pattern.test(name));
        assert.ok(selected.length > 0, 'reference feature exists');
        for (const [name, property] of selected) {
            assert.ok(actual.has(name), 'missing special property ' + name);
            assert.equal(actual.get(name).type.replace(/\s/g, ''), property.type.replace(/\s/g, ''), name + ' type/range');
            assert.equal(actual.get(name).value, property.value, name + ' default');
        }
        assert.ok(!/UsePass\s+"FairyToon\//.test(text), 'all passes must use this shader material layout');
        const blocks = [...text.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)];
        const common = [...text.matchAll(/\bHLSLINCLUDE\b([\s\S]*?)ENDHLSL/g)].map(m => m[1]).join('\n');
        assert.ok(blocks.length >= 5, 'forward and required supporting passes');
        for (const [, block] of blocks) {
            const label = block.match(/Name\s+"([^"]+)"/)?.[1];
            const wrapper = block.indexOf('#include "FairyToonVariantInput.hlsl"');
            const body = block.search(/#include "FairyToon(?:Passes|Fur|Variants|SeeThrough)\.hlsl"/);
            assert.ok(wrapper >= 0 && body > wrapper, variant + '/' + label + ' includes variant input first');
            assert.ok((common + block).includes('#define FAIRYTOON_' + variant.toUpperCase()), variant + '/' + label + ' variant define');
        }
    });
}

test('special variants keep one material buffer and reuse material samplers', () => {
    const file = path.join(root, 'Shaders/FairyToonVariantInput.hlsl');
    assert.ok(fs.existsSync(file), 'missing variant material input');
    const variantInput = read('Shaders/FairyToonVariantInput.hlsl');
    const commonBuffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    assert.match(commonBuffer, /\bFT_VARIANT_PROPERTIES\b/);
    assert.ok(!/CBUFFER_START|cbuffer\s+UnityPerMaterial/.test(variantInput), 'variant properties extend the common buffer');
    assert.equal([...variantInput.matchAll(/\bSAMPLER\s*\(/g)].length, 0, 'reuse the shared sampler states');
    assert.ok(!/\b_Fur\w*\b|\b_Gem\w*\b|\b_Refraction\w*\b/.test(shader), 'main shader has no special properties');
});

// The pre-merge main-shader hash is superseded by the approved parity fixes.
for (const [variant] of specialVariants) test(variant + ' exposes the parity-fixed common property contract', () => {
    const text = read('Shaders/FairyToon' + variant + '.shader');
    const actual = properties(text);
    const renderDefaults = new Set(['_TransparentMode', '_Cull', '_ZWrite', '_SrcBlend', '_DstBlend',
        '_SrcBlendAlpha', '_DstBlendAlpha', '_AlphaToMask']);
    for (const [name, value] of current) {
        if (/^_(?:UseWet|Wet)/.test(name)) continue; // First wetness pass is main-shader only.
        assert.ok(actual.has(name), variant + ' missing ' + name);
        assert.equal(actual.get(name).type, value.type, name + ' type');
        if (!renderDefaults.has(name)) assert.equal(actual.get(name).value, value.value, name + ' default');
    }
    for (const name of ['_Metallic', '_Reflectance'])
        assert.ok(text.split('\n').some(line => line.includes('[Gamma] ' + name + ' ')), name + ' Gamma upload');
});

test('offline tooling accepts a read-only package cache and verifies all declared shader stages', () => {
    const compile = read('Tests~/CompileShaders.ps1');
    assert.match(compile, /\[string\]\$PackageCache\s*=\s*\(Join-Path \$ProjectRoot 'Library\/PackageCache'\)/);
    assert.ok(compile.includes('Directory.GetDirectories(packageCache)'));
    assert.ok(!compile.includes('Directory.GetDirectories(Path.Combine(root,"Library/PackageCache"))'));
    for (const stage of ['vs_5_0', 'gs_5_0', 'ps_5_0']) assert.ok(compile.includes(stage), stage);
    assert.ok(compile.includes('128') && compile.includes('16'), 'DX11 per-stage resource limits');
    assert.ok(compile.includes('$referenceLayouts[$shaderName]'), 'layouts compare within a shader');
    assert.ok(!compile.includes("if($shaderName -eq 'FairyToon') {"), 'stage interfaces are checked on every shader');
    assert.ok(compile.includes('Test-StageInterface'), 'each compiled stage interface is checked');
    assert.ok(fs.existsSync(path.join(__dirname, 'ReportVariants.cjs')), 'missing material/pipeline variant count report');
});

test('special modes route materials and converter output to separate shaders', () => {
    for (const variant of ['Refraction', 'Fur', 'Gem']) {
        assert.ok(setup.includes('FairyToon/' + variant), variant + ' shader routing');
    }
    for (const api of ['IsFairyToonShader', 'GetRenderingMode', 'SetRenderingMode']) {
        assert.match(setup, new RegExp('public static [^\\n]+\\b' + api + '\\('), api + ' API');
    }
    assert.ok(setup.includes('CopyPropertiesFromMaterial'), 'shader switches preserve named textures and transforms');
    assert.ok(setup.includes('"_FT_REFRACTION_BLUR"') && setup.includes('"_RefractionBlur"'), 'blur keyword is material-derived');
    assert.ok(setup.includes('FairyToon.SeeThroughMaskLightMode, furTwoPass'), 'second Fur pass uses its LightMode');
    assert.ok(!/SetShaderPassEnabled\("FurSecond"/.test(setup), 'pass names are not LightMode tags');
    assert.ok(!/Fur shells dropped|Refraction dropped|Gem refraction -> reflective transparent/.test(converter), 'supported variants do not report old dropped-feature losses');
});

test('converter chooses the destination before loss auditing and maps named and Multi modes', () => {
    const convert = converter.slice(converter.indexOf('public static FairyToonConvertReport Convert('), converter.indexOf('public static List<FairyToonConvertReport> ConvertAll('));
    assert.ok(convert.indexOf('MapVariant(snapshot, report)') < convert.indexOf('CollectLosses(snapshot, shader, report)'));
    assert.ok(convert.indexOf('Shader.Find(shaderName)') < convert.indexOf('CollectLosses(snapshot, shader, report)'));
    const begin = converter.indexOf('private static VariantMapping MapVariant(');
    const method = converter.slice(begin, converter.indexOf('private static void CollectLosses(', begin));
    const object = method.match(/return new VariantMapping\s*\{([\s\S]*?)\};/);
    assert.ok(object, 'variant mapping result');
    let body = method.slice(method.indexOf('{') + 1, method.lastIndexOf('}'));
    body = body.replace(object[0], 'return {' + object[1].replace(/\b(\w+)\s*=/g, '$1:') + '};')
        .replace(/\b(?:string|int|bool|float)\s+/g, 'let ')
        .replace(/VariantFeatures\[i,\s*(\d)\]/g, 'VariantFeatures[i][$1]')
        .replace(/(\d+\.\d+)f\b/g, '$1');
    const constants = { Opaque: 0, Cutout: 1, Transparent: 2, Refraction: 3, Fur: 4, Gem: 5, FurTransparent: 0, FurCutout: 1, FurTwoPass: 2 };
    const run = new Function('source', 'report', 'FairyToonMaterialSetup', 'Contains', 'VariantFeatures', 'Add', 'IsNumber', 'Number', 'Mathf', body);
    const mapping = (shaderName, mode) => {
        const report = { SourceShader: shaderName, UnsupportedFeatures: [], AppliedOverrides: { Add() {} } };
        const source = { shader: { FindPropertyIndex: () => mode === undefined ? -1 : 0, GetPropertyType: () => 'Float' },
            IsKeywordEnabled: () => false, HasProperty: () => false, GetFloat: () => mode };
        return run(source, report, constants, (a, b) => a.toLowerCase().includes(b.toLowerCase()), { GetLength: () => 0 },
            (list, value) => list.push(value), () => true, () => mode, { RoundToInt: Math.round, Approximately: (a, b) => Math.abs(a - b) < 1e-6 });
    };
    for (const [name, expectedMode, furMode, blur] of [
        ['lilToon', 0, 0, false], ['Hidden/lilToonCutout', 1, 1, false], ['Hidden/lilToonTransparent', 2, 0, false],
        ['Hidden/lilToonRefraction', 3, 0, false], ['Hidden/lilToonRefractionBlur', 3, 0, true],
        ['Hidden/lilToonFur', 4, 0, false], ['Hidden/lilToonFurCutout', 4, 1, false], ['Hidden/lilToonFurTwoPass', 4, 2, false],
        ['Hidden/lilToonGem', 5, 0, false],
    ]) {
        const actual = mapping(name);
        assert.deepEqual([actual.Mode, actual.FurMode, actual.Blur], [expectedMode, furMode, blur], name);
    }
    for (const mode of [0, 1, 2, 3, 4, 5, 6]) {
        const actual = mapping('Hidden/lilToonMulti', mode);
        assert.equal(actual.Mode, [0, 1, 2, 3, 4, 4, 5][mode], 'Multi mode ' + mode);
        if ([4, 5].includes(mode)) assert.equal(actual.FurMode, mode === 5 ? 1 : 0);
    }
    for (const [name, expected] of [['Hidden/lilToonMultiRefraction', 3], ['Hidden/lilToonMultiFur', 4], ['Hidden/lilToonMultiGem', 5]]) {
        assert.equal(mapping(name, 0).Mode, expected, 'named special shader wins over a stale Multi default');
    }
});

test('mode switching snapshots saved values before shader replacement and then rebuilds state', () => {
    const begin = setup.indexOf('public static bool SetRenderingMode(');
    const method = setup.slice(begin, setup.indexOf('public static void Apply(', begin));
    const snapshot = method.indexOf('new Material(material)');
    const replace = method.indexOf('material.shader = shader;');
    const copy = method.indexOf('material.CopyPropertiesFromMaterial(snapshot)');
    assert.ok(snapshot >= 0 && replace > snapshot && copy > replace, 'copy saved properties after switching from a full snapshot');
    assert.ok(method.indexOf('Apply(material)') > copy, 'derive keyword/pass state after restore');
    assert.ok(!/SetTexture\(|SetTextureScale\(|SetTextureOffset\(/.test(method), 'no lossy fixed texture allowlist');
    assert.match(method, /finally\s*\{\s*Object\.DestroyImmediate\(snapshot\)/);
});

test('special modes neutralize only exact managed stencil presets', () => {
    assert.match(setup, /ResetManagedStencil\(material, true\)/, 'special shader switches request exact preset matching');
    const begin = setup.indexOf('private static void ResetManagedStencil(');
    const method = setup.slice(begin, setup.indexOf('private static ', begin + 15));
    assert.match(method, /bool exact\s*=\s*false/, 'main shader keeps its established cleanup behavior');
    const guard = method.match(/if\s*\(exact\s*&&([\s\S]*?)\)\s*return;/)?.[1];
    assert.ok(guard, 'custom fail actions must retain their stencil setup');
    for (const property of ['_StencilFail', '_StencilZFail']) assert.ok(guard.includes(property), property + ' is part of exact matching');
    assert.ok(guard.includes('StencilOp.Keep'), 'managed presets keep both failure paths');
    const body = method.slice(method.indexOf('{') + 1, method.lastIndexOf('}')).replace(/\(int\)/g, '');
    const cleanup = new Function('material', 'exact', 'Get', 'StencilIs', 'FairyToon', 'CompareFunction', 'StencilOp', body);
    for (const exact of [false, true]) for (const managed of [false, true])
    for (const fail of [0, 1, 7]) for (const zfail of [0, 2, 5]) {
        const writes = {};
        cleanup({ SetInt: (name, value) => writes[name] = value }, exact,
            (_, name) => name === '_StencilFail' ? fail : zfail, () => managed,
            { FaceBit: 4, SeeThroughBit: 8 }, { Always: 8, NotEqual: 6 }, { Keep: 0, Replace: 2, Zero: 1 });
        assert.equal(Object.hasOwn(writes, '_StencilRef'), managed && (!exact || (fail === 0 && zfail === 0)), 'custom stencil failure action must survive a special mode switch');
    }
});

test('Fur separates body, generated fur and second-pass routing', () => {
    const file = path.join(root, 'Shaders/FairyToonFur.shader');
    assert.ok(fs.existsSync(file), 'missing Fur shader');
    const fur = read('Shaders/FairyToonFur.shader');
    const blocks = new Map([...fur.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)]
        .map(([, block]) => [block.match(/Name\s+"([^"]+)"/)?.[1], block]));
    for (const [name, tag] of [['Forward', 'SRPDefaultUnlit'], ['FurForward', 'UniversalForward'], ['FurSecond', 'UniversalForwardOnly']]) {
        assert.ok(blocks.has(name), 'missing Fur ' + name);
        assert.match(blocks.get(name), new RegExp('"LightMode"\\s*=\\s*"' + tag + '"'));
    }
    assert.match(blocks.get('Forward'), /#pragma vertex FairyToonForwardVertex/);
    assert.match(blocks.get('Forward'), /#pragma fragment FairyToonForwardFragment/);
    for (const name of ['FurForward', 'FurSecond']) {
        assert.match(blocks.get(name), /#pragma vertex FairyToonFurVertex/);
        assert.match(blocks.get(name), /#pragma geometry FairyToonFurGeometry/);
        assert.match(blocks.get(name), /#pragma fragment FairyToonFurFragment/);
    }
    assert.match(blocks.get('FurSecond'), /#define FAIRYTOON_FUR_SECOND/);
    assert.ok(!blocks.has('Outline'), 'Fur preserves its source shader pass topology');
});

test('Fur retains the saved outline setting without compiling or enabling an invisible motion hull', () => {
    const fur = read('Shaders/FairyToonFur.shader');
    assert.ok(properties(fur).has('_UseOutline'), 'saved outline setting survives mode switches');
    const expression = setup.match(/SetKeyword\(material, "_FT_OUTLINE", ([^;]+)\);/)?.[1];
    assert.ok(expression, 'outline keyword expression');
    const enabled = new Function('outline', 'fur', 'gem', 'return ' + expression);
    for (const outline of [false, true]) for (const fur of [false, true]) {
        assert.equal(enabled(outline, fur, false), outline && !fur, 'Fur has no rendered outline hull');
        assert.equal(enabled(outline, fur, true), false, 'Gem reserves the outline tag for PRE');
    }
    const motion = [...fur.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)].find(([, block]) => /Name "MotionVectors"/.test(block))?.[1];
    assert.ok(motion, 'Fur motion pass');
    assert.ok(!/#pragma shader_feature[^\n]*\b_FT_OUTLINE\b/.test(motion), 'Fur motion has no outline variants');
});

test('Fur alpha-to-coverage follows Cutout and TwoPass PRE independently of the body', () => {
    for (const furMode of [0, 1, 2]) {
        const material = queueMaterial(4, furMode, defaultRenderQueue(4, furMode));
        setFurShellDefaults(material, furMode);
        assert.equal(material.values._FurAlphaToMask, furMode === 0 ? 0 : 1);
        assert.equal(material.values._FurZWrite, furMode === 0 ? 0 : 1);
    }
    const destination = setup.match(/material\.SetInt\("_FurDstBlendAlpha", ([^;]+)\);/)?.[1];
    const alphaBlend = new Function('mode', 'Transparent', 'BlendMode', 'return ' + destination.replace(/\(int\)/g, ''));
    for (const mode of [0, 1, 2]) {
        const expected = Number(properties(read('../jp.lilxyzw.liltoon/Shader/lts_fur.shader')).get('_FurDstBlendAlpha').value);
        assert.equal(alphaBlend(mode, 2, { OneMinusSrcAlpha: 10, Zero: 0 }), expected, 'Fur alpha blending matches source in every mode');
    }
});

function variantPasses(variant) {
    return new Map([...read('Shaders/FairyToon' + variant + '.shader').matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)]
        .map(([, block]) => [block.match(/Name\s+"([^"]+)"/)?.[1], block]));
}

function csharpBody(name) {
    const declaration = setup.match(new RegExp('(?:public|private|internal) static [^\\n]+?\\b' + name + '\\('));
    assert.ok(declaration, 'missing C# method ' + name);
    const start = setup.indexOf('{', declaration.index);
    let depth = 1, end = start + 1;
    for (; depth && end < setup.length; end++) {
        if (setup[end] === '{') depth++;
        if (setup[end] === '}') depth--;
    }
    assert.equal(depth, 0);
    return setup.slice(start + 1, end - 1);
}
const renderConstants = { Opaque: 0, Cutout: 1, Transparent: 2, Refraction: 3, Fur: 4, Gem: 5,
    FurTransparent: 0, FurCutout: 1, FurTwoPass: 2 };
const renderQueues = { Geometry: 2000, AlphaTest: 2450, GeometryLast: 2500, Transparent: 3000 };
const hairQueueTag = 'FairyToonManagedHairQueue';
const seeThroughConstants = { SeeThroughHairLightMode: 'hair', SeeThroughMaskLightMode: 'mask', FakeShadowLightMode: 'fake',
    SeeThroughEarlyZLightMode: 'early', FaceBit: 1, SeeThroughBit: 2 };
function isManagedHairQueue(material) {
    const source = csharpBody('IsManagedHairQueue').replace(/\(int\)/g, '').replace(/\bstring\s+/g, 'let ');
    return new Function('material', 'RenderQueue', 'ManagedHairQueueTag', 'FairyToon', source)
        (material, renderQueues, hairQueueTag, seeThroughConstants);
}
function defaultRenderQueue(mode, fur = 0) {
    return new Function('renderingMode', 'furMode', 'RenderQueue', ...Object.keys(renderConstants),
        csharpBody('GetDefaultRenderQueue').replace(/\(int\)/g, ''))(mode, fur, renderQueues, ...Object.values(renderConstants));
}

// Execute the actual C# render-state derivation; Unity APIs are represented by their material state effects.
function renderState(renderingMode, furMode = 0, zwrite = 1, queue = renderingMode === 3 || renderingMode === 5 ? 2900 : 3000, history = {}) {
    const constants = { Opaque: 0, Cutout: 1, Transparent: 2, Refraction: 3, Fur: 4, Gem: 5,
        FurTransparent: 0, FurCutout: 1, FurTwoPass: 2 };
    const values = { _TransparentMode: renderingMode <= 2 ? renderingMode : 0, _FurMode: furMode, _ZWrite: zwrite, _Cull: 1 };
    const passes = { ...history.passes }, keywords = { ...history.keywords };
    const tags = { ...history.tags };
    const material = { shader: {}, rawRenderQueue: queue,
        GetTag: (name, _, fallback) => tags[name] ?? fallback,
        GetShaderPassEnabled: name => !!passes[name], IsKeywordEnabled: name => !!keywords[name],
        get renderQueue() { return this.rawRenderQueue < 0 ? 2000 : this.rawRenderQueue; },
        set renderQueue(value) { this.rawRenderQueue = value; },
        SetFloat: (k, v) => values[k] = v, SetInt: (k, v) => values[k] = v,
        SetOverrideTag: () => {}, SetShaderPassEnabled: (k, v) => passes[k] = v };
    const begin = setup.indexOf('int renderingMode = GetRenderingMode(material);');
    const body = setup.slice(begin, setup.indexOf('// (Endfield replaces toon shadow', begin))
        .replace(/\b(?:bool|int)\s+/g, 'let ').replace(/\(int\)/g, '');
    new Function('writeCoverageBlends', 'material', 'GetRenderingMode', 'Get', 'SetKeyword', 'On', 'Mathf', 'BlendMode', 'RenderQueue', 'FairyToon', 'GetDefaultRenderQueue', 'IsManagedHairQueue',
        ...Object.keys(constants), body)(true, material, () => renderingMode, (_, name) => values[name] || 0,
        (_, name, value) => keywords[name] = value, () => false, { Clamp: (v, a, b) => Math.min(b, Math.max(a, v)), RoundToInt: Math.round },
        { One: 1, Zero: 0, SrcAlpha: 5, OneMinusSrcAlpha: 10 },
        { Geometry: 2000, AlphaTest: 2450, GeometryLast: 2500, Transparent: 3000 }, { CharacterShadowLightMode: 'CharacterShadow', CharacterMaskLightMode: 'CharacterMask' }, defaultRenderQueue, isManagedHairQueue,
        ...Object.values(constants));
    return { values, passes, keywords, material };
}

test('Gem clears once then adds every visible face independently of triangle order', () => {
    const passes = variantPasses('Gem');
    const pre = passes.get('GemPre');
    assert.ok(pre, 'Gem requires a black PRE draw');
    assert.match(pre, /"LightMode"\s*=\s*"SRPDefaultUnlit"/);
    assert.match(pre, /Blend One Zero/);
    for (const block of [pre, passes.get('Forward')]) assert.match(block, /Cull \[_Cull\]/);
    assert.ok(!passes.has('Outline'), 'Gem PRE owns SRPDefaultUnlit');
    const expected = properties(read('../jp.lilxyzw.liltoon/Shader/lts_gem.shader'));
    const actual = properties(read('Shaders/FairyToonGem.shader'));
    for (const name of ['_Cull', '_ZWrite', '_SrcBlend', '_DstBlend']) assert.deepEqual(actual.get(name),
        { ...actual.get(name), value: expected.get(name).value }, name + ' matches source default');
    const { values } = renderState(5);
    assert.equal(values._SrcBlend, 1);
    assert.equal(values._DstBlend, 1);
    assert.equal(values._Cull, 1, 'authored/converted culling survives Apply');
    const composite = layers => layers.reduce((dst, src) => src * values._SrcBlend + dst * values._DstBlend, 0);
    assert.equal(composite([0.1, 0.25, 0.5]), composite([0.5, 0.25, 0.1]), 'front and interior faces accumulate');
    const optics = read('Shaders/FairyToonVariants.hlsl');
    assert.match(optics, /MixFogColor\(col, (?:0\.0|half3\(0[^)]*\)), input.fogFactor\)/, 'additive final fog fades towards black');
    assert.match(hlslBody(optics, 'FairyToonVariantFragment'), /half4\(MixFogColor\(col, 0\.0, input\.fogFactor\), s\.alpha\)/,
        'Gem retains source material alpha for alpha consumers while additive RGB stays unweighted');
});

test('Fur body mode states match source premultiplied transparency and keep body auxiliaries', () => {
    for (const mode of [0, 1, 2]) {
        const { values, passes, keywords } = renderState(4, mode);
        const cutout = mode === 1;
        assert.equal(values._TransparentMode, cutout ? 1 : 2, 'TwoPass body uses LIL_RENDER 2');
        assert.equal(values._SrcBlend, cutout ? 1 : 5, 'straight RGB counterpart of source One');
        assert.equal(values._DstBlend, cutout ? 0 : 10);
        assert.equal(values._DstBlendAlpha, 10, 'all Fur bodies accumulate alpha like source');
        assert.equal(values._ZWrite, 1);
        assert.equal(keywords._ALPHATEST_ON, cutout);
        for (const pass of ['ShadowCaster', 'CharacterShadow', 'CharacterMask', 'DepthOnly', 'DepthNormals', 'MotionVectors'])
            assert.equal(passes[pass], true, mode + '/' + pass + ' retains body geometry');
        if (!cutout) for (const a of [0, 0.2, 0.7, 1]) {
            const source = (0.6 * a) + 0.3 * (1 - a);
            const factors = { 0: 0, 1: 1, 5: a, 10: 1 - a };
            assert.equal(0.6 * factors[values._SrcBlend] + 0.3 * factors[values._DstBlend], source,
                'straight body equals source premultiplied body');
        }
    }
    assert.match(variantPasses('Fur').get('FurForward'), /AlphaToMask \[_FurAlphaToMask\]/);
});

test('optical Apply preserves authored depth and Refraction defaults to source depth/queue', () => {
    for (const mode of [3, 5]) for (const zwrite of [0, 1]) assert.equal(renderState(mode, 0, zwrite).values._ZWrite, zwrite);
    const ref = read('Shaders/FairyToonRefraction.shader');
    assert.equal(properties(ref).get('_ZWrite').value, '1');
    assert.match(ref, /"Queue"\s*=\s*"Transparent-100"/);
    const gui = read('Editor/FairyToonGUI.cs');
    assert.ok(!/DisabledScope\(SelectedSpecialMode\(\)\)\) P\("_ZWrite"\)/.test(gui), 'Refraction depth stays editable');
    assert.match(gui, /m_Editor\.PropertiesChanged\(\);\s*GUIUtility\.ExitGUI\(\);/, 'shader swap exits stale GUI traversal');
});

test('Fur shells only shade shadow, rim shade and fur rim using vertex normals and source inverse lighting', () => {
    const fur = read('Shaders/FairyToonFur.hlsl');
    assert.ok(!/FairyToonShade\(/.test(fur), 'no full surface effect stack per ribbon');
    assert.match(fur, /FairyToonForwardSurface\(input\.body,/);
    assert.doesNotMatch(hlslBody(fur, 'FairyToonFurFragment'), /s\.N\s*=/, 'shared facing must survive in Endfield ribbons');
    assert.match(fur, /FairyToonRimShadeMultiplier\(s\)/);
    assert.match(fur, /FairyToonDiffuse\(s, l\)/);
    assert.match(fur, /FairyToonGray\(invLighting\)/);
    const inverse = fur.match(/half3 invLighting = ([^;]+);/)?.[1];
    assert.ok(inverse, 'source light-color inverse lighting');
    const evaluate = new Function('lightColor', 'saturate', 'sqrt', 'max', 'return ' + inverse);
    for (const light of [0, 0.1, 0.4, 1, 2]) assert.equal(evaluate(light, x => Math.max(0, Math.min(1, x)), Math.sqrt, Math.max),
        Math.max(0, Math.min(1, (1 - light) * Math.sqrt(light))));
    for (const name of ['FurForward', 'FurSecond']) {
        const block = variantPasses('Fur').get(name);
        for (const keyword of ['_NORMALMAP', '_FT_NORMAL2ND', '_FT_ANISOTROPY', '_FT_BACKLIGHT', '_FT_GLITTER', '_FT_REFLECTION', '_FT_MATCAP', '_FT_RIM', '_FT_EMISSION'])
            assert.ok(!new RegExp('#pragma shader_feature[^\\n]*\\b' + keyword + '(?:_UV)?\\b').test(block), name + ' strips ' + keyword);
    }
});

test('optical blur derives from roughness once, widens nearby, and vanishes on a smooth surface', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const lodExpression = optics.match(/float refractLod = ([^;]+);/)?.[1];
    assert.ok(lodExpression, 'missing roughness/depth blur relation');
    const lod = new Function('perceptualRoughness', 'viewDepth', 'min', 'max', 'sqrt', 'return ' + lodExpression);
    const actualLOD = (roughness, depth) => lod(roughness, depth, Math.min, Math.max, Math.sqrt);
    const radiusExpression = optics.match(/float2 radius = ([^;]+);/)?.[1];
    assert.ok(radiusExpression, 'missing texel-based blur radius');
    const radius = new Function('refractLod', 'texelSize', 'exp2', 'return ' + radiusExpression.replace('_CameraOpaqueTexture_TexelSize.xy', 'texelSize'));
    for (const roughness of [0, 0.04, 0.25, 1]) for (const depth of [0.01, 1, 4, 100]) {
        const expected = Math.min(Math.sqrt(roughness * 5 / Math.sqrt(depth)), 10);
        assert.ok(Math.abs(actualLOD(roughness, depth) - expected) < 1e-12, 'single roughness/depth conversion');
    }
    assert.equal(radius(actualLOD(0, 1), 1 / 1024, x => 2 ** x), 0);
    assert.ok(actualLOD(0.5, 1) > actualLOD(0.5, 4), 'nearby surfaces blur more');
    assert.ok(Number.isFinite(actualLOD(1, 0)), 'near plane guard');
    const roughness = optics.match(/float FairyToonOpticalRoughness\([^)]*\)\s*\{([\s\S]*?)\n\}/)?.[1];
    assert.ok(roughness, 'missing optical roughness helper');
    assert.ok(!/ddx|ddy|_GSAAStrength/.test(roughness), 'optical blur roughness comes exactly from Smoothness times texture, without GSAA');
    for (const smoothness of [0, 0.2, 0.8, 1]) for (const map of [0, 0.3, 1])
    for (const anisotropy of [-1, 0, 0.7, 1]) for (const enabled of [false, true]) {
        const actual = scalarHlsl(roughness, ['_FT_ANISOTROPY'], {
            s: { anisotropy }, FairyToonOpticalSmoothness: () => smoothness * map,
            FairyToonAnisotropyReflection: () => enabled, abs: Math.abs, saturate: x => Math.min(1, Math.max(0, x)),
        });
        const factor = enabled ? Math.min(1, 1.2 - Math.abs(anisotropy)) : 1;
        assert.equal(actual, (1 - smoothness * map) * factor, 'anisotropy roughness applied exactly once before blur');
    }
});

test('Refraction base matches lilToon render-2 premultiply then optical mix and opaque output', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const fragment = optics.slice(optics.indexOf('half4 FairyToonVariantFragment('));
    const shade = hlslBody(optics, 'FairyToonOpticalShade');
    const mix = shade.match(/col = (lerp\(scene, col \* s\.alpha, s\.alpha\));/)?.[1];
    assert.ok(mix, 'render-2 premultiplication precedes the optical mix');
    const compose = new Function('scene', 'col', 's', 'lerp', 'return ' + mix);
    const lerp = (a, b, t) => a + (b - a) * t;
    for (const alpha of [0, 0.01, 0.25, 0.75, 1]) {
        assert.ok(Math.abs(compose(0.2, 1.8, { alpha }, lerp) - (0.2 * (1 - alpha) + 1.8 * alpha * alpha)) < 1e-12);
    }
    assert.match(fragment, /return half4\(MixFog\(col, input\.fogFactor\), 1\.0\);/);
    assert.ok(!fragment.includes('FairyToonShade('), 'full shared shading cannot run before optics');
    assert.match(shade, /col = lerp\(scene, col \* s\.alpha, s\.alpha\);\s*return FairyToonOpticalLayers\(s, l, col, 1\.0\);/);
});

test('post-optical emissions run after scene composition with one independent weight per layer', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const core = read('Shaders/FairyToonCore.hlsl');
    const layers = hlslBody(optics, 'FairyToonOpticalLayers');
    const shade = hlslBody(optics, 'FairyToonOpticalShade');
    const blendBody = core.match(/half3 FairyToonEmissionBlend\([^)]*\)\s*\{([^}]+)\}/)[1].replace(/\bhalf3\s+/g, 'let ');
    const blend = new Function('col', 'emission', 'amount', 'mode', 'alpha', 'max', blendBody);
    const weightBody = core.match(/half amount = e\.a[\s\S]*?return FairyToonEmissionBlend\([^;]+;/)[0]
        .replace(/\bhalf\s+/g, 'let ').replace(/^\s*#(?:if|endif).*$/gm, '');
    const weighted = new Function('col', 'e', 'blend', 'FairyToonBlink', 'blink', 's', 'mode', 'FairyToonEmissionBlend', weightBody);
    const lerp = (a, b, t) => a + (b - a) * t;
    const sat = x => Math.max(0, Math.min(1, x));
    const oracle = (col, emission, weight, mode) => lerp(col, [emission, col + emission, Math.max(col + emission - col * emission, col), col * emission][mode], weight);
    for (const variant of ['FAIRYTOON_REFRACTION', 'FAIRYTOON_GEM'])
    for (const alpha of [0, 0.25, 1]) for (const scene of [0, 0.4, 3]) for (const nv of [0.1, 1]) for (const mode of [0, 1, 2, 3]) {
        const calls = [];
        const cover = variant === 'FAIRYTOON_REFRACTION' ? 1 : alpha;
        const emissionLayer = (s, l, col, map, mask, grad, color, st, sr, uv, maskST, maskSR, amount, blendMode) => {
            calls.push({ amount, alpha: s.alpha });
            return weighted(col, { rgb: color, a: 1 }, amount, () => 1, 0, s, blendMode,
                (c, e, a, m, coverage) => blend(c, e, a, m, coverage, Math.max));
        };
        const layerBindings = {
            _UseEmission: 1, _UseEmission2nd: 1, _EmissionBlend: 0.6, _Emission2ndBlend: 0.3,
            _EmissionColor: 1.7, _Emission2ndColor: 0.8, _EmissionBlendMode: mode, _Emission2ndBlendMode: (mode + 1) % 4,
            FairyToonEmissionLayer: emissionLayer,
        };
        const s = { alpha, albedo: 1, dissolveEdge: 0, screenUV: 0, positionWS: 0, layers: { nv }, N: 0 };
        const actual = scalarHlsl(shade, [variant], {
            s, uv0: 0, _LightMaxLimit: 10, _RefractionFresnelPower: 1, _RefractionStrength: 0,
            _RefractionColor: { rgb: 1 }, min: Math.min, saturate: sat, pow: Math.pow, Luminance: x => x,
            TransformWorldToViewDir: () => ({ xy: 0 }), TransformWorldToView: () => ({ z: -1 }),
            FairyToonGetLight: () => ({ addLightColor: 0, aoLit: 0, lightColor: 1, shadowmix: 0 }),
            FairyToonDiffuse: () => 2, FairyToonAdditionalLights() {}, FairyToonMainLayersUnlit: (_, col) => col,
            FairyToonShadeBase: surface => { surface.emissionAlbedo = surface.albedo; return 2; },
            FairyToonOpticalRoughness: () => 0.5, FairyToonRefractedScene: () => scene,
            FairyToonGemOptics: (_, light, col) => scene + col * nv,
            lerp, FairyToonOpticalLayers: (surface, light, col, coverage) => scalarHlsl(layers, ['_FT_EMISSION'], {
                ...layerBindings, s: { ...surface }, l: light, col, coverage, _DissolveColor: {rgb: 0},
            }),
        });
        const base = variant === 'FAIRYTOON_REFRACTION' ? scene * (1 - alpha) + 2 * alpha * alpha : scene + 2 * nv;
        const expected = oracle(oracle(base, 1.7, 0.6 * cover, mode), 0.8, 0.3 * cover, (mode + 1) % 4);
        assert.ok(Math.abs(actual - expected) < 1e-12, variant + ' post-optic emission blend');
        assert.deepEqual(calls, [{ amount: 0.6 * cover, alpha: 1 }, { amount: 0.3 * cover, alpha: 1 }]);
    }
    assert.ok(shade.indexOf('FairyToonShadeBase(') >= 0 && shade.indexOf('FairyToonShadeBase(') < shade.indexOf('FairyToonRefractedScene('));
    assert.ok(layers.indexOf('FairyToonReflection(') < layers.indexOf('FairyToonMatCapLayer('));
    assert.ok(layers.indexOf('FairyToonMatCapLayer(') < layers.indexOf('FairyToonOpticalRim('));
    assert.ok(layers.indexOf('FairyToonOpticalRim(') < layers.indexOf('FairyToonGlitter('));
    assert.ok(layers.indexOf('FairyToonGlitter(') < layers.indexOf('FairyToonEmissionLayer('));
});

test('optical Backlight retains Endfield direct-light ownership without ambient fill', () => {
    const shade = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonShadeBase');
    const start = shade.indexOf('#if defined(_FT_BACKLIGHT)');
    const end = shade.indexOf('return col;', start);
    const block = shade.slice(start, end) + '\nreturn col;';
    for (const endfield of [false, true]) {
        const actual = scalarHlsl(block, ['_FT_BACKLIGHT', ...(endfield ? ['_FT_ENDFIELD'] : [])], {
            s: {}, col: 0, l: { lightColor: 0.8, backlightColor: 0.2 },
            FairyToonBacklight: (s, l, col) => col + l.backlightColor,
        });
        assert.equal(actual, endfield ? 0.2 : 0.8);
    }
});

test('Gem post-optical MatCaps, Rim and Glitter apply their own transparency masks once', () => {
    const layers = hlslBody(read('Shaders/FairyToonVariants.hlsl'), 'FairyToonOpticalLayers');
    const lerp = (a, b, t) => a + (b - a) * t;
    for (const coverage of [0, 0.2, 1]) for (const first of [0, 1]) for (const second of [0, 1]) for (const rim of [0, 1]) for (const glitter of [0, 1]) {
        const actual = scalarHlsl(layers, ['_FT_MATCAP', '_FT_RIM', '_FT_GLITTER'], {
            s: { alpha: coverage, dissolveEdge: 0 }, l: {}, col: 0.7, coverage, lerp, _DissolveColor: {rgb: 0},
            _UseMatCap: 1, _UseMatCap2nd: 1, _MatCapBlend: 0.4, _MatCap2ndBlend: 0.6,
            _MatCapApplyTransparency: first, _MatCap2ndApplyTransparency: second,
            _RimApplyTransparency: rim, _GlitterApplyTransparency: glitter,
            FairyToonMatCapNormal: () => 0,
            FairyToonMatCapLayer: (s, l, col, tex, mask, color, st, maskST, amount) => { assert.equal(s.alpha, 1); return col + amount; },
            FairyToonOpticalRim: (s, l, col, alpha) => { assert.equal(s.alpha, 1); return col + 0.8 * lerp(1, alpha, rim); },
            FairyToonGlitter: (s, l, col) => { assert.equal(s.alpha, 1); return col + 1.2; },
        });
        const expected = 0.7 + 0.4 * lerp(1, coverage, first) + 0.6 * lerp(1, coverage, second)
            + 0.8 * lerp(1, coverage, rim) + 1.2 * lerp(1, coverage, glitter);
        assert.ok(Math.abs(actual - expected) < 1e-12);
    }
});

test('Gem weights direct and indirect Rim masks before sequential blending in all modes', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const rim = hlslBody(optics, 'FairyToonOpticalRim');
    const tail = rim.slice(rim.indexOf('rimDir *= lerp('));
    assert.ok(tail.includes('rimIndir *= lerp('), 'each rim mask has its own coverage');
    const coreBlend = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonTransparentBlend');
    const lerp = (a, b, t) => a + (b - a) * t;
    const blend = (col, layer, amount, mode, alpha, applyTransparency) => scalarHlsl(coreBlend, ['_SURFACE_TYPE_TRANSPARENT'], {
        col, layer, amount, mode, alpha, applyTransparency, max: Math.max,
        clamp: (x, low, high) => Math.max(low, Math.min(high, x)),
    });
    const oracle = (col, layer, amount, mode) => lerp(col,
        [layer, col + layer, Math.max(col + layer - col * layer, col), col * layer][mode], amount);
    const actual = (col, coverage, apply, mode, amount, direct = 1, indirect = 1) => scalarHlsl(tail, [], {
        col, coverage, rimDir: amount, rimIndir: amount,
        rimColor: { rgb: direct, a: 1 }, rimIndirColor: { rgb: indirect, a: 1 },
        _RimApplyTransparency: apply, _RimBlendMode: mode, _RimEnableLighting: 0,
        s: { alpha: 1 }, l: { lightColor: 1 }, lerp, FairyToonTransparentBlend: blend,
    });
    assert.equal(actual(0, 0.5, 1, 0, 0.5), 0.4375, 'two half-strength Normal rims with half coverage');
    for (const col of [0, 0.4, 1.2]) for (const coverage of [0, 0.5, 1]) for (const apply of [0, 1])
    for (const mode of [0, 1, 2, 3]) for (const amount of [0.2, 0.5, 0.9]) {
        const mask = amount * lerp(1, coverage, apply);
        const expected = oracle(oracle(col, 0.8, mask, mode), 0.3, mask, mode);
        assert.ok(Math.abs(actual(col, coverage, apply, mode, amount, 0.8, 0.3) - expected) < 1e-12);
    }
    assert.match(hlslBody(optics, 'FairyToonOpticalLayers'), /col = FairyToonOpticalRim\(s, l, col, coverage\);/);
});

test('Gem reverses its interior normal before diffuse and all optical layers', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const surface = hlslBody(read('Shaders/FairyToonPasses.hlsl'), 'FairyToonForwardSurface');
    const fragment = hlslBody(optics, 'FairyToonVariantFragment');
    const flip = surface.match(/if \(s\.facing < 0\.0\) s\.N = ([^;]+);/)?.[1];
    assert.ok(flip, 'backface tilt must affect every shading stage');
    assert.ok(surface.indexOf('s.N = normalize(-') < surface.indexOf('FairyToonSetupAnisotropy('));
    assert.ok(surface.indexOf('s.N = normalize(-') < surface.indexOf('FairyToonLayeredBase('));
    assert.ok(fragment.indexOf('FairyToonForwardSurface(') < fragment.indexOf('FairyToonOpticalShade('));
    const scalar = new Function('s', 'normalize', 'return ' + flip);
    assert.equal(scalar({ N: 0.6, V: 1 }, x => x), -0.8);
    assert.match(surface, /#if defined\(FAIRYTOON_GEM\)\s*s\.layers\.nv = abs\(dot\(s\.N, s\.V\)\);/);
    assert.ok(!/s\.N\s*=/.test(fragment), 'interior N is not flipped again in the fragment wrapper');
    assert.ok(!/s\.N\s*=/.test(hlslBody(optics, 'FairyToonGemOptics')), 'normal is not flipped a second time inside optics');
});

test('Fur taper follows installed signed cubic/quartic alpha and only pre-pass length is shortened', () => {
    const fur = read('Shaders/FairyToonFur.hlsl');
    const shifted = fur.match(/float shifted = ([^;]+);/)?.[1];
    const taper = fur.match(/float furAlpha = ([\s\S]*?);/)?.[1];
    const multiply = fur.match(/s\.alpha \*= ([^;]+);/)?.[1];
    const remap = fur.match(/s\.alpha = (saturate\(s\.alpha \* 5\.0 - 2\.0\));/)?.[1];
    assert.ok(shifted && taper && multiply && remap, 'missing Fur alpha/taper arithmetic');
    const alpha = new Function('furLayer', '_FurRootOffset', 'furNoise', 'cutout', 'furMask', 's', 'saturate', 'pow', 'abs',
        'const shifted = ' + shifted + '; const furAlpha = ' + taper + '; s.alpha *= ' + multiply + '; if(cutout) s.alpha = ' + remap + '; return s.alpha;');
    const sat = x => Math.max(0, Math.min(1, x));
    for (const layer of [0, 0.2, 0.75, 1]) for (const root of [-1, -0.4, 0])
    for (const noise of [0, 0.3, 1]) for (const mask of [0, 0.5, 1]) for (const base of [0, 0.25, 1]) for (const cutout of [false, true]) {
        const x = layer * (1 - root) + root;
        const falloff = cutout ? sat(noise - x * Math.abs(x) ** 3 + 0.25) : sat(noise - x ** 3);
        const coverage = base * falloff * mask;
        const expected = cutout ? sat(coverage * 5 - 2) : coverage;
        assert.ok(Math.abs(alpha(layer, root, noise, cutout, mask, { alpha: base }, sat, Math.pow, Math.abs) - expected) < 1e-12);
    }
    const prepass = fur.match(/#if !defined\(FAIRYTOON_FUR_SECOND\)([\s\S]*?)#endif/)?.[1];
    assert.ok(prepass, 'second pass keeps full Fur length');
    const shorten = new Function('_FurMode', '_FurCutoutLength', 'output', prepass.replace(/\bfloat\b/g, 'let') + '; return output.furVector;');
    for (const mode of [0, 1, 2]) for (const length of [0, 0.2, 0.8, 1]) {
        assert.equal(shorten(mode, length, { furVector: 3 }), mode === 2 ? 3 * length : 3);
    }
    assert.equal((fur.match(/output\.furVector \*= _FurCutoutLength/g) || []).length, 1, 'pre-pass length is not applied twice');
});

test('special variants do not exceed common material keyword dimensions except Refraction Forward blur', () => {
    const { parseShader, countProgram } = require('./ReportVariants.cjs');
    const counts = text => new Map(parseShader(text).map(pass => [pass.name,
        countProgram(pass.source, 'test.shader', name => ({ file: name, source: '' })).material]));
    const baseline = counts(shader);
    for (const variant of ['Refraction', 'Gem', 'Fur']) {
        for (const [pass, count] of counts(read('Shaders/FairyToon' + variant + '.shader'))) {
            const basePass = ['FurForward', 'FurSecond', 'GemPre'].includes(pass) ? 'Forward' : pass;
            const factor = variant === 'Refraction' && pass === 'Forward' ? 2n : 1n;
            assert.ok(BigInt(count) <= BigInt(baseline.get(basePass)) * factor, variant + '/' + pass + ' material variants');
        }
        const baselineKeywords = new Set([...shader.matchAll(/#pragma shader_feature[^\n]+/g)].flatMap(m => m[0].match(/\b_[A-Z][A-Z0-9_]+\b/g) || []));
        for (const pass of parseShader(read('Shaders/FairyToon' + variant + '.shader'))) {
            const newKeywords = [...pass.source.matchAll(/#pragma shader_feature[^\n]+/g)].flatMap(m => m[0].match(/\b_[A-Z][A-Z0-9_]+\b/g) || []);
            for (const keyword of newKeywords) {
                assert.ok(baselineKeywords.has(keyword) || (variant === 'Refraction' && pass.name === 'Forward' && keyword === '_FT_REFRACTION_BLUR'), variant + '/' + pass.name + ' adds keyword ' + keyword);
            }
            if (newKeywords.includes('_FT_REFRACTION_BLUR')) assert.match(pass.source, /#pragma shader_feature_local_fragment _FT_REFRACTION_BLUR/);
        }
    }
    const gem = read('Shaders/FairyToonGem.shader');
    assert.ok(!/#pragma shader_feature[^\n]*\b_FT_REFLECTION\b/.test(gem), 'Gem uses its inherent optical environment reflection');
});
test('supported layer names and defaults match installed lilToon', () => {
    assert.equal(names.length, 84);
    for (const name of names) {
        assert.ok(reference.has(name), name + ' missing in lilToon');
        assert.equal(current.get(name).value, reference.get(name).value, name + ' default');
    }
});
test('layer constants share an unconditional buffer and textures are unique', () => {
    const buffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    assert.ok(!buffer.includes('#if'));
    for (const name of names) {
        if (current.get(name).type !== '2D') assert.ok(buffer.includes(name + ';'), name);
        else assert.equal([...input.matchAll(new RegExp('TEXTURE2D\\(' + name + '\\)', 'g'))].length, 1, name);
    }
    const samplers = [...input.matchAll(/SAMPLER\((\w+)\)/g)].map(m => m[1]);
    assert.equal(samplers.length, 7, 'main layers and emission maps retain their own texture sampling states');
    assert.equal(new Set(samplers).size, samplers.length);
});
test('independent 3rd and emission-2nd toggles share requested keywords', () => {
    assert.ok(setup.includes('"_FT_MAIN_LAYERS", On(material, "_UseMain2ndTex") || On(material, "_UseMain3rdTex")'));
    assert.ok(setup.includes('"_FT_EMISSION", On(material, "_UseEmission") || On(material, "_UseEmission2nd")'));
    assert.equal((shader.match(/#pragma shader_feature_local _FT_MAIN_LAYERS/g) || []).length, 11);
    assert.equal((shader.match(/#pragma shader_feature_local _FT_EMISSION\b/g) || []).length, 2);
    const outline = shader.slice(shader.indexOf('Name "Outline"'), shader.indexOf('Name "ShadowCaster"'));
    assert.ok(!outline.includes('_FT_MAIN_LAYERS'), 'outline has no main-layer variants');
    const core = read('Shaders/FairyToonCore.hlsl');
    for (const name of ['_UseMain2ndTex','_UseMain3rdTex']) assert.ok(input.includes('if (' + name + ' > 0.5)'));
    for (const name of ['_UseEmission','_UseEmission2nd']) assert.ok(core.includes('if (' + name + ' > 0.5)'));
});
test('body coverage is layered; outline and motion hull use base alpha only', () => {
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const seeThrough = read('Shaders/FairyToonSeeThrough.hlsl');
    assert.ok(passes.includes('FairyToonLayeredBase(s.layers, s.layer2, s.layer3, s.dissolveEdge, s.layerDissolveEdges)'));
    assert.ok(passes.includes('clip(FairyToonLayeredBase(layers).a'));
    assert.equal((passes.match(/FairyToonDepthClip\(FT_LAYER_CONTEXT/g) || []).length, 5);
    assert.equal((seeThrough.match(/FairyToonDepthClip\(FT_LAYER_CONTEXT/g) || []).length, 3);
    assert.ok(!passes.includes('FairyToonDepthClip(input.uv0)'));
    assert.ok(!seeThrough.includes('FairyToonDepthClip(input.uv0)'));
    const outline = passes.slice(passes.indexOf('half4 FairyToonOutlineFragment('), passes.indexOf('// ShadowCaster /'));
    assert.ok(outline.includes('FairyToonBaseColor(uvMain, input.uv0, FT_DISSOLVE_POSITION_OS(input))'));
    assert.ok(!outline.includes('FairyToonLayeredBase'));
    const motion = passes.slice(passes.indexOf('float4 FairyToonMotionFragment('));
    assert.match(motion, /#if defined\(_FT_OUTLINE\)[\s\S]*?FairyToonBaseColor[\s\S]*?#else[\s\S]*?FairyToonDepthClip/);
    const layered = input.slice(input.indexOf('half4 FairyToonLayeredBase('));
    assert.ok(layered.indexOf('FairyToonApplyAlphaMask(col, uvMain)') > layered.indexOf('FairyToonMainLayerAlpha(col.a, layer3.a'));
});
test('converter copies by name and only reports omitted sub-options', () => {
    assert.ok(converter.includes('target.CopyPropertiesFromMaterial(snapshot)'));
    const features = converter.match(/FeatureNames =([\s\S]*?);/)[1];
    for (const name of ['_UseMain2ndTex','_UseMain3rdTex','_UseEmission2nd']) assert.ok(!features.includes(name));
    for (const prefix of ['_Main2nd','_Main3rd','_Emission2nd']) assert.ok(!converter.includes('{ "' + prefix + '",'));
    for (const name of names) assert.ok(read('README.md').includes(name), 'documented mapping ' + name);
});

// Channel-wise arithmetic is executed from the actual HLSL helper, with an independent premultiplied oracle.
test('straight emission matches lilToon premultiplied blending in every mode', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const helper = core.match(/half3 FairyToonEmissionBlend\([^)]*\)\s*\{([^}]+)\}/);
    assert.ok(helper, 'missing straight-alpha emission blend helper');
    const blend = new Function('col', 'emission', 'amount', 'mode', 'alpha', 'max',
        helper[1].replace(/\bhalf3\s+/g, 'let '));
    // Also execute the real transparent weight/caller so reintroducing amount *= alpha fails.
    const weighting = core.match(/half amount = e\.a[\s\S]*?return FairyToonEmissionBlend\([^;]+;/);
    assert.ok(weighting, 'missing emission weight/call');
    const emit = new Function('col', 'e', 'blend', 'FairyToonBlink', 'blink', 's', 'mode', 'FairyToonEmissionBlend',
        weighting[0].replace(/\bhalf\s+/g, 'let ').replace(/^\s*#(?:if|endif).*$/gm, ''));
    const straightBlend = (c, e, w, m, a) => blend(c, e, w, m, a, Math.max);
    const layer = (c, e, w, m, a) => emit(c, { rgb: e, a: 1 }, w, () => 1, 0, { alpha: a }, m, straightBlend);
    const lilBlend = (p, e, w, mode) => {
        const target = [e, p + e, Math.max(p + e - p * e, p), p * e][mode];
        return p * (1 - w) + target * w;
    };
    const close = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-10, actual + ' != ' + expected);
    for (const a of [0, 1e-6, 0.2, 0.5, 1])
    for (const c of [0, 0.3, 2])
    for (const e of [0, 0.8, 4])
    for (const w of [0, 0.4, 1])
    for (const mode of [0, 1, 2, 3]) {
        const straight = layer(c, e, w, mode, a);
        close(straight * a, lilBlend(c * a, e, w * a, mode));
        const second = layer(straight, 1.7, 0.6, (mode + 1) % 4, a);
        close(second * a, lilBlend(lilBlend(c * a, e, w * a, mode), 1.7, 0.6 * a, (mode + 1) % 4));
        if (mode === 1) close(straight * a, (c + e * w) * a); // Emission 1st legacy Add.
    }
    assert.ok(core.includes('FairyToonEmissionBlend(col, e.rgb, amount, mode, alpha)'));
});

test('both emission masks use raw UV0 even at zero scroll; keep RGBA and pre-Rim albedo', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    assert.ok(core.includes('FairyToonAnimateUV(s.layers.uv0, maskST, maskSR)'));
    assert.ok(!core.includes('maskBaseUV'));
    assert.ok(core.includes('e *= SAMPLE_TEXTURE2D(mask, sampler_MainTex, maskUV)'));
    assert.ok(core.indexOf('s.emissionAlbedo = s.albedo') < core.indexOf('s.albedo *= s.rimShade'));
});

test('Forward decal edge uses mapped normal before layer sampling; Rim retains absolute N.V', () => {
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const forward = hlslBody(passes, 'FairyToonForwardSurface');
    assert.ok(forward.indexOf('s.N = normalize(TransformTangentToWorld') < forward.indexOf('s.layers.nv = saturate(dot(s.N, s.V))'));
    assert.ok(forward.indexOf('s.layers.nv = saturate(dot(s.N, s.V))') < forward.indexOf('FairyToonLayeredBase(s.layers'));
    assert.ok(forward.includes('s.layers.nvabs = abs(dot(s.N, s.V))'));
    assert.ok(input.includes('emission ? float2(c.nvabs, c.nvabs) : c.uvMat'));
});

const surfaceGroups = [
    ['Normal2nd', /^_(?:UseBump2ndMap|Bump2nd)/],
    ['Anisotropy', /^_(?:UseAnisotropy|Anisotropy)/],
    ['Backlight', /^_(?:UseBacklight|Backlight)/],
    ['Glitter', /^_(?:UseGlitter|Glitter)/],
];
for (const [label, pattern] of surfaceGroups) {
    test(label + ' import names, types and defaults match installed lilToon', () => {
        const expected = [...reference.keys()].filter(name => pattern.test(name));
        assert.ok(expected.length > 0, 'reference feature exists');
        for (const name of expected) {
            assert.ok(current.has(name), 'missing imported property ' + name);
            assert.equal(current.get(name).type.replace(/\s/g, ''), reference.get(name).type.replace(/\s/g, ''), name + ' type/range');
            assert.equal(current.get(name).value, reference.get(name).value, name + ' default');
        }
    });
}
test('new surface constants retain the unconditional material buffer and shared sampler budget', () => {
    const buffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    assert.ok(!buffer.includes('#if'), 'variant-independent material constants');
    for (const [, pattern] of surfaceGroups) {
        for (const [name, property] of reference) {
            if (!pattern.test(name)) continue;
            assert.ok(current.has(name), name);
            if (property.type === '2D') {
                assert.equal([...input.matchAll(new RegExp('TEXTURE2D\\(' + name + '\\)', 'g'))].length, 1, name);
            } else {
                assert.match(buffer, new RegExp('\\b' + name + '\\s*;'), name);
            }
        }
    }
    assert.equal([...input.matchAll(/SAMPLER\((\w+)\)/g)].length, 7, 'surface masks still reuse samplers; only main layers/emission maps own states');
});

test('new feature keywords reach only passes and stages that consume them', () => {
    const passBlocks = [...shader.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)];
    const expected = new Map([
        ['_FT_NORMAL2ND', ['Forward', 'DepthNormals', 'SeeThroughHair']],
        ['_FT_ANISOTROPY', ['Forward', 'SeeThroughHair']],
        ['_FT_BACKLIGHT', ['Forward', 'SeeThroughHair']],
        ['_FT_GLITTER', ['Forward', 'SeeThroughHair']],
    ]);
    for (const [keyword, passes] of expected) {
        const found = [];
        for (const [, block] of passBlocks) {
            const pragma = block.match(new RegExp('#pragma shader_feature_local(_fragment)?\\s+(?:_\\s+)?' + keyword + '\\b'));
            if (!pragma) continue;
            found.push(block.match(/Name\s+"([^"]+)"/)[1]);
            assert.equal(Boolean(pragma[1]), ['_FT_ANISOTROPY', '_FT_BACKLIGHT'].includes(keyword), keyword + ' stage scope');
        }
        assert.deepEqual(found.sort(), passes.sort(), keyword + ' pass scope');
    }
    for (const [keyword, toggle] of [['_FT_ANISOTROPY', '_UseAnisotropy'], ['_FT_BACKLIGHT', '_UseBacklight']]) {
        assert.match(setup, new RegExp('"' + keyword + '"\\s*,\\s*On\\(material,\\s*"' + toggle + '"\\)'), toggle + ' keyword');
    }
});

test('converter preserves new surface toggles and documents the complete imported option set', () => {
    const features = converter.match(/FeatureNames =([\s\S]*?);/)[1];
    for (const name of ['_UseBump2ndMap', '_UseAnisotropy', '_UseBacklight', '_UseGlitter']) {
        assert.ok(!features.includes(name), 'supported toggle must not be reported lost: ' + name);
    }
    for (const [, pattern] of surfaceGroups) {
        for (const name of [...reference.keys()].filter(name => pattern.test(name))) {
            assert.ok(read('README.md').includes(name), 'documented mapping ' + name);
        }
    }
});

test('second normal combines XY additively and Z multiplicatively as installed lilToon', () => {
    const file = path.join(root, 'Shaders/FairyToonNormals.hlsl');
    assert.ok(fs.existsSync(file), 'missing normal helper');
    const normals = read('Shaders/FairyToonNormals.hlsl');
    const helper = normals.match(/float3 FairyToonBlendNormal\(float3 dstNormal,\s*float3 srcNormal\)\s*\{([^}]+)\}/);
    assert.ok(helper, 'missing normal combination helper');
    const combine = new Function('dstNormal', 'srcNormal', 'float3', helper[1]);
    for (const [first, second, want] of [
        [[0, 0, 1], [0.3, -0.4, 0.866], [0.3, -0.4, 0.866]],
        [[0.6, 0, 0.8], [0, 0, 1], [0.6, 0, 0.8]],
        [[0.3, -0.4, 0.866], [-0.2, 0.5, 0.84], [0.1, 0.1, 0.72744]],
    ]) {
        for (const axis of [0, 1]) {
            const result = combine({xy: first[axis], z: first[2]}, {xy: second[axis], z: second[2]}, (xy,z) => [xy,z]);
            assert.ok(Math.abs(result[0] - want[axis]) < 1e-10, 'normal XY axis ' + axis);
            assert.ok(Math.abs(result[1] - want[2]) < 1e-10, 'normal Z');
        }
    }
    assert.match(normals, /_FT_NORMAL2ND/);
    assert.match(normals, /FairyToonLayerUV\([^;]*_Bump2ndMap_UVMode/);
    assert.match(normals, /_Bump2ndScaleMask/);
});
test('Glitter transparency preserves one alpha factor and guards unmasked zero-alpha division', () => {
    const file = path.join(root, 'Shaders/FairyToonGlitter.hlsl');
    assert.ok(fs.existsSync(file), 'missing Glitter implementation');
    const glitter = read('Shaders/FairyToonGlitter.hlsl');
    const helper = glitter.match(/float FairyToonGlitterTransparency\(float alpha,\s*float applyTransparency\)\s*\{([\s\S]*?)\n\}/);
    assert.ok(helper, 'missing transparent contribution helper');
    const factor = new Function('alpha', 'applyTransparency', 'max', helper[1]);
    for (const [alpha, wantUnmasked] of [[0,0], [1e-10,4096], [1e-6,4096], [1/4096,4096], [0.2,5], [0.5,2], [1,1]]) {
        const masked = factor(alpha, 1, Math.max);
        const unmasked = factor(alpha, 0, Math.max);
        assert.equal(masked, alpha === 0 ? 0 : 1, 'ApplyTransparency true at ' + alpha);
        assert.equal(unmasked, wantUnmasked, 'ApplyTransparency false at ' + alpha);
        assert.ok(Number.isFinite(masked) && Number.isFinite(unmasked));
        assert.ok(unmasked <= 4096, 'reciprocal fits native half with HDR headroom');
        if (alpha >= 1/4096) {
            const base = 0.3, glitterAmount = 2;
            assert.ok(Math.abs((base + glitterAmount * masked) * alpha - (base * alpha + glitterAmount * alpha)) < 1e-10);
            assert.ok(Math.abs((base + glitterAmount * unmasked) * alpha - (base * alpha + glitterAmount)) < 1e-10);
        }
    }
    assert.match(glitter, /FairyToonGlitterTransparency\(s\.alpha,\s*_GlitterApplyTransparency\)/, 'actual Glitter call uses tested helper');
    const output = glitter.match(/return clamp\(\(float3\)col \+ contribution, [^;]+;/);
    assert.ok(output, 'transparent HDR must clamp before half conversion');
    const finish = new Function('col','contribution','clamp',output[0].replace(/\(float3\)/g,''));
    for (const a of [0,1e-10,1e-6,1/4096,0.2,1]) {
        const result = finish(65000,65000*factor(a,0,Math.max),(x,lo,hi)=>Math.min(hi,Math.max(lo,x)));
        assert.ok(Number.isFinite(result) && Math.abs(result)<=65504, 'HDR Glitter fits native half');
    }
});
test('Backlight adds in straight RGB so transparent blending weights it exactly once', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const helper = core.match(/half3 FairyToonBacklight\([^)]*\)\s*\{([\s\S]*?)\n\}/);
    assert.ok(helper, 'missing Backlight implementation');
    assert.ok(!helper[1].includes('s.alpha'), 'Backlight is before premultiply in lilToon');
    const lastReturn = [...helper[1].matchAll(/return\s+([^;]+);/g)].at(-1);
    assert.ok(lastReturn, 'missing additive contribution');
    const straight = new Function('col', 'amount', 'color', 'l', 's', 'return ' + lastReturn[1]);
    for (const [alpha, expected] of [[0,0], [1e-6,1.5e-6], [0.2,0.3], [0.5,0.75], [1,1.5]]) {
        const result = straight(0.3, 0.4, {a:0.5,rgb:2}, {backlightColor:3}, {alpha}) * alpha;
        assert.ok(Math.abs(result - expected) < 1e-10, 'Backlight alpha ' + alpha);
    }
    assert.equal((core.match(/= FairyToonBacklight\(s,\s*(?:l|el),\s*(?:col|ecol)\)/g) || []).length, 1,
        'Toon and Endfield both receive Backlight');
});
test('layer normal reconstructs Z from scaled XY to retain lilToon normal strength', () => {
    const normals = read('Shaders/FairyToonNormals.hlsl');
    const helper = normals.match(/float FairyToonNormalZ\(float2 xy\)\s*\{([^}]+)\}/);
    assert.ok(helper, 'missing scaled-XY normal reconstruction helper');
    const normalZ = new Function('xy', 'sqrt', 'saturate', 'dot', helper[1]);
    const dot = (a,b) => a[0]*b[0]+a[1]*b[1];
    const saturate = x => Math.min(1,Math.max(0,x));
    for (const [xy, want] of [[[0,0],1], [[0.3,0.4],0.8660254037844386], [[0.6,0.8],0], [[3,4],0]]) {
        assert.ok(Math.abs(normalZ(xy,Math.sqrt,saturate,dot) - want) < 1e-10);
    }
    const decode = normals.match(/(?:half|float)3 FairyToonUnpackLayerNormal\([^)]*\)\s*\{([\s\S]*?)\n\}/);
    assert.ok(decode, 'missing lilToon-compatible layer decoder');
    assert.match(decode[1], /(\w+)\.xy\s*\*=\s*scale\s*;\s*\1\.z\s*=\s*FairyToonNormalZ\(\1\.xy\)\s*;/,
        'BC5 branch must scale XY immediately before reconstructing Z');
    assert.ok((normals.match(/FairyToonUnpackLayerNormal\(/g) || []).length >= 3, 'both normal layers use corrected decoder');
});
test('anisotropic lobes retain oriented widths, shifts and independent second-lobe strength', () => {
    const anisotropy = read('Shaders/FairyToonAnisotropy.hlsl');
    const helper = anisotropy.match(/float FairyToonAnisoLobe\([^)]*\)\s*\{([^}]+)\}/);
    assert.ok(helper, 'missing anisotropic distribution helper');
    const body = helper[1].replace(/T - N \* shift/g, 'sub(T, mul(N, shift))')
        .replace(/B - N \* shift/g, 'sub(B, mul(N, shift))').replace(/\bfloat[23]?\s+/g, 'let ');
    const dot = (a,b) => a.reduce((sum,x,i) => sum+x*b[i],0);
    const normalize = v => {const len=Math.sqrt(dot(v,v)); return v.map(x => x/len);};
    const mul = (v,n) => v.map(x => x*n), sub = (a,b) => a.map((x,i) => x-b[i]);
    const raw = new Function('T','B','N','H','rt','rb','shift','normalize','sub','mul','float3','dot','saturate','max',body);
    const lobe = (T,B,N,H,rt,rb,shift) => raw(T,B,N,H,rt,rb,shift,normalize,sub,mul,(...v)=>v,dot,x=>Math.max(0,Math.min(1,x)),Math.max);
    const T=[1,0,0], B=[0,1,0], N=[0,0,1];
    for (const [t,b,h,rt,rb,shift,want] of [
        [T,B,N,0.5,0.5,0,4],
        [T,B,N,0.5,0.25,0,8],
        [T,B,T,0.5,0.25,0,0.5],
        [T,B,B,0.5,0.25,0,0.03125],
        [B,[-1,0,0],T,0.5,0.25,0,0.03125],
        [T,B,N,0.5,0.5,1,0.16],
        [T,B,N,0,0.5,0,0],
    ]) {
        assert.ok(Math.abs(lobe(t,b,N,h,rt,rb,shift)-want)<1e-10,'oriented lobe fixture '+JSON.stringify([h,rt,rb,shift]));
    }
    const dual = anisotropy.match(/\bggx = [\s\S]*?;\s*ggx \+= [\s\S]*?;/);
    assert.ok(dual, 'missing two-lobe combination');
    const controls = ['_AnisotropyTangentWidth','_AnisotropyBitangentWidth','_AnisotropyShiftNoiseScale','_AnisotropyShift','_AnisotropySpecularStrength',
        '_Anisotropy2ndTangentWidth','_Anisotropy2ndBitangentWidth','_Anisotropy2ndShiftNoiseScale','_Anisotropy2ndShift','_Anisotropy2ndSpecularStrength'];
    const combine = new Function('s','N','H','rt','rb','noise','FairyToonAnisoLobe',...controls,'let ggx;\n'+dual[0]+'\nreturn ggx;');
    for (const [values,want] of [
        [[1,1,0,0,1,1,1,0,0,0],4],
        [[1,1,0,0,0.5,1,1,0,0,2],10],
        [[1,1,0,0,0,2,1,0,0,3],6],
        [[1,1,0,0,1,1,1,0,1,2],4.32],
    ]) {
        const actual = combine({anisoT:T,anisoB:B},N,N,0.5,0.5,0,lobe,...values);
        assert.ok(Math.abs(actual-want)<1e-10,'two independently weighted lobes');
    }
});
test('anisotropic toon boundary stays finite with disabled AA or zero derivatives', () => {
    const anisotropy = read('Shaders/FairyToonAnisotropy.hlsl');
    const helper = anisotropy.match(/float FairyToonAnisoToon\(float value,\s*float derivative\)\s*\{([^}]+)\}/);
    assert.ok(helper, 'missing guarded anisotropic toon threshold');
    const toon = new Function('value','derivative','saturate','max',helper[1]);
    const saturate = x => Math.max(0,Math.min(1,x));
    for (const [value,derivative,want] of [[0.4,0,0],[0.5,0,0],[0.6,0,1],
        [0.4,1e-9,0],[0.5,1e-9,0],[0.6,1e-9,1],[0.4,0.2,0],[0.5,0.2,0],[0.6,0.2,0.5],
        [0.4,2,0],[0.5,2,0],[0.6,2,0.1],[1.5,2,1]]) {
        const actual=toon(value,derivative,saturate,Math.max);
        assert.ok(Number.isFinite(actual),'finite toon boundary');
        assert.ok(Math.abs(actual-want)<1e-10,'guarded threshold fixture '+JSON.stringify([value,derivative]));
    }
    const core=read('Shaders/FairyToonCore.hlsl');
    assert.match(core,/FairyToonAnisoToon\([^;]*fwidth\([^)]*\)\s*\*\s*_AAStrength/,'anisotropic highlight uses tested safe threshold');
});

test('Rim, both MatCaps and Reflection match premultiplied blending in all modes', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const helper = core.match(/half3 FairyToonTransparentBlend\([^)]*\)\s*\{([\s\S]*?)\n\}/);
    assert.ok(helper, 'missing premultiplied-to-straight surface blend');
    const body = helper[1].split('#if defined(_SURFACE_TYPE_TRANSPARENT)')[1].split('#else')[0];
    const blend = new Function('col', 'layer', 'amount', 'mode', 'alpha', 'applyTransparency', 'max', 'clamp',
        body.replace(/\bfloat3\s+/g, 'let ').replace(/\(float3\)/g, ''));
    const run = (c,s,b,m,a,apply) => blend(c,s,b,m,a,apply,Math.max,(x,lo,hi)=>Math.min(hi,Math.max(lo,x)));
    const premult = (p,s,b,m) => p*(1-b)+[s,p+s,Math.max(p+s-p*s,p),p*s][m]*b;
    for (const a of [0,1e-10,1e-6,1/4096,0.2,0.5,1])
    for (const c of [0,0.3,2]) for (const s of [0,0.8,4])
    for (const b of [0,0.4,1]) for (const m of [0,1,2,3]) for (const apply of [0,1]) {
        const result = run(c,s,b,m,a,apply);
        assert.ok(Number.isFinite(result) && Math.abs(result)<=65504, 'finite half output');
        if (a === 0 || (!apply && a < 1/4096)) continue;
        const want = premult(c*a,s,b*(apply?a:1),m);
        assert.ok(Math.abs(result*a-want)<1e-10, 'premultiplied mode '+m+', alpha '+a+', apply '+apply);
        const next = run(result,1.7,0.6,(m+1)%4,a,apply);
        assert.ok(Math.abs(next*a-premult(want,1.7,0.6*(apply?a:1),(m+1)%4))<1e-10, 'sequential layers');
    }
    for (const m of [0,1,2,3]) for (const apply of [0,1]) {
        assert.ok(Math.abs(run(0.3,0.8,0.4,m,1,apply)-premult(0.3,0.8,0.4,m))<1e-10, 'opaque unchanged');
        assert.ok(Number.isFinite(run(65000,65000,65000,m,1e-10,apply)), 'HDR near zero fits half');
    }
    for (const [begin,end,expectedCalls] of [
        ['half3 FairyToonRim(', '// MatCap (',2],
        ['half3 FairyToonMatCapLayer(', 'half3 FairyToonMatCap(',1],
        ['half3 FairyToonReflection(', '// Emission',2],
    ]) {
        const caller = core.slice(core.indexOf(begin),core.indexOf(end));
        assert.equal((caller.match(/FairyToonTransparentBlend\(/g)||[]).length,expectedCalls,begin);
        assert.ok(!/\*= s\.alpha/.test(caller), 'caller must not pre-apply alpha');
    }
});

test('Backlight receives scene shadows only and Endfield supplies direct light', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const endfield = read('Shaders/FairyToonEndfield.hlsl');
    const body = core.match(/half3 FairyToonBacklight\([^)]*\)\s*\{([\s\S]*?)\n\}/)[1];
    // Toon (lilToon) path: raw scene attenuation, no _ShadowReceive, no character shadow.
    const toon = body.match(/#else\s+half attenuation = ([^;]+);/);
    assert.ok(toon, 'toon Backlight attenuation');
    assert.equal(toon[1].trim(), 'l.shadowAttenuation');
    // Endfield path: honours _ShadowReceive.
    const ef = body.match(/#if defined\(_FT_ENDFIELD\)\s+half attenuation = ([^;]+);/);
    assert.ok(ef, 'Endfield Backlight attenuation');
    const receive = new Function('l','_ShadowReceive','lerp','return '+ef[1]);
    for (const [weight,want] of [[0,1],[0.5,0.625],[1,0.25]]) {
        assert.equal(receive({shadowAttenuation:0.25},weight,(a,b,t)=>a+(b-a)*t),want);
    }
    assert.ok(!/charShadow/.test(body), 'Backlight must not use the character shadow');
    assert.match(body,/ln \*= saturate\(attenuation \+ distance\(l\.L, l\.origL\)\);/);
    const exported = endfield.slice(endfield.lastIndexOf('#if defined(_FT_BACKLIGHT)'),endfield.indexOf('return col;',endfield.lastIndexOf('#if defined(_FT_BACKLIGHT)')));
    assert.match(exported,/l\.backlightColor = lightColor \* day;/);
    assert.match(body,/color\.rgb \* l\.backlightColor/);
});

test('Endfield cloth gives anisotropic highlight ownership to lilToon Reflection', () => {
    const endfield = read('Shaders/FairyToonEndfield.hlsl');
    assert.ok(!endfield.includes('FairyToonAnisotropicGGX('), 'anisotropy must not also replace Endfield GGX');
    assert.match(endfield,/#if defined\(_FT_ANISOTROPY\) && defined\(_FT_REFLECTION\) && !defined\(_FT_EF_SKIN\)/);
    const gate = endfield.match(/if \(([^\n]+)\) specWeight = 0\.0;/);
    assert.ok(gate, 'suppress duplicate cloth/metal direct highlight only with active Reflection specular');
    const owns = new Function('_ApplySpecular','FairyToonAnisotropyReflection','return '+gate[1]);
    for (const reflection of [0,1]) for (const spec of [0,1]) for (const aniso of [false,true]) {
        assert.equal(Boolean(reflection && owns(spec,()=>aniso)),Boolean(reflection&&spec&&aniso));
    }
});


test('UV0 and extra-UV feature alternatives are exclusive and follow actual material selectors', () => {
    for (const keyword of ['_FT_NORMAL2ND','_FT_GLITTER']) {
        assert.ok(!shader.includes('[Toggle('+keyword+')]'), 'property drawer must not enable a second group option');
        const count = keyword === '_FT_NORMAL2ND' ? 3 : 2;
        assert.equal((shader.match(new RegExp('#pragma shader_feature_local _ '+keyword+' '+keyword+'_UV\\b','g'))||[]).length,count);
    }
    const block = setup.slice(setup.indexOf('            bool normal2nd ='),setup.indexOf('            SetKeyword(material, "_FT_SHADOW_RAMP"'));
    assert.ok(block.includes('bool normal2ndUV'), 'material setup must derive UV layout');
    const run = new Function('material','On','Get','SetKeyword',block.replace(/\bbool\b/g,'let').replace(/(\d+\.\d+)f/g,'$1'));
    for (const enabled of [false,true]) for (const texture of [false,true])
    for (const normalUV of [0,1,2,3]) for (const particleUV of [0,1]) for (const colorUV of [0,1,2,3]) {
        const flags = {};
        const values = {_Bump2ndMap_UVMode:normalUV,_GlitterUVMode:particleUV,_GlitterColorTex_UVMode:colorUV};
        run({GetTexture:()=>texture?{}:null},()=>enabled,(_,name)=>values[name],(_,name,on)=>flags[name]=on);
        assert.equal(flags._FT_NORMAL2ND,enabled&&texture&&normalUV===0);
        assert.equal(flags._FT_NORMAL2ND_UV,enabled&&texture&&normalUV!==0);
        assert.equal(flags._FT_GLITTER,enabled&&particleUV===0&&colorUV===0);
        assert.equal(flags._FT_GLITTER_UV,enabled&&(particleUV!==0||colorUV!==0));
    }
});


test('custom cubemap tint and lighting match lilToon, including the dark Sleeve case', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_macro.hlsl');
    const name = '_ReflectionCubeEnableLighting';
    assert.equal(current.get(name)?.value, reference.get(name).value, name + ' default');
    assert.ok(input.includes(name + ';'));
    assert.match(lil, /_ReflectionCubeColor\.rgb \* lerp\(1\.0, fd\.lightColor, _ReflectionCubeEnableLighting\)/);
    const override = core.slice(core.indexOf('if (FairyToonUseReflectionCube())'), core.indexOf('half oneMinusReflectivity'));
    const multiply = override.match(/env \*= ([^;]+);/);
    assert.ok(multiply, 'custom cube must receive its tint and lighting multiplier');
    assert.ok(override.indexOf(multiply[0]) < override.indexOf('else'), 'probe lighting must stay unchanged');
    const run = new Function('env', '_ReflectionCubeColor', 'l', '_ReflectionCubeEnableLighting', 'lerp',
        'return env * (' + multiply[1].replace(/_ReflectionCubeColor\.rgb/g, '_ReflectionCubeColor') + ');');
    const lerp = (a,b,t) => a+(b-a)*t;
    for (const sample of [0,0.5,4]) for (const tint of [0,0.25,1,2])
    for (const light of [0.05,0.3,1]) for (const enabled of [0,0.5,1]) {
        assert.ok(Math.abs(run(sample,tint,{lightColor:light},enabled,lerp) -
            sample*tint*((1-enabled)+enabled*light)) < 1e-12);
    }
    assert.ok(Math.abs(run(0.5,1,{lightColor:0.05},1,lerp)-0.025)<1e-12, 'Sleeve: 20x dimmer at the light floor');
});

test('two-sided mapped normals flip before lighting, MatCaps and anisotropy', () => {
    const normals = read('Shaders/FairyToonNormals.hlsl');
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_pass_forward_normal.hlsl');
    assert.equal(current.get('_FlipNormal')?.value, reference.get('_FlipNormal').value);
    assert.ok(input.includes('_FlipNormal;'));
    assert.match(lil, /fd\.N = fd\.facing < \(_FlipNormal-1\.0\) \? -fd\.N : fd\.N;/);
    const helper = normals.match(/float3 FairyToonFacingNormal\([^)]*\)\s*\{\s*return ([^;]+);/);
    assert.ok(helper, 'missing facing normal helper');
    const run = new Function('normal', 'facing', '_FlipNormal', 'return '+helper[1]);
    for (const flip of [0,1]) for (const facing of [-1,1]) for (const n of [-0.8,0.6])
        assert.equal(run(n,facing,flip), flip && facing < 0 ? -n : n);
    const forward = hlslBody(passes, 'FairyToonForwardSurface');
    assert.match(forward, /s\.N = FairyToonFacingNormal\(s\.N, s\.facing\);/);
    assert.ok(forward.indexOf('s.N = FairyToonFacingNormal') < forward.indexOf('FairyToonSetupAnisotropy'));
    assert.ok(!/s\.origN\s*=\s*FairyToonFacingNormal/.test(forward), 'origN stays geometric like lilToon');
    const depth = passes.slice(passes.indexOf('void FairyToonDepthNormalsFragment('));
    assert.match(depth, /normalWS \*= IS_FRONT_VFACE\(isFront, 1\.0, -1\.0\);/);
});

test('MatCap RGB masks and reflection RGBA maps apply their own ST after uvMain', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    for (const name of ['_MatCapBlendMask','_MatCap2ndBlendMask','_SmoothnessTex','_MetallicGlossMap','_ReflectionColorTex']) {
        assert.ok(input.includes(name+'_ST;'), name+' ST in unconditional material buffer');
        assert.ok(lil.includes('LIL_SAMPLE_2D_ST('+name+', samp, fd.uvMain)'), name+' reference UV');
    }
    assert.match(core, /SAMPLE_TEXTURE2D\(mask, sampler_MainTex, s\.uvMain \* maskST\.xy \+ maskST\.zw\)\.rgb/);
    for (const name of ['_MatCapBlendMask','_MatCap2ndBlendMask']) assert.ok(core.includes(name+'_ST,'));
    for (const [name,channel] of [['_SmoothnessTex','.r'],['_MetallicGlossMap','.r'],['_ReflectionColorTex','']])
        assert.ok(core.includes('SAMPLE_TEXTURE2D('+name+', sampler_MainTex, s.uvMain * '+name+'_ST.xy + '+name+'_ST.zw)'+channel));
    // sRGB decoding is already done by texture sampling; never decode sampled masks a second time.
    const surface = core.slice(core.indexOf('half3 FairyToonMatCapLayer('),core.indexOf('// Emission'));
    assert.ok(!/SRGBToLinear|LinearToSRGB/.test(surface));
});

test('third toon shadow preserves lilToon defaults, blue masks and blend order', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    for (const name of ['_Shadow3rdColor','_Shadow3rdColorTex','_Shadow3rdNormalStrength','_Shadow3rdBorder',
        '_Shadow3rdBlur','_Shadow3rdReceive','_ShadowAOShift2']) {
        assert.equal(current.get(name)?.value,reference.get(name).value,name+' default');
        assert.ok(input.includes(name+(name.endsWith('Tex')?');':';')),name+' input');
    }
    const diffuse = core.slice(core.indexOf('half3 FairyToonDiffuse('),core.indexOf('// Additional lights'));
    assert.ok(diffuse.includes('_Shadow3rdBlur * blurMask.b'));
    assert.ok(diffuse.includes('borderMask.b * _ShadowAOShift2.x + _ShadowAOShift2.y'));
    assert.ok(diffuse.includes('lns.z = FairyToonStep(aa, lns.z, _Shadow3rdBorder, blur3)'));
    assert.ok(diffuse.includes('lns *= backface;'));
    const blend = diffuse.match(/indirectCol = lerp\(indirectCol, shadow3rd, ([^;]+)\);/);
    assert.ok(blend, 'third shadow missing');
    const amount = new Function('_Shadow3rdColor','lns','return '+blend[1]);
    for(const alpha of [0,0.25,1]) for(const z of [0,0.3,1]) {
        assert.ok(Math.abs(amount({a:alpha},{z})-alpha*(1-z))<1e-12);
    }
    const srgb = x => x <= 0.04045 ? x/12.92 : Math.pow((x+0.055)/1.055,2.4);
    const second = srgb(0.6499941), third = srgb(0.34299853);
    const result = second+(third-second)*amount({a:0.25},{z:0});
    assert.ok(Math.abs(result-0.3091272)<1e-7, 'Sleeve fully shadowed red coefficient');
    assert.ok(diffuse.indexOf(blend[0]) > diffuse.indexOf('indirectCol = lerp(indirectCol, shadow2nd'));
    assert.ok(diffuse.indexOf(blend[0]) < diffuse.indexOf('indirectCol = lerp(indirectCol, indirectCol * albedo'));
});

test('Toon and Endfield specular both receive FairyToon shadowmix', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const reflection = hlslBody(core, 'FairyToonReflection');
    assert.doesNotMatch(reflection, /\bSHADOWS_SCREEN\b/);
    assert.doesNotMatch(reflection, /specularAttenuation\s*=\s*1\.0/, 'isotropic Toon must not reset attenuation');
    const attenuation = reflection.match(/half specularAttenuation\s*=\s*l\.shadowmix\s*;/)?.[0];
    assert.ok(attenuation, 'both models use FairyToon shadow control');
    assert.equal([...reflection.matchAll(/\bspecularAttenuation\s*=/g)].length, 1, 'no model or specular branch may reset attenuation');
    for (const ef of [false, true]) for (const scene of [0, 0.25, 1]) for (const artistic of [0, 0.6, 1]) {
        const actual = scalarHlsl(attenuation + '\nreturn specularAttenuation;', ef ? ['_FT_ENDFIELD'] : [],
            { l: { shadowAttenuation: scene, shadowmix: artistic } });
        assert.equal(actual, artistic);
    }
    assert.match(hlslBody(core, 'FairyToonGetLight'), /l\.shadowAttenuation = mainLight\.shadowAttenuation/);
    assert.match(reflection, /term \*= saturate\(dot\(N, l\.L\)\) \* specularAttenuation/);
    assert.match(reflection, /spec \*= specularAttenuation;/);
});


test('DepthNormals always orients backfaces independently of FlipNormal and keeps both packing formats', () => {
    const depth = hlslBody(read('Shaders/FairyToonPasses.hlsl'), 'FairyToonDepthNormalsFragment');
    assert.doesNotMatch(depth.replace(/\/\/[^\n]*/g, ''), /FairyToonFacingNormal|_FlipNormal|_ShadingModel/);
    const orient = depth.match(/normalWS (?:\*=|=) ([^;]*IS_FRONT_VFACE[^;]*);/)?.[0];
    assert.ok(orient, 'DepthNormals must use the fragment facing sign');
    const run = new Function('normalWS', 'isFront', 'IS_FRONT_VFACE', orient + '\nreturn normalWS;');
    for (const front of [false, true]) for (const normal of [-1, -0.6, 0.8])
        assert.equal(run(normal, front, (f, a, b) => f ? a : b), front ? normal : -normal);
    assert.match(depth, /PackNormalOctQuadEncode\(normalize\(normalWS\)\)/);
    assert.match(depth, /PackFloat2To888\(saturate\(octNormalWS \* 0\.5 \+ 0\.5\)\)/);
    assert.match(depth, /half4\(NormalizeNormalPerPixel\(normalWS\), 0\.0\)/);
    assert.doesNotMatch(depth, /PackNormalOctRectEncode/);
});

test('Endfield Face retains the oriented geometric normal while other lighting uses mapped N', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const endfield = read('Shaders/FairyToonEndfield.hlsl');
    const shade = hlslBody(core, 'FairyToonShadeBase');
    assert.doesNotMatch(shade, /s\.origN\s*=\s*s\.N/);
    assert.match(shade, /s\.origN = s\.facing < 0\.0 \? -s\.origN : s\.origN/);
    assert.match(endfield, /bool useBackfaceNormal = s\.facing < 0\.0;/);
    assert.match(endfield, /#if defined\(_FT_EF_FACE\)[\s\S]*?N = s\.origN;/);
    assert.match(endfield, /if \(useBackfaceNormal\)\s*\{\s*NoL = meshNoL;\s*faceMeshBlend = 1\.0;\s*\}/);
    for (const term of ['dot(N, s.V)', 'dot(N, L)', 'dot(N, viewLightDir)', 'dot(N, H)',
        'FairyToonCharShadowFaded(s.positionWS, N)', 'FairyToonEFAmbient(N, s)',
        'FairyToonEFTopLight(N, character)', 'reflect(-s.V, N)', 'FairyToonEFDepthEdge(s, N)',
        'FairyToonEFNormalize(N.xz)', 'FairyToonEFSmallLights(col, s, N,'])
        assert.ok(endfield.includes(term), term);
    const expression = shade.match(/s\.origN = ([^;]+);/)?.[1];
    assert.ok(expression);
    const orient = new Function('s', 'return ' + expression);
    for (const facing of [-1, 1]) for (const origN of [-0.7, 0.9])
        assert.equal(orient({ facing, origN, N: 0.123 }), facing < 0 ? -origN : origN, 'Face must not inherit mapped N');
});

test('Endfield force shadow covers view ramp and eye bypasses without multiplying final RGB', () => {
    const endfield = read('Shaders/FairyToonEndfield.hlsl');
    const factor = endfield.match(/half backface = ([^;]+);/)[1];
    const runFactor = new Function('s','_BackfaceForceShadow','return '+factor);
    assert.match(endfield,/half lit = min\(min\(selfShadow, aoLit\), ramp\.a\) \* backface;/);
    assert.match(endfield,/if \(s\.facing < 0\) eyeLit \*= backface;/);
    assert.ok(endfield.indexOf('eyeLit *= backface') < endfield.indexOf('lit = lerp(lit, eyeLit, eye)'));
    const gated = endfield.match(/if \(s\.facing < 0\)\s*\{\s*viewLit \*= backface;\s*ramp\.a \*= backface;\s*\}/);
    assert.ok(gated, 'force camera ramp and deeper tone only on backs');
    assert.ok(gated.index > endfield.indexOf('viewLit = lerp(viewLit, 1.0, eye)'));
    const run = new Function('s','backface','viewLit','ramp',gated[0]+'; return [viewLit,ramp.a];');
    for (const facing of [-1,1]) for (const force of [0,0.25,1]) {
        const factor = runFactor({facing},force);
        const expected = facing < 0 ? 1-force : 1;
        assert.equal(factor,expected);
        assert.deepEqual(run({facing},factor,0.8,{a:0.6}),[0.8*expected,0.6*expected]);
    }
    assert.doesNotMatch(endfield, /(?:col|diffuseLit) \*= backface/);
});

test('BackfaceColor matches lilToon after emission in both models and leaves alpha untouched', () => {
    assert.deepEqual(current.get('_BackfaceColor'), {type:'Color',value:reference.get('_BackfaceColor').value});
    assert.match(shader,/\[HDR\] _BackfaceColor/);
    assert.match(input,/half4\s+_BackfaceColor;/);
    const core = read('Shaders/FairyToonCore.hlsl');
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_pass_forward_normal.hlsl');
    assert.match(lil,/lerp\(fd.col.rgb, _BackfaceColor.rgb \* fd.lightColor, _BackfaceColor.a\)/);
    const helper = core.match(/half3 FairyToonBackfaceColor\([^)]*\)\s*\{([\s\S]*?)\n\}/);
    assert.ok(helper, 'missing backface RGB override');
    assert.doesNotMatch(helper[1],/s\.alpha\s*=|s\.albedo|s\.origN/);
    const run = new Function('s','l','col','_BackfaceColor','FairyToonTransparentBlend',helper[1]);
    const lerp = (a,b,t) => a+(b-a)*t;
    for (const facing of [-1,1]) for (const alpha of [0,0.25,1]) for (const col of [0,0.3,2]) {
        const result = run({facing},{lightColor:0.7},col,{rgb:1.5,a:alpha},lerp);
        assert.equal(result,facing < 0 && alpha !== 0 ? lerp(col,1.5*0.7,alpha) : col);
    }
    const shade = core.slice(core.indexOf('half3 FairyToonShade('));
    assert.ok(shade.indexOf('return FairyToonBackfaceColor(s, l, col);') > shade.indexOf('FairyToonEmission(s, l, col)'));
    assert.ok(read('Editor/FairyToonGUI.cs').includes('P("_BackfaceColor", "Color")'));
});

test('transparent BackfaceColor matches post-premultiply lilToon RGB without changing coverage', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const helper = core.match(/half3 FairyToonBackfaceColor\([^)]*\)\s*\{([\s\S]*?)\n\}/)[1];
    assert.match(helper,/FairyToonTransparentBlend\(col, _BackfaceColor.rgb \* l.lightColor, _BackfaceColor.a, 0.0, s.alpha, 0.0\)/);
    const transparent = core.match(/half3 FairyToonTransparentBlend\([^)]*\)\s*\{([\s\S]*?)\n\}/)[1]
        .split('#if defined(_SURFACE_TYPE_TRANSPARENT)')[1].split('#else')[0];
    const blend = new Function('col','layer','amount','mode','alpha','applyTransparency','max','clamp',
        transparent.replace(/\bfloat3\s+/g,'let ').replace(/\(float3\)/g,''));
    const bounded = (...args) => blend(...args,Math.max,(x,lo,hi)=>Math.min(hi,Math.max(lo,x)));
    const run = new Function('s','l','col','_BackfaceColor','FairyToonTransparentBlend',helper);
    for (const facing of [-1,1]) for (const alpha of [0,1e-10,1/4096,0.2,0.7,1])
    for (const amount of [0,0.3,1]) for (const col of [0,0.5,2]) {
        const surface = {facing,alpha};
        const result = run(surface,{lightColor:0.7},col,{rgb:1.5,a:amount},bounded);
        assert.equal(surface.alpha,alpha,'coverage must be unchanged');
        assert.ok(Number.isFinite(result) && Math.abs(result)<=65504);
        if (facing > 0 || amount === 0) assert.equal(result,col,'front and disabled are exact passthrough');
        else if (alpha >= 1/4096)
            assert.ok(Math.abs(result*alpha - (col*alpha*(1-amount)+1.5*0.7*amount))<1e-10);
        else if (alpha === 0) assert.equal(result*alpha,0,'finite SrcAlpha cannot reproduce nonzero light at zero alpha');
    }
});

test('reflection Gamma flags preserve lilToon F0 and metallic uploads', () => {
    const lil = read('../jp.lilxyzw.liltoon/CustomShaderResources/Properties/Default.lilblock');
    const srgb = x => x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4;
    function upload(source, name, saved, linear) {
        const line = source.split('\n').find(l => new RegExp('\\b'+name+'\\s*\\(').test(l));
        assert.ok(line, name);
        return linear && line.includes('[Gamma]') ? srgb(saved) : saved;
    }
    for (const name of ['_Reflectance','_Metallic'])
        for (const value of [0,0.04,0.076,0.5,1]) for (const linear of [false,true])
            assert.equal(upload(shader,name,value,linear),upload(lil,name,value,linear),name);
    assert.ok(Math.abs(upload(shader,'_Reflectance',0.076,true)-0.006693375758)<1e-12);
    const reduction = 1 / (1 + (1-0.337)**4);
    assert.ok(Math.abs(upload(shader,'_Reflectance',0.076,true)*reduction-0.005609502587)<1e-12);
    const core = read('Shaders/FairyToonCore.hlsl');
    assert.match(core, /specular = lerp\(_Reflectance, s\.emissionAlbedo, metallic\)/);
    assert.ok(!/(?:SRGBToLinear|GammaToLinearSpace)\(_(?:Reflectance|Metallic)\)/.test(core), 'no double decode');
});

test('GSAA uses the larger derivative length, including strength zero', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const body = core.match(/float FairyToonReflectionSmoothness\([^)]*\)\s*\{([\s\S]*?)\n\}/)?.[1];
    assert.ok(body, 'GSAA helper');
    const run = new Function('smoothness','N','strength','ddx','ddy','dot','max','min','saturate',
        body.replace(/\bfloat[23]?\s+/g,'let '));
    const dot = (a,b) => a.reduce((s,v,i)=>s+v*b[i],0), sat=x=>Math.max(0,Math.min(1,x));
    for (const strength of [0,1,2]) for (const s of [0.337,0.99,1]) {
        const dx=[0.02,0,0],dy=[0,0.02,0];
        const want=Math.min(s,sat(1-Math.max(0,0.0004/(0.0004*5+0.002)*strength)));
        assert.ok(Math.abs(run(s,null,strength,()=>dx,()=>dy,dot,Math.max,Math.min,sat)-want)<1e-12);
    }
    assert.match(core, /smoothness = FairyToonReflectionSmoothness\(smoothness, s\.N, _GSAAStrength\)/);
});

test('GGX retains lilToon epsilon with a shared finite roughness floor', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const body = core.match(/float FairyToonReflectionGGX\([^)]*\)\s*\{([\s\S]*?)\n\}/)?.[1];
    assert.ok(body, 'GGX helper');
    const run=new Function('roughness','nv','nl','nh','max',body.replace(/\bfloat\s+/g,'let '));
    for(const r of [0,0.001,0.439569,1]) for(const nv of [0,0.4,1]) for(const nl of [0,0.6,1]) for(const nh of [0,0.8,1]) {
        const a=Math.max(r,0.002),a2=a*a,d=(nh*a2-nh)*nh+1;
        const want=0.5/(nl*(nv*(1-a)+a)+nv*(nl*(1-a)+a)+1e-5)*a2/(d*d+1e-7);
        assert.ok(Math.abs(run(r,nv,nl,nh,Math.max)-want)<Math.max(1,Math.abs(want))*1e-10);
    }
    assert.match(core, /float roughness = max\(0\.002, perceptualRoughness \* perceptualRoughness\);/);
    assert.match(core, /FairyToonReflectionGGX\(roughness, nv, nl, nh\)/);
    assert.match(core, /UNITY_COLORSPACE_GAMMA[\s\S]*?sqrt\(max\(1e-4, term\)\)/);
    assert.match(core, /UNITY_COLORSPACE_GAMMA[\s\S]*?1\.0 - 0\.28 \* roughness \* perceptualRoughness/);
});

test('environment ray keeps unnormalized normal blend but Fresnel uses mapped N', () => {
    const core=read('Shaders/FairyToonCore.hlsl');
    const env=core.slice(core.indexOf('if (_ApplyReflection > 0.5)'),core.indexOf('// Emission'));
    assert.match(env,/float3 N = lerp\(s\.origN, reflectionN, _ReflectionNormalStrength\);/);
    assert.match(env,/float nv = saturate\(dot\(s\.N, s\.V\)\);/);
    assert.ok(!/nv = saturate\(dot\(N, s\.V\)\)/.test(env));
    const lil=read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    assert.match(lil,/lilFresnelLerp\(specular, grazingTerm, fd\.nv\)/);
    const lerp=(a,b,t)=>a+(b-a)*t;
    const n=[lerp(0,0.6,0.5),0,lerp(1,0.8,0.5)];
    const ray=[2*n[2]*n[0],0,-1+2*n[2]*n[2]];
    assert.ok(Math.abs(ray[0]-0.54)<1e-12 && Math.abs(ray[2]-0.62)<1e-12);
    // Flipping N changes mapped nv, although reflect(-V,+N) == reflect(-V,-N).
    const f0=0.006693375758,f90=0.377;
    assert.ok(lerp(f0,f90,(1-0.8)**5)<0.007);
    assert.equal(lerp(f0,f90,(1-0)**5),f90);
});

test('custom cube uses lilToon trilinear mip and decode in both Toon and Endfield', () => {
    const core=read('Shaders/FairyToonCore.hlsl');
    const lil=read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_input.hlsl');
    assert.match(lil,/#define lil_sampler_linear_repeat\s+lil_sampler_trilinear_repeat/);
    assert.match(input,/SAMPLER\(sampler_ft_trilinear_repeat\)/);
    assert.match(core,/SAMPLE_TEXTURECUBE_LOD\(_ReflectionCubeTex, sampler_ft_trilinear_repeat, R, mip\)/);
    assert.match(core,/float mip = perceptualRoughness \* \(10\.2 - 4\.2 \* perceptualRoughness\);/);
    assert.match(core,/FairyToonDecodeReflectionCube\([\s\S]*?_ReflectionCubeTex_HDR\)/);
    const decode=core.match(/float3 FairyToonDecodeReflectionCube\([^)]*\)\s*\{([\s\S]*?)\n\}/)?.[1];
    assert.ok(decode);
    const alphaExpr=decode.match(/float alpha = ([^;]+);/)[1];
    const branches=[...decode.matchAll(/return ([^;]+);/g)].map(m=>m[1]);
    assert.equal(branches.length,3,'Gamma, native HDR, encoded HDR');
    const alpha=new Function('data','hdr','return '+alphaExpr);
    const evaluate=branches.map(b=>new Function('data','hdr','alpha','pow','abs','return '+b));
    for(const a of [0.25,1]) {
        const data={a,rgb:0.5},hdr={x:2,y:2.2,w:1},v=alpha(data,hdr);
        for(const [i,want] of [a,1,a**2.2].entries())
            assert.ok(Math.abs(evaluate[i](data,hdr,v,Math.pow,Math.abs)-want)<1e-12);
    }
    assert.match(core,/env = FairyToonSampleReflectionCube\(R, perceptualRoughness\);/);
    assert.match(read('Shaders/FairyToonEndfield.hlsl'),/FairyToonSampleReflectionCube\(R, e\.perceptualRoughness\) \* ambient/);
    assert.match(core,/unity_SpecCube0\.GetDimensions/);
    assert.match(core,/width < 15 \|\| unity_SpecCube0_HDR\.x == 0/);
});

test('Toon primary normals use lilToon decoding while Endfield preserves authored strength', () => {
    const normals=read('Shaders/FairyToonNormals.hlsl');
    const sample=normals.slice(normals.indexOf('half3 FairyToonNormalTS('));
    assert.match(sample,/normalTS = legacyPrimary \? UnpackNormalScale\(packed, _BumpScale\) : FairyToonUnpackLayerNormal\(packed, _BumpScale\);/);
    const selectors=[...sample.matchAll(/bool legacyPrimary = ([^;]+);/g)].map(m=>m[1]);
    assert.equal(selectors.length,2,'second-normal and primary-only variants');
    const run=selectors.map(expr=>new Function('_ShadingModel','_UseBump2ndMap','return '+expr));
    for(const model of [0,1,2,3,4,5]) for(const second of [0,1]) {
        assert.equal(run[0](model,second),model>0 && !second);
        assert.equal(run[1](model,second),model>0);
    }
    assert.ok(!/^\s*#if.*_FT_ENDFIELD/m.test(sample),'DepthNormals lacks the model keyword; use shared material selector');
    assert.match(normals,/normalTex\.a \*= normalTex\.r/,'BC5 and DXT5nm share A*R decoding');
    const zBody=normals.match(/float FairyToonNormalZ\(float2 xy\)\s*\{([^}]+)\}/)[1];
    const z=new Function('xy','sqrt','saturate','dot',zBody);
    for(const scale of [0,0.5,1,2]) {
        const xy=[0.6*scale,0],want=Math.sqrt(Math.max(0,1-0.36*scale*scale));
        assert.ok(Math.abs(z(xy,Math.sqrt,x=>Math.max(0,Math.min(1,x)),(a,b)=>a[0]*b[0]+a[1]*b[1])-want)<1e-12);
    }
});

test('Toon orthographic view matches lilToon and Endfield keeps URP view', () => {
    const passes=read('Shaders/FairyToonPasses.hlsl');
    assert.match(passes,/#if defined\(_FT_ENDFIELD\)\s+s\.V = GetWorldSpaceNormalizeViewDir\(input\.positionWS\);\s+#else[\s\S]*?s\.V = normalize\(GetCameraPositionWS\(\) - input\.positionWS\);\s+#endif/);
});

test('Gamma grazing reflectivity uses lilToon dielectric constant instead of URP linear constant', () => {
    const core=read('Shaders/FairyToonCore.hlsl');
    const lil=read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_macro.hlsl');
    assert.match(lil,/LIL_DIELECTRIC_SPECULAR float4\(0\.220916301/);
    assert.match(core,/#if defined\(UNITY_COLORSPACE_GAMMA\)\s+half oneMinusReflectivity = \(1\.0 - 0\.220916301\) \* \(1\.0 - metallic\);/);
    assert.match(core,/#else\s+half oneMinusReflectivity = kDielectricSpec\.a - metallic \* kDielectricSpec\.a;/);
    for(const m of [0,0.5,1]) {
        const grazing=Math.min(1,0.337+1-(1-0.220916301)*(1-m));
        assert.ok(grazing>=0.557916301-1e-12 && grazing<=1);
    }
});

test('clustered probes stay selected when the legacy cube is empty, with explicit custom-cube override', () => {
    const selector = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonUseReflectionCube')
        .replace(/uint width, height, levels;/, '')
        .replace(/unity_SpecCube0\.GetDimensions\([^;]+;/, '');
    for (const endfield of [false, true]) for (const clustered of [false, true])
    for (const override of [0, 1]) for (const width of [1, 128]) for (const hdr of [0, 1]) {
        const defines = [...(endfield ? ['_FT_ENDFIELD'] : []), ...(clustered ? ['USE_CLUSTER_LIGHT_LOOP'] : [])];
        const result = scalarHlsl(selector, defines, { _ReflectionCubeOverride: override, width, unity_SpecCube0_HDR: { x: hdr } });
        assert.equal(result, !!override || (!endfield && !clustered && (width < 15 || hdr === 0)),
            JSON.stringify({ endfield, clustered, override, width, hdr }));
    }
});


test('all Forward surfaces share mapped-normal setup with explicit Toon, Endfield, Gem and Fur facing behavior', () => {
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const surface = hlslBody(passes, 'FairyToonForwardSurface');
    const index = surface.indexOf('if (s.facing < 0.0) s.N = normalize(-');
    assert.ok(index > 0);
    const facing = surface.slice(surface.lastIndexOf('#if ', index), surface.indexOf('#endif', index) + 6) + '\nreturn s.N;';
    const orient = new Function('normal', 'facing', '_FlipNormal', hlslBody(read('Shaders/FairyToonNormals.hlsl'), 'FairyToonFacingNormal'));
    for (const model of ['Toon', 'Endfield']) for (const variant of ['', 'FAIRYTOON_REFRACTION', 'FAIRYTOON_GEM', 'FAIRYTOON_FUR_INCLUDED'])
    for (const flip of [0, 1]) for (const front of [-1, 1]) {
        const s = { N: 0.6, origN: 0.9, V: 1, facing: front };
        const actual = scalarHlsl(facing, [variant, ...(model === 'Endfield' ? ['_FT_ENDFIELD'] : [])],
            { s, normalize: n => n, FairyToonFacingNormal: (n, f) => orient(n, f, flip) });
        const expected = front > 0 ? 0.6 : variant === 'FAIRYTOON_GEM' ? -0.8 : model === 'Endfield' ? -0.6 : variant === 'FAIRYTOON_FUR_INCLUDED' ? 0.6 : flip ? -0.6 : 0.6;
        assert.equal(actual, expected, JSON.stringify({ model, variant, flip, front }));
        assert.equal(s.origN, 0.9, 'surface keeps geometric normal distinct from mapped N');
    }
    for (const [source, entry] of [[passes, 'FairyToonForwardFragment'], [optics, 'FairyToonVariantFragment'],
        [read('Shaders/FairyToonFur.hlsl'), 'FairyToonFurFragment']]) {
        const body = hlslBody(source, entry);
        assert.equal((body.match(/FairyToonForwardSurface\(/g) || []).length, 1);
        assert.doesNotMatch(body, /FairyToonNormalTS\(|FairyToonSetupAnisotropy\(|FairyToonLayeredBase\(/, 'no duplicated surface pipeline');
    }
    assert.ok(surface.indexOf('FairyToonNormalTS(') < index);
    assert.ok(index < surface.indexOf('FairyToonSetupAnisotropy('));
    assert.ok(surface.indexOf('FairyToonSetupAnisotropy(') < surface.indexOf('FairyToonLayeredBase('));
});

test('Gem environment reuses corrected cube sampling and color-space roughness', () => {
    const optics = read('Shaders/FairyToonVariants.hlsl');
    const env = hlslBody(optics, 'FairyToonGemEnvironment');
    assert.match(env, /FairyToonUseReflectionCube\(\)/);
    assert.match(env, /FairyToonSampleReflectionCube\(R, roughness\)/);
    assert.ok(!/DecodeHDREnvironment|sampler_ReflectionCubeTex/.test(env));
    const gem = hlslBody(optics, 'FairyToonGemOptics');
    assert.match(gem, /UNITY_COLORSPACE_GAMMA[\s\S]*?0\.220916301/);
    assert.match(gem, /1\.0 - 0\.28 \* roughness \* perceptualRoughness/);
});


test('optical smoothness applies the reflection transform for Refraction and retains Gem main UV', () => {
    const body = hlslBody(read('Shaders/FairyToonVariants.hlsl'), 'FairyToonOpticalSmoothness')
        .replace(/SAMPLE_TEXTURE2D\([^)]*\)\.r/g, 'sample(uv)');
    for (const variant of ['FAIRYTOON_REFRACTION', 'FAIRYTOON_GEM']) {
        let sampled;
        const value = scalarHlsl(body, [variant], { s: { uvMain: 0.3 }, _Smoothness: 0.8,
            _SmoothnessTex_ST: { xy: 2, zw: 0.1 }, sample: uv => { sampled = uv; return 0.5; } });
        assert.equal(sampled, variant === 'FAIRYTOON_REFRACTION' ? 0.7 : 0.3);
        assert.equal(value, 0.4);
    }
});

test('Refraction backface color runs last with framebuffer alpha one while Gem retains its own stack', () => {
    const layers = hlslBody(read('Shaders/FairyToonVariants.hlsl'), 'FairyToonOpticalLayers');
    assert.ok(layers.lastIndexOf('FairyToonBackfaceColor(') > layers.lastIndexOf('FairyToonEmissionLayer('));
    for (const gem of [false, true]) for (const alpha of [0, 0.2, 1]) for (const facing of [-1, 1]) {
        let calls = 0;
        const value = scalarHlsl(layers, [gem ? 'FAIRYTOON_GEM' : 'FAIRYTOON_REFRACTION'], {
            s: { alpha, facing, dissolveEdge: 0 }, l: { lightColor: 0.6 }, col: 0.8, coverage: alpha, _DissolveColor: {rgb: 0},
            FairyToonBackfaceColor: (surface, light, col) => {
                calls++; assert.equal(surface.alpha, 1);
                return surface.facing < 0 ? col * 0.75 + 0.4 * light.lightColor * 0.25 : col;
            },
        });
        assert.equal(calls, gem ? 0 : 1);
        assert.equal(value, !gem && facing < 0 ? 0.8 * 0.75 + 0.4 * 0.6 * 0.25 : 0.8);
    }
});


test('Apply preserves inherited and explicit queues across coverage, optical and Fur modes', () => {
    const modes = [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0], [4, 1], [4, 2], [5, 0]];
    for (const [mode, fur] of modes) {
        assert.equal(renderState(mode, fur, 1, -1).material.rawRenderQueue, -1);
        for (const queue of [2000, 2450, 2460, 2461, 2462, 2463, 2464, 2465, 2466, 2500, 2501, 2600, 2900, 3000, 3100]) {
            const first = renderState(mode, fur, 1, queue).material.renderQueue;
            assert.equal(first, queue, JSON.stringify({ mode, fur, queue }));
            assert.equal(renderState(mode, fur, 1, first).material.renderQueue, first, 'repeated Apply is idempotent');
        }
    }
    for (const variant of ['Refraction', 'Gem'])
        assert.match(read('Shaders/FairyToon' + variant + '.shader'), /"Queue"\s*=\s*"Transparent-100"/);
    assert.match(converter, /target\.renderQueue = snapshot\.renderQueue;/);
    assert.match(converter, /target\.SetOverrideTag\(FairyToonMaterialSetup\.ManagedHairQueueTag, "0"\)/,
        'converted lilToon queues explicitly retain authored provenance');
});


function setFurShellDefaults(material, furMode) {
    const body = csharpBody('SetFurShellDefaults').replace(/\bbool\s+/g, 'let ').replace(/\(int\)|(?<=\d)f\b/g, '');
    const blend = { One: 1, Zero: 0, SrcAlpha: 5, OneMinusSrcAlpha: 10 };
    new Function('material', 'furMode', 'BlendMode', ...Object.keys(renderConstants), body)(
        material, furMode, blend, ...Object.values(renderConstants));
}

// Execute both public transition methods with the same queue effects as Unity's Material API.
function queueTransition(material, selected, fur = false) {
    const shaderNames = ['FairyToon', 'FairyToon', 'FairyToon', 'FairyToon/Refraction', 'FairyToon/Fur', 'FairyToon/Gem'];
    const shaders = Object.fromEntries(shaderNames.map(name => [name, { name }]));
    const modeOf = m => m.shader.name === 'FairyToon' ? m.values._TransparentMode : shaderNames.indexOf(m.shader.name);
    const bindings = { ...renderConstants, SetFurShellDefaults: setFurShellDefaults, GetDefaultRenderQueue: defaultRenderQueue,
        GetRenderingMode: modeOf, GetShaderName: mode => shaderNames[mode],
        IsFairyToonShader: shader => !!shaders[shader.name], Shader: { Find: name => shaders[name] },
        Get: (m, key) => m.values[key] || 0, Material: function(m) { return { ...m, values: { ...m.values } }; },
        Object: { DestroyImmediate() {} }, FairyToon: { CharacterShadowLightMode: 'CharacterShadow', CharacterMaskLightMode: 'CharacterMask' },
        Mathf: { Clamp: (n, lo, hi) => Math.max(lo, Math.min(hi, n)), RoundToInt: Math.round },
        Apply(m) {
            const state = renderState(modeOf(m), m.values._FurMode, 1, m.rawRenderQueue);
            m.renderQueue = state.material.renderQueue;
            m.values = { ...m.values, ...state.values };
        },
    };
    const source = csharpBody(fur ? 'SetFurMode' : 'SetRenderingMode')
        .replace(/ \{ hideFlags = HideFlags.HideAndDontSave \}/g, '')
        .replace(/System\.Array\.Empty<string>\(\)/g, '[]').replace(/(?<=\d)f\b/g, '')
        .replace(/\b(?:int|bool|string|var)\s+/g, 'let ');
    return new Function(...Object.keys(bindings), 'material', fur ? 'furMode' : 'renderingMode', source)
        (...Object.values(bindings), material, selected);
}
function queueMaterial(mode, fur, queue) {
    return { values: { _TransparentMode: Math.min(mode, 2), _FurMode: fur },
        shader: { name: ['FairyToon', 'FairyToon', 'FairyToon', 'FairyToon/Refraction', 'FairyToon/Fur', 'FairyToon/Gem'][mode] },
        rawRenderQueue: queue,
        get renderQueue() { return this.rawRenderQueue; }, set renderQueue(value) { this.rawRenderQueue = value; },
        HasProperty() { return true; }, SetInt(k, v) { this.values[k] = v; },
        SetFloat(k, v) { this.values[k] = v; }, SetShaderPassEnabled() {},
        CopyPropertiesFromMaterial(other) { this.values = { ...other.values }; this.rawRenderQueue = other.rawRenderQueue; },
    };
}

test('rendering mode transitions migrate defaults, preserve custom queues and keep no-op selections intact', () => {
    for (const oldMode of [0, 1, 2, 3, 4, 5]) for (const nextMode of [0, 1, 2, 3, 4, 5])
    for (const fur of [0, 1, 2]) for (const custom of [false, true]) {
        const queue = custom ? 2463 : defaultRenderQueue(oldMode, fur);
        const material = queueMaterial(oldMode, fur, queue);
        assert.equal(queueTransition(material, nextMode), true);
        assert.equal(material.renderQueue, custom || oldMode === nextMode ? queue : defaultRenderQueue(nextMode, fur));
        assert.equal(material.values._FurMode, fur, 'hidden Fur settings survive shader switches');
    }
});

test('Fur subtype transitions migrate only default queues and GUI uses the queue-aware API', () => {
    for (const oldMode of [0, 1, 2]) for (const nextMode of [0, 1, 2]) for (const queue of [-1, 2450, 2460, 2600, 3000]) {
        const material = queueMaterial(4, oldMode, queue);
        assert.equal(queueTransition(material, nextMode, true), true);
        const derived = queue < 0 || queue === defaultRenderQueue(4, oldMode);
        assert.equal(material.renderQueue, oldMode !== nextMode && derived ? defaultRenderQueue(4, nextMode) : queue);
    }
    const gui = read('Editor/FairyToonGUI.cs');
    assert.match(gui, /FairyToonMaterialSetup\.SetFurMode\(material, selected\)/);
    assert.ok(!gui.includes('P("_FurMode", "Fur Mode")'));
});


test('Gem custom-cube lighting and probe branches match the corrected shared reflection semantics', () => {
    const body = hlslBody(read('Shaders/FairyToonVariants.hlsl'), 'FairyToonGemEnvironment');
    const lerp = (a, b, t) => a + (b - a) * t;
    for (const endfield of [false, true]) for (const cube of [false, true]) for (const lighting of [0, 0.5, 1]) {
        let probeArity;
        const result = scalarHlsl(body, endfield ? ['_FT_ENDFIELD'] : [], {
            s: { V: 1, positionWS: 2, screenUV: 3 }, l: { lightColor: 0.4 }, N: 0.5, roughness: 0.6,
            _ReflectionCubeColor: { rgb: 0.7 }, _ReflectionCubeEnableLighting: lighting, lerp,
            reflect: () => 1, FairyToonUseReflectionCube: () => cube, FairyToonSampleReflectionCube: () => 0.8,
            GlossyEnvironmentReflection: (...args) => { probeArity = args.length; return 0.9; },
        });
        assert.equal(result, cube ? 0.8 * 0.7 * (1 + (0.4 - 1) * lighting) : 0.9);
        if (!cube) assert.equal(probeArity, 5);
    }
});

test('Gem grazing uses smoothness independently of anisotropy roughness in Gamma and Linear projects', () => {
    const gem = hlslBody(read('Shaders/FairyToonVariants.hlsl'), 'FairyToonGemOptics');
    const body = gem.slice(gem.indexOf('float roughness ='), gem.indexOf('float particle =')) + '\nreturn [grazingTerm, surfaceReduction];';
    for (const gamma of [false, true]) for (const smoothness of [0, 0.337, 1]) for (const perceptualRoughness of [0, 0.2, 1]) {
        const result = scalarHlsl(body, gamma ? ['UNITY_COLORSPACE_GAMMA'] : [], {
            s: {}, perceptualRoughness, FairyToonOpticalSmoothness: () => smoothness,
            saturate: x => Math.min(1, Math.max(0, x)), rcp: x => 1 / x,
        });
        assert.deepEqual(result, [Math.min(1, smoothness + (gamma ? 0.220916301 : 0.04)),
            gamma ? 1 - 0.28 * perceptualRoughness ** 3 : 1 / (perceptualRoughness ** 4 + 1)]);
    }
});


test('See-through provenance distinguishes managed and authored 2460 without tagging ordinary materials', () => {
    const body = csharpBody('ApplySeeThrough').replace(/\b(?:int|bool)\s+/g, 'let ').replace(/\(int\)/g, '');
    const run = new Function('material', 'mode', 'managedHairQueue', 'Get', 'On', 'Mathf', 'FairyToon', 'RoleFace', 'RoleHair', 'RoleNone',
        'Transparent', 'Cutout', 'CompareFunction', 'StencilOp', 'RenderQueue', 'ResetManagedStencil', 'ManagedHairQueueTag', 'SetHairQueueTag', body);
    const setTag = new Function('material', 'managed', 'RenderQueue', 'ManagedHairQueueTag',
        csharpBody('SetHairQueueTag').replace(/\bbool\s+/g, 'let ').replace(/\(int\)/g, ''));
    const make = (queue, provenance = '', priorHair = false, priorMask = false, transparent = false) => {
        const passes = { hair: priorHair, mask: priorMask }, tags = provenance === '' ? {} : { [hairQueueTag]: provenance };
        return { renderQueue: queue, get rawRenderQueue() { return this.renderQueue; }, role: 2, tags, passes, HasProperty: () => true, GetTexture: () => ({}), SetInt() {},
            GetTag: (key, _, fallback) => tags[key] ?? fallback, SetOverrideTag: (key, value) => tags[key] = value,
            IsKeywordEnabled: () => transparent,
            GetShaderPassEnabled: name => !!passes[name], SetShaderPassEnabled: (name, value) => passes[name] = value };
    };
    const apply = (m, mode) => run(m, mode, isManagedHairQueue(m), m => m.role, () => true,
        { RoundToInt: Math.round, Max: Math.max }, seeThroughConstants, 1, 2, 0, 2, 1,
        { Always: 8, NotEqual: 6 }, { Replace: 2, Zero: 1, Keep: 0 }, renderQueues, () => {}, hairQueueTag, (m, managed) => setTag(m, managed, renderQueues, hairQueueTag));
    for (const mode of [0, 1, 2]) for (const queue of [defaultRenderQueue(mode), 2460, 2461, 2466, 2471]) {
        const material = make(queue);
        apply(material, mode); apply(material, mode);
        assert.equal(material.renderQueue, mode === 2 ? queue : Math.max(queue, 2460));
        const generated = mode !== 2 && queue < 2460;
        assert.equal(material.tags[hairQueueTag], generated ? '1' : queue === 2460 ? '0' : undefined);
        material.role = 0; apply(material, mode);
        assert.equal(material.renderQueue, generated ? defaultRenderQueue(mode) : queue,
            'only generated Hair ordering is restored after leaving Hair');
    }
    assert.match(read('Editor/FairyToonConverter.cs'), /SetOverrideTag\(\s*FairyToonMaterialSetup\.ManagedHairQueueTag,\s*"0"\)/,
        'conversion explicitly records authored provenance');
    for (const mode of [0, 1, 2]) for (const queue of [defaultRenderQueue(mode), 2460, 2461, 2471])
    for (const tag of ['', '0', '1']) {
        const material = make(queue, tag);
        material.role = 0;
        apply(material, mode); apply(material, mode);
        assert.equal(material.tags[hairQueueTag], queue === 2460 || tag !== '' ? '0' : undefined);
        if (queue !== 2460 && tag === '')
            assert.equal(Object.hasOwn(material.tags, hairQueueTag), false, 'ordinary materials must not acquire an override tag');
    }
    for (const tag of ['', '0', '1']) for (const hair of [false, true]) for (const mask of [false, true])
    for (const transparent of [false, true]) {
        const material = make(2460, tag, hair, mask, transparent);
        const expectedManaged = tag === '1' || (tag === '' && hair && !mask && !transparent);
        assert.equal(isManagedHairQueue(material), expectedManaged);
        const history = { tags: material.tags, passes: material.passes, keywords: { _SURFACE_TYPE_TRANSPARENT: transparent } };
        assert.equal(renderState(2, 0, 1, 2460, history).material.renderQueue, expectedManaged ? 3000 : 2460,
            JSON.stringify({ tag, hair, mask, transparent }));
        material.role = 0; apply(material, 1);
        assert.equal(material.renderQueue, expectedManaged ? 2450 : 2460);
        assert.equal(material.tags[hairQueueTag], expectedManaged && tag === '' ? undefined : '0',
            'legacy untagged Hair stays untagged after restoration; existing provenance is cleared');
    }
});


test('review: all reflection probe paths pass world position and screen UV', () => {
    for (const file of ['FairyToonCore.hlsl', 'FairyToonEndfield.hlsl', 'FairyToonVariants.hlsl']) {
        const source = read('Shaders/' + file);
        const calls = [...source.matchAll(/GlossyEnvironmentReflection\(([^;]+)\)/g)];
        assert.ok(calls.length, file + ' has a probe path');
        for (const call of calls) {
            const args = call[1].split(',').map(v => v.trim());
            assert.equal(args.length, 5, file + ' uses position-dependent probe blending');
            assert.equal(args[1], 's.positionWS');
            assert.equal(args[4], 's.screenUV');
        }
    }
});

test('review: Endfield backfaces do not depend on the Toon FlipNormal toggle', () => {
    const source = read('Shaders/FairyToonEndfield.hlsl');
    const condition = source.match(/bool useBackfaceNormal = ([^;]+);/)?.[1];
    assert.ok(condition);
    const backface = new Function('s', '_FlipNormal', 'return ' + condition);
    for (const facing of [-1, 1]) for (const flip of [0, 1])
        assert.equal(backface({ facing }, flip), facing < 0, 'Endfield facing=' + facing + ', FlipNormal=' + flip);
});

test('review: shared Toon and Endfield Reflection stays finite at smoothness one with a 0.002 roughness floor', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const reflection = hlslBody(core, 'FairyToonReflection');
    const expression = reflection.match(/float roughness = ([^;]+);/)?.[1];
    assert.ok(expression);
    const roughness = new Function('perceptualRoughness', 'max', 'return ' + expression);
    assert.ok(hlslBody(core, 'FairyToonShade').includes('FairyToonReflection(s, l, col)'), 'both shading models use the shared reflection');
    for (const smoothness of [0, 0.337, 0.99, 1]) {
        const pr = 1 - smoothness, actual = roughness(pr, Math.max);
        assert.equal(actual, Math.max(0.002, pr * pr));
        assert.ok(Number.isFinite(1 / actual), 'toon specular exponent stays finite at smoothness=1');
        for (const nh of [0, 0.5, 1]) assert.ok(Number.isFinite(Math.pow(nh, 1 / actual)));
    }
});


test('shared pre-optics shading preserves model lighting, layer order and emission albedo', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const base = hlslBody(core, 'FairyToonShadeBase').replace(/return col;/, 'return { col, s, l };');
    for (const endfield of [false, true]) {
        const calls = [];
        const actual = scalarHlsl(base, ['_FT_RIMSHADE', '_FT_BACKLIGHT', ...(endfield ? ['_FT_ENDFIELD'] : [])], {
            s: { albedo: 0.8, N: -0.6, origN: 0.9, facing: -1 }, l: {}, _LightMaxLimit: 2,
            min: Math.min, saturate: x => Math.max(0, Math.min(1, x)), Luminance: x => x,
            FairyToonRimShadeMultiplier: () => { calls.push('rim shade'); return 0.5; },
            FairyToonGetLight: () => { calls.push('light'); return { lightColor: 0.6, addLightColor: 0, aoLit: 0.5, shadowmix: 0.3 }; },
            FairyToonDiffuse: (s, l) => { calls.push('toon'); return s.albedo * l.lightColor; },
            FairyToonAdditionalLights: (s, l) => { calls.push('additional'); l.addLightColor = 0.2; },
            FairyToonEndfieldShade: (s, l) => { calls.push('endfield'); l.backlightColor = 0.15; return s.albedo * 0.5; },
            FairyToonMainLayersUnlit: (s, col) => { calls.push('unlit layers'); return col + 0.1; },
            FairyToonBacklight: (s, l, col) => { calls.push('backlight'); return col + l.backlightColor; },
        });
        assert.deepEqual(calls, endfield ? ['rim shade', 'endfield', 'unlit layers', 'backlight'] :
            ['rim shade', 'light', 'toon', 'additional', 'unlit layers', 'backlight']);
        assert.ok(Math.abs(actual.col - (endfield ? 0.45 : 1.13)) < 1e-12);
        assert.equal(actual.s.emissionAlbedo, 0.8);
        assert.equal(actual.s.albedo, endfield ? 0.4 : 0.8);
        assert.equal(actual.s.origN, endfield ? -0.9 : 0.9);
    }
    for (const [source, entry] of [[core, 'FairyToonShade'], [read('Shaders/FairyToonVariants.hlsl'), 'FairyToonOpticalShade']]) {
        const body = hlslBody(source, entry);
        assert.equal((body.match(/FairyToonShadeBase\(/g) || []).length, 1);
        assert.doesNotMatch(body, /FairyToonRimShadeMultiplier\(|FairyToon(?:EndfieldShade|Diffuse|AdditionalLights|MainLayersUnlit|Backlight)\(/,
            'the pre-optics pipeline must have a single implementation');
        const rest = body.slice(body.indexOf('FairyToonShadeBase(') + 'FairyToonShadeBase('.length);
        assert.ok(/FairyToonReflection\(|FairyToonRefractedScene\(/.test(rest), 'surface layers and optics run after base shading');
    }
});

test('Endfield Fur shell geometric normals remain aligned across rim-shade normal strengths', () => {
    const body = hlslBody(read('Shaders/FairyToonFur.hlsl'), 'FairyToonFurFragment');
    const orientation = body.match(/#if defined\(_FT_ENDFIELD\)\s*(?:\/\/[^\n]*\n\s*)*s\.origN = s\.N;\s*#endif/)?.[0];
    assert.ok(orientation, 'Fur bypasses ShadeBase and must orient its geometric normal once');
    for (const endfield of [false, true]) for (const facing of [-1, 1]) {
        const s = { N: endfield && facing < 0 ? -0.8 : 0.8, origN: 0.8 };
        scalarHlsl(orientation, endfield ? ['_FT_ENDFIELD'] : [], { s });
        for (const strength of [0, 0.5, 1]) {
            assert.equal(s.origN + (s.N - s.origN) * strength, s.N, 'normal interpolation cannot cancel to zero');
        }
    }
});

test('all shaders expose transforms for every texture with a material ST uniform', () => {
    const transforms = [...input.matchAll(/\bfloat4\s+(_\w+)_ST\s*;/g)].map(m => m[1]);
    assert.ok(transforms.length >= 24, 'test covers the complete common texture transform contract');
    const extensions = [...read('Shaders/FairyToonVariantInput.hlsl').matchAll(/\bfloat4\s+(_\w+)_ST\s*;/g)].map(m => m[1]);
    for (const variant of ['', 'Refraction', 'Fur', 'Gem']) {
        const source = read('Shaders/FairyToon' + variant + '.shader');
        const required = [...transforms, ...extensions.filter(name => properties(source).has(name))];
        for (const texture of required) {
            const line = source.split('\n').find(line => new RegExp('\\b' + texture + '\\s*\\(').test(line));
            assert.ok(line, variant + '/' + texture);
            assert.doesNotMatch(line, /\[NoScaleOffset\]/, variant + '/' + texture + ' transform must remain editable');
        }
    }
    const gui = read('Editor/FairyToonGUI.cs');
    for (const texture of ['_Bump2ndScaleMask', '_AnisotropyScaleMask', '_BacklightColorTex', '_SmoothnessTex', '_MetallicGlossMap', '_ReflectionColorTex'])
        assert.ok(gui.includes('ScaleOffset("' + texture + '")'), texture + ' custom inspector control');
    assert.ok(gui.includes('ScaleOffset(n + "BlendMask")'));
    assert.ok(gui.includes('ScaleOffset(n + "Tex")'));
});

test('mixed Rendering and Fur popups change every target only after a user selection', () => {
    const gui = read('Editor/FairyToonGUI.cs');
    for (const name of ['RenderingMode', 'FurMode']) {
        const declaration = gui.indexOf('private void ' + name + '()');
        assert.ok(declaration >= 0);
        const start = gui.indexOf('{', declaration);
        let depth = 1, end = start + 1;
        for (; depth && end < gui.length; end++) { if (gui[end] === '{') depth++; if (gui[end] === '}') depth--; }
        assert.equal(depth, 0, name + ' has a complete method body');
        const body = gui.slice(start + 1, end - 1)
            .replace(/foreach \(Object target in m_Editor.targets\)/g, 'for (const target of m_Editor.targets)')
            .replace(/ as Material/g, '').replace(/\b(?:int|bool|var)\s+/g, 'let ')
            .replace(/new\[\]\s*\{([^}]+)\}/g, '[$1]');
        for (const mixed of [false, true]) for (const chooseFirst of [false, true]) {
            const materials = [{ mode: 1 }, { mode: mixed ? 2 : 1 }];
            const edits = [], undo = [];
            let changed = false, began = false, popupCount = 0;
            const editorGUI = { showMixedValue: false, BeginChangeCheck: () => { began = true; }, EndChangeCheck: () => changed };
            const bindings = {
                m_Editor: { target: materials[0], targets: materials, PropertiesChanged() {} }, m_Props: null,
                Prop: () => ({ hasMixedValue: mixed, floatValue: 1 }), Mathf: { RoundToInt: Math.round },
                FairyToonMaterialSetup: { GetRenderingMode: m => m.mode,
                    SetRenderingMode: (m, value) => { edits.push(value); m.mode = value; return true; },
                    SetFurMode: (m, value) => { edits.push(value); m.mode = value; return true; } },
                EditorGUI: editorGUI, EditorGUILayout: { Popup(label, index) {
                    popupCount++; assert.equal(began, true); assert.equal(!!editorGUI.showMixedValue, mixed);
                    assert.equal(index, mixed ? -1 : 1, 'mixed index must allow explicitly choosing the first material value');
                    changed = chooseFirst && index !== 1;
                    return chooseFirst ? 1 : index;
                } },
                Undo: { RecordObjects: targets => undo.push(targets.length) }, EditorUtility: { SetDirty() {} },
                MaterialEditor: { GetMaterialProperties: () => [] }, GUIUtility: { ExitGUI() {} },
                RowVisible: () => true, FairyToonL10n: { Text: (key) => key },
                Number: (prop) => prop.floatValue,
                Debug: { LogError: () => assert.fail('known rendering mode must exist') },
            };
            new Function(...Object.keys(bindings), body)(...Object.values(bindings));
            assert.equal(popupCount, 1);
            assert.equal(editorGUI.showMixedValue, false);
            assert.deepEqual(edits, mixed && chooseFirst ? [1, 1] : []);
            assert.deepEqual(undo, mixed && chooseFirst ? [2] : []);
            assert.deepEqual(materials.map(m => m.mode), mixed && !chooseFirst ? [1, 2] : [1, 1]);
        }
    }
});


test('smooth isotropic Toon and GGX specular receive shadowmix in both models without NaN or a branch reset', () => {
    const reflection = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonReflection');
    const start = reflection.indexOf('{', reflection.indexOf('if (_ApplySpecular > 0.5)'));
    let depth = 1, end = start + 1;
    for (; depth && end < reflection.length; end++) { if (reflection[end] === '{') depth++; if (reflection[end] === '}') depth--; }
    assert.equal(depth, 0, 'specular branch has a complete body');
    const specular = reflection.slice(start + 1, end - 1) + '\nreturn col;';
    const expression = reflection.match(/float roughness = ([^;]+);/)[1];
    const roughness = new Function('perceptualRoughness', 'max', 'return ' + expression)(0, Math.max);
    for (const endfield of [false, true]) for (const toon of [0, 1]) for (const sceneShadow of [0, 0.25, 1])
    for (const shadowmix of [0, 0.6, 1]) {
        const actual = scalarHlsl(specular, endfield ? ['_FT_ENDFIELD'] : [], {
            s: { origN: 1, N: 1, V: 1, alpha: 1 }, l: { L: 1, lightColor: 1, shadowAttenuation: sceneShadow, shadowmix },
            col: 0, roughness, perceptualRoughness: 0, specular: 0.2, reflectionColor: { rgb: 1, a: 1 }, _SpecularToon: toon,
            normalize: () => 1, lerp: (a, b, t) => a + (b - a) * t, saturate: x => Math.max(0, Math.min(1, x)),
            dot: (a, b) => a * b, pow: Math.pow, FairyToonStep: (_, value) => value,
            FairyToonReflectionGGX: () => 1, FairyToonTransparentBlend: (col, layer, amount) => amount,
        });
        assert.ok(Number.isFinite(actual));
        assert.equal(actual, (toon ? 1 : 0.2) * shadowmix);
    }
});


test('both MatCap custom normal layers retain lilToon property defaults in every variant', () => {
    const lil = properties(read('../jp.lilxyzw.liltoon/Shader/lts.shader'));
    for (const variant of ['', 'Fur', 'Gem', 'Refraction']) {
        const text = read('Shaders/FairyToon' + variant + '.shader');
        const actual = properties(text);
        for (const layer of ['', '2nd']) for (const suffix of ['CustomNormal', 'BumpMap', 'BumpScale']) {
            const name = '_MatCap' + layer + suffix;
            assert.ok(actual.has(name), variant + ' ' + name);
            assert.equal(actual.get(name).type.replace(/\s/g, ''), lil.get(name).type.replace(/\s/g, ''), name + ' range/type');
            assert.equal(actual.get(name).value, lil.get(name).value, name + ' default');
            assert.ok(input.includes(suffix === 'BumpMap' ? 'TEXTURE2D(' + name + ')' : name + ';'));
            if (suffix === 'BumpMap') assert.ok(input.includes(name + '_ST;'));
        }
        assert.ok(!/#pragma[^\n]*(?:MATCAP.*NORMAL|MATCAP.*BUMP)/i.test(text), 'no extra custom-normal variants');
    }
    const gui = read('Editor/FairyToonGUI.cs');
    for (const suffix of ['CustomNormal', 'BumpMap', 'BumpScale']) assert.ok(gui.includes('n + "' + suffix + '"'), suffix + ' inspector control');
    assert.ok(gui.includes('ScaleOffset(n + "BumpMap")'), 'independent UV transform controls');
});

test('MatCap setup and conversion preserve custom normals independently of main normal keywords', () => {
    const expression = setup.match(/SetKeyword\(material, "_FT_MATCAP", ([^;]+)\);/)[1];
    const keyword = new Function('material', 'On', 'return ' + expression);
    for (const useFirst of [false, true]) for (const useSecond of [false, true])
    for (const customFirst of [false, true]) for (const customSecond of [false, true]) {
        const material = { _UseMatCap: useFirst, _UseMatCap2nd: useSecond,
            _MatCapCustomNormal: customFirst, _MatCap2ndCustomNormal: customSecond,
            _UseBumpMap: false, _BumpMap: null, _MatCapBumpMap: null, _MatCap2ndBumpMap: null };
        assert.equal(keyword(material, (m, n) => m[n]), useFirst || useSecond);
    }
    assert.ok(!/Set(?:Float|Flag)\([^\n]*"_MatCap(?:2nd)?CustomNormal"/.test(setup), 'setup does not clear textureless custom normals');
    assert.ok(!/SetKeyword\([^\n]*_MatCap(?:2nd)?CustomNormal/.test(setup), 'no custom-normal keyword gate');
    const convert = converter.slice(converter.indexOf('public static FairyToonConvertReport Convert('), converter.indexOf('public static List<FairyToonConvertReport> ConvertAll('));
    const snapshot = convert.indexOf('new Material(source)');
    const changeShader = convert.indexOf('target.shader = shader;');
    const copy = convert.indexOf('target.CopyPropertiesFromMaterial(snapshot);');
    assert.ok(snapshot >= 0 && changeShader > snapshot && copy > changeShader, 'full source snapshot copied after selecting destination');
    assert.ok(!/Set(?:Float|Texture|TextureScale|TextureOffset)\([^\n]*"_MatCap(?:2nd)?(?:CustomNormal|BumpMap|BumpScale)"/.test(convert), 'no override loses saved custom normals or ST');
});

test('MatCap custom normal decoding, TBN, UVs, precedence and facing match lilToon', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const normals = read('Shaders/FairyToonNormals.hlsl');
    const referenceNormals = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_functions.hlsl');
    const referenceFrag = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    const body = hlslBody(core, 'FairyToonMatCapNormal').replace(
        /s\.uvMain \* (_MatCap(?:2nd)?BumpMap_ST)\.xy \+ \1\.zw/g, 'transformUV(s.uvMain, $1)')
        .replace('s.facing < 0.0 ? -N : N', 's.facing < 0.0 ? N.map(x => -x) : N');
    assert.match(passes, /s\.matcapTBN = float3x3\(input\.tangentWS\.xyz,\s*input\.tangentWS\.w \* cross\(input\.normalWS, input\.tangentWS\.xyz\), input\.normalWS\)/);
    assert.ok(passes.indexOf('s.matcapTBN =') < passes.indexOf('s.N = FairyToonFacingNormal'), 'retain unflipped mesh frame');
    assert.ok(!core.slice(core.indexOf('half3 FairyToonMatCapLayer('), core.indexOf('half3 FairyToonMatCap(')).includes('normalStrength'), 'custom override is not blended back with mesh normal');
    assert.match(referenceFrag, /N = normalize\(mul\(normalmap, fd\.TBN\)\);\s*N = fd\.facing < \(_FlipNormal-1\.0\) \? -N : N;/);
    const normalize = v => { const length = Math.hypot(...v); return v.map(x => x / length); };
    const lerp = (a, b, t) => a.map((x, i) => x + (b[i] - x) * t);
    const mul = (v, m) => m[0].map((_, i) => v.reduce((sum, x, j) => sum + x * m[j][i], 0));
    const dot = (a, b) => a.reduce((sum, x, i) => sum + x * b[i], 0);
    const saturate = x => Math.max(0, Math.min(1, x));
    const transformUV = (uv, st) => uv.map((x, i) => x * st[i] + st[i + 2]);
    const assertVector = (a, b, label) => a.forEach((x, i) => assert.ok(Math.abs(x - b[i]) < 1e-10, label + ' component ' + i + ': ' + x + '/' + b[i]));
    const unpack = (text, name, packed, scale, defines) => {
        const decoder = hlslBody(text, name)
            .replace('float3 normal;', 'float normal = { xy: [0, 0], z: 0 };')
            .replace('half3 normal;', 'half normal = { xy: [0, 0], z: 0 };')
            .replace('normal = normalTex.rgb * 2.0 - 1.0;', 'normal = { xy: [normalTex.r * 2 - 1, normalTex.g * 2 - 1], z: normalTex.b * 2 - 1 };')
            .replace('normal.xy = normalTex.ag * 2.0 - 1.0;', 'normal.xy = [normalTex.a * 2 - 1, normalTex.g * 2 - 1];')
            .replace(/normal.xy \*= scale;/g, 'normal.xy = normal.xy.map(x => x * scale);');
        const normal = scalarHlsl(decoder, defines, { normalTex: { ...packed }, scale, dot, saturate, sqrt: Math.sqrt,
            FairyToonNormalZ: xy => scalarHlsl(hlslBody(normals, 'FairyToonNormalZ'), [], { xy, dot, saturate, sqrt: Math.sqrt }) });
        return [...normal.xy, normal.z];
    };
    const flat = { r: 0.5, g: 0.5, b: 1, a: 1 };
    const packed = [{ r: 0.8, g: 0.7, b: 0.9, a: 1 }, { r: 1, g: 0.25, b: 0.9, a: 0.6 }];
    const original = normalize([0.1, 0.2, 1]);
    const mapped = normalize([0.4, -0.3, 0.8]);
    const aniso = normalize([-0.4, 0.6, 0.5]);
    const tbn = [[0, 1.2, 0], [-0.8, 0, 0], [0.1, 0.2, 1]];
    const orient = new Function('normal', 'facing', '_FlipNormal', hlslBody(normals, 'FairyToonFacingNormal'));
    function actual(options = {}) {
        const { second = false, custom = true, scale = 0.2, normalStrength = 0, anisotropy = true,
            facing = 1, flip = 1, texture = packed[second ? 1 : 0], encoding = [], mirrored = false, endfield = false } = options;
        const frame = tbn.map(row => [...row]);
        if (mirrored) frame[1] = frame[1].map(x => -x);
        let samples = 0;
        const result = scalarHlsl(body, ['_FT_MATCAP', '_FT_ANISOTROPY', ...(endfield ? ['_FT_ENDFIELD'] : [])], {
            s: { N: mapped, origN: original, anisoN: aniso, uvMain: [0.2, 0.4], matcapTBN: frame, facing }, second,
            _UseAnisotropy: anisotropy ? 1 : 0, _Anisotropy2MatCap: 1, _Anisotropy2MatCap2nd: 1,
            _MatCapNormalStrength: normalStrength, _MatCap2ndNormalStrength: normalStrength,
            _MatCapCustomNormal: custom && !second ? 1 : 0, _MatCap2ndCustomNormal: custom && second ? 1 : 0,
            _MatCapBumpMap: 'first', _MatCap2ndBumpMap: 'second', _MatCapBumpScale: scale, _MatCap2ndBumpScale: scale,
            _MatCapBumpMap_ST: [2, 3, 0.1, -0.2], _MatCap2ndBumpMap_ST: [4, 5, -0.1, 0.3], sampler_MainTex: null,
            lerp, normalize, mul, transformUV,
            SAMPLE_TEXTURE2D: (map, sampler, uv) => {
                samples++;
                assert.equal(map, second ? 'second' : 'first');
                assertVector(uv, second ? [0.7, 2.3] : [0.5, 1], 'own map ST after uvMain');
                return texture || flat;
            },
            FairyToonUnpackLayerNormal: (tex, strength) => unpack(normals, 'FairyToonUnpackLayerNormal', tex, strength, encoding),
            FairyToonFacingNormal: (n, f) => n.map(x => orient(x, f, flip)),
        });
        const referenceTS = unpack(referenceNormals, 'lilUnpackNormalScale', texture || flat, scale, encoding);
        const expected = custom ? normalize(mul(referenceTS, frame)).map(x => (endfield ? facing < 0 : facing < flip - 1) ? -x : x)
            : normalize(lerp(original, anisotropy ? aniso : mapped, normalStrength));
        assertVector(result, expected, 'lilToon custom-normal reference');
        assert.equal(samples, custom ? 1 : 0, 'disabled path never samples custom normal maps');
        return result;
    }
    for (const second of [false, true]) for (const encoding of [[], ['UNITY_NO_DXT5nm'], ['UNITY_ASTC_NORMALMAP_ENCODING']])
    for (const scale of [-1, 0, 0.2, 1]) for (const facing of [-1, 1]) for (const flip of [0, 1])
    for (const mirrored of [false, true]) for (const endfield of [false, true]) actual({ second, scale, encoding, facing, flip, mirrored, endfield });
    for (const second of [false, true]) for (const normalStrength of [0, 0.3, 1]) for (const anisotropy of [false, true]) {
        actual({ second, custom: false, normalStrength, anisotropy });
        const custom = actual({ second, normalStrength, anisotropy });
        assertVector(custom, actual({ second }), 'custom normal overrides both strength and anisotropy');
        assertVector(actual({ second, texture: null, normalStrength, anisotropy }), normalize(tbn[2]), 'unassigned bump map uses mesh normal');
    }
});


test('Mermaid alpha-mask ST follows main UVs in all coverage modes and preserves identity masks', () => {
    const body = hlslBody(input, 'FairyToonApplyAlphaMask') + '\nreturn col.a;';
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    assert.match(lil, /LIL_SAMPLE_2D_ST\(_AlphaMask, sampler_MainTex, fd\.uvMain\)\.r/);
    assert.match(input, /float4\s+_AlphaMask_ST;/);
    const sat = x => Math.min(1, Math.max(0, x));
    // Evaluate each UV component separately: scalarHlsl executes the actual HLSL expression.
    for (const defines of [['_SURFACE_TYPE_TRANSPARENT'], ['_ALPHATEST_ON'], []])
    for (const mode of [0, 1, 2, 3, 4]) for (const scale of [1, 75]) for (const offset of [0, 0.2])
    for (const texel of [0, 0.3, 1]) {
        const samples = [], initial = 0.957;
        const actual = scalarHlsl(body, defines, {
            col: {a: initial}, uvMain: 0.127, _AlphaMask_ST: {xy: scale, zw: offset},
            _AlphaMaskMode: mode, _AlphaMaskScale: 1, _AlphaMaskValue: 0.505,
            _AlphaMask: 'mask', sampler_MainTex: 'main', saturate: sat,
            SAMPLE_TEXTURE2D: (texture, sampler, uv) => { samples.push(uv); return {r: texel}; },
        });
        const mask = sat(texel + 0.505), enabled = defines.length && mode;
        const expected = !enabled ? initial : [initial, mask, initial * mask, sat(initial + mask), sat(initial - mask)][mode];
        assert.equal(actual, expected);
        assert.deepEqual(samples, enabled ? [0.127 * scale + offset] : []);
    }
    assert.match(read('Editor/FairyToonGUI.cs'), /ScaleOffset\("_AlphaMask"\)/);
});

test('active Mermaid Main2nd and Emission maps use source samplers within the DX11 budget', () => {
    const core = read('Shaders/FairyToonCore.hlsl'), optics = read('Shaders/FairyToonVariants.hlsl');
    const macro = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_macro.hlsl');
    assert.match(macro, /LIL_GET_SUBTEX[^\n]+LIL_SAMP_IN\(sampler##tex\)/);
    assert.match(macro, /LIL_GET_EMITEX[^\n]+LIL_SAMPLE_2D\(tex, sampler##tex,/);
    for (const name of ['Main2ndTex', 'EmissionMap']) {
        assert.ok(input.includes('SAMPLER(sampler_' + name + ')'));
        const files = name.startsWith('Main') ? [input] : [core, optics];
        for (const text of files) assert.ok(text.includes('TEXTURE2D_ARGS(_' + name + ', sampler_' + name + ')'));
    }
    assert.match(input, /half4 layer = SAMPLE_TEXTURE2D\(tex, samp, outUV\)/);
    assert.match(core, /half4 e = color \* SAMPLE_TEXTURE2D\(tex, samp, uv\)/);
    assert.match(input, /SAMPLE_TEXTURE2D\(mask, sampler_MainTex, uvMain\)\.r/);
    assert.match(core, /e \*= SAMPLE_TEXTURE2D\(mask, sampler_MainTex, maskUV\)/);
});

test('post-lighting MatCap, metallic and Rim tints retain lilToon albedo before Rim Shade', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const lil = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    assert.ok(!lil.slice(lil.indexOf('void lilGetRimShade('), lil.indexOf('#if !defined(OVERRIDE_RIMSHADE)')).includes('fd.albedo ='));
    assert.match(lil, /matCapColor\.rgb \* fd\.albedo/);
    const lerp = (a,b,t) => a + (b-a)*t;
    for (const [prefix, amount, c] of [['c', 'mainStrength', 0.7], ['rimColor', '_RimMainStrength', 0.4]]) {
        const expression = core.match(new RegExp(prefix + '\\.rgb = (lerp\\(' + prefix + '\\.rgb, ' + prefix + '\\.rgb \\* s\\.emissionAlbedo, ' + amount + '\\));'))?.[1];
        assert.ok(expression, prefix + ' pre-rim tint');
        const evaluate = new Function(prefix, 's', amount, 'lerp', 'return ' + expression);
        for (const rim of [0, 0.5, 1]) for (const strength of [0, 0.2, 0.4, 1])
            assert.equal(evaluate({rgb:c}, {albedo:0.8*rim,emissionAlbedo:0.8}, strength, lerp), lerp(c,c*0.8,strength));
    }
    const expression = core.match(/specular = (lerp\(_Reflectance, s\.emissionAlbedo, metallic\));/)?.[1];
    assert.ok(expression);
    const evaluate = new Function('_Reflectance','s','metallic','lerp','return ' + expression);
    assert.equal(evaluate(0.18, {albedo:0.1,emissionAlbedo:0.8}, 0.121, lerp), lerp(0.18,0.8,0.121));
});

test('offline Mermaid matrix covers custom MatCaps without primary normals and the stripped feature path', () => {
    const compile = read('Tests~/CompileShaders.ps1');
    assert.match(compile, /\[switch\]\$MermaidMatrix/);
    assert.match(compile, /Name='CustomMatCapOnly'; NormalMap=\$false; Surface='None'; MatCap=\$true/);
    assert.match(compile, /Name='NoMatCap'; NormalMap=\$true; Surface='All'; MatCap=\$false/);
    assert.match(compile, /if\(!\$featureConfiguration\.MatCap\).*#undef _FT_MATCAP/);
});


// Execute the actual scalar control flow; lower only vector operations unsupported by JavaScript.
function dissolveVector(...values) {
    const v = [...values];
    for (const [letters, indices] of [['xy',[0,1]],['xyz',[0,1,2]],['zw',[2,3]]])
        Object.defineProperty(v, letters, {get: () => dissolveVector(...indices.map(i => v[i])), set: a => indices.forEach((n,i) => v[n]=a[i])});
    for (const [i, names] of ['xr','yg','zb','wa'].entries()) for (const name of names)
        Object.defineProperty(v, name, {get: () => v[i], set: a => v[i]=a});
    return v;
}
const dissolveMath = {
    round: v => Array.isArray(v) ? dissolveVector(...v.map(Math.round)) : Math.round(v),
    saturate: x => Math.min(1,Math.max(0,x)), abs: Math.abs, max: Math.max,
    dot: (a,b) => a.reduce((sum,x,i) => sum+x*b[i],0),
    normalize: v => dissolveVector(...v.map(x => x/Math.hypot(...v))),
    distance: (a,b) => Math.hypot(...a.map((x,i)=>x-b[i])),
};
function dissolveRotateUV(uv,angle) {
    return dissolveVector((uv[0]-.5)*Math.cos(angle)-(uv[1]-.5)*Math.sin(angle)+.5,
        (uv[0]-.5)*Math.sin(angle)+(uv[1]-.5)*Math.cos(angle)+.5);
}
function executeDissolve(options = {}, source = false) {
    const {candidateSource=input,mode=1,shape=0,threshold=.5,width=.3,useNoise=1,noise=.7,noiseStrength=.8,mask=.6,time=2.5,alpha=.37,
        uv=[.17,.83],position=[-.4,.2,1.3],pos=[.3,-.6,.8,1.1],maskST=[2,-3,.1,.2],noiseST=[-4,5,.3,-.2],scroll=[-.3,.2,9,17]}=options;
    const samples=[], frac=x=>x-Math.floor(x);
    const transformUV=(v,st)=>dissolveVector(...v.map((x,i)=>x*st[i]+st[i+2]));
    const animatedUV=(v,st,sr)=>dissolveVector(...transformUV(v,st).map((x,i)=>x+frac(sr[i]*time)));
    const sample=(tex,sampler,sampleUV)=>{ samples.push([tex,sampler,[...sampleUV]]); return {r:tex==='mask'?mask:noise}; };
    const uv0=dissolveVector(...uv), positionOS=dissolveVector(...position), dissolveParams=dissolveVector(mode,shape,threshold,width), dissolvePos=dissolveVector(...pos);
    let body;
    if(source) {
        const lil=read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_functions.hlsl');
        body=hlslBody(lil,useNoise?'lilCalcDissolveWithNoise':'lilCalcDissolve')
            .replace(/dot\((positionOS, normalize\(dissolvePos\.xyz\))\)\.x/g,'dot($1)')+'\nreturn {alpha, edge:dissolveAlpha};';
    } else {
        body=hlslBody(candidateSource,'FairyToonCalcDissolve')
            .replace(/\bUNITY_BRANCH\b/g,'')
            .replace(/uv0 \* noiseST\.xy \+ noiseST\.zw \+ frac\(noiseScrollRotate\.xy \* _Time\.y\)/g,'animatedUV(uv0,noiseST,noiseScrollRotate)')
            .replace(/uv0 \* maskST\.xy \+ maskST\.zw/g,'transformUV(uv0,maskST)')
            .replace(/\breturn;/g,'return {alpha,edge};')+'\nreturn {alpha,edge};';
    }
    const result=scalarHlsl(body,[],{...dissolveMath,alpha,edge:0,dissolveAlpha:0,uv0,uv:uv0,positionOS,dissolveParams,dissolvePos,
        useNoise,noiseStrength,mask:'mask',noiseMask:'noise',maskST:dissolveVector(...maskST),noiseST:dissolveVector(...noiseST),
        noiseScrollRotate:dissolveVector(...scroll),_Time:{y:time},sampler_MainTex:'main',SAMPLE_TEXTURE2D:sample,
        dissolveMask:'mask',dissolveMask_ST:dissolveVector(...maskST),dissolveMaskEnabled:true,
        dissolveNoiseMask:'noise',dissolveNoiseMask_ST:dissolveVector(...noiseST),dissolveNoiseMask_ScrollRotate:dissolveVector(...scroll),
        dissolveNoiseStrength:noiseStrength,samp:'main',LIL_SAMPLE_2D:sample,
        lilCalcUV:(v,st,sr)=>sr?animatedUV(v,st,sr):transformUV(v,st),lilRotateUV:dissolveRotateUV,
        FairyToonRotateUV:dissolveRotateUV,transformUV,animatedUV});
    return {...result,samples};
}
function executeRootDissolve(options={},defines=['_SURFACE_TYPE_TRANSPARENT']) {
    let samples=[];
    const uv0=dissolveVector(...(options.uv??[.17,.83])),positionOS=dissolveVector(...(options.position??[-.4,.2,1.3]));
    const body=hlslBody(input,'FairyToonApplyDissolve').replace(/FairyToonCalcDissolve\(alpha,([\s\S]*?), edge\);/,
        'const evaluated = run(alpha,$1); alpha=evaluated.alpha; edge=evaluated.edge;')+'\nreturn {alpha,edge};';
    const result=scalarHlsl(body,defines,{alpha:options.alpha??.37,edge:0,uv0,positionOS,
        _DissolveParams:dissolveVector(options.mode??1,options.shape??0,options.threshold??.5,options.width??.3),
        _DissolvePos:dissolveVector(...(options.pos??[.3,-.6,.8,1.1])),
        _DissolveMask:'rootMask',_DissolveNoiseMask:'rootNoise',_DissolveMask_ST:dissolveVector(...(options.maskST??[2,-3,.1,.2])),
        _DissolveNoiseMask_ST:dissolveVector(...(options.noiseST??[-4,5,.3,-.2])),
        _DissolveNoiseMask_ScrollRotate:dissolveVector(...(options.scroll??[-.3,.2,9,17])),
        _DissolveNoiseStrength:options.noiseStrength??.8,_FT_DissolveUseNoise:options.useNoise??1,
        run:(alpha,uv,position,params,pos,mask,maskST,noiseMask,noiseST,scroll,strength,useNoise)=>{
            assert.equal(uv,uv0);assert.equal(position,positionOS);assert.equal(mask,'rootMask');assert.equal(noiseMask,'rootNoise');
            const r=executeDissolve({...options,alpha,mode:params.x,shape:params.y,threshold:params.z,width:params.w,
                uv:[...uv],position:[...position],pos:[...pos],maskST:[...maskST],noiseST:[...noiseST],scroll:[...scroll],noiseStrength:strength,useNoise});
            samples=r.samples;return r;
        }});
    return {...result,samples};
}

test('all root dissolve modes and shapes execute installed lilToon noisy and no-noise equations', () => {
    for(const mode of [0,1,2,3]) for(const shape of [0,1]) for(const useNoise of [0,1])
    for(const threshold of [-.5,0,.4,1.2]) for(const noise of [0,.5,1]) for(const noiseStrength of [0,.35,1])
    for(const time of [-2,0,3.7]) for(const width of [.07,.668]) {
        const options={mode,shape,useNoise,threshold,noise,noiseStrength,time,width};
        const actual=executeRootDissolve(options), expected=executeDissolve(options,true);
        assert.equal(actual.alpha,expected.alpha,JSON.stringify(options));
        assert.ok(Math.abs(actual.edge-expected.edge)<1e-12,JSON.stringify(options));
        assert.deepEqual(actual.samples,expected.samples,'raw UV0, own ST, XY scroll only, shared main sampler');
        assert.equal(actual.samples.length,mode===0?0:(mode===1?1:0)+useNoise);
    }
    // No-noise UV direction is rotation about 0.5; noise-enabled strength zero still uses dot(UV, direction).
    const directional={mode:2,shape:1,noiseStrength:0,pos:[.3,-.6,.8,1.1],threshold:.3,width:1};
    assert.notEqual(executeDissolve({...directional,useNoise:0}).edge,executeDissolve({...directional,useNoise:1}).edge);
});

test('dissolve threshold sign, finite zero width and disabled root guards are explicit', () => {
    for(const mode of [1,2,3]) for(const useNoise of [0,1]) {
        const base={mode,useNoise,shape:0,noise:.5,noiseStrength:1,uv:[.3,.4],position:[0,.3,.4],pos:[0,0,0,0],mask:.5,width:0};
        for(const delta of [-1e-7,0,1e-7]) {
            const result=executeRootDissolve({...base,threshold:.5+delta});
            assert.equal(result.alpha,delta<0?.37:0,'strict greater-than keeps equality a hole');
            assert.ok(Number.isFinite(result.edge),'intentional finite width-zero policy');
        }
    }
    for(const defines of [[],['_SURFACE_TYPE_TRANSPARENT','FAIRYTOON_GEM'],['_ALPHATEST_ON','FAIRYTOON_FUR_SHELL']])
    for(const mode of [0,1,2,3]) {
        assert.deepEqual(executeRootDissolve({mode},defines),{alpha:.37,edge:0,samples:[]});
    }
    assert.deepEqual(executeRootDissolve({mode:0}),{alpha:.37,edge:0,samples:[]});
});

function executeLayerDissolve({active=[2,3],modes=[2,3],shapes=[0,1],noises=[1,0],alphaModes=[0,0],layerAlpha=.35,defines=['_SURFACE_TYPE_TRANSPARENT'],alphaMask=null}={}) {
    const calls=[], sourceResults=[], c={uv0:dissolveVector(.17,.83),positionOS:dissolveVector(-.4,.2,1.3)};
    const bindings={c,layer2:0,layer3:0,dissolveEdge:0,layerDissolveEdges:0,sampler_MainTex:'main',sampler_Main2ndTex:'second',
        _MainTex:'base',_MainTex_ST:{xy:1,zw:0},_Color:1,_UseMain2ndTex:active.includes(2)?1:0,_UseMain3rdTex:active.includes(3)?1:0,
        _Main2ndEnableLighting:1,_Main3rdEnableLighting:1,_Main2ndTexAlphaMode:alphaModes[0],_Main3rdTexAlphaMode:alphaModes[1],
        _Main2ndTexBlendMode:0,_Main3rdTexBlendMode:0,FairyToonSampleMainColor:()=>({rgb:.2,a:.6}),
        FairyToonMainLayer:()=>({rgb:.8,a:layerAlpha}),
        FairyToonBlend:(dst,src,a,mode)=>scalarHlsl(hlslBody(input,'FairyToonBlend'),[],{dst,src,srcA:a,mode,multiplier:1,max:Math.max,lerp:(a,b,t)=>a+(b-a)*t}),
        FairyToonApplyAlphaMask:col=>{if(alphaMask!==null)col.a=alphaMask;},
        applyAlphaMode:(col,layer,mode)=>{
            const result=scalarHlsl(hlslBody(input,'FairyToonMainLayerAlpha')+'\nreturn {alpha,layerAlpha};',defines,
                {alpha:col.a,layerAlpha:layer.a,mode,saturate:dissolveMath.saturate});
            col.a=result.alpha;layer.a=result.layerAlpha;
        },
        applyLayerDissolve:(layer,axis,uv,position,params,pos,mask,maskST,noiseMask,noiseST,scroll,strength,useNoise)=>{
            const index=axis==='x'?0:1, prefix='_Main'+(index===0?'2nd':'3rd')+'Dissolve';
            assert.equal(uv,c.uv0,'layer texture UV selector must not replace raw dissolve UV0');
            assert.equal(position,c.positionOS,'post-skinning object position reaches layer helper');
            assert.equal(mask,prefix+'Mask');assert.equal(noiseMask,prefix+'NoiseMask');
            const options={alpha:layer.a,mode:params.x,shape:params.y,threshold:params.z,width:params.w,
                uv:[...uv],position:[...position],pos:[...pos],maskST:[...maskST],noiseST:[...noiseST],scroll:[...scroll],
                noiseStrength:strength,useNoise,mask:.53,noise:.8,time:-1.7};
            const result=executeDissolve(options),expected=executeDissolve(options,true);
            assert.equal(result.alpha,expected.alpha);assert.ok(Math.abs(result.edge-expected.edge)<1e-12);
            assert.deepEqual(result.samples,expected.samples);
            calls.push({index,alpha:result.alpha,edge:result.edge});sourceResults[index]=expected;
            layer.a=result.alpha;edgeResult[axis]=result.edge;
        },
    };
    for(let i=0;i<2;i++) {
        const prefix='_Main'+(i===0?'2nd':'3rd')+'Dissolve';
        bindings[prefix+'Params']=dissolveVector(modes[i],shapes[i],.4,.668);
        bindings[prefix+'Pos']=dissolveVector(.3,-.6,.8,1.1);
        for(const suffix of ['Mask','NoiseMask'])bindings[prefix+suffix]=prefix+suffix;
        bindings[prefix+'Mask_ST']=dissolveVector(2,-3,.1,.2);
        bindings[prefix+'NoiseMask_ST']=dissolveVector(-4,5,.3,-.2);
        bindings[prefix+'NoiseMask_ScrollRotate']=dissolveVector(-.3,.2,9,17);
        bindings[prefix+'NoiseStrength']=.7;
        bindings['_FT'+prefix+'UseNoise']=noises[i];
        // A deliberately unrelated texture UV mode proves dissolve still consumes c.uv0.
        bindings['_Main'+(i===0?'2nd':'3rd')+'Tex_UVMode']=i+2;
    }
    const edgeResult={x:0,y:0};
    const body=hlslBody(input,'FairyToonLayeredBase')
        .replace(/float2 uvMain = [^;]*;/,'float2 uvMain = 9;')
        .replace('layerDissolveEdges = 0;','layerDissolveEdges = edgeResult;')
        .replace(/FairyToonCalcDissolve\((layer[23])\.a,([\s\S]*?), layerDissolveEdges\.([xy])\);/g,"applyLayerDissolve($1,'$3',$2);")
        .replace(/FairyToonMainLayerAlpha\(col\.a, (layer[23])\.a, ([^)]+)\);/g,'applyAlphaMode(col,$1,$2);')
        .replace(/FairyToonApplyDissolve\([^;]*;/,'dissolveEdge=0;')
        .replace('return col;','return {col,layer2,layer3,layerDissolveEdges};');
    const actual=scalarHlsl(body,['_FT_MAIN_LAYERS',...defines],{...bindings,edgeResult});
    const allowed=!defines.includes('FAIRYTOON_GEM')&&!defines.includes('FAIRYTOON_FUR_SHELL');
    assert.equal(calls.length,allowed?active.length:0,'inactive layers and excluded fragments never sample dissolve');
    let expectedAlpha=.6;
    for(let i=0;i<2;i++) {
        const layer=i+2, enabled=active.includes(layer), result=actual['layer'+layer];
        if(!enabled){assert.equal(result,0);continue;}
        let expectedLayerAlpha=allowed?sourceResults[i].alpha:layerAlpha;
        if(defines.some(d=>['_ALPHATEST_ON','_SURFACE_TYPE_TRANSPARENT'].includes(d))&&alphaModes[i]!==0) {
            expectedAlpha=[expectedAlpha,expectedLayerAlpha,expectedAlpha*expectedLayerAlpha,
                dissolveMath.saturate(expectedAlpha+expectedLayerAlpha),dissolveMath.saturate(expectedAlpha-expectedLayerAlpha)][alphaModes[i]];
            expectedLayerAlpha=1;
        }
        assert.equal(result.a,expectedLayerAlpha,'dissolve precedes layer AlphaMode, including layerAlpha reset');
        assert.equal(actual.layerDissolveEdges[i===0?'x':'y'],allowed?sourceResults[i].edge:0,'edge survives zero/culled layer alpha');
    }
    assert.equal(actual.col.a,alphaMask===null?expectedAlpha:alphaMask,'root alpha mask follows both layer alpha operations');
    return actual;
}

test('Main2nd and Main3rd dissolve match source before alpha modes and keep independent edges', () => {
    for(const layer of [2,3]) for(const mode of [0,1,2,3]) for(const shape of [0,1]) for(const useNoise of [0,1])
    for(const alphaMode of [0,1,2,3,4]) for(const layerAlpha of [0,.35,1]) {
        const i=layer-2,modes=[0,0],shapes=[0,0],noises=[0,0],alphaModes=[0,0];
        modes[i]=mode;shapes[i]=shape;noises[i]=useNoise;alphaModes[i]=alphaMode;
        executeLayerDissolve({active:[layer],modes,shapes,noises,alphaModes,layerAlpha});
    }
    for(const definitions of [[],['_ALPHATEST_ON'],['_SURFACE_TYPE_TRANSPARENT'],['_SURFACE_TYPE_TRANSPARENT','FAIRYTOON_GEM'],['_SURFACE_TYPE_TRANSPARENT','FAIRYTOON_FUR_SHELL']])
    for(const first of [1,2,3]) for(const second of [1,2,3]) for(const alphaMask of [null,.27])
        executeLayerDissolve({modes:[first,second],alphaModes:[2,3],defines:definitions,alphaMask});
    executeLayerDissolve({active:[]});
});

test('dissolve regressions detect noise sign and late edge-alpha mutations in memory', () => {
    const options={mode:1,mask:.45,noise:.9,noiseStrength:.8,threshold:.5,width:.668};
    const expected=executeDissolve(options,true);
    const mutated=input.replace(') * noiseStrength;',') * -noiseStrength;');
    assert.notEqual(mutated,input,'mutation reaches actual HLSL formula');
    assert.notDeepEqual(executeDissolve({...options,candidateSource:mutated}),expected,'noise sign mutation changes coverage and edge');
    const core=hlslBody(read('Shaders/FairyToonCore.hlsl'),'FairyToonShade');
    const expression=core.match(/col \+= ([^;]*_DissolveColor[^;]*);/)[1];
    const run=source=>new Function('_DissolveColor','_Main2ndDissolveColor','_Main3rdDissolveColor','s','return '+source)
        ({rgb:0},{rgb:2},{rgb:0},{dissolveEdge:0,layerDissolveEdges:{x:.7,y:0},layer2:{a:0}});
    assert.equal(run(expression),1.4);
    assert.notEqual(run(expression.replace('s.layerDissolveEdges.x','s.layerDissolveEdges.x * s.layer2.a')),1.4);
});

test('offline dissolve matrix specializes real uniforms and retains a disabled-resource regression', {skip:process.platform!=='win32'}, () => {
    const {spawnSync}=require('node:child_process'), compile=read('Tests~/CompileShaders.ps1');
    const start=compile.indexOf('$dissolveConfigurations=@('),end=compile.indexOf('$referenceLayouts=@{}',start);
    assert.ok(start>=0&&end>start);
    const fixture="$DissolveMatrix=$true\n$DissolveCases=@()\n$nl=[Environment]::NewLine\n"+compile.slice(start,end)+String.raw`
$items=@(foreach($configuration in $dissolveConfigurations) {
 [pscustomobject]@{Name=$configuration.Name;Mode=$configuration.Mode;Shape=$configuration.Shape;Noise=$configuration.Noise;Source=(Get-DissolveUniformOverrides $configuration)}
})
ConvertTo-Json -Depth 4 -Compress -InputObject $items
`;
    const result=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(fixture,'utf16le').toString('base64')],{encoding:'utf8'});
    assert.equal(result.status,0,result.stderr||String(result.error));
    const cases=JSON.parse(result.stdout.replace(/^\uFEFF/,''));
    assert.equal(cases.length,12);assert.equal(cases[0].Name,'Runtime');assert.equal(cases[0].Source,'');
    assert.ok(cases.some(c=>c.Name==='Disabled'&&c.Mode===0));
    for(const entry of cases.slice(1)) {
        for(const prefix of ['_','_Main2nd','_Main3rd']) {
            const uniform=prefix+'DissolveParams';
            assert.ok(entry.Source.includes('#define '+uniform+' float4('+entry.Mode+'.0, '+entry.Shape+'.0, 0.175, 0.2)'));
            const noise=prefix==='_'?'_FT_DissolveUseNoise':'_FT'+prefix+'DissolveUseNoise';
            assert.ok(entry.Source.includes('#define '+noise+' '+entry.Noise+'.0'));
        }
        assert.match(entry.Source,/#define _UseMain2ndTex 1\.0/);
        assert.match(entry.Source,/#define _UseMain3rdTex 1\.0/);
        assert.doesNotMatch(entry.Source,/#pragma|#define _FT_DISSOLVE/);
    }
    assert.match(compile,/includeText\.Insert\(end\+marker\.Length,"\\n"\+uniformOverrides/,'specialization follows CBUFFER_END declarations');
    for(const stage of ['vs','ps','gs']) assert.ok(compile.includes("'"+stage+"_5_0',$uniformOverrides)"),'actual '+stage+' compilation consumes specialization');
    assert.match(compile,/Gem unexpectedly samples dissolve textures/);
    assert.match(compile,/Opaque unexpectedly samples whole-material dissolve textures/);
    assert.match(compile,/Disabled dissolve unexpectedly samples textures/);
    assert.match(compile,/Position dissolve unexpectedly samples mask textures/);
    assert.match(compile,/No-noise dissolve unexpectedly samples noise textures/);
    assert.match(compile,/unexpected object-position VS TEXCOORD12/);
});
test('object dissolve reconstructs position only for active mode3 and preserves biased outline coverage', () => {
    const needed=hlslBody(input,'FairyToonNeedsDissolvePosition');
    for(const defines of [[],['_SURFACE_TYPE_TRANSPARENT'],['_ALPHATEST_ON','_FT_MAIN_LAYERS'],['_FT_MAIN_LAYERS'],['_FT_MAIN_LAYERS','FAIRYTOON_GEM'],['_SURFACE_TYPE_TRANSPARENT','FAIRYTOON_FUR_SHELL']])
    for(const rootMode of [0,1,2,3]) for(const mode2 of [0,3]) for(const mode3 of [0,3]) for(const use2 of [0,1]) for(const use3 of [0,1]) {
        const actual=scalarHlsl(needed,defines,{_DissolveParams:{x:rootMode},_Main2ndDissolveParams:{x:mode2},_Main3rdDissolveParams:{x:mode3},_UseMain2ndTex:use2,_UseMain3rdTex:use3,round:Math.round});
        const allowed=!defines.includes('FAIRYTOON_GEM')&&!defines.includes('FAIRYTOON_FUR_SHELL');
        const rootAllowed=defines.some(d=>['_SURFACE_TYPE_TRANSPARENT','_ALPHATEST_ON'].includes(d));
        assert.equal(actual,allowed&&((rootAllowed&&rootMode===3)||(defines.includes('_FT_MAIN_LAYERS')&&((use2===1&&mode2===3)||(use3===1&&mode3===3)))));
    }
    const passes=read('Shaders/FairyToonPasses.hlsl');
    const outline=hlslBody(passes,'FairyToonOutlineVertexProgram'),motion=hlslBody(passes,'FairyToonMotionVertex');
    let outlineSlice=outline.slice(outline.indexOf('float3 positionOS ='),outline.indexOf('output.positionCS ='))+'\nreturn output.positionWS;';
    outlineSlice=outlineSlice.replace('input.positionOS.xyz + FairyToonOutlineOffsetOS(v)','add(input.positionOS.xyz, FairyToonOutlineOffsetOS(v))')
        .replace('positionWS -= GetWorldSpaceNormalizeViewDir(positionWS) * _OutlineZBias;','positionWS = subtractScaled(positionWS,GetWorldSpaceNormalizeViewDir(positionWS),_OutlineZBias);');
    const motionSlice=motion.slice(motion.indexOf('#if defined(FT_DISSOLVE_MOTION_PACKED)'))
        .replace(/UNITY_BRANCH/g,'')
        .replace(/output\.positionCSNoJitter\.z = positionOS\.x;[\s\S]*?output\.uv0\.z = positionOS\.z;/,'output.dissolvePositionOS = positionOS;')
        .replace('dissolveWS -= GetWorldSpaceNormalizeViewDir(dissolveWS) * _OutlineZBias;','dissolveWS = subtractScaled(dissolveWS,GetWorldSpaceNormalizeViewDir(dissolveWS),_OutlineZBias);');
    assert.doesNotMatch(motionSlice.replace(/\/\/[^\n]*/g,''),/towardCamera/,'motion-only depth push must not distort dissolve coordinates');
    const add=(a,b)=>a.map((x,i)=>x+b[i]), subtractScaled=(a,b,t)=>a.map((x,i)=>x-b[i]*t);
    const toWorld=v=>[2*v[1]+10,-3*v[0]-4,.5*v[2]+2],toObject=v=>[(v[1]+4)/-3,(v[0]-10)/2,(v[2]-2)/.5];
    const vertex=[-.4,.2,1.3],offset=[.03,-.07,.09],expanded=add(vertex,offset);
    const shared={add,subtractScaled,TransformObjectToWorld:toWorld,TransformWorldToObject:toObject,
        GetWorldSpaceNormalizeViewDir:v=>dissolveMath.normalize(subtractScaled([4,3,-2],v,1)),
        FairyToonNeedsDissolvePosition:()=>true};
    for(const bias of [-.12,0,.24]) {
        const ws=scalarHlsl(outlineSlice,[],{...shared,input:{positionOS:{xyz:vertex}},v:{},output:{},_OutlineZBias:bias,FairyToonOutlineOffsetOS:()=>offset});
        const transported=scalarHlsl(motionSlice,['FT_DISSOLVE_MOTION_PACKED','_FT_OUTLINE'],{...shared,positionOS:expanded,output:{},_OutlineZBias:bias}).dissolvePositionOS;
        const expected=toObject(ws);
        transported.forEach((x,i)=>assert.ok(Math.abs(x-expected[i])<1e-12,'nonidentity object transform and authored outline Z bias'));
        for(const shape of [0,1]) for(const useNoise of [0,1]) {
            const actual=executeDissolve({mode:3,shape,useNoise,position:transported}),reference=executeDissolve({mode:3,shape,useNoise,position:expected},true);
            assert.equal(actual.alpha,reference.alpha);assert.ok(Math.abs(actual.edge-reference.edge)<1e-12);
        }
    }
});
test('fractional dissolve selectors round like lilToon and real Mermaid position parameters retain their boundaries', () => {
    for(const mode of [.1,.49,.51,1.4,1.6,2.4,2.6,3.4]) for(const shape of [.1,.49,.51,1.4]) for(const useNoise of [0,1]) {
        const options={mode,shape,useNoise};
        const actual=executeDissolve(options),expected=executeDissolve(options,true);
        assert.equal(actual.alpha,expected.alpha);assert.ok(Math.abs(actual.edge-expected.edge)<1e-12);
        assert.deepEqual(actual.samples,expected.samples);
    }
    // Source EF5_M_HeadTrans: Params(2,1,.175,.2), Pos(1,-.1,0,0), NoiseStrength .5.
    const v=.63,boundary=.175*Math.sqrt(1.01)+.1*v;
    for(const offset of [-.02,.02]) {
        const uv=[boundary+offset,v],distance=(uv[0]-.1*uv[1])/Math.sqrt(1.01);
        const actual=executeRootDissolve({mode:2,shape:1,threshold:.175,width:.2,pos:[1,-.1,0,0],noise:.5,noiseStrength:.5,uv});
        assert.equal(actual.alpha,offset>0?.37:0);
        assert.ok(Math.abs(actual.edge-(1-Math.abs(distance-.175)/.2))<1e-12);
    }
    // Source EF5_M_BodyReplaceMaterial: Params(3,1,-1,1.1), Pos(0,1,0,0), NoiseStrength .1.
    for(const y of [-1.2,-1,-.8,0]) {
        const actual=executeRootDissolve({mode:3,shape:1,threshold:-1,width:1.1,pos:[0,1,0,0],noise:.5,noiseStrength:.1,position:[4,y,-9]});
        assert.equal(actual.alpha,y>-1?.37:0);
        assert.ok(Math.abs(actual.edge-(1-dissolveMath.saturate(Math.abs(y+1)/1.1)))<1e-12);
    }
});

test('dissolve position GUI edits one mixed-selection component and noise controls stay uniform defaults', () => {
    const gui=read('Editor/FairyToonGUI.cs'),position=hlslBody(gui,'DissolvePosition');
    const onGUI=hlslBody(gui,'DrawAdvanced');
    const order=['Glitter();','Gem();','Outline();','Dissolve();','Refraction();','Fur();','SeeThrough();','Stencil();','Rendering();'];
    for(let i=1;i<order.length;i++) assert.ok(onGUI.indexOf(order[i])>onGUI.indexOf(order[i-1]),'lilToon section order '+order[i]);
    assert.match(onGUI,/if \(AnyRunsWholeDissolve\(\)\) Dissolve\(\);/);
    assert.match(hlslBody(gui,'Dissolve'),/BeginSection\("Dissolve", "sDissolve"/);
    assert.doesNotMatch(hlslBody(gui,'MainColor'),/DissolveControls/);

    assert.match(position,/DissolveComponentMixed\(property, component\)/);
    assert.match(position,/EndChangeCheck\(\)\) SetDissolveComponents\(property, value, 1 << component\)/);
    assert.doesNotMatch(position,/Vector[234]Field/);
    assert.match(position,/finally[\s\S]*showMixedValue = previousMixed/);
    for(const variant of ['', 'Refraction', 'Fur', 'Gem']) {
        const props=properties(read('Shaders/FairyToon'+variant+'.shader'));
        for(const name of ['_FT_DissolveUseNoise','_FT_Main2ndDissolveUseNoise','_FT_Main3rdDissolveUseNoise']) {
            assert.equal(props.get(name).value,'1');assert.equal(props.get(name).type,'Float');
            assert.ok(input.includes(name+';'));
        }
    }
    const uniforms=['_FT_DissolveUseNoise','_FT_Main2ndDissolveUseNoise','_FT_Main3rdDissolveUseNoise'];
    for(const name of uniforms) assert.ok(!setup.includes('SetKeyword(material, "'+name+'"'));
});
test('all dissolve property families preserve lilToon defaults, coverage order and uniform selection', () => {
    const dissolveNames = ['_Dissolve','_Main2ndDissolve','_Main3rdDissolve'].flatMap(prefix => ['Params','Pos','Mask','NoiseMask','NoiseMask_ScrollRotate','NoiseStrength','Color'].map(suffix => prefix+suffix));
    for (const suffix of ['', 'Refraction', 'Fur', 'Gem']) {
        const props = properties(read('Shaders/FairyToon' + suffix + '.shader'));
        for (const n of dissolveNames) {
            assert.equal(props.get(n).type.toLowerCase(), reference.get(n).type.toLowerCase(), n + '/' + suffix + ' type');
            assert.equal(props.get(n).value, reference.get(n).value, n + '/' + suffix + ' default');
        }
    }
    const layered = hlslBody(input, 'FairyToonLayeredBase');
    assert.ok(layered.indexOf('FairyToonApplyDissolve(') > layered.indexOf('FairyToonApplyAlphaMask('));
    assert.match(layered, /FairyToonApplyDissolve\(col\.a, c\.uv0, c\.positionOS, dissolveEdge\)/);
    assert.match(hlslBody(input,'FairyToonBaseColor'), /FairyToonApplyDissolve\(col\.a, uv0, positionOS, dissolveEdge\)/);
    assert.match(converter, /int dissolveMode = Mathf\.RoundToInt\(dissolve\.x\)/);
    assert.match(converter, /dissolveMode < 1 \|\| dissolveMode > 3/);
    assert.match(read('Editor/FairyToonGUI.cs'), /DissolveControls\(prefix \+ "Dissolve", false\)/);
    for (const f of ['Shaders/FairyToon.shader','Editor/FairyToonMaterialSetup.cs'])
        assert.ok(!read(f).includes('_FT_DISSOLVE'), 'uniform branch adds no keyword dimension');
});

test('dissolve edge follows emissions and contributes alpha once in Transparent and optical paths', () => {
    const core = read('Shaders/FairyToonCore.hlsl'), optics = read('Shaders/FairyToonVariants.hlsl');
    const shade = hlslBody(core, 'FairyToonShade'), layers = hlslBody(optics,'FairyToonOpticalLayers');
    assert.ok(shade.indexOf('_DissolveColor') > shade.indexOf('FairyToonEmission('));
    assert.ok(shade.indexOf('_DissolveColor') < shade.indexOf('FairyToonBackfaceColor('));
    assert.ok(layers.indexOf('_DissolveColor') > layers.indexOf('FairyToonEmissionLayer('));
    const straightExpr = shade.match(/col \+= ([^;]*_DissolveColor[^;]*);/)?.[1];
    const opticalExpr = layers.match(/col \+= ([^;]*_DissolveColor[^;]*);/)?.[1];
    assert.ok(straightExpr && opticalExpr);
    const straight = new Function('_DissolveColor','s','_Main2ndDissolveColor','_Main3rdDissolveColor','return '+straightExpr);
    const optical = new Function('_DissolveColor','s','coverage','_Main2ndDissolveColor','_Main3rdDissolveColor','return '+opticalExpr);
    for (const alpha of [0,0.3,0.957,1]) for (const edge of [0,0.25,1])
    for (const edge2 of [0,.4,1]) for (const edge3 of [0,.7,1]) {
        const color={rgb:2.9960787,a:-4}, color2={rgb:1.7,a:0}, color3={rgb:3.2,a:.2};
        const s={alpha,dissolveEdge:edge,layerDissolveEdges:{x:edge2,y:edge3},layer2:{a:0},layer3:{a:.1}};
        const sum=color.rgb*edge+color2.rgb*edge2+color3.rgb*edge3;
        assert.ok(Math.abs(straight(color,s,color2,color3)*alpha-sum*alpha)<1e-12);
        assert.equal(optical(color,s,1,color2,color3),sum); // Refraction replaces the framebuffer.
        assert.ok(Math.abs(optical(color,s,alpha,color2,color3)-sum*alpha)<1e-12);
    }
});


// Follow local includes/directives in ShaderLab order up to the first definition of the helper.
// External URP includes cannot define FairyToon pass flags; skip them. Include guards are honoured.
function definesAtPassHelper(shaderText, passName, coverage, helper, returnMacros = false) {
    const common = [...shaderText.matchAll(/\bHLSLINCLUDE\b([\s\S]*?)\bENDHLSL\b/g)].map(m => m[1]).join('\n');
    const pass = [...shaderText.matchAll(/\bPass\s*\{([\s\S]*?)\bENDHLSL\s*\}/g)]
        .map(m => m[1]).find(block => block.match(/Name\s+"([^"]+)"/)?.[1] === passName);
    assert.ok(pass, passName);
    const macros = new Map(coverage.map(n => [n, '1']));
    const condition = expression => new Function('return ' + expression.replace(/\/\/.*$/, '')
        .replace(/defined\s*\(\s*(\w+)\s*\)/g, (_, n) => macros.has(n) ? '1' : '0')
        .replace(/\b[A-Za-z_]\w*\b/g, n => /^\d+$/.test(macros.get(n) || '') ? macros.get(n) : '0'))();
    function visit(source) {
        let active = true;
        const stack = [];
        for (const line of source.replace(/\\\r?\n/g, '').split('\n')) {
            let m;
            if ((m = line.match(/^\s*#(if|ifdef|ifndef)\s+(.+)/))) {
                const yes = m[1] === 'if' ? condition(m[2]) : m[1] === 'ifdef' ? macros.has(m[2].trim()) : !macros.has(m[2].trim());
                stack.push({parent: active, taken: !!yes}); active = active && !!yes;
            } else if ((m = line.match(/^\s*#elif\s+(.+)/))) {
                const top = stack.at(-1), yes = !top.taken && condition(m[1]);
                active = top.parent && !!yes; top.taken ||= !!yes;
            } else if (/^\s*#else\b/.test(line)) {
                const top = stack.at(-1); active = top.parent && !top.taken; top.taken = true;
            } else if (/^\s*#endif\b/.test(line)) {
                active = stack.pop().parent;
            } else if (active) {
                if (line.includes('void ' + helper + '(')) return returnMacros ? macros : [...macros.keys()];
                if ((m = line.match(/^\s*#define\s+(\w+)\b(.*)/))) macros.set(m[1], m[2].trim() || '1');
                else if ((m = line.match(/^\s*#undef\s+(\w+)/))) macros.delete(m[1]);
                else if ((m = line.match(/^\s*#include\s+"(FairyToon[^"/]+\.hlsl)"/))) {
                    const found = visit(read('Shaders/' + m[1])); if (found) return found;
                }
            }
        }
    }
    const result = visit(common + '\n' + pass.slice(pass.indexOf('HLSLPROGRAM') + 'HLSLPROGRAM'.length));
    assert.ok(result, passName + ' reaches ' + helper);
    return result;
}

test('Fur dissolve is disabled in real shell include order and retained on body/coverage passes', () => {
    const fur = read('Shaders/FairyToonFur.shader');
    const body = hlslBody(input, 'FairyToonApplyDissolve') + '\nreturn {alpha, edge};';
    for (const coverage of [['_SURFACE_TYPE_TRANSPARENT'], ['_ALPHATEST_ON']])
    for (const pass of ['Forward', 'FurForward', 'FurSecond', 'ShadowCaster', 'DepthOnly', 'DepthNormals', 'MotionVectors']) {
        const defines = definesAtPassHelper(fur, pass, coverage, 'FairyToonApplyDissolve');
        assert.ok(!defines.includes('FAIRYTOON_FUR_INCLUDED'), 'Fur include guard has not been defined when Input is read');
        const shell = pass === 'FurForward' || pass === 'FurSecond';
        assert.equal(defines.includes('FAIRYTOON_FUR_SHELL'), shell);
        for(const mode of [1,2,3]) for(const useNoise of [0,1]) {
            const result=executeRootDissolve({alpha:.7,mode,useNoise,threshold:10},defines);
            assert.equal(result.alpha,shell?.7:0,pass+' coverage');
            assert.equal(result.samples.length,shell?0:(mode===1?1:0)+useNoise,pass+' dissolve sampling');
            if(shell) assert.equal(result.edge,0);
        }
    }
    assert.ok(!read('../jp.lilxyzw.liltoon/Shader/Includes/lil_pass_forward_fur.hlsl').includes('OVERRIDE_DISSOLVE'));
});

test('dissolve notices use final target coverage after conversion profiles and include mixed GUI selections', () => {
    const call = converter.indexOf('ReportInactiveDissolve(snapshot, target, report);');
    assert.ok(call > converter.indexOf('ApplyProfiles(source, target, profiles, report);'));
    assert.ok(call > converter.indexOf('FairyToonMaterialSetup.Apply(target);'));
    const notice = converter.slice(converter.indexOf('private static void ReportInactiveDissolve('), converter.indexOf('private static void ReportVectorFeature('));
    assert.match(notice, /GetRenderingMode\(target\)/);
    assert.match(notice, /HasActiveDissolve\(source, "_Dissolve"\)/);
    assert.match(notice, /mode == FairyToonMaterialSetup\.Opaque \|\| mode == FairyToonMaterialSetup\.Gem/);
    const gui = read('Editor/FairyToonGUI.cs');
    assert.match(gui, /mode != FairyToonMaterialSetup\.Opaque && mode != FairyToonMaterialSetup\.Gem/);
    assert.match(gui, /EverySelectedIsGem\(\)/);
});


test('Mermaid gamma HDR colour flags match lilToon in every shader without changing native HDR emission', () => {
    const lil = read('../jp.lilxyzw.liltoon/Shader/lts_trans.shader');
    const declaration = (text, name) => text.split('\n').find(line => new RegExp('\\b' + name + '\\s*\\(').test(line));
    const isHDR = (text, name) => /\[HDR\]/.test(declaration(text, name));
    // Metadata is the native upload contract. This CPU curve illustrates the difference; it does not
    // measure Unity's native Color.linear implementation (particularly its extension above 1).
    const decode = x => x <= 0.04045 ? x / 12.92 : x <= 1 ? ((x + 0.055) / 1.055) ** 2.4 : x ** 2.2;
    const upload = (text, name, rgba, linear) => rgba.map((x, i) => linear && i < 3 && !isHDR(text, name) ? decode(x) : x);
    const cases = [['_BacklightColor', [7.377215, 7.377215, 7.377215, 1]], ['_Color2nd', [0.7329843, 0.9367802, 2, 1]], ['_Color3rd', [0.2, 0.6, 3, 0.4]],
        ['_DissolveColor', [0.67450976, 0.795309, 2.9960787, 1]], ['_Main2ndDissolveColor', [.3, 1.4, 2.7, 0]],
        ['_Main3rdDissolveColor', [2.2, .4, 3.1, .2]], ['_GlitterColor', [16, 16, 16, 1]], ['_EmissionColor', [2, 2, 2, 1]], ['_Emission2ndColor', [0.2, 2, 32, 0.3]]];
    for (const suffix of ['', 'Refraction', 'Fur', 'Gem']) {
        const text = read('Shaders/FairyToon' + suffix + '.shader');
        for (const [name, saved] of cases) {
            assert.equal(isHDR(text, name), isHDR(lil, name), suffix + '/' + name + ' native HDR flag');
            for (const linear of [false, true]) {
                const result = upload(text, name, saved, linear);
                assert.deepEqual(result, upload(lil, name, saved, linear));
                assert.equal(result[3], saved[3], 'alpha never receives gamma conversion');
                if (!linear) assert.deepEqual(result, saved);
            }
            if (!name.startsWith('_Emission')) assert.match(declaration(text, name), /\[FairyToonHDR\]/);
        }
    }
    const gui = read('Editor/FairyToonGUI.cs');
    assert.match(gui, /class FairyToonHDRDrawer : MaterialPropertyDrawer/);
    assert.match(gui, /EditorGUI\.ColorField\(field, GUIContent\.none, value, true, true, true\)/);
    assert.match(gui, /FairyToonHDRDrawer\.Draw\([^;]+\)\) other\.colorValue = color/);
    assert.ok(!/prop\.colorValue\s*=.*\.(?:linear|gamma)/.test(gui), 'drawer preserves raw authored values');
    const coreAndInput = read('Shaders/FairyToonCore.hlsl') + input;
    assert.ok(!/(?:SRGBToLinear|GammaToLinearSpace)\(_(?:Color2nd|Color3rd|BacklightColor|(?:Main2nd|Main3rd)?DissolveColor)\)/.test(coreAndInput), 'no double decode');
    assert.match(read('../jp.lilxyzw.liltoon/Editor/lilOptimizer.cs'), /Color c = ShouldLinear\(shader, name\) \? v\.c\.linear : v\.c/);
});

test('frozen fin sequence keeps source premultiplied and FairyToon straight framebuffer RGB/alpha equivalent', () => {
    const core = read('Shaders/FairyToonCore.hlsl');
    const sourcePass = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_pass_forward_normal.hlsl');
    const sourceFrag = read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_frag.hlsl');
    const sourceBlend = hlslBody(read('../jp.lilxyzw.liltoon/Shader/Includes/lil_common_functions.hlsl'), 'lilBlendColor');
    const blend = (dstCol, srcCol, srcA, blendMode) => scalarHlsl(sourceBlend, [], {dstCol, srcCol, srcA, blendMode,
        max: Math.max, lerp: (a,b,t) => a+(b-a)*t});
    const sat = x => Math.min(1, Math.max(0,x));
    const clamp = (x,lo,hi) => Math.min(hi, Math.max(lo,x));
    const post = (col,layer,amount,mode,alpha,defines) => scalarHlsl(hlslBody(core, 'FairyToonTransparentBlend'), defines,
        {col,layer,amount,mode,alpha,applyTransparency:1,max:Math.max,clamp});
    const emit = (col,emission,amount,alpha) => scalarHlsl(hlslBody(core, 'FairyToonEmissionBlend'), [],
        {col,emission,amount,mode:1,alpha,max:Math.max});
    const order = ['OVERRIDE_ALPHAMASK','OVERRIDE_DISSOLVE','LIL_PREMULTIPLY','OVERRIDE_REFLECTION','OVERRIDE_MATCAP',
        'OVERRIDE_MATCAP_2ND','OVERRIDE_RIMLIGHT','OVERRIDE_EMISSION_1ST','OVERRIDE_DISSOLVE_ADD'];
    for (let i=1; i<order.length; i++) assert.ok(sourcePass.indexOf(order[i]) > sourcePass.indexOf(order[i-1]), order[i]);
    assert.match(sourceFrag, /#define LIL_PREMULTIPLY fd\.col\.rgb \*= fd\.col\.a/);
    assert.match(sourceFrag, /emissionBlend \*= fd\.col\.a/);
    // Actual shader inputs after upload. Fix animation samples explicitly: no moving noise/hex/emission ablation.
    for (const endfield of [false,true]) for (const mask of [0,0.002,0.2,0.6,1]) for (const noise of [0,0.5,1]) {
        const defines = ['_SURFACE_TYPE_TRANSPARENT', ...(endfield ? ['_FT_ENDFIELD'] : [])];
        const col = {a:0.95686275};
        scalarHlsl(hlslBody(input,'FairyToonApplyAlphaMask'), defines, {col,uvMain:0.3,
            _AlphaMaskMode:1,_AlphaMaskScale:1,_AlphaMaskValue:0,_AlphaMask_ST:{xy:1,zw:0},sampler_MainTex:0,
            SAMPLE_TEXTURE2D:()=>({r:mask}),saturate:sat});
        const coverage=executeRootDissolve({alpha:col.a,mask,noise,threshold:0,width:.668,noiseStrength:1},defines);
        const alpha = mask * (mask+noise-0.5 > 0 ? 1 : 0);
        assert.equal(coverage.alpha,alpha,'Replace alpha then dissolve; main tint alpha is discarded');
        assert.ok(Math.abs(coverage.edge-(1-sat(Math.abs(mask+noise-0.5)/0.668)))<1e-12);
        if (alpha < 0.001) continue; // Both fragment paths clip this sample before lighting.
        for (let channel=0;channel<3;channel++) {
            // Different diffuse illumination is allowed; the post stack must match for either context.
            const base = [0.3,0.25,1.2][channel] * (endfield ? 0.8 : 0.6);
            let straight = base, premult = base*alpha;
            // Metallic diffuse reduction, additive specular, MatCap1, MatCap2 (all active fin ApplyTransparency=1).
            straight *= 1-0.15; premult *= 1-0.15;
            for (const [layer,amount] of [[0.4,0.2],[0.7,0.6],[0.5,0.617]]) {
                straight = post(straight,layer,amount,1,alpha,defines);
                premult = blend(premult,layer,amount*alpha,1);
            }
            const emission = [0.5,0.8,3][channel], edgeColor = [0.4,0.6,11][channel];
            straight = emit(straight,emission,0.7,alpha);
            premult = blend(premult,emission,0.7*alpha,1);
            const edgeExpression = hlslBody(core,'FairyToonShade').match(/col \+= ([^;]*_DissolveColor[^;]*);/)[1];
            straight += new Function('_DissolveColor','s','_Main2ndDissolveColor','_Main3rdDissolveColor','return '+edgeExpression)({rgb:edgeColor},{dissolveEdge:coverage.edge,layerDissolveEdges:{x:0,y:0}},{rgb:0},{rgb:0});
            premult += edgeColor*coverage.edge*alpha;
            for (const background of [0,0.05,0.5]) {
                assert.ok(Math.abs(straight*alpha+background*(1-alpha)-(premult+background*(1-alpha)))<1e-10,
                    'One vs SrcAlpha final RGB on black/dark/light backgrounds');
            }
            const backgroundAlpha=0.3;
            assert.equal(alpha+backgroundAlpha*(1-alpha),coverage.alpha+backgroundAlpha*(1-coverage.alpha));
        }
    }
    const fragment = hlslBody(read('Shaders/FairyToonPasses.hlsl'),'FairyToonForwardFragment');
    assert.match(fragment,/return half4\(col, s\.alpha\)/);
    assert.match(setup,/SetInt\("_SrcBlend", \(int\)BlendMode\.SrcAlpha\)/);
    assert.match(setup,/SetInt\("_SrcBlendAlpha", \(int\)BlendMode\.One\)/);
});


test('world dissolve position helper skips matrix work when object coordinates are unused', () => {
    const body=hlslBody(input,'FairyToonDissolvePositionOS');
    assert.equal((body.match(/\breturn\s+\w+\s*;/g)||[]).length, 1,
        'D3D12 branch lowering needs an initialized result and one return');
    assert.match(body,/UNITY_BRANCH\s+if\s*\(FairyToonNeedsDissolvePosition\(\)\)/);
    for (const active of [false,true]) {
        let matrices=0;
        const value=scalarHlsl(body.replace(/UNITY_BRANCH/g,''),[],{
            positionWS:[2,3,4], FairyToonNeedsDissolvePosition:()=>active,
            TransformWorldToObject:v=>{matrices++;return v;},float3:(...v)=>v
        });
        assert.deepEqual(value,active?[2,3,4]:[0,0,0]);
        assert.equal(matrices,active?1:0);
    }
});

function dissolveTransport(pass, flags=[]) {
    const macros=definesAtPassHelper(shader,pass,['_ALPHATEST_ON',...flags],'FairyToonApplyDissolve',true);
    const transfer=macros.get('FT_TRANSFER_DISSOLVE').replace(/^\(i,o\)\s*/, '').replace(/\bi\./g,'input.').replace(/\bo\./g,'output.');
    const expression=macros.get('FT_DISSOLVE_POSITION_OS').replace(/^\(i\)\s*/, '').replace(/\bi\./g,'output.');
    return {macros,transfer,expression};
}
function transportVertex(pass, position, clip, flags=[]) {
    const transport=dissolveTransport(pass,flags);
    const output={positionCS:dissolveVector(...clip),uv0:dissolveVector(.2,.7,0,0),normalWS:dissolveVector(.1,.2,.9,0),
        tangentWS:dissolveVector(.8,.6,0,-1),dissolvePositionOS:dissolveVector(0,0,0)};
    scalarHlsl(transport.transfer,[],{input:{positionOS:dissolveVector(...position)},output});
    const value=scalarHlsl('return '+transport.expression+';',[],{output,float3:(...v)=>v.flat()});
    return {output,value:[...value]};
}

test('all auxiliary OS paths avoid inverse VP and retain transport with foveated defines', () => {
    for(const source of [input,read('Shaders/FairyToonSeeThrough.hlsl')]) {
        assert.doesNotMatch(source.replace(/\/\/[^\n]*/g,''),/UNITY_MATRIX_I_VP|ComputeWorldSpacePosition|GetScaledScreenParams|SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER/);
    }
    for(const pass of ['DepthOnly','DepthNormals','SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ']) {
        const actual=dissolveTransport(pass);
        const foveated=dissolveTransport(pass,['SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER']);
        assert.equal(actual.transfer,foveated.transfer);assert.equal(actual.expression,foveated.expression);
        assert.doesNotMatch(actual.expression,/positionCS|FromCS|SeeThroughDissolvePosition/);
        const layered=dissolveTransport(pass,['_FT_MAIN_LAYERS']);
        assert.equal(layered.expression,'FairyToonDissolvePositionOS(output.layerPosition)');
    }
});

test('depth packing preserves normal-map inputs and interpolates OS independently of target orientation and viewport', () => {
    const vertices=[[-.4,.2,1.3],[.7,.4,3.1],[.1,.7,2.5]],weights=[.2,.3,.5];
    // Nonuniform mirrored object transform; camera horizontal centre is world y=1.4.
    const toWorld=v=>[-2*v[0]+.4,3*v[1]+1.4,.5*v[2]+4];
    const barycentric=(p,a,b,c)=>{
        const det=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1]);
        const x=((b[1]-c[1])*(p[0]-c[0])+(c[0]-b[0])*(p[1]-c[1]))/det;
        const y=((c[1]-a[1])*(p[0]-c[0])+(a[0]-c[0])*(p[1]-c[1]))/det;
        return [x,y,1-x-y];
    };
    for(const pass of ['DepthOnly','DepthNormals','SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ'])
    for(const perspective of [false,true])for(const flip of [-1,1])for(const scale of [.5,1,1.5])
    for(const origin of [[0,0],[137,91]])for(const jitter of [0,.013]){
        const clips=vertices.map(v=>{
            const world=toWorld(v).map((x,i)=>x-(pass==='SeeThroughFakeShadow'?[.4,-.2,.7][i]:0));
            const w=perspective?world[2]:1;
            return [world[0]+jitter*w,flip*(world[1]-1.4)-jitter*w,.3,w];
        });
        const screens=clips.map(c=>[origin[0]+(c[0]/c[3]+1)*720*scale,origin[1]+(1-c[1]/c[3])*405*scale]);
        const pixel=[0,1].map(k=>screens.reduce((sum,p,i)=>sum+weights[i]*p[k],0));
        const rasterWeights=barycentric(pixel,...screens);
        const denominator=clips.reduce((sum,c,i)=>sum+rasterWeights[i]/c[3],0);
        const interpolate=values=>values.reduce((sum,v,i)=>sum+rasterWeights[i]*v/clips[i][3],0)/denominator;
        const packed=vertices.map((v,i)=>transportVertex(pass,v,clips[i]));
        const actual=[0,1,2].map(k=>interpolate(packed.map(p=>p.value[k])));
        const expected=[0,1,2].map(k=>interpolate(vertices.map(v=>v[k])));
        actual.forEach((v,k)=>assert.ok(Math.abs(v-expected[k])<1e-12,pass));
        for(const {output} of packed){
            assert.deepEqual([...output.uv0.xy],[.2,.7]);
            assert.deepEqual([...output.tangentWS],[.8,.6,0,-1]);
            if(pass==='DepthNormals')assert.deepEqual([...output.normalWS.xyz],[.1,.2,.9]);
        }
        for(const mode of [0,1,2,3]){
            const options={mode,shape:1,useNoise:0,threshold:0,pos:[0,1,0,0]};
            const got=executeRootDissolve({...options,position:actual}),want=executeRootDissolve({...options,position:expected});
            assert.equal(got.alpha,want.alpha);assert.equal(got.edge,want.edge);
        }
    }
    const depth=hlslBody(read('Shaders/FairyToonPasses.hlsl'),'FairyToonDepthVertex');
    assert.ok(depth.indexOf('FT_TRANSFER_LAYERS')>depth.indexOf('output.normalWS.xyz ='),'packing follows geometric normal writes');
    const normals=hlslBody(read('Shaders/FairyToonPasses.hlsl'),'FairyToonDepthNormalsFragment');
    assert.match(normals,/float3 normalWS = input.normalWS.xyz;/);
    assert.match(normals,/cross\(input.normalWS.xyz, input.tangentWS.xyz\)/);
    assert.match(normals,/half3x3\(input.tangentWS.xyz, bitangent, input.normalWS.xyz\)/);
    assert.match(normals,/input.uv0.xy \* _MainTex_ST.xy/);
});

test('backbuffer inverse-VP mismatch flips the old height cut but cannot alter transported OS', () => {
    // Reproduce DepthOnly's flipped raster VP with an unflipped inverse VP. With y=0.4
    // relative to the camera centre, old raw-raster reconstruction returns -0.4.
    const os=[.2,.4,2],clip=[.2,-.4,.3,2];
    const rawUV=[clip[0]/clip[3]*.5+.5,1-(clip[1]/clip[3]*.5+.5)];
    const ndcY=-(rawUV[1]*2-1);
    const oldReconstructed=[os[0],ndcY*clip[3],os[2]];
    const options={mode:3,shape:1,useNoise:0,threshold:0,pos:[0,1,0,0]};
    const expected=executeRootDissolve({...options,position:os});
    assert.equal(expected.alpha,.37);
    assert.equal(executeRootDissolve({...options,position:oldReconstructed}).alpha,0,'fixture detects Round 4 mirrored height');
    for(const pass of ['DepthOnly','DepthNormals','SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ']){
        const actual=transportVertex(pass,os,clip).value;
        assert.equal(executeRootDissolve({...options,position:actual}).alpha,expected.alpha);
        const mutant=actual.map((v,i)=>i===1?-v:v);
        assert.notEqual(executeRootDissolve({...options,position:mutant}).alpha,expected.alpha,'mirrored transport mutation is detected');
    }
});

test('motion packing preserves clip XYW and UVxy through perspective interpolation', () => {
    const motion=hlslBody(read('Shaders/FairyToonPasses.hlsl'),'FairyToonMotionVertex');
    const writes=motion.match(/output\.positionCSNoJitter\.z = positionOS\.x;[\s\S]*?output\.uv0\.z = positionOS\.z;/)?.[0];
    assert.ok(writes,'actual packing assignments');
    const positions=[[-.4,.2,1.3],[.7,.4,3.1],[.1,-.3,2.5]],weights=[.2,.3,.5],rasterW=[2,3,7];
    const outputs=positions.map((p,i)=>{
        const output={positionCSNoJitter:{x:11+i,y:22+i,z:33+i,w:44+i},
            previousPositionCSNoJitter:{x:55+i,y:66+i,z:77+i,w:88+i},uv0:{x:.2+i,y:.3+i,z:0}};
        const before=structuredClone(output);
        scalarHlsl(writes,[],{output,positionOS:{x:p[0],y:p[1],z:p[2]}});
        for(const field of ['positionCSNoJitter','previousPositionCSNoJitter'])
            for(const component of ['x','y','w'])assert.equal(output[field][component],before[field][component]);
        for(const c of ['x','y'])assert.equal(output.uv0[c],before.uv0[c]);
        return [output.positionCSNoJitter.z,output.previousPositionCSNoJitter.z,output.uv0.z];
    });
    const interpolate=values=>values.reduce((s,v,i)=>s+weights[i]*v/rasterW[i],0)/weights.reduce((s,v,i)=>s+v/rasterW[i],0);
    for(let k=0;k<3;k++)assert.equal(interpolate(outputs.map(p=>p[k])),interpolate(positions.map(p=>p[k])));
    const cache=path.resolve(root,'../../Library/PackageCache');
    const urp=fs.readdirSync(cache).find(n=>n.startsWith('com.unity.render-pipelines.universal@'));
    const motionHelpers=fs.readFileSync(path.join(cache,urp,'ShaderLibrary/MotionVectorsCommon.hlsl'),'utf8');
    const helper=hlslBody(motionHelpers,'CalcNdcMotionVectorFromCsPositions');
    assert.doesNotMatch(helper,/\b(?:posCS|prevPosCS)\.[xyzw]*z/,'packed Z must remain unused by the installed motion helper');
    const asw=hlslBody(motionHelpers,'CalcAswNdcMotionVectorFromCsPositions');
    assert.match(asw,/\bposCS\.[xyzw]*z/,'ASW consumes the clip Z overwritten by OS packing');
    const fragment=hlslBody(read('Shaders/FairyToonPasses.hlsl'),'FairyToonMotionFragment');
    assert.match(fragment,/CalcNdcMotionVectorFromCsPositions\(input.positionCSNoJitter, input.previousPositionCSNoJitter\)/);
    assert.doesNotMatch(fragment,/CalcAswNdcMotionVectorFromCsPositions\s*\(/,'ASW requires a separate clip-Z-preserving layout');
    assert.match(motion,/CalcAswNdcMotionVectorFromCsPositions reads posCS.z;[\s\S]*?must not be used with this packing/);
    assert.match(input,/#define FT_DISSOLVE_POSITION_OS\(i\) float3\(i.positionCSNoJitter.z, i.previousPositionCSNoJitter.z, i.uv0.z\)/);
});

test('see-through direct OS survives fake-shadow clip shifts and preserves mesh UV', () => {
    const source=read('Shaders/FairyToonSeeThrough.hlsl');
    const base=hlslBody(source,'FairyToonSeeThroughVertex');
    for(const pass of ['SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ']){
        const transport=dissolveTransport(pass);
        const baseSlice=base.slice(base.indexOf('output.positionCS ='))
            .replace('FT_TRANSFER_LAYERS(input, output)',transport.transfer);
        const output=scalarHlsl(baseSlice,[],{
            output:{uv0:dissolveVector(0,0)}, input:{positionOS:dissolveVector(1,2,3),uv0:[.2,.3]},
            TransformObjectToHClip:()=>dissolveVector(.3,.4,.8,2)
        });
        assert.deepEqual([...output.uv0],[.2,.3]);assert.deepEqual([...output.dissolvePositionOS],[1,2,3]);
        const fake=hlslBody(source,'FairyToonFakeShadowVertex');
        const fakeSlice=fake.slice(fake.indexOf('output.positionCS -='))
            .replace('output.positionCS -= lightShiftCS;','output.positionCS = subtract(output.positionCS,lightShiftCS);');
        scalarHlsl(fakeSlice,[],{output,lightShiftCS:dissolveVector(-.2,.1,.06,.17),subtract:(a,b)=>dissolveVector(...a.map((v,i)=>v-b[i]))});
        assert.deepEqual([...output.dissolvePositionOS],[1,2,3]);assert.deepEqual([...output.uv0],[.2,.3]);
        assert.notEqual(output.positionCS.w,2);
        assert.match(fake,/float4\(FairyToonFakeShadowOffsetWS\(\), 0\)/);
    }
});


test('lilToon conversion disables SSAO only for the Toon destination', () => {
    const body = hlslBody(converter, 'ApplyLilToonSSAO');
    for (const [model, overrides, expected] of [[0, {}, [0, 0]], [1, {}, [1, 0.2]],
        [0, {_SSAOStrength:.65}, [.65,0]], [0, {_SSAOLitStrength:.07}, [0,.07]],
        [1, {_SSAOStrength:0,_SSAOLitStrength:0}, [0,0]]]) {
        const values = { _ShadingModel: model, _SSAOStrength: 1, _SSAOLitStrength: 0.2, ...overrides };
        const code = body.replace(/([01])f\b/g, '$1');
        new Function('target', 'Mathf', 'FairyToonMaterialSetup', 'profileProperties', code)(
            { GetFloat: n => values[n], SetFloat: (n,v) => values[n] = v },
            { RoundToInt: Math.round }, { ShadingLilToon: 0 }, { Contains: n => Object.hasOwn(overrides,n) });
        assert.deepEqual([values._SSAOStrength, values._SSAOLitStrength], expected);
    }
    assert.ok(converter.indexOf('ApplyLilToonSSAO(target, profileProperties);') > converter.indexOf('ApplyProfiles(source, target, profiles, report);'),
        'destination profile determines the shading model before parity defaults');
});


test('Toon RimShade multiplies the completed shadow once, including ShadowMainStrength', () => {
    const body = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonShadeBase');
    for (const rimShade of [0.25, 0.5, 1]) {
        const actual = scalarHlsl(body, ['_FT_RIMSHADE'], {
            s: { albedo: 0.8 }, l: {}, _LightMaxLimit: 1,
            FairyToonRimShadeMultiplier: () => rimShade,
            FairyToonGetLight: () => ({lightColor:1, addLightColor:0, aoLit:1, shadowmix:0}),
            // Authored shadow colour .6, albedo .8, active hair ShadowMainStrength .464.
            FairyToonDiffuse: s => 0.6 * s.albedo * (1 - 0.464 + 0.464 * s.albedo),
            FairyToonAdditionalLights: () => {}, FairyToonMainLayersUnlit: (s,col) => col,
            min: Math.min, saturate: x => Math.max(0,Math.min(1,x)), Luminance: x => x,
        });
        assert.ok(Math.abs(actual - 0.435456 * rimShade) < 1e-12);
    }
});


test('unlit main layers receive Toon RimShade exactly once for every blend mode', () => {
    const body = hlslBody(read('Shaders/FairyToonCore.hlsl'), 'FairyToonMainLayersUnlit');
    const blend = hlslBody(input, 'FairyToonBlend');
    const lerp = (a,b,t) => a+(b-a)*t;
    for (const endfield of [false,true]) for (const layer of [2,3]) for (const mode of [0,1,2,3]) for (const lighting of [0,.5,1]) {
        const incoming = endfield ? .16 : .4, rim = .4;
        const actual = scalarHlsl(body, ['_FT_MAIN_LAYERS', ...(endfield?['_FT_ENDFIELD']:[])], {
            col: incoming, s:{rimShade:rim,layer2:{rgb:.7,a:.6},layer3:{rgb:.7,a:.6}},
            _UseMain2ndTex:layer===2?1:0, _UseMain3rdTex:layer===3?1:0,
            _Main2ndEnableLighting:lighting,_Main3rdEnableLighting:lighting,
            _Main2ndTexBlendMode:mode,_Main3rdTexBlendMode:mode,
            FairyToonBlend:(dst,src,srcA,mode,multiplier=1)=>scalarHlsl(blend,[],{dst,src,srcA,mode,multiplier,max:Math.max,lerp}),
        });
        const mul=endfield?rim:1;
        const mixed=[.7*mul,incoming+.7*mul,Math.max(incoming+.7*mul-incoming*.7,incoming),incoming*.7][mode];
        const want=lerp(incoming,mixed,.6*(1-lighting))*(endfield?1:rim);
        assert.ok(Math.abs(actual*(endfield?1:rim)-want)<1e-12);
    }
});


// Wetness contracts plus arithmetic evaluated from the actual HLSL (no Unity process).
const wetSource = read('Shaders/FairyToonWet.hlsl');
const wetProperties = {
    _UseWet: ['Int', '0'], _WetPorosity: ['Range(0,1)', '0.7'], _WetDarken: ['Range(0,1)', '0.2'],
    _WetSaturation: ['Range(0.5,2)', '1.1'], _WetSmoothness: ['Range(0,1)', '0.85'],
    _WetSpecular: ['Range(0,2)', '0.5'], _WetEdgeDarken: ['Range(0,1)', '0.15'],
    _WetEdgeNoise: ['Range(0,0.1)', '0.02'],
    _WetStreakAmount: ['Range(0,1)', '0.55'], _WetStreakScale: ['Float', '0.006'],
    _WetStreakDarken: ['Range(0,1)', '0.45'], _WetStreakBump: ['Range(0,1)', '0.15'],
    _WetStreakFlow: ['Range(0,0.2)', '0.03'], _WetStreakAxis: ['Float', '0'],
    _WetFilm: ['Range(0,2)', '0.7'], _WetFilmStretch: ['Range(0,1)', '0.85'],
    _WetDropAmount: ['Range(0,1)', '0.04'], _WetDropScale: ['Float', '0.025'],
};
const wetDetailProperties = [
    '_WetStreakAmount', '_WetStreakScale', '_WetStreakDarken', '_WetStreakBump', '_WetStreakFlow',
    '_WetStreakAxis', '_WetFilm', '_WetFilmStretch', '_WetDropAmount', '_WetDropScale',
];
test('wet detail controls keep the declared material order and unconditional buffer layout', () => {
    const buffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    const names = [...current.keys()];
    const declarations = [...buffer.matchAll(/\b(?:half|float)\s+(_\w+)\s*;/g)].map(match => match[1]);
    for (const list of [names, declarations]) {
        const start = list.indexOf('_WetStreakAmount');
        assert.ok(start >= 0, 'wet detail controls are present');
        assert.deepEqual(list.slice(start, start + wetDetailProperties.length), wetDetailProperties);
    }
    let depth = 0;
    for (const line of buffer.split('\n')) {
        if (/^\s*#if/.test(line)) depth++;
        if (/^\s*#endif/.test(line)) depth--;
        for (const name of wetDetailProperties) {
            if (new RegExp('\\b' + name + '\\s*;').test(line)) assert.equal(depth, 0, name + ' layout is unconditional');
        }
    }
    for (const name of wetDetailProperties) {
        assert.ok(Number.isFinite(Number(current.get(name).value)), name + ' finite scalar default');
    }
});
test('wetness is opt-in and keeps all material values in the unconditional shared buffer', () => {
    const buffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    for (const [name, [type, value]] of Object.entries(wetProperties)) {
        assert.equal(current.get(name)?.type.replace(/\s/g, ''), type, name + ' type/range');
        assert.equal(current.get(name)?.value, value, name + ' default');
        assert.match(buffer, new RegExp((wetDetailProperties.includes(name) ? 'float' : 'half') + '\\s+' + name + ';'));
    }
    assert.ok(!/#if[^\n]*_FT_WET/.test(buffer), 'wet properties must not change buffer offsets');
    assert.match(setup, /SetKeyword\(material, "_FT_WET", On\(material, "_UseWet"\)\)/);
    assert.ok(!current.has('_FT_Wet0') && !current.has('_FT_Wet1'), 'globals never shadowed by materials');
    assert.match(wetSource, /float4 _FT_Wet0;/);
    assert.match(wetSource, /float4 _FT_Wet1;/);
    assert.ok(!/SAMPLE_TEXTURE|TEXTURE2D/.test(wetSource), 'wetness adds no textures or samplers');
});
test('wet keywords affect only main Forward, SeeThroughHair and Outline fragments', () => {
    for (const [, block] of shader.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)) {
        const name = block.match(/Name\s+"([^"]+)"/)[1];
        const wet = [...block.matchAll(/#pragma (shader_feature\w*) (_FT_WET)\b/g)];
        assert.equal(wet.length, ['Forward', 'SeeThroughHair', 'Outline'].includes(name) ? 1 : 0, name);
        if (wet.length) assert.equal(wet[0][1], 'shader_feature_local_fragment');
    }
    for (const [name] of specialVariants) {
        const special = read('Shaders/FairyToon' + name + '.shader');
        assert.ok(!/_FT_WET|_UseWet/.test(special), name + ' remains outside first-pass wet scope');
    }
    const passes = read('Shaders/FairyToonPasses.hlsl');
    const forward = hlslBody(passes, 'FairyToonForwardSurface');
    assert.match(forward, /s\.albedo = base\.rgb;\s*#if defined\(_FT_WET\)\s*FairyToonApplyWet\(s, input\.uv0\)/);
    assert.match(forward, /FairyToonSurface s = \(FairyToonSurface\)0/);
    assert.match(forward, /FairyToonApplyWetDetails\(s, input\.uv0\)/);
    assert.ok(forward.indexOf('FairyToonClipAlpha(base.a)') < forward.indexOf('FairyToonApplyWetDetails('), 'wet opacity cannot resurrect clipped pixels');
    const outline = hlslBody(passes, 'FairyToonOutlineFragment');
    assert.equal((outline.match(/FairyToonApplyWet\(/g) || []).length, 2);
    assert.ok(!/FairyToonApplyWetDetails|FairyToonWetSpecular/.test(outline), 'outline keeps global darkening only');
});
function wetMask({height=0, line=1, amount=1, rain=0, normalY=1, porosity=.7, width=.04, noise=.5, noiseStrength=.02}={}) {
    const body = hlslBody(wetSource, 'FairyToonWetMask')
        .replace('FairyToonWetNoise(uv)', '(++calls, FairyToonWetNoise(uv))')
        .replace('return 0.0;', 'return { mask: 0, edge, calls };')
        .replace('return saturate(max(soak, rain));', 'return { mask: saturate(max(soak, rain)), edge, calls };');
    const sat = x => Math.min(1, Math.max(0, x));
    return scalarHlsl(body, [], {positionWS:{y:height}, N:{y:normalY}, uv:0, edge:0, calls:0,
        _FT_Wet0:{x:line,y:amount}, _FT_Wet1:{x:rain,y:width}, _WetPorosity:porosity, _WetEdgeNoise:noiseStrength,
        FairyToonWetNoise:()=>noise, max:Math.max, saturate:sat, exp2:x=>2**x, lerp:(a,b,t)=>a+(b-a)*t,
        smoothstep:(a,b,x)=>{const t=sat((x-a)/(b-a));return t*t*(3-2*t);}});
}
test('wet mask stays dry without state and separates immersion from rain', () => {
    assert.deepEqual(wetMask({amount:0,rain:0}), {mask:0,edge:0,calls:0});
    assert.equal(wetMask({height:0}).mask, 1);
    assert.equal(wetMask({height:0}).calls, 1);
    assert.equal(wetMask({height:2}).mask, 0);
    assert.equal(wetMask({height:2,amount:0,rain:.5,normalY:1}).mask, .5);
    assert.equal(wetMask({height:2,amount:0,rain:.5,normalY:-1}).mask, .3);
    assert.equal(wetMask({height:1,amount:0,rain:1}).edge, 0);
    assert.equal(wetMask({amount:.25,porosity:0}).mask, .25);
    assert.equal(wetMask({amount:.25,porosity:1}).mask, .5);
    assert.equal(wetMask({amount:.5,rain:.9}).mask, .9);
    assert.ok(wetMask({height:.98}).edge > wetMask({height:.7}).edge, 'edge is concentrated below the line');
    assert.ok(wetMask({height:1,noise:0}).mask > wetMask({height:1,noise:1}).mask, 'UV jitter moves the front');
    assert.ok(Number.isFinite(wetMask({height:1,width:0}).mask), 'zero edge width remains finite');
    assert.ok(!/_Time|positionWS/.test(hlslBody(wetSource, 'FairyToonWetNoise')), 'front noise stays on mesh UVs');
});
test('wet albedo preserves dry colours exactly and darkens porous materials more', () => {
    const body = hlslBody(wetSource, 'FairyToonApplyWet').replace('half edge;', 'half edge = testEdge;');
    const apply = (albedo, wet, porosity, edge=0) => {
        const surface={albedo, positionWS:0,N:0};
        scalarHlsl(body, [], {s:surface,uv0:0,testEdge:edge,_WetPorosity:porosity,_WetDarken:.2,
            _WetSaturation:1.1,_WetEdgeDarken:.15,FairyToonWetMask:()=>wet,FairyToonGray:()=>albedo,
            saturate:x=>Math.min(1,Math.max(0,x)),lerp:(a,b,t)=>a+(b-a)*t});
        return surface.albedo;
    };
    for (const colour of [0,.01,.5,1,4]) assert.equal(apply(colour,0,1),colour);
    assert.ok(Math.abs(apply(.5,1,0)-.475)<1e-12);
    assert.ok(Math.abs(apply(.5,1,1)-.4)<1e-12);
    assert.ok(apply(.5,1,1,.8)<apply(.5,1,1));
});
test('Endfield wet smoothness precedes geometric AA without lowering dry smoothness', () => {
    const ef=hlslBody(read('Shaders/FairyToonEndfield.hlsl'),'FairyToonEFGetSurface');
    assert.ok(ef.indexOf('max(smoothness, _WetSmoothness)')<ef.indexOf('ddx(s.N)'));
    const adjustment=ef.match(/smoothness = lerp\(smoothness, max\(smoothness, _WetSmoothness\), s\.wet\);/)[0];
    for(const dry of [0,.3,.9,1]) for(const wet of [0,.5,1]) {
        const actual=scalarHlsl(adjustment+'return smoothness;',[],{smoothness:dry,s:{wet},_WetSmoothness:.85,
            max:Math.max,lerp:(a,b,t)=>a+(b-a)*t});
        assert.ok(actual>=dry && actual<=1);
        if(wet===0) assert.equal(actual,dry);
    }
});
test('wet film follows material reflection before MatCap and receives each mode character shadow', () => {
    const body=hlslBody(read('Shaders/FairyToonCore.hlsl'),'FairyToonShade');
    for (const wet of [false,true]) for (const endfield of [false,true]) {
        const events=[];
        const col=scalarHlsl(body,['_FT_REFLECTION','_FT_MATCAP',...(wet?['_FT_WET']:[]),...(endfield?['_FT_ENDFIELD']:[])],{
            s:{dissolveEdge:0},uv0:17,ftCharShadowVisibility:.3,_DissolveColor:{rgb:0},
            FairyToonShadeBase:(s,l)=>{events.push('base');l.charShadow=.7;return 2;},
            FairyToonReflection:(s,l,col)=>{events.push('reflection');return col+7;},
            FairyToonMatCap:(s,l,col)=>{events.push('matcap');return col+11;},
            FairyToonWetSpecular:(s,shadow,uv)=>{events.push(['wet',shadow,uv]);return 3;},
            FairyToonBackfaceColor:(s,l,col)=>col,
        });
        assert.equal(col,wet?23:20);
        assert.deepEqual(events,wet?['base','reflection',['wet',endfield?.3:.7,17],'matcap']:['base','reflection','matcap']);
    }
});
test('wet inspector and offline matrix expose shared film controls and legacy Toon strength', () => {
    const gui=hlslBody(read('Editor/FairyToonGUI.cs'),'Wetness');
    assert.match(gui,/P\("_WetSmoothness"/);
    assert.ok(!/if\s*\(endfield\)\s*P\("_WetSmoothness"/.test(gui));
    assert.match(gui,/if \(!endfield\) P\("_WetSpecular"/);
    assert.match(gui,/Foldout\([^;]*"Wet details"/);
    for(const name of Object.keys(wetProperties)) assert.ok(gui.includes(name),name+' visible');
    const compile=read('Tests~/CompileShaders.ps1');
    assert.match(compile,/ValidateSet\('Both','Off','On'\).*\$Wetness/);
    assert.match(compile,/#define _FT_WET 1/);
    assert.match(compile,/Wet globals mismatch/);
    assert.match(compile,/Wet globals leaked into vertex stage/);
});
const wetMath = {
    min:Math.min,max:Math.max,abs:Math.abs,sqrt:Math.sqrt,sin:Math.sin,cos:Math.cos,floor:Math.floor,
    saturate:x=>Math.min(1,Math.max(0,x)),clamp:(x,a,b)=>Math.min(b,Math.max(a,x)),
    lerp:(a,b,t)=>a+(b-a)*t,frac:x=>x-Math.floor(x),step:(a,x)=>x>=a?1:0,
    smoothstep:(a,b,x)=>{const t=Math.min(1,Math.max(0,(x-a)/(b-a)));return t*t*(3-2*t);},
};
const wetVec = (x,y,z) => ({x,y,z});
const wetDot = (a,b) => a.x*b.x+a.y*b.y+a.z*b.z;
const wetScale = (a,b) => wetVec(a.x*b,a.y*b,a.z*b);
const wetAdd = (a,b) => wetVec(a.x+b.x,a.y+b.y,a.z+b.z);
const wetSub = (a,b) => wetVec(a.x-b.x,a.y-b.y,a.z-b.z);
const wetCross = (a,b) => wetVec(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
const wetNormalize = a => wetScale(a,1/Math.sqrt(Math.max(wetDot(a,a),1e-12)));
function wetGravity(N) {
    const body=hlslBody(wetSource,'FairyToonWetGravity')
        .replace('float3(0.0, -1.0, 0.0) + N * N.y','add(float3(0.0, -1.0, 0.0), scale(N, N.y))')
        .replace('G * rsqrt(max(g2, 1e-6))','scale(G, rsqrt(max(g2, 1e-6)))')
        .replace('float3(0.0, 0.0, 1.0) - N * N.z','sub(float3(0.0, 0.0, 1.0), scale(N, N.z))');
    return scalarHlsl(body,[],{...wetMath,N,float3:wetVec,dot:wetDot,add:wetAdd,sub:wetSub,
        scale:wetScale,rsqrt:x=>1/Math.sqrt(x),SafeNormalize:wetNormalize});
}
function wetGGX(N,G,V,L,acrossRoughness,alongRoughness) {
    const body=hlslBody(wetSource,'FairyToonWetGGX').replace('SafeNormalize(V + L)','SafeNormalize(add(V, L))');
    return scalarHlsl(body,[],{...wetMath,N,G,V,L,acrossRoughness,alongRoughness,float3:wetVec,
        cross:wetCross,dot:wetDot,length:x=>Math.sqrt(wetDot(x,x)),add:wetAdd,SafeNormalize:wetNormalize,
        FairyToonWetFresnel:cosine=>scalarHlsl(hlslBody(wetSource,'FairyToonWetFresnel'),[],{...wetMath,cosine})});
}
test('wet anisotropic GGX remains finite at roughness zero, horizontal normals and grazing views', () => {
    for(const N of [wetVec(0,0,1),wetVec(0,1,0),wetVec(0,-1,0)]) {
        const G=wetGravity(N),T=wetNormalize(wetCross(G,N));
        assert.ok(Math.abs(wetDot(N,G))<1e-8,'gravity frame is tangent');
        assert.ok(Math.abs(wetDot(G,G)-1)<1e-8,'horizontal fallback is normalized');
        for(const ax of [0,1e-8,.0025,.0225,.28,1]) for(const ay of [0,.0025,.28,1]) {
            for(const V of [N,wetNormalize(wetAdd(T,wetScale(N,1e-7)))]) {
                const value=wetGGX(N,G,V,N,ax,ay);
                assert.ok(Number.isFinite(value) && value>=0 && value<=20,'finite capped GGX');
                assert.equal(wetGGX(N,G,V,wetScale(N,-1),ax,ay),0,'back light has no direct highlight');
            }
        }
    }
    const N=wetVec(0,0,1),G=wetGravity(N),T=wetCross(G,N),V=N;
    const along=wetNormalize(wetAdd(N,wetScale(G,.2))),across=wetNormalize(wetAdd(N,wetScale(T,.2)));
    assert.ok(wetGGX(N,G,V,along,.0225,.28)>wetGGX(N,G,V,across,.0225,.28),'film lobe stretches along gravity');
    assert.ok(Math.abs(wetGGX(N,G,V,along,.1,.1)-wetGGX(N,G,V,across,.1,.1))<1e-12,'zero stretch is isotropic');
});
test('wet flow slows continuously to rest after eight seconds and rain keeps moving', () => {
    const body=hlslBody(wetSource,'FairyToonWetFlowTime');
    const flow=(time,rain=0,clock=123)=>scalarHlsl(body,[],{...wetMath,_FT_Wet0:{w:time},_FT_Wet1:{x:rain},_Time:{y:clock}});
    assert.equal(flow(-1),0);
    const times=[0,1,4,7.9,8,9,90],positions=times.map(time=>flow(time));
    for(let i=1;i<positions.length;i++) assert.ok(positions[i]>=positions[i-1]);
    assert.equal(flow(8),flow(90));
    assert.ok(flow(8)-flow(7.99)<.0001,'velocity approaches zero without a snap');
    assert.ok(flow(90,1,124)>flow(90,1,123));
});
test('wet trails preserve alpha holes, affect only transparent coverage and skip dry or submerged details', () => {
    const body=hlslBody(wetSource,'FairyToonApplyWetDetails');
    const alpha=body.match(/#if defined\(_SURFACE_TYPE_TRANSPARENT\)([\s\S]*?)#endif/)[0];
    for(const defines of [[],['_ALPHATEST_ON'],['_SURFACE_TYPE_TRANSPARENT']]) {
        for(const value of [0,.001,.125,.4,1]) for(const streak of [0,.5,1]) {
            const s={alpha:value,wetStreak:streak};
            scalarHlsl(alpha,defines,{...wetMath,s,_WetStreakDarken:.45});
            assert.ok(Number.isFinite(s.alpha) && s.alpha>=value && s.alpha<=1);
            if(!defines.includes('_SURFACE_TYPE_TRANSPARENT') || value===0 || streak===0) assert.equal(s.alpha,value);
            else if(value<1) assert.ok(s.alpha>value);
        }
    }
    for(const state of [{wet:0,height:2},{wet:1,height:-1},{wet:1,height:0}]) {
        const s={albedo:.7,alpha:.4,N:wetVec(0,0,1),wet:state.wet,positionWS:{y:state.height}};
        scalarHlsl(body,[],{...wetMath,s,uv0:0,ddx:()=>0,ddy:()=>0,_WetStreakAmount:.55,_FT_Wet0:{z:0},
            FairyToonWetStreak:()=>assert.fail('dry/submerged fragments must skip streak work')});
        assert.equal(s.albedo,.7);assert.equal(s.alpha,.4);assert.equal(s.wetStreak,0);
        assert.deepEqual(s.wetFilmNormal,s.N);
    }
    assert.ok(body.indexOf('ddx(uv0)')<body.indexOf('if (s.wet'),'derivatives precede the wet-front branch');
});
test('wet streak default coverage permits branching from the same column hash', () => {
    const body=hlslBody(wetSource,'FairyToonWetStreak');
    assert.equal((body.match(/FairyToonWetHash4\(/g)||[]).length,1);
    let hashCalls=0,coreCalls=0;
    const value=scalarHlsl(body,[],{...wetMath,p:{x:.5,y:7},px:{x:.01,y:0},py:{x:0,y:.01},gradient:0,
        _WetStreakAmount:Number(current.get('_WetStreakAmount').value),_WetStreakFlow:.03,
        FairyToonWetHash4:()=>{hashCalls++;return {x:.5,y:.5,z:.9,w:.3};},FairyToonWetFlowTime:()=>0,
        FairyToonWetTrailCore:()=>++coreCalls===1?0:1,float2:()=>0});
    assert.equal(hashCalls,1);
    assert.ok(value>0,'a branch interior remains eligible at default trail coverage');
});
test('wet reflection survives direct-light shadowing and layer mismatch in both shading modes', () => {
    const full=hlslBody(wetSource,'FairyToonWetSpecular');
    const body=full.slice(full.indexOf('Light light =')).replace('Light light =','float light =');
    const shade=(matching,shadow,characterShadow)=>{
        let probes=0;
        const value=scalarHlsl(body,['_LIGHT_LAYERS'],{...wetMath,
            s:{wet:.8,wetStreak:.1,positionWS:{y:1},V:wetVec(0,0,1),screenUV:0},N:wetVec(0,0,1),G:0,
            acrossRoughness:.01,alongRoughness:.2,perceptualRoughness:.1,strength:.7,characterShadow,
            _WetDropAmount:0,_FT_Wet0:{z:0},GetMainLight:()=>({color:4,distanceAttenuation:.5,shadowAttenuation:shadow,layerMask:1}),
            TransformWorldToShadowCoord:()=>0,half4:()=>0,IsMatchingLightLayer:()=>matching,GetMeshRenderingLayer:()=>1,
            FairyToonWetGGX:()=>2,reflect:()=>0,dot:wetDot,FairyToonWetFresnel:()=>.02,
            GlossyEnvironmentReflection:()=>{probes++;return 3;},
        });
        assert.equal(probes,1);
        return value;
    };
    const env=shade(false,1,1);
    assert.ok(env>0);
    assert.equal(shade(true,0,1),env);
    assert.equal(shade(true,1,0),env);
    assert.ok(shade(true,1,1)>env);
    const prefix=full.slice(0,full.indexOf('float3 N ='))+'return strength;';
    for(const endfield of [false,true]) {
        const run=(wet,film,spec)=>scalarHlsl(prefix,endfield?['_FT_ENDFIELD']:[],{...wetMath,
            s:{wet,positionWS:0},uv0:0,ddx:()=>0,ddy:()=>0,_WetFilm:film,_WetSpecular:spec});
        assert.equal(run(0,.7,.5),0);assert.equal(run(1,0,.5),0);
        assert.equal(run(1,.7,.5),endfield?.7:.35);
        assert.equal(run(1,.7,0),endfield?.7:0);
    }
});
test('wet droplets keep static raw-UV seeds and AA, and disappear below the current water surface', () => {
    const drop=hlslBody(wetSource,'FairyToonWetDrop'),spec=hlslBody(wetSource,'FairyToonWetSpecular');
    assert.ok(!/_Time|positionWS/.test(drop),'droplet pattern stays attached to the mesh');
    assert.match(drop,/uv0\s*\/\s*scale/);
    assert.match(drop,/max\(_WetDropScale,\s*1e-5\)/);
    assert.match(drop,/length\(duvx\)\s*\+\s*length\(duvy\)/);
    assert.match(spec,/if \(_WetDropAmount > 0\.0 && aboveWater > 0\.0\)/);
    assert.match(spec,/FairyToonWetDrop\(uv0, duvx, duvy, dome\) \* s\.wet \* aboveWater/);
    assert.ok(spec.indexOf('ddx(uv0)')<spec.indexOf('if (s.wet'));
});

test('wet trail analytic gradients agree with finite differences through the antialiased edge', () => {
    const body=hlslBody(wetSource,'FairyToonWetTrailCore').replace(/return ([^;]+);/,'return {mask:($1),gradient};');
    const sample=(distance,width,aa)=>scalarHlsl(body,[],{...wetMath,distance,width,aa,gradient:0,sign:Math.sign});
    for(const width of [.0833,.2,.35]) for(const aa of [.001,.05,.5]) {
        for(const distance of [-.8,-.4,-.15,-.08,0,.08,.15,.4,.8]) {
            const value=sample(distance,width,aa),step=1e-7;
            const numerical=(sample(distance+step,width,aa).mask-sample(distance-step,width,aa).mask)/(2*step);
            assert.ok(Number.isFinite(value.mask) && value.mask>=0 && value.mask<=1);
            assert.ok(Number.isFinite(value.gradient));
            assert.ok(Math.abs(value.gradient-numerical)<2e-5,'normal gradient '+JSON.stringify({distance,width,aa,analytic:value.gradient,numerical}));
        }
    }
});
test('wet UV direction handles mirrored and degenerate mappings and scale zero is bounded', () => {
    const body=hlslBody(wetSource,'FairyToonWetDownUV').replace('sa * duvx + sb * duvy','add(scale(duvx, sa), scale(duvy, sb))');
    const vec2=(x,y)=>({x,y});
    const down=(axis,dpx,dpy,duvx=vec2(.1,0),duvy=vec2(0,.1))=>scalarHlsl(body,[],{...wetMath,
        _WetStreakAxis:axis,dpx,dpy,duvx,duvy,float2:vec2,
        dot:(a,b)=>a.x*b.x+a.y*b.y+(a.z||0)*(b.z||0),
        add:(a,b)=>vec2(a.x+b.x,a.y+b.y),scale:(a,b)=>vec2(a.x*b,a.y*b)});
    const zero=wetVec(0,0,0),X=wetVec(1,0,0),Y=wetVec(0,1,0),downY=wetVec(0,-1,0);
    assert.deepEqual(down(0,X,downY),vec2(0,1));
    assert.deepEqual(down(0,X,Y),vec2(0,-1));
    assert.deepEqual(down(0,downY,X),vec2(1,0));
    assert.deepEqual(down(0,downY,X,vec2(-.1,0)),vec2(-1,0));
    assert.deepEqual(down(0,zero,zero),vec2(0,1));
    for(const [axis,want] of [[1,vec2(0,1)],[2,vec2(0,-1)],[3,vec2(1,0)],[4,vec2(-1,0)]]) {
        assert.deepEqual(down(axis,zero,zero),want);
    }
    for(const [fn,prop] of [['FairyToonApplyWetDetails','_WetStreakScale'],['FairyToonWetDrop','_WetDropScale']]) {
        const initializer=hlslBody(wetSource,fn).match(/float scale = [^;]+;/)[0]+'return scale;';
        for(const value of [-1,0,1e-8,.006,.025]) {
            const scale=scalarHlsl(initializer,[],{...wetMath,[prop]:value});
            assert.ok(Number.isFinite(1/scale) && scale>=1e-5);
            if(value>=1e-5) assert.equal(scale,value);
        }
    }
});

test('wet streak masks and gradients converge to zero at independent hash-column boundaries', () => {
    const trailBody=hlslBody(wetSource,'FairyToonWetTrailCore').replace(/return ([^;]+);/,'return {mask:($1),gradient};');
    const trail=(distance,width,aa)=>scalarHlsl(trailBody,[],{...wetMath,distance,width,aa,gradient:0,sign:Math.sign});
    let body=hlslBody(wetSource,'FairyToonWetStreak');
    body=body.replace(/float (mainCore|forkCore) = FairyToonWetTrailCore\((.*), (mainGrad|forkGrad)\);/g,
        (_,name,args,gradient)=>'const '+name+'Sample=trail('+args+'); float '+name+'='+name+'Sample.mask; '+gradient+'='+name+'Sample.gradient;');
    body=body.replace('gradient = (gradient * columnFade + float2(core * columnGradient, 0.0)) * envelope * coverage * resolved;',
        'gradient = mul2(add2(mul2(gradient, columnFade), float2(core * columnGradient, 0.0)), envelope * coverage * resolved);');
    body=body.replace(/return ([^;]+);\s*$/,'return {mask:($1),gradient};');
    const sample=(x,y,footprint)=>{
        let hashes=0;
        const result=scalarHlsl(body,[],{...wetMath,p:{x,y},px:{x:footprint,y:0},py:{x:0,y:.01},gradient:0,
            _WetStreakAmount:.55,_WetStreakFlow:.03,FairyToonWetFlowTime:()=>0,trail,float2:(x,y)=>({x,y}),
            mul2:(v,a)=>({x:v.x*a,y:v.y*a}),add2:(a,b)=>({x:a.x+b.x,y:a.y+b.y}),
            FairyToonWetHash4:cell=>{hashes++;return {x:Math.abs(cell.x)%2,y:1,z:.9,w:.3};}});
        assert.equal(hashes,1,'only one hash per streak column');
        assert.ok(Number.isFinite(result.gradient.x) && Number.isFinite(result.gradient.y));
        assert.ok(Math.abs(result.gradient.x)<1000 && Math.abs(result.gradient.y)<1000);
        return result;
    };
    for(const boundary of [-1,0,1,2]) for(const y of [7,53,99]) for(const footprint of [.01,.8]) {
        assert.equal(sample(boundary,y,footprint).mask,0);
        const left=sample(boundary-1e-6,y,footprint),right=sample(boundary+1e-6,y,footprint);
        assert.ok(left.mask<1e-8 && right.mask<1e-8,'mask vanishes from both adjacent columns');
        assert.ok(Math.abs(left.gradient.x)<.01 && Math.abs(right.gradient.x)<.01,'normal gradient also vanishes at the seam');
    }
});

// Endfield iris contracts and analytic regressions execute the HLSL scalar root solver offline.
const irisPasses = read('Shaders/FairyToonPasses.hlsl');
const irisUniforms = {
    _EFIris:['Float','0'], _EFIrisFrame:['2D','"black"'], _EFIrisDepth:['Range(0, 1)','0.3'],
    _EFPupilLayer:['Float','1'], _EFPupilDepth:['Range(0, 1)','0.5'],
    _EFIrisWorldRadius:['Float','0.01233'], _EFIrisDish:['Range(0, 1)','0.24'],
    _EFIrisScale:['Range(0.3, 1.5)','0.72'], _EFIrisRim:['Range(0, 1.5)','1.5'], _EFIrisGaze:['Range(0, 1)','1'],
    _EFCorneaBulge:['Range(0, 1)','0.2'], _EFCorneaNormalRange:['Range(0, 1)','0.4'],
    _EFIrisRefraction:['Range(0, 2)','1'], _EFIrisEdge:['Range(0.5, 1)','0.96'],
    _EFIrisEdgeSoftness:['Range(0, 1)','0.4'],
    _EFIrisEdgeFade:['Range(0, 1)','0'], _EFIrisVertical:['Range(0, 1)','1'],
    _EFIrisMaxShift:['Range(0.05, 2)','2'],
    _EFIrisCenterL:['Vector','(0.15347,0.82393,0,0)'], _EFIrisCenterR:['Vector','(0.84653,0.82393,0,0)'],
    _EFIrisRadius:['Float','0.13507'], _EFPupil:['Range(0, 1)','1'],
    _EFPupilCenterL:['Vector','(0.34669,0.86296,0,0)'], _EFPupilCenterR:['Vector','(0.65331,0.86296,0,0)'],
    _EFPupilRadius:['Float','0.03715'], _EFPupilYRatio:['Float','1.27'], _EFPupilScale:['Float','0.2068'],
    _EFPupilOffset:['Vector','(0.2342,-0.0708,0,0)'], _EFIrisSparkleTex:['2D','"black"'],
    _EFIrisSparkle:['Range(0, 4)','1'], _EFIrisGlowStrength:['Range(0, 2)','1'],
    _EFIrisSparkleScale:['Float','3'], _EFCorneaIOR:['Float','8'],
    _EFIrisGlowColor:['Color','(0.3645,0.8106,1.2403,1)'],
};

test('3D Iris has synchronized defaults and an independent Toon and Endfield vertex and fragment keyword', () => {
    const buffer = input.match(/CBUFFER_START\(UnityPerMaterial\)([\s\S]*?)CBUFFER_END/)[1];
    for (const name of ['', 'Fur', 'Gem', 'Refraction']) {
        const text=read('Shaders/FairyToon'+name+'.shader'), props=properties(text);
        for (const [uniform,[type,value]] of Object.entries(irisUniforms)) {
            assert.deepEqual(props.get(uniform),{type,value},name+'/'+uniform);
            if(type==='2D') assert.match(text,new RegExp('\\[NoScaleOffset\\] '+uniform+' '));
            else assert.match(buffer,new RegExp('\\b'+uniform+';'));
        }
        assert.match(text,/\[Toggle\] _EFIris /);
        assert.match(text,/\[HDR\] _EFIrisGlowColor /);
        assert.match(text,/_EFIrisSparkle \("Reflection Strength",/);
        assert.match(text,/_EFIrisGlowStrength \("Glow Strength",/);
        assert.match(text,/_EFIrisRim \("Fixed Rim",/);
        assert.match(text,/_EFIrisEdge \("Pattern Edge",/);
        assert.match(text,/_EFIrisEdgeSoftness \("Edge Softness",/);
        const blocks=[...text.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)];
        for(const [,block] of blocks) {
            const partLine=block.split('\n').find(line=>line.includes('#pragma')&&line.includes('_FT_EF_SKIN'));
            if(partLine) {
                assert.ok(!partLine.includes('_FT_EF_IRIS'),'iris is independent of part selection');
                assert.match(block,/#pragma shader_feature_local _ _FT_EF_IRIS\s*\n/);
            }
            if(/Name "(?:ShadowCaster|CharacterShadow)"/.test(block))
                assert.doesNotMatch(block,/#pragma[^\n]*_FT_EF_IRIS/,'light-view passes never shift');
        }
    }
    const keyword=setup.match(/SetKeyword\(material, "_FT_EF_IRIS", ([^;]+)\);/)[1];
    const enabled=new Function('endfield','material','On','return '+keyword);
    for(const endfield of [false,true]) for(const iris of [false,true])
        assert.equal(enabled(endfield,{_EFIris:iris},(material,name)=>material[name]),iris,
            (endfield?'Endfield':'Toon')+' iris='+iris);
    const gui=hlslBody(read('Editor/FairyToonGUI.cs'),'Iris');
    for(const uniform of Object.keys(irisUniforms)) assert.ok(gui.includes('"'+uniform+'"'),uniform+' visible');
    for(const uniform of Object.keys(irisUniforms))
        assert.match(gui,new RegExp('P\\("'+uniform+'", "[^"]+", "[^"]+"\\)'),uniform+' has an on-screen tooltip');
    const ordered=['_EFIrisDepth','_EFPupilLayer','_EFPupilDepth','_EFIrisRefraction','_EFIrisScale','_EFIrisRim','_EFIrisEdgeFade','_EFIrisVertical','_EFIrisMaxShift','_EFIrisEdge','_EFIrisEdgeSoftness','_EFIrisGaze','Cornea','Cornea matcap','ft.IrisAvatar','_EFIrisFrame','Pupil fold'];
    for(let i=1;i<ordered.length;i++) assert.ok(gui.indexOf('"'+ordered[i-1]+'"')<gui.indexOf('"'+ordered[i]+'"'),ordered[i]+' inspector order');
    assert.match(gui,/BeginSection\("IrisAvatarData", "ft\.IrisAvatar", null, false, PropertyBlock\.IrisAvatar\)/);
    assert.match(gui,/iris\.hasMixedValue/);
    assert.ok(gui.includes('Radius (iris units) from which the painted rim stays on the eye surface; set just inside the dark ring. 1.5 = off (the whole iris sinks).'));
    assert.ok(gui.includes('Maximum lookup radius inside the painted pattern; keep it inside the painted dark ring and clear of painted highlight dots.'));
    assert.equal((gui.match(/P\("_EFIrisEdge"/g)||[]).length,1,'Pattern Edge appears only in the look group');
    assert.ok(gui.includes('How the pattern behaves near Pattern Edge: 0 repeats the edge colour (fibre streaks), higher squeezes the outer pattern toward the edge instead (no streaks); the inner part is untouched below (1 - softness) x Pattern Edge.'));
    assert.equal((gui.match(/P\("_EFIrisEdgeSoftness"/g)||[]).length,1,'Edge Softness appears only in the look group');
    const compile=read('Tests~/CompileShaders.ps1');
    assert.match(compile,/ValidateSet\('Both','Off','On'\).*\$Iris/);
    assert.match(compile,/#define _FT_EF_IRIS 1/);
});

test('3D Iris Toon inspector reuses the iris controls after main layers and keeps Endfield controls', () => {
    const gui=read('Editor/FairyToonGUI.cs');
    const draw=hlslBody(gui,'DrawAdvanced');
    assert.match(hlslBody(gui,'MainColor'), /MainColorLayer\("3rd"\)/);
    assert.match(draw,/MainColor\(\);[\s\S]*if \(showToon && !EverySelectedIsGem\(\)\) ToonIris\(\);[\s\S]*if \(showEndfield\) Endfield\(\);/);
    const toon=hlslBody(gui,'ToonIris');
    assert.match(toon,/BeginSection\("Iris", "ft\.Iris", Prop\("_EFIris"\), false, PropertyBlock\.Iris\)/);
    assert.match(toon,/Iris\(false\)/);
    assert.match(gui,/private void Iris\(bool showToggle = true\)/);
    assert.match(hlslBody(gui,'Iris'),/if \(showToggle\) P\("_EFIris"/);
    assert.match(hlslBody(gui,'Endfield'),/Iris\(\)/);
});

test('3D Iris lab keeps enabled tuning before cloning and resets only Mayo face', () => {
    const lab=fs.readFileSync(path.resolve(root,'../../Assets/Lab/Editor/LabPhase2E.cs'),'utf8');
    const convert=hlslBody(lab,'ConvertEndfieldMaterial'),capture=hlslBody(lab,'CaptureIrisTuning');
    assert.ok(convert.indexOf('CaptureIrisTuning(copy)')<convert.indexOf('copy.CopyPropertiesFromMaterial(source)'));
    assert.match(capture,/enabledOnly && Float\(material, "_EFIris", 0f\) != 1f/);
    const filter=lab.match(/internal static bool IsIrisProperty\([^)]*\) => ([\s\S]*?);/)[1];
    for(const prefix of ['_EFIris','_EFCornea','_EFPupil']) assert.ok(filter.includes('StartsWith("'+prefix+'", StringComparison.Ordinal)'));
    const isIris=new Function('name','return '+filter.replace(/\.StartsWith\(("[^"]+"), StringComparison.Ordinal\)/g,'.startsWith($1)'));
    assert.match(capture,/shader.GetPropertyCount\(\)/);
    const types=['Float','Range','Int','Vector','Color','Texture'];
    for(const type of types) assert.ok(capture.includes('ShaderPropertyType.'+type));
    // Execute the C# snapshot loop, including authored textures and their transforms.
    const snapshot=capture.replace(/\b(?:var|int|string)\s+/g,'let ').replace(/(\d)f\b/g,'$1');
    const ranges=['_EFIris','_EFIrisEdgeSoftness','_EFIrisEdgeFade','_EFIrisVertical','_EFIrisMaxShift','_EFPupilLayer','_EFPupilDepth','_EFIrisWorldRadius','_EFIrisDish'];
    const propertyTypes={...Object.fromEntries(ranges.map(name=>[name,'Range'])),_EFPupilIndex:'Int',_EFPupilOffset:'Vector',
        _EFIrisGlowColor:'Color',_EFIrisFrame:'Texture',_EFIrisSparkleTex:'Texture',_Color:'Color'};
    const snapshotNames=Object.keys(propertyTypes);
    let irisEnabled=1;
    const material={shader:{GetPropertyCount:()=>snapshotNames.length,GetPropertyName:i=>snapshotNames[i],GetPropertyType:i=>propertyTypes[snapshotNames[i]]},
        GetFloat:name=>name==='_EFIris'?irisEnabled:.73,GetInteger:()=>7,GetVector:()=>[.2,-.1,0,0],GetColor:()=>[.1,.6,1.6,1],
        GetTexture:name=>name+' asset',GetTextureScale:()=>[2,3],GetTextureOffset:()=>[.1,-.2]};
    const runSnapshot=new Function('material','enabledOnly','Float','IsIrisProperty','IrisTuning','ShaderPropertyType',snapshot);
    const captureMaterial=enabledOnly=>runSnapshot(material,enabledOnly,(m,name,fallback)=>m==null?fallback:m.GetFloat(name),isIris,
        class { constructor(){for(const name of ['floats','integers','vectors','colours','textures','textureScales','textureOffsets'])this[name]={};} },
        Object.fromEntries(types.map(type=>[type,type])));
    const tuning=captureMaterial(true);
    for(const name of ranges.filter(n=>n!=='_EFIris')) assert.equal(tuning.floats[name],.73,name+' survives refresh');
    assert.equal(tuning.integers._EFPupilIndex,7);
    assert.deepEqual(tuning.vectors._EFPupilOffset,[.2,-.1,0,0]);
    assert.deepEqual(tuning.colours._EFIrisGlowColor,[.1,.6,1.6,1]);
    for(const texture of ['_EFIrisFrame','_EFIrisSparkleTex']) {
        assert.equal(tuning.textures[texture],texture+' asset');
        assert.deepEqual(tuning.textureScales[texture],[2,3]);
        assert.deepEqual(tuning.textureOffsets[texture],[.1,-.2]);
    }
    assert.ok(!Object.hasOwn(tuning.colours,'_Color'),'snapshot stays iris-only');
    irisEnabled=0;
    assert.equal(captureMaterial(true),null,'refresh only preserves explicitly enabled iris tuning');
    assert.equal(captureMaterial(false).floats._EFIris,0,'explicit copy can snapshot disabled source tuning');
    for(const texture of ['_EFIrisFrame','_EFIrisSparkleTex']) assert.ok(convert.includes('SetTexture("'+texture+'"'));
    assert.match(convert,/if \(irisTuning != null\) irisTuning.Apply\(copy\);\s*else ApplyIrisDefaults\(copy\);/);
    assert.match(convert,/kept tuned/); assert.match(convert,/defaults/);
    const defaults=hlslBody(lab,'ApplyIrisDefaults');
    for(const [name,[type]] of Object.entries(irisUniforms)) if(type!=='2D')
        assert.ok(defaults.includes('("'+name+'",'),name+' has a reset default');
    for(const name of ['_EFIrisSparkle','_EFIrisGlowStrength'])
        assert.ok(defaults.includes('SetFloat("'+name+'", 0.5f)'),name+' has gentler Mayo default');
    for(const [name,value] of [['_EFPupilLayer','1'],['_EFPupilDepth','0.5'],['_EFIrisWorldRadius','0.01233'],['_EFIrisDish','0.24'],['_EFIrisRim','0.82'],['_EFIrisEdge','0.76'],['_EFIrisEdgeSoftness','0.4'],['_EFIrisScale','0.6'],['_EFIrisEdgeFade','0.4'],['_EFIrisVertical','0.6'],['_EFIrisMaxShift','0.5']])
        assert.ok(defaults.includes('SetFloat("'+name+'", '+value+'f)'),name+' Mayo art default');
    const reset=hlslBody(lab,'ResetIrisBatch');
    assert.match(reset,/MaterialFolder \+ "\/MAYO_Face.mat"/);
    assert.match(reset,/ApplyIrisDefaults\(face\)/);
    assert.match(reset,/SaveAssetIfDirty\(face\)/);
    assert.doesNotMatch(reset,/CopyPropertiesFromMaterial|\bConvert\(|SaveAssets\(|FairyToonMaterialSetup\.Apply/,'reset touches only iris values on Mayo face');
    assert.match(reset,/Float\(face, "_ShadingModel", 0f\) > 0\.5f[\s\S]*?EnableKeyword\("_FT_EF_IRIS"\)/);
    assert.match(reset,/if \(Application.isBatchMode\) EditorApplication.Exit\(result\)/);
});

test('3D Iris Toon capture composes five transient shots, toggles real material state and restores exact face slots', () => {
    const capture=fs.readFileSync(path.resolve(root,'../../Assets/Lab/Editor/LabPhase2ECaptureIris.cs'),'utf8');
    const driver=fs.readFileSync(path.resolve(root,'../../Assets/Lab/Editor/LabPhase2ECapture.cs'),'utf8');
    const add=hlslBody(capture,'AddIrisSteps'),initialize=hlslBody(capture,'InitializeIrisMaterials');
    assert.match(driver,/Contains\("-labIris"\) \|\| Environment.GetCommandLineArgs\(\).Contains\("-labIrisToon"\)/);
    const firstSession=new Function('args','return '+add.match(/InitializeIrisMaterials\((.+)\);/)[1].replace(/\.Contains\(/g,'.includes('));
    for(const args of [['-labIris'],['-labIrisToon'],['-labIris','-labIrisToon']])
        assert.equal(firstSession(args),args.length===1 && args[0]==='-labIrisToon');
    assert.ok(add.indexOf('Add(1, RestoreIris)')<add.indexOf('InitializeIrisMaterials(true)'),'combined flags restore Endfield before initializing Toon');
    assert.match(add,/if \(args.Contains\("-labIrisToon"\)\)[\s\S]*InitializeIrisMaterials\(true\)[\s\S]*AddToonIrisSteps\(sun, eyesPose\)/);
    assert.ok(initialize.indexOf('SetSide("endfield")')<initialize.indexOf('GetComponentsInChildren<SkinnedMeshRenderer>'));
    assert.match(initialize,/p.endfield.name == "MAYO_Face"/);
    const schedule=hlslBody(capture,'AddToonIrisSteps')
        .replace('foreach (int yaw in new[] { 30, -30 })','for (const yaw of [30,-30])')
        .replace('$"iris_toon_yaw{yaw}_on.png"','"iris_toon_yaw"+yaw+"_on.png"')
        .replace(', yaw: yaw',', yaw').replace(', eyeLook: shape, pupilLayer: 1f',', shape, 1').replace(/(\d)f\b/g,'$1');
    const shots=[],cleanup=[];
    const run=shape=>scalarHlsl(schedule,[],{irisFace:{sharedMesh:{GetBlendShapeIndex:name=>{assert.equal(name,'Pupil_Kira2_big');return shape;}}},
        InvalidOperationException:Error,eyesPose:'pose',sun:'sun',IrisShot:(...args)=>shots.push(args),Add:(delay,action)=>cleanup.push(action),RestoreIris:'restore'});
    run(42);
    assert.deepEqual(shots.map(shot=>shot[0]),['iris_toon_off.png','iris_toon_on.png','iris_toon_yaw30_on.png','iris_toon_yaw-30_on.png','iris_toon_pupil_kira2_big_on.png']);
    assert.deepEqual(shots.map(shot=>shot[4]),['off','on','on','on','on']);
    assert.deepEqual(shots.map(shot=>shot[3]),[7,7,7,7,7]);
    assert.equal(shots[4][6],42);assert.equal(shots[4][7],1);assert.deepEqual(cleanup,['restore']);
    shots.length=0;assert.throws(()=>run(-1),/Pupil_Kira2_big/);assert.equal(shots.length,0,'missing required expression cannot silently omit a shot');
    const set=hlslBody(capture,'SetIris').match(/if \(irisToonCapture\)\s*\{([\s\S]*?)continue;/)[1];
    for(const variant of ['off','on']) {
        const values={},applied=[];
        const material={SetFloat:(name,value)=>{values[name]=value;}};
        scalarHlsl(set.replace(/(\d)f\b/g,'$1'),[],{variant,material,FairyToonMaterialSetup:{Apply:m=>applied.push({material:m,iris:values._EFIris})}});
        assert.deepEqual(values,{_EFIris:variant==='off'?0:1});assert.deepEqual(applied,[{material,iris:variant==='off'?0:1}]);
    }
    const prepare=hlslBody(capture,'PrepareToonIris'),slots=hlslBody(capture,'ApplyIrisToonRendererSlots');
    assert.match(prepare,/toonPath = "Assets\/Lab\/Materials\/FairyToon\/MAYO_Face.mat"/);
    assert.match(prepare,/LoadAssetAtPath<Material>\(toonPath\)/);assert.match(prepare,/new Material\(toon\)/);
    assert.match(prepare,/CopyIrisProperties\(pair.endfield, temporary\)/);assert.match(prepare,/irisToonRendererMaterials.Add\(renderer, slots\)/);
    assert.match(slots,/irisToonMaterials.TryGetValue\(slots\[i\], out var temporary\)/);assert.match(slots,/slots\[i\] = temporary/);
    const shot=hlslBody(capture,'IrisShot');
    assert.ok(shot.indexOf('CharacterBase(')<shot.indexOf('ApplyIrisToonRendererSlots()'));
    assert.match(hlslBody(capture,'RestoreIrisToonRendererSlots'),/entry.Key.sharedMaterials = entry.Value/);
    assert.match(hlslBody(capture,'RestoreIris'),/finally[\s\S]*RestoreIrisToonRendererSlots\(\)[\s\S]*irisToonMaterials.Values[\s\S]*DestroyImmediate\(material\)/);
    assert.match(driver,/try \{ RestoreIris\(\); \}/,'capture failure cleanup also releases transient materials');
    assert.doesNotMatch(capture,/SaveAssets|SaveAssetIfDirty|CreateAsset|SetDirty|WriteAll/,'capture changes no material or mesh assets');
});

test('3D Iris capture accepts thirteen finite values, keeps omitted tuning and disables flat glow', () => {
    const capture=fs.readFileSync(path.resolve(root,'../../Assets/Lab/Editor/LabPhase2ECaptureIris.cs'),'utf8');
    assert.match(capture,/v.Length < 4 \|\| v.Length > 13/);
    assert.ok(capture.includes('depth,bulge,refraction,sparkle[,scale[,gaze[,glow[,rim[,edge[,softness[,fade[,vertical[,maxShift]]]]]]]]]'));
    assert.match(capture,/if \(tuning.Length >= 6\) material.SetFloat\("_EFIrisGaze", tuning\[5\]\)/);
    assert.match(capture,/if \(tuning.Length >= 7\) material.SetFloat\("_EFIrisGlowStrength", tuning\[6\]\)/);
    assert.match(capture,/if \(tuning.Length >= 8\) material.SetFloat\("_EFIrisRim", tuning\[7\]\)/);
    assert.match(capture,/if \(tuning.Length >= 9\) material.SetFloat\("_EFIrisEdge", tuning\[8\]\)/);
    assert.match(capture,/if \(tuning.Length >= 10\) material.SetFloat\("_EFIrisEdgeSoftness", tuning\[9\]\)/);
    assert.ok(capture.includes('depth/bulge/refraction/sparkle/scale/gaze/glow/rim/edge/softness/fade/vertical/maxShift'));
    assert.match(capture,/new\[\] \{ "_EFIrisDepth", "_EFCorneaBulge", "_EFIrisRefraction", "_EFIrisSparkle", "_EFIrisScale", "_EFIrisGaze", "_EFIrisGlowStrength", "_EFIrisRim", "_EFIrisEdge", "_EFIrisEdgeSoftness", "_EFIrisEdgeFade", "_EFIrisVertical", "_EFIrisMaxShift" \}/);
    assert.match(hlslBody(capture,'SetIris'),/variant == "flat"[\s\S]*?SetFloat\("_EFIrisGlowStrength", 0f\)/);
    assert.match(capture,/float.IsNaN\(n\) \|\| float.IsInfinity\(n\)/,'capture rejects non-finite controls');
    for(const [index,name] of [[10,'_EFIrisEdgeFade'],[11,'_EFIrisVertical'],[12,'_EFIrisMaxShift']])
        assert.ok(capture.includes('if (tuning.Length >= '+(index+1)+') material.SetFloat(\"'+name+'\", tuning['+index+'])'));
    // Run the shot assignments so omitted softness keeps the restored material value at every legacy length.
    const shot=hlslBody(capture,'IrisShot'),start=shot.indexOf('material.SetFloat("_EFIrisDepth"');
    const assignments=shot.slice(start,shot.indexOf('\n                    }',start)).replace(/tuning.Length/g,'tuning.length');
    const controls=["_EFIrisDepth","_EFCorneaBulge","_EFIrisRefraction","_EFIrisSparkle","_EFIrisScale","_EFIrisGaze","_EFIrisGlowStrength","_EFIrisRim","_EFIrisEdge","_EFIrisEdgeSoftness","_EFIrisEdgeFade","_EFIrisVertical","_EFIrisMaxShift"];
    for(let count=4;count<=13;count++) {
        const values=Object.fromEntries(controls.map(name=>[name,.73])),tuning=Array.from({length:count},(_,i)=>i/10);
        scalarHlsl(assignments,[],{tuning,material:{SetFloat:(name,value)=>{values[name]=value;}}});
        controls.forEach((name,i)=>assert.equal(values[name],i<count?tuning[i]:.73,name+' at length '+count));
    }
});

function irisTestUV() {
    const frame=hlslBody(irisPasses,'FairyToonEFIrisGazeFrame')
        .replace('    Z = lerp','    float3 Z = lerp').replace('    X = T -','    float3 X = T -').replace('    Y = Bt -','    float3 Y = Bt -');
    const optics=hlslBody(irisPasses,'FairyToonEFIrisOptics').replace(/\bp\b/g,'p0')
        .replace('    Nc =','    float3 Nc =').replace('    pArt =','    float2 pArt =');
    return hlslBody(irisPasses,'FairyToonEFIrisUV')
        .replace(/        float3 X, Y, Z;\n        FairyToonEFIrisGazeFrame\([^;]+;/,frame)
        .replace(/        float2 pArt;\n        float3 Nc;\n        FairyToonEFIrisOptics\(p0, I, k, pArt, Nc\);/,optics);
}

function irisRay(A,B,C,allowZero=false) {
    return scalarHlsl(hlslBody(irisPasses,'FairyToonEFIrisRayTime'),[],
        {A,B,C,allowZero,abs:Math.abs,sqrt:Math.sqrt,min:Math.min});
}
function irisHit(p,D,d,k,allowZero) {
    if(!d) return {p:[...p,0],hit:false};
    const invK2=1/(k*k);
    const t=irisRay(D[0]**2+D[1]**2*invK2,2*(p[0]*D[0]+p[1]*D[1]*invK2)-D[2]/d,
        p[0]**2+p[1]**2*invK2-1,allowZero);
    return {p:t<0?[...p,0]:[p[0]+t*D[0],p[1]+t*D[1],t*D[2]],hit:t>=0};
}

const irisVec=(x,y,z)=>({x,y,z});
const irisDot=(a,b)=>a.x*b.x+a.y*b.y+a.z*b.z;
const irisScale=(v,s)=>irisVec(v.x*s,v.y*s,v.z*s);
const irisSub=(a,b)=>irisVec(a.x-b.x,a.y-b.y,a.z-b.z);
const irisAdd=(a,b)=>irisVec(a.x+b.x,a.y+b.y,a.z+b.z);
const irisNormalize=v=>irisScale(v,1/Math.hypot(v.x,v.y,v.z));
const irisCross=(a,b)=>irisVec(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
function irisViewFrame(Ng,T,w,gaze=1,V=irisVec(0,0,1),forward=irisVec(0,0,1),facing=1) {
    const uv=irisTestUV();
    // Execute the shader's frame construction, translating only vector operators and the out-parameter call.
    const frame=uv.slice(uv.indexOf('float3 T ='),uv.indexOf('float2 p0 ='))
        .replace(/float3 headForward, headRight, headUp, headPosition;/,'')
        .replace(/FairyToonEFHeadFrame\(headForward, headRight, headUp, headPosition\);/,'')
        .replace('tangentWS.w * cross(Ng, T)','scale(cross(Ng, T), tangentWS.w)')
        .replace('T - Z * dot(T, Z)','sub(T, scale(Z, dot(T, Z)))')
        .replace('Bt - Z * dot(Bt, Z)','sub(Bt, scale(Z, dot(Bt, Z)))')
        .replace('Y -= X * dot(Bt, X);','Y = sub(Y, scale(X, dot(Bt, X)));');
    return scalarHlsl(frame+'return {I, X:typeof X==="undefined"?T:X, Y:typeof Y==="undefined"?Bt:Y, Z:typeof Z==="undefined"?Ng:Z};',[],{
        normalWS:Ng,tangentWS:{xyz:T,w},_EFIrisGaze:gaze,V,facing,headForward:forward,
        dot:irisDot,cross:irisCross,normalize:irisNormalize,float3:irisVec,scale:irisScale,sub:irisSub,
        lerp:(a,b,t)=>irisAdd(a,irisScale(irisSub(b,a),t))});
}

test('3D Iris gaze frame keeps face-on art centred on a surface tilted 30 degrees', () => {
    const flat=irisViewFrame(irisVec(0,0,1),irisVec(1,0,0),1);
    const normal=irisVec(.5,0,Math.sqrt(.75)),tangent=irisVec(Math.sqrt(.75),0,-.5);
    const tilted=irisViewFrame(normal,tangent,1);
    const art=irisTestUV().match(/float2 pArt = ([^;]+);/)[1];
    // With a flat cornea and refraction disabled, the bowl/art expressions isolate gaze displacement.
    const lookup=frame=>{
        const I=[frame.I.x,frame.I.y,frame.I.z],hit=irisHit([.2,.1],I.map(v=>-v),.3,1.2,false);
        return [0,1].map(axis=>scalarHlsl('return '+art+';',[],{
            _EFIrisScale:.72,hitB:{xy:hit.p[axis]},_EFIrisRefraction:0,I:0,Nc:0,reflect:()=>({xy:0})}));
    };
    for(const actual of [lookup(flat),lookup(tilted)])
        actual.forEach((v,i)=>assert.ok(Math.abs(v-[.144,.072][i])<1e-14,'face-on art must not slide'));
    const mesh=lookup(irisViewFrame(normal,tangent,1,0));
    assert.ok(Math.abs(mesh[0]-.144)>.05,'gaze zero retains the old mesh-normal parallax');
    const blend=irisViewFrame(normal,tangent,1,.5);
    assert.ok(Math.abs(blend.I.x)>0 && Math.abs(blend.I.x)<.5);
});

test('3D Iris degenerate gaze axes fall back to the surface frame and back faces only flip depth', () => {
    const N=irisVec(0,0,1),T=irisVec(1,0,0),V=irisVec(.3,.4,Math.sqrt(.75));
    for(const [forward,gaze] of [[T,1],[irisVec(0,0,-1),.5],[irisVec(0,1,0),1]]) {
        const frame=irisViewFrame(N,T,1,gaze,V,forward);
        assert.deepEqual(frame.X,T);assert.deepEqual(frame.Y,irisVec(0,1,0));assert.deepEqual(frame.Z,N);
        assert.deepEqual(frame.I,V);
    }
    const front=irisViewFrame(N,T,-1,1,V),back=irisViewFrame(N,T,-1,1,V,N,-1);
    assert.equal(back.I.x,front.I.x);assert.equal(back.I.y,front.I.y);assert.equal(back.I.z,-front.I.z);
});

test('3D Iris matcap uses viewer-right and head-up for mirrored UVs at every gaze blend', () => {
    const uv=irisTestUV();
    const fragment=uv.slice(uv.indexOf('float3 Rr ='),uv.indexOf('corneaMatcap = SAMPLE_TEXTURE2D'))
        .replace('refract(-I,','refract(scale(I, -1),')
        .replace('Rr.x * X + Rr.y * Y + Rr.z * Z','add(add(scale(X, Rr.x), scale(Y, Rr.y)), scale(Z, Rr.z))');
    const normal=irisVec(.5,0,Math.sqrt(.75)),tangent=irisVec(Math.sqrt(.75),0,-.5);
    const worldRay=irisVec(.1,.2,-Math.sqrt(.95));
    for(const gaze of [0,.5,1]) for(const hand of [-1,1]) {
        const frame=irisViewFrame(normal,irisScale(tangent,hand),hand,gaze);
        const actual=[0,1].map(axis=>scalarHlsl(fragment+'return muv;',[],{...frame,
            I:frame.I,Nc:irisVec(0,0,1),tangentWS:{w:hand},_EFCorneaIOR:8,_EFIrisSparkleScale:3,
            headRight:irisVec(1,0,0),headUp:irisVec(0,1,0),headForward:irisVec(0,0,1),
            refract:()=>irisVec(irisDot(worldRay,frame.X),irisDot(worldRay,frame.Y),irisDot(worldRay,frame.Z)),
            float3:(x,y,z)=>({x,y,z,xy:axis===0?x:y}),float2:(x,y)=>axis===0?x:y,
            dot:irisDot,scale:irisScale,add:irisAdd,max:Math.max,sign:Math.sign}));
        actual.forEach((v,i)=>assert.ok(Math.abs(v-[.35,.8][i])<1e-14,'head-space matcap orientation'));
    }
    assert.match(uv,/FairyToonEFHeadFrame\(headForward, headRight, headUp, headPosition\)/);
});

test('3D Iris root solver handles face-on, zero rim, no hit and cancellation', () => {
    assert.equal(irisRay(0,-2,1),.5);
    assert.equal(irisRay(1e-10,-2,1),.5);
    assert.equal(irisRay(0,0,1),-1);
    assert.equal(irisRay(1,0,1),-1);
    assert.equal(irisRay(1,3,2),-1);
    assert.equal(irisRay(1,-3,2),1);
    assert.equal(irisRay(1,-2,0,false),2);
    assert.equal(irisRay(1,-2,0,true),0);
    assert.ok(irisRay(1,0,0,true)===0);
    assert.equal(irisRay(1,0,0,false),-1);
    assert.ok(Math.abs(irisRay(1,-1e8,1)-1e-8)<1e-20);
    assert.deepEqual(irisHit([0,0],[0,0,-1],.3,1.2,false),{p:[0,0,-.3],hit:true});
    assert.deepEqual(irisHit([0,0],[0,0,1],-.2,1.2,true),{p:[0,0,.2],hit:true});
    assert.deepEqual(irisHit([.2,.1],[0,0,-1],0,1.2,false),{p:[.2,.1,0],hit:false});
});

test('3D Iris oblique roots remain on the elliptical bowl and dome across viewing angles', () => {
    let hits=0;
    for(const k of [1,1.1,1.32]) for(const x of [-.8,0,.8]) for(const y of [-.3,0,.3])
    for(const vx of [-.85,-.2,0,.2,.85]) for(const vy of [-.25,0,.25]) for(const d of [.3,-.2]) {
        const z=Math.sqrt(1-vx*vx-vy*vy),sign=d>0?-1:1,D=[vx*sign,vy*sign,z*sign];
        const result=irisHit([x,y],D,d,k,d<0);
        assert.ok(result.p.every(Number.isFinite));
        if(result.hit) {
            hits++;
            const [hx,hy,hz]=result.p;
            assert.ok(Math.abs(hz/d+1-hx*hx-(hy/k)**2)<1e-9,'paraboloid residual');
            assert.ok(d>0?hz<0:hz>=0,'correct bowl/dome direction');
        }
    }
    assert.equal(hits,810);
    const hit=hlslBody(irisPasses,'FairyToonEFIrisHit');
    assert.match(hit,/D\.y \* D\.y \* invK2/);
    assert.match(hit,/- D\.z \/ d/);
    assert.match(hit,/hit \? float3\(p0, 0\.0\) \+ t \* D : float3\(p0, 0\.0\)/);
});

test('3D Iris uses geometric handedness, positive dome normals and a flat rim transition', () => {
    const uv=irisTestUV();
    assert.match(uv,/tangentWS\.w \* cross\(Ng, T\)/);
    assert.match(uv,/float faceSign = facing < 0\.0 \? -1\.0 : 1\.0/);
    assert.match(uv,/dot\(V, X\), dot\(V, Y\), faceSign \* dot\(V, Z\)/);
    const incident=uv.match(/float3 I = ([^;]+);/)[1];
    for(const faceSign of [-1,1]) {
        const actual=scalarHlsl('return '+incident+';',[],{V:[.3,.4,.5],X:0,Y:1,Z:2,faceSign,
            dot:(v,axis)=>v[axis],float3:(x,y,z)=>[x,y,z],normalize:v=>v.map(c=>c/Math.hypot(...v))});
        assert.ok(Math.abs(actual[0]-.3/Math.sqrt(.5))<1e-15);
        assert.ok(Math.abs(actual[1]-.4/Math.sqrt(.5))<1e-15);
        assert.ok(Math.abs(actual[2]-.5*faceSign/Math.sqrt(.5))<1e-15);
    }
    assert.match(uv,/float d = -_EFCorneaBulge/);
    assert.match(uv,/FairyToonEFIrisHit\(p0, -I, _EFIrisDepth, k, false/);
    assert.match(uv,/FairyToonEFIrisHit\(p0, I, d, k, true/);
    const normal=uv.slice(uv.indexOf('float3 n ='),uv.indexOf('\n        }',uv.indexOf('float3 n =')))
        .replace('n.z < 0.0 ? -n : n','n.z < 0.0 ? scale(n, -1) : n');
    const v=(x,y,z)=>({x,y,z}),normalize=n=>{const l=Math.hypot(n.x,n.y,n.z);return v(n.x/l,n.y/l,n.z/l);};
    const run=(hitD,range)=>scalarHlsl(normal+'\nreturn Nc;',[],{hitD,d:-.2,k:1.2,Nc:null,
        _EFCorneaNormalRange:range,_EFCorneaBulge:.2,float3:v,normalize,
        scale:(n,s)=>v(n.x*s,n.y*s,n.z*s),saturate:x=>Math.min(1,Math.max(0,x)),
        lerp:(a,b,t)=>v(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t)});
    assert.deepEqual(run(v(1,0,0),.4),v(0,0,1));
    const expected=normalize(v(.5,0,2.5));
    for (const actual of [run(v(.5,0,.08),.4),run(v(.5,0,0),0)])
        assert.ok(Math.hypot(actual.x-expected.x,actual.y-expected.y,actual.z-expected.z)<1e-14);
    const fade=run(v(.5,0,.04),.4);
    assert.ok(fade.x>0&&fade.x<expected.x&&fade.z>0);
});

test('3D Iris remaps every colour layer and keeps the legacy fold and leaves fragment coverage unchanged', () => {
    const uv=irisTestUV(),surface=hlslBody(irisPasses,'FairyToonForwardSurface');
    assert.match(uv,/SAMPLE_TEXTURE2D_LOD\(_EFIrisFrame, sampler_ft_linear_clamp, uv0, 0\)/);
    assert.match(uv,/UNITY_BRANCH if \(abs\(f\.a\) >= 0\.5\)/);
    assert.match(uv,/abs\(f\.a\) > 5\.0 \? abs\(f\.a\) - 10\.0 : abs\(f\.a\)/);
    assert.match(uv,/length\(float2\(pArt\.x, pArt\.y \/ k\)\)/);
    assert.match(uv,/f\.a > 0\.0 \? uv0 \+ \(pArt - p0\) \* f\.b : irisCentre \+ pArt \* _EFIrisRadius/);
    assert.match(uv,/float2 q = \(pArt - off\) \/ max\(_EFPupilScale, 1e-6\)/);
    assert.match(uv,/if \(_EFPupilLayer <= 0\.5 && f\.a < 5\.0 && _EFPupil > 0\.0\)/);
    assert.match(uv,/left \? _EFPupilOffset\.x : -_EFPupilOffset\.x/);
    assert.match(uv,/pupilUV = pupilUV \* _MainTex_ST\.xy \+ _MainTex_ST\.zw/);
    assert.match(uv,/1\.5 \* fwidth\(pupilDistance\)/);
    assert.match(uv,/pupilFold\.a \*= inside \* _EFPupil/);
    assert.doesNotMatch(uv,/\bclip\(|\bdiscard\s*;/);
    assert.ok(surface.indexOf('FairyToonEFIrisUV(')<surface.indexOf('FT_LAYER_CONTEXT('));
    assert.match(surface,/#if defined\(_FT_EF_IRIS\)[\s\S]*?input\.uv0 = FairyToonEFIrisUV/);
    assert.doesNotMatch(surface,/#if[^\n]*_FT_ENDFIELD[^\n]*_FT_EF_IRIS/);
    assert.match(surface,/s\.albedo = lerp\(s\.albedo, pupilFold\.rgb, pupilFold\.a\)/);
    assert.ok(surface.indexOf('FairyToonLayeredBase(')<surface.indexOf('s.albedo = lerp(s.albedo, pupilFold'));
    const outline=hlslBody(irisPasses,'FairyToonOutlineFragment');
    assert.ok(outline.indexOf('FairyToonEFIrisUV(')<outline.indexOf('if (_ShadingModel > 0.5)'),
        'both models remap the iris before choosing the outline lighting model');
    assert.ok(outline.indexOf('FairyToonClipAlpha(')<outline.indexOf('FairyToonEFIrisUV('));
    for(const name of ['FairyToonDepthClip','FairyToonDepthFragment','FairyToonDepthNormalsFragment'])
        assert.doesNotMatch(hlslBody(irisPasses,name),/EFIris|pupilFold/);
});

test('3D Iris screens independent reflection and glow into folded albedo, with both zero unchanged', () => {
    const uv=irisTestUV(),surface=hlslBody(irisPasses,'FairyToonForwardSurface');
    assert.match(uv,/refract\(-I, Nc, 1\.0 \/ max\(_EFCorneaIOR, 1e-6\)\)/);
    assert.match(uv,/#if !defined\(FAIRYTOON_PASS_OUTLINE\)[\s\S]*?SAMPLE_TEXTURE2D\(_EFIrisSparkleTex, sampler_ft_linear_clamp, muv\)[\s\S]*?#endif/);
    const composite=surface.match(/half3 light = [\s\S]*?s\.albedo = 1\.0 - [^;]+;/)[0];
    assert.ok(surface.indexOf(composite)>surface.indexOf('s.albedo = lerp(s.albedo, pupilFold'));
    const screen=(base,rgb,alpha,color,reflection,glow)=>scalarHlsl(composite+'\nreturn s.albedo;',[],
        {s:{albedo:base},corneaMatcap:{rgb,a:alpha},_EFIrisGlowColor:{rgb:color},_EFIrisSparkle:reflection,_EFIrisGlowStrength:glow,
            saturate:x=>Math.min(1,Math.max(0,x))});
    for(const base of [0,.2,.95,1,2]) for(const rgb of [0,.2,2]) for(const alpha of [0,.3,1])
    for(const color of [.1093,.622,1.606]) for(const reflection of [0,.5,1,4]) for(const glow of [0,.5,1,2]) {
        const expected=1-(1-Math.min(1,alpha*glow)*color)*(1-(base+rgb*reflection));
        assert.ok(Math.abs(screen(base,rgb,alpha,color,reflection,glow)-expected)<1e-12);
        assert.ok(Math.abs(screen(base,rgb,alpha,color,0,0)-base)<1e-15,'both zero disable RGB light and alpha glow');
    }
    assert.ok(screen(.2,.3,.5,.622,0,1)>.2,'glow survives reflection zero');
    assert.equal(screen(.2,.3,.5,.622,1,0),.5,'reflection survives glow zero');
    assert.equal(screen(1,0,1,.622,1,1),1,'screen preserves a bright iris');
    assert.ok(screen(0,0,1,1.606,1,1)>1,'HDR glow is not clamped');
    assert.doesNotMatch(read('Shaders/FairyToonCore.hlsl')+read('Shaders/FairyToonEndfield.hlsl'),/irisGlow|irisLight/);
    assert.doesNotMatch(hlslBody(irisPasses,'FairyToonOutlineFragment'),/SAMPLE_TEXTURE2D\(_EFIrisSparkleTex|s\.albedo = 1\.0 -/);
});
// Execute the actual scalar edge mapping once, sharing its math with the vector fixtures below.
const irisSoftClamp=scalarHlsl('return function(radius, edge, softness) {\n'+hlslBody(irisPasses,'FairyToonEFSoftClamp')+'\n};',[],
    {min:Math.min,exp:Math.exp});
const irisEdgeLookup=(radius,edge=.96,softness=.4)=>{
    const knee=(1-softness)*edge,softRadius=irisSoftClamp(radius,edge,softness);
    const factor=scalarHlsl('return '+hlslBody(irisPasses,'FairyToonEFIrisOptics').match(/pArt \*= ([^;]+);/)[1]+';',[],
        {radius,knee,softRadius,max:Math.max});
    return {radius:softRadius,factor};
};

test('3D Iris zero edge softness exactly preserves the previous hard hold, including the centre', () => {
    for(const edge of [.5,.76,.96,1]) for(const radius of [0,1e-12,1e-8,5e-7,1e-6,.1,edge-1e-8,edge,edge+1e-8,2,10,1e6]) {
        const result=irisEdgeLookup(radius,edge,0);
        assert.equal(result.factor,Math.min(1,edge/Math.max(radius,1e-6)),edge+'/'+radius);
        assert.equal(result.radius,Math.min(radius,edge));
    }
    for(const softness of [1e-8,1e-6]) for(const radius of [0,.1,.76,2])
        assert.equal(irisEdgeLookup(radius,.76,softness).factor,Math.min(1,.76/Math.max(radius,1e-6)),
            'a collapsed knee width stays finite and uses the hard hold');
});

test('3D Iris edge softness leaves the inner pattern exactly unchanged', () => {
    for(const edge of [.5,.76,.96,1]) for(const softness of [0,.0001,.1,.4,.9]) {
        const knee=(1-softness)*edge;
        for(const fraction of [0,1e-12,1e-8,1e-6,.1,.5,1]) {
            const radius=knee*fraction,result=irisEdgeLookup(radius,edge,softness);
            assert.equal(result.radius,radius);
            assert.equal(result.factor,1,'preserve identity even below the division epsilon');
        }
    }
});

test('3D Iris soft edge is continuous and joins both sides of the knee with slope one', () => {
    for(const edge of [.5,.76,.96,1]) for(const softness of [.0001,.1,.4,.9,1]) {
        const knee=(1-softness)*edge,width=edge-knee,h=width*1e-5;
        const at=irisEdgeLookup(knee,edge,softness).radius;
        const right=irisEdgeLookup(knee+h,edge,softness).radius;
        assert.equal(at,knee);
        assert.ok(Math.abs((right-at)/h-1)<1e-5,'right derivative at '+edge+'/'+softness);
        assert.ok(Math.abs(right-at)<=h*(1+1e-7),'continuous from above');
        if(knee>0) {
            const left=irisEdgeLookup(knee-h,edge,softness).radius;
            assert.ok(Math.abs((at-left)/h-1)<1e-5,'left derivative');
            assert.ok(Math.abs(at-left)<=h*(1+1e-7),'continuous from below');
        }
    }
});

test('3D Iris soft edge increases toward the pattern edge without repeating it', () => {
    for(const edge of [.5,.76,.96,1]) for(const softness of [.001,.1,.4,1]) {
        const knee=(1-softness)*edge,width=edge-knee;
        let previous=knee;
        // Up to 16 widths, the gap is representable in JS; extreme float saturation is checked separately.
        for(let step=1;step<=256;step++) {
            const radius=knee+width*step/16,result=irisEdgeLookup(radius,edge,softness);
            assert.ok(result.radius>previous,'strictly monotone before rounding saturation');
            assert.ok(result.radius<edge,'no hard edge-colour hold');
            assert.ok(result.radius<radius,'only squeeze the outer pattern');
            previous=result.radius;
        }
        assert.ok(Math.abs(previous-edge)<width*2e-7,'approaches the painted edge');
        for(const radius of [knee+width*32,100,1e6]) {
            const result=irisEdgeLookup(radius,edge,softness);
            assert.ok(Number.isFinite(result.radius)&&Number.isFinite(result.factor));
            assert.ok(result.radius>=previous&&result.radius<=edge,'finite precision may round to the edge, never overshoot');
            previous=result.radius;
        }
    }
});

// Evaluate each component of the actual affine lookup expression: both opaque meshes must draw one picture.
test('3D Iris main and pupil islands map an identical view ray to an identical layered UV', () => {
    const body=irisTestUV();
    const expression=body.match(/float2 irisUV = ([^;]+);/)[1];
    const lookup=(code,uv0,p0,pArt,centre,radius)=>scalarHlsl('return '+expression+';',[],
        {f:{a:code,b:radius},uv0,p0,pArt,irisCentre:centre,_EFIrisRadius:radius});
    for(const left of [false,true]) for(const p0 of [-.7,0,.7]) for(const pI of [-.96,.1,.96]) {
        const centre=left?.15347:.84653,radius=.13507,uv0=centre+p0*radius;
        const iris=lookup(1.1,uv0,p0,pI,centre,radius);
        const pupil=lookup(-1.1,left?.34669:.65331,p0,pI,centre,radius);
        assert.ok(Math.abs(iris-pupil)<1e-15);
        assert.ok(Math.abs(lookup(11.1,uv0,p0,pI,centre,radius)-iris)<1e-15);
    }
    const radiusExpression=body.match(/float radius = ([^;]+);/)[1];
    for(const softness of [0,.4,1]) for(const k of [1.1,1.32]) for(const p of [{x:0,y:0},{x:.2,y:.3},{x:3,y:-4}]) {
        const radius=scalarHlsl('return '+radiusExpression+';',[],{pArt:p,k,float2:(x,y)=>({x,y}),length:v=>Math.hypot(v.x,v.y)});
        const factor=irisEdgeLookup(radius,.96,softness).factor;
        assert.ok(Math.hypot(p.x*factor,p.y*factor/k)<=.96+1e-15);
        if(radius<=(1-softness)*.96) assert.equal(factor,1);
    }
});


test('3D Iris lower art scale zooms in and enlarges the visible pupil', () => {
    const uv=irisTestUV();
    const art=uv.match(/float2 pArt = ([^;]+);/)[1],fold=uv.match(/float2 q = ([^;]+);/)[1];
    const lookup=new Function('_EFIrisScale','hitB','_EFIrisRefraction','I','Nc','reflect','return '+art);
    const pupil=new Function('pArt','off','_EFPupilScale','max','return '+fold);
    const size=.2068;
    const radius=scale=>{
        const boundary=size/scale;
        const pArt=lookup(scale,{xy:boundary},0,0,0,()=>({xy:0}));
        assert.ok(Math.abs(pupil(pArt,0,size,Math.max)-1)<1e-12,'boundary still reaches the same pupil-art edge');
        return boundary;
    };
    assert.ok(radius(.3)>radius(.72)); assert.ok(radius(.72)>radius(1.5));
});

test('3D Iris default art scale keeps the face-on maximum inside the art edge', () => {
    const uv=irisTestUV();
    const art=uv.match(/float2 pArt = ([^;]+);/)[1];
    assert.match(art,/_EFIrisScale \* \(hitB\.xy \+ _EFIrisRefraction \* reflect\(-I, Nc\)\.xy\)/);
    assert.ok(uv.indexOf('float2 pArt =')>uv.indexOf('float3 hitD ='),'scale only the art after geometry');
    assert.ok(uv.indexOf('pArt *=')>uv.indexOf('float2 pArt ='),'clamp after scaling');
    const scale=Number(current.get('_EFIrisScale').value),bulge=Number(current.get('_EFCorneaBulge').value);
    const range=Number(current.get('_EFCorneaNormalRange').value),edge=Number(current.get('_EFIrisEdge').value);
    const refraction=Number(current.get('_EFIrisRefraction').value);
    const lookup=new Function('_EFIrisScale','hitB','_EFIrisRefraction','I','Nc','reflect','return '+art);
    const softness=Number(current.get('_EFIrisEdgeSoftness').value);
    let maxRadius=0,maxClampedRadius=0;
    for(const k of [1,1.1,1.32]) for(let step=0;step<=1000;step++) for(let angle=0;angle<16;angle++) {
        const r=step/1000,theta=angle*Math.PI/8,x=r*Math.cos(theta),y=k*r*Math.sin(theta);
        const z=bulge*(1-r*r),fade=Math.min(1,Math.max(0,z/(bulge*range)));
        const l=Math.hypot(x,y/(k*k),1/(2*bulge));
        let n=[x/l*fade,y/(k*k)/l*fade,1-fade+1/(2*bulge)/l*fade];
        const nl=Math.hypot(...n); n=n.map(c=>c/nl);
        // Evaluate the source lookup componentwise; reflect(-I,N).xy = 2*N.z*N.xy face-on.
        const p=[x,y].map((v,axis)=>lookup(scale,{xy:v},refraction,1,n,
            (_,normal)=>({xy:2*normal[2]*normal[axis]})));
        const radius=Math.hypot(p[0],p[1]/k);
        maxRadius=Math.max(maxRadius,radius);
        maxClampedRadius=Math.max(maxClampedRadius,radius*irisEdgeLookup(radius,edge,softness).factor);
    }
    assert.ok(maxRadius>.95,'exercise the maximum near the dome fade, not just the centre');
    // The raw peak is about 0.965; the soft knee now squeezes the outer pattern before reaching the art edge.
    assert.ok(maxRadius<1,'the raw face-on lookup stays inside Mayo art');
    assert.ok(maxClampedRadius<edge,'final face-on lookup stays strictly inside the art edge');
});

// Execute the actual post-geometry HLSL, translating vector operators and supplying deterministic texture samples.
// Geometry/frame regressions above cover the inputs; this checks return order, UVs and both out values together.
const iv=(x,y,z=0)=>({x,y,z,get xy(){return iv(this.x,this.y);}});
const ia=(a,b)=>iv(a.x+b.x,a.y+b.y,(a.z||0)+(b.z||0));
const im=(a,t)=>iv(a.x*t,a.y*t,(a.z||0)*t);
const is=(a,b)=>ia(a,im(b,-1));
const id=(a,b)=>a.x*b.x+a.y*b.y+(a.z||0)*(b.z||0);
const il=a=>Math.hypot(a.x,a.y,a.z||0);
const ino=a=>im(a,1/il(a));
const irisDefaults={_EFIrisEdgeFade:0,_EFIrisVertical:1,_EFIrisMaxShift:2,_EFPupilLayer:1,_EFPupilDepth:.5,_EFIrisDepth:.3,_EFIrisScale:.6,_EFIrisRim:.82,
    _EFIrisRefraction:1,_EFIrisEdge:.76,_EFIrisEdgeSoftness:.4,_EFCorneaBulge:.2,_EFCorneaNormalRange:.4,
    _EFPupilOffset:iv(.2342,-.0708),_EFPupilScale:.2068,_EFPupilYRatio:1.27,_EFIrisDish:.24,_EFIrisWorldRadius:.01233};
function irisOpticsVectorSource(source) {
    return source
        .replace('FairyToonEFIrisHit(p, -I, _EFIrisDepth, k, false, bowlHit)','hit(p, im(I, -1), _EFIrisDepth, k, false)')
        .replace('float3 hitD = FairyToonEFIrisHit(p, I, d, k, true, domeHit);','float3 hitD = hit(p, I, d, k, true); domeHit = hitD.hit;')
        .replace('n.z < 0.0 ? -n : n','n.z < 0.0 ? im(n, -1) : n')
        .replace('_EFIrisScale * (hitB.xy + _EFIrisRefraction * reflect(-I, Nc).xy)',
            'im(ia(hitB.xy, im(reflect(im(I, -1), Nc).xy, _EFIrisRefraction)), _EFIrisScale)')
        .replace('float2 d = pArt - p;','float2 d = is(pArt, p);')
        .replace(/d \*= ([^;]+);/g,'d = im(d, $1);')
        .replace('pArt = p + d;','pArt = ia(p, d);')
        .replace(/pArt \*= ([^;]+);/,'pArt = im(pArt, $1);');
}
function irisOpticsCPU(p,I,k,options={},beforePattern=false) {
    let body=hlslBody(irisPasses,'FairyToonEFIrisOptics');
    if(beforePattern) body=body.slice(0,body.indexOf('float radius ='));
    const source=irisOpticsVectorSource(body);
    return scalarHlsl(source+'return {pArt,Nc};',[],{...irisDefaults,...options,p,I,k,pArt:null,Nc:null,
        float2:iv,float3:(a,b,c)=>typeof a==='object'?iv(a.x,a.y,b):iv(a,b,c),normalize:ino,ia,im,is,
        min:Math.min,max:Math.max,length:il,saturate:x=>Math.max(0,Math.min(1,x)),lerp:(a,b,t)=>ia(a,im(is(b,a),t)),
        reflect:(i,n)=>is(i,im(n,2*id(n,i))),FairyToonEFSoftClamp:irisSoftClamp,
        hit:(p,D,d,k,zero)=>{const result=irisHit([p.x,p.y],[D.x,D.y,D.z],d,k,zero);return Object.assign(iv(...result.p),{hit:result.hit});}});
}
// Freeze the raw displacement to isolate the fade's own Jacobian from the varying bowl/cornea geometry.
// This executes the source block before Pattern Edge, with an injected art lookup rather than a copied fade formula.
function irisAdjustedLookupCPU(p,pArt,k,options={}) {
    const body=hlslBody(irisPasses,'FairyToonEFIrisOptics'),raw=body.indexOf('pArt = _EFIrisScale *');
    const source=irisOpticsVectorSource(body.slice(body.indexOf(';',raw)+1,body.indexOf('float radius =',raw)));
    return scalarHlsl(source+'return pArt;',[],{...irisDefaults,...options,p,pArt,k,ia,im,is,
        float2:iv,min:Math.min,max:Math.max,length:il,saturate:x=>Math.max(0,Math.min(1,x)),FairyToonEFSoftClamp:irisSoftClamp});
}
function irisPupilShiftCPU(uv0,I,k,options={}) {
    const source=hlslBody(irisPasses,'FairyToonEFPupilShift').replace('[unroll] ','')
        .replace('FairyToonEFIrisOptics(p, I, k, pArt, Nc);','({pArt,Nc} = optics(p, I, k));')
        .replace('p = a - _EFPupilDepth * (pArt - p);','p = is(a, im(is(pArt, p), _EFPupilDepth));')
        .replace(/p \*= ([^;]+);/,'p = im(p, $1);').replace('return p - a;','return is(p, a);')
        .replace('return 0.0;','return iv(0,0);');
    return scalarHlsl(source,[],{...irisDefaults,...options,uv0,I,k,iv,im,is,float2:iv,
        length:il,min:Math.min,max:Math.max,FairyToonEFSoftClamp:irisSoftClamp,
        optics:(p,I,k)=>irisOpticsCPU(p,I,k,options)});
}

function irisSurfaceLookup(p0, code, rim=1.5, outline=false, softness=.4, layer=0, options={}) {
    const body=hlslBody(irisPasses,'FairyToonEFIrisUV');
    let source=body.slice(body.indexOf('float2 pArt;'),
        body.indexOf('return irisUV;')+'return irisUV;'.length);
    source=source
        .replace('FairyToonEFIrisOptics(p0, I, k, pArt, Nc);','({pArt, Nc} = optics(p0, I, k));')
        .replace('FairyToonEFIrisOptics(p0 + s, I, k, unusedArt, Nc);','({pArt:unusedArt, Nc} = optics(add(p0, s), I, k));')
        .replace('FairyToonEFPupilShift(uv0, I, k)','shift(uv0, I, k)')
        .replace('_EFIrisScale * (hitB.xy + _EFIrisRefraction * reflect(-I, Nc).xy)',
            'scale(add(hitB.xy, scale(reflect(scale(I, -1), Nc).xy, _EFIrisRefraction)), _EFIrisScale)')
        .replace(/pArt \*= ([^;]+);/,'pArt = scale(pArt, $1);')
        .replace('uv0 + (pArt - p0) * f.b','add(uv0, scale(sub(pArt, p0), f.b))')
        .replace('irisCentre + pArt * _EFIrisRadius','add(irisCentre, scale(pArt, _EFIrisRadius))')
        .replace('(pArt - off) / max(_EFPupilScale, 1e-6)','scale(sub(pArt, off), 1 / max(_EFPupilScale, 1e-6))')
        .replace('pupilCentre + q * _EFPupilRadius','add(pupilCentre, scale(q, _EFPupilRadius))')
        .replace('pupilUV * _MainTex_ST.xy + _MainTex_ST.zw','add(mul(pupilUV, _MainTex_ST.xy), _MainTex_ST.zw)')
        .replace('SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pupilUV) * _Color','SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, pupilUV)')
        .replace('refract(-I,','refract(scale(I, -1),')
        .replace('Rr.x * X + Rr.y * Y + Rr.z * Z','add(add(scale(X, Rr.x), scale(Y, Rr.y)), scale(Z, Rr.z))')
        .replace('RrHead.xy * _EFIrisSparkleScale * 0.5 + 0.5','add(scale(RrHead.xy, _EFIrisSparkleScale * 0.5), float2(0.5, 0.5))')
        .replace(/return (uv0|irisUV);/g,'return {uv:$1, pupilFold, corneaMatcap};');
    const vec=(x,y,z=0)=>({x,y,z,get xy(){return {x:this.x,y:this.y};}});
    const add=(a,b)=>vec(a.x+b.x,a.y+b.y,(a.z||0)+(b.z||0));
    const scale=(v,s)=>vec(v.x*s,v.y*s,(v.z||0)*s),sub=(a,b)=>add(a,scale(b,-1));
    const I=options.I||vec(.5,0,Math.sqrt(.75)),Nc=vec(0,0,1),X=vec(1,0,0),Y=vec(0,1,0),Z=vec(0,0,1);
    const k=Math.abs(code)>5?Math.abs(code)-10:Math.abs(code);
    const hit=irisHit(p0,[-I.x,-I.y,-I.z],.3,k,false).p;
    return scalarHlsl(source,outline?['FAIRYTOON_PASS_OUTLINE']:[],{
        p0:vec(...p0),k,f:{a:code,b:.13507},uv0:vec(.18,.82),hitB:vec(...hit),I,X,Y,Z,
        _EFPupilLayer:layer,
        optics:(p,I,k)=>irisOpticsCPU(p,I,k,{_EFIrisScale:.72,_EFCorneaBulge:0,_EFIrisEdge:.96,_EFIrisEdgeSoftness:softness,...options}),
        shift:(uv,I,k)=>irisPupilShiftCPU(uv,I,k,{_EFPupilLayer:layer,...options}),
        pupilFold:0,corneaMatcap:0,headRight:X,headUp:Y,headForward:Z,
        _EFIrisRim:rim,_EFIrisScale:.72,_EFIrisRefraction:1,_EFIrisEdge:.96,_EFIrisEdgeSoftness:softness,
        _EFIrisCenterL:vec(.15347,.82393),_EFIrisCenterR:vec(.84653,.82393),_EFIrisRadius:.13507,
        _EFPupil:1,_EFPupilOffset:vec(0,0),_EFPupilScale:10,_EFPupilRadius:.03715,_EFPupilYRatio:1.27,
        _EFPupilCenterL:vec(.34669,.86296),_EFPupilCenterR:vec(.65331,.86296),
        _MainTex_ST:{xy:vec(1,1),zw:vec(0,0)},_MainTex:'pupil',sampler_MainTex:0,
        _EFIrisSparkleTex:'cornea',sampler_ft_linear_clamp:0,_EFIrisSparkleScale:3,_EFCorneaIOR:8,
        float2:vec,float3:vec,length:v=>Math.hypot(v.x,v.y),min:Math.min,max:Math.max,exp:Math.exp,
        dot:irisDot,add,sub,scale,mul:(a,b)=>vec(a.x*b.x,a.y*b.y),fwidth:()=>.001,
        reflect:(i,n)=>sub(i,scale(n,2*irisDot(n,i))),
        refract:(i,n,eta)=>{const d=irisDot(n,i),q=1-eta*eta*(1-d*d);return q<0?vec(0,0,0):sub(scale(i,eta),scale(n,eta*d+Math.sqrt(q)));},
        smoothstep:(a,b,x)=>{const t=Math.max(0,Math.min(1,(x-a)/(b-a)));return t*t*(3-2*t);},
        SAMPLE_TEXTURE2D:(texture,sampler,uv)=>texture==='cornea'?{r:uv.x,g:uv.y,b:.75,a:.5}:{r:.2,g:.3,b:.4,a:1}
    });
}

// Execute the actual Forward view/iris call block so model or view-vector gates cannot be bypassed by the optics fixtures.
function irisForwardLookupCPU(endfield,p0,code,layer,cameraWS,positionWS,orthographic=false) {
    const body=hlslBody(irisPasses,'FairyToonForwardSurface');
    const source=body.slice(body.indexOf('s.facing ='),body.indexOf('s.layers ='))
        .replace('GetCameraPositionWS() - input.positionWS','is(GetCameraPositionWS(), input.positionWS)')
        .replace(/input\.uv0 = FairyToonEFIrisUV\(([^;]+), pupilFold, corneaMatcap\);/,
            '({uv:input.uv0,pupilFold,corneaMatcap} = FairyToonEFIrisUV($1));');
    let calls=0;
    const result=scalarHlsl(source+'return {uv:input.uv0,pupilFold:typeof pupilFold === "undefined" ? 0 : pupilFold,corneaMatcap:typeof corneaMatcap === "undefined" ? 0 : corneaMatcap,materialView:s.V};',
        ['_FT_EF_IRIS',...(endfield?['_FT_ENDFIELD']:[])],{
            s:{},input:{uv0:iv(.18,.82),positionWS,positionCS:0,normalWS:iv(0,0,1),tangentWS:{xyz:iv(1,0,0),w:1}},facing:1,
            GetCameraPositionWS:()=>cameraWS,GetWorldSpaceNormalizeViewDir:p=>orthographic?iv(0,0,1):ino(is(cameraWS,p)),
            normalize:ino,is,GetNormalizedScreenSpaceUV:()=>0,
            FairyToonEFIrisUV:(uv,V)=>{calls++;return irisSurfaceLookup(p0,code,.82,false,.4,layer,{I:V,_EFCorneaBulge:.2});}});
    return {...result,materialView:[result.materialView.x,result.materialView.y,result.materialView.z],calls};
}

test('3D Iris perspective Toon and Endfield produce identical UV, fold and matcap results', () => {
    const position=iv(.1,1.6,.03),k=1.0986;
    for(const distance of [.15,.4,2]) for(const [yaw,pitch] of [[0,0],[30,0],[-30,15]])
    for(const layer of [0,1]) for(const code of [-k,k,k+10]) for(const p0 of [[0,0],[.2,.1],[.7,-.1]]) {
        const camera=ia(position,im(irisView(yaw,pitch),distance));
        const toon=irisForwardLookupCPU(false,p0,code,layer,camera,position),endfield=irisForwardLookupCPU(true,p0,code,layer,camera,position);
        assert.equal(toon.calls,1,'Toon executes the fragment iris mapping');
        assert.deepEqual(toon,endfield,'perspective iris UV, legacy fold and cornea matcap are model independent');
        assert.notEqual(toon.corneaMatcap,0,'the comparison includes the cornea sample');
        if(layer===0 && code>0 && code<5) assert.equal(toon.pupilFold.a,1,'the comparison includes an active legacy fold');
    }
});

test('3D Iris orthographic view follows the pupil vertex ray while Toon material lighting retains its camera-position view', () => {
    const position=iv(.1,1.6,.03),camera=ia(position,iv(.3,.2,.4));
    const toon=irisForwardLookupCPU(false,[.2,.1],1.0986,0,camera,position,true);
    const endfield=irisForwardLookupCPU(true,[.2,.1],1.0986,0,camera,position,true);
    assert.equal(toon.calls,1);
    assert.deepEqual(toon.uv,endfield.uv);
    assert.deepEqual(toon.pupilFold,endfield.pupilFold);
    assert.deepEqual(toon.corneaMatcap,endfield.corneaMatcap);
    const cameraRay=ino(is(camera,position));
    assert.deepEqual(toon.materialView,[cameraRay.x,cameraRay.y,cameraRay.z],'preserve lilToon material lighting');
    assert.deepEqual(endfield.materialView,[0,0,1]);
    const vertex=hlslBody(irisPasses,'FairyToonEFPupilPosition');
    assert.match(vertex,/GetWorldSpaceNormalizeViewDir\(positionWS\)/,'fragment and vertex use the same URP camera ray');
});

test('3D Iris fixed rim returns exact surface UV without pupil fold and keeps the cornea matcap', () => {
    const body=irisTestUV();
    assert.match(body,/float rho0 = length\(float2\(p0\.x, p0\.y \/ k\)\)/);
    const gate=body.indexOf('if (f.a > 0.0 && rho0 >= _EFIrisRim) return uv0;');
    assert.ok(gate>body.indexOf('corneaMatcap = SAMPLE_TEXTURE2D'),'the cornea covers the limbus');
    assert.ok(gate<body.indexOf('float2 irisUV ='),'fixed surface bypasses both art remap and pupil fold');
    for(const k of [1,1.1,1.32]) for(const code of [k,k+10]) for(const outline of [false,true])
    for(const p of [[.82,0],[-.9,0],[0,.9*k],[.2,.9*k],[1,0]]) {
        const result=irisSurfaceLookup(p,code,.82,outline),off=irisSurfaceLookup(p,code,1.5,outline);
        assert.equal(result.uv.x,.18);assert.equal(result.uv.y,.82);
        assert.equal(result.pupilFold,0,'the painted rim must not pick up a folded pupil');
        assert.deepEqual(result.corneaMatcap,off.corneaMatcap,'surface rim leaves the matcap unchanged');
        if(!outline) assert.notEqual(result.corneaMatcap,0,'a fixed rim still samples the cornea');
        if(code<5) assert.equal(off.pupilFold.a,1,'fixture would otherwise fold the pupil across the rim');
    }
    const inside=irisSurfaceLookup([.82-1e-6,0],1.1,.82);
    assert.notEqual(inside.uv.x,.18,'the switch is hard, including the boundary on the surface side');
    assert.equal(inside.pupilFold.a,1);
});

test('3D Iris rim off with zero softness preserves previous lookups and pupil meshes ignore the fixed rim', () => {
    for(const k of [1,1.1,1.32]) for(const code of [k,k+10,-k])
    for(const p of [[0,0],[.82,0],[-.9,0],[0,.95*k],[.8,.5*k]]) {
        const result=irisSurfaceLookup(p,code,1.5,false,0);
        // Zero softness must preserve the previous bowl + reflection lookup and hard hold at 30 degrees.
        const hit=irisHit(p,[-.5,0,-Math.sqrt(.75)],.3,k,false).p;
        const art=[.72*(hit[0]-.5),.72*hit[1]],r=Math.hypot(art[0],art[1]/k),hold=Math.min(1,.96/Math.max(r,1e-6));
        const expected=code>0?[.18+(art[0]*hold-p[0])*.13507,.82+(art[1]*hold-p[1])*.13507]
            :[.15347+art[0]*hold*.13507,.82393+art[1]*hold*.13507];
        assert.ok(Math.abs(result.uv.x-expected[0])<1e-14);
        assert.ok(Math.abs(result.uv.y-expected[1])<1e-14);
        assert.equal(code<5?result.pupilFold.a:result.pupilFold,code<5?1:0);
        if(code<0) for(const rim of [0,.82])
            assert.deepEqual(irisSurfaceLookup(p,code,rim,false,0),result,'pupil-mesh islands keep drawing the sunk picture');
    }
});

function irisPupilPositionCPU(positionWS, f, V, options={}) {
    const source=hlslBody(irisPasses,'FairyToonEFPupilPosition')
        .replace('FairyToonEFHeadFrame(headForward, headRight, headUp, headPosition);','')
        .replace('FairyToonEFIrisGazeFrame(normalWS, tangentWS, headForward, X, Y, Z);','({X,Y,Z} = frame());')
        .replace('(s.x * X + s.y * Y) * _EFIrisWorldRadius','im(ia(im(X, s.x), im(Y, s.y)), _EFIrisWorldRadius)')
        .replace(/positionWS \+= V \* ([^;]+);/,'positionWS = ia(positionWS, im(V, $1));')
        .replace(/positionWS \+= (im\([^;]+);/,'positionWS = ia(positionWS, $1);');
    return scalarHlsl(source,[],{...irisDefaults,...options,positionWS,normalWS:iv(0,0,1),tangentWS:{xyz:iv(1,0,0),w:1},
        SAMPLE_TEXTURE2D_LOD:()=>f,uv0:iv(.18,.82),_EFIrisFrame:0,sampler_ft_linear_clamp:0,
        float2:iv,float3:iv,abs:Math.abs,max:Math.max,normalize:ino,dot:id,im,ia,
        frame:()=>irisViewFrame(irisVec(0,0,1),irisVec(1,0,0),1),GetWorldSpaceNormalizeViewDir:p=>options.cameraWS?ino(is(options.cameraWS,p)):V,
        FairyToonEFPupilShift:(uv,I,k)=>irisPupilShiftCPU(uv,I,k,options)});
}

test('3D Iris camera passes share skinned inputs and both light-view casters stay fixed', () => {
    const camera=['Forward','Outline','DepthOnly','DepthNormals','MotionVectors','CharacterMask',
        'SeeThroughMask','SeeThroughFakeShadow','SeeThroughEarlyZ','SeeThroughHair'];
    for(const suffix of ['','Fur','Gem','Refraction']) {
        const text=read('Shaders/FairyToon'+suffix+'.shader');
        for(const [,block] of text.matchAll(/\bPass\s*\{([\s\S]*?)ENDHLSL\s*\}/g)) {
            const name=block.match(/Name "([^"]+)"/)[1];
            if(camera.includes(name)) assert.match(block,/#pragma shader_feature_local _ _FT_EF_IRIS\s*\n/,suffix+'/'+name);
            if(['ShadowCaster','CharacterShadow'].includes(name)) assert.doesNotMatch(block,/#pragma[^\n]*_FT_EF_IRIS/);
        }
    }
    for(const name of ['FairyToonForwardVertex','FairyToonDepthVertex','FairyToonOutlineVertexProgram','FairyToonMotionVertex'])
        assert.match(hlslBody(irisPasses,name),/FairyToonEFPupilVertexPositionWS\(input\.positionOS\.xyz, input\.normalOS, input\.tangentOS, input\.uv0\)/);
    const see=read('Shaders/FairyToonSeeThrough.hlsl');
    assert.match(hlslBody(see,'FairyToonSeeThroughVertex'),/FairyToonEFPupilVertexPositionWS\(input\.positionOS\.xyz, input\.normalOS, input\.tangentOS, input\.uv0\)/);
    assert.match(hlslBody(see,'FairyToonFakeShadowVertex'),/FairyToonSeeThroughVertex\(input\)/);
    const outline=hlslBody(irisPasses,'FairyToonOutlineVertexProgram');
    assert.ok(outline.indexOf('FairyToonEFPupilVertexPositionWS')<outline.indexOf('FairyToonOutlineOffsetOS'));
    const motion=hlslBody(irisPasses,'FairyToonMotionVertex');
    assert.match(motion,/currentWS = [^;]+\+ pupilShiftWS \+ towardCamera/);
    assert.match(motion,/previousWS = [^;]+\+ pupilShiftWS \+ towardCamera/);
    for(const name of ['FairyToonShadowVertex','FairyToonCharShadowVertex']) assert.doesNotMatch(hlslBody(irisPasses,name),/EFPupil|EFIris/);
    assert.match(hlslBody(input,'FairyToonEFHeadFrame'),/_FaceForward[\s\S]*_FaceRight[\s\S]*_SeeThroughHead/);
});

test('3D Iris layer preserves pupil UV and never folds main, pupil or expression fragments, including outline', () => {
    for(const code of [-1.0986,1.0986,11.0986]) for(const outline of [false,true])
    for(const point of [[0,0],[.2342,-.0708],[.7,.2],[.9,0]]) {
        const result=irisSurfaceLookup(point,code,.82,outline,.4,1,{_EFCorneaBulge:.2});
        assert.equal(result.pupilFold,0);
        if(code<0) {assert.equal(result.uv.x,.18);assert.equal(result.uv.y,.82);}
    }
    const uv=hlslBody(irisPasses,'FairyToonEFIrisUV');
    assert.match(uv,/if \(_EFPupilLayer > 0\.5 && f\.a < 0\.0\) return uv0/);
    assert.match(uv,/if \(_EFPupilLayer <= 0\.5 && f\.a < 5\.0 && _EFPupil > 0\.0\)/);
});

test('3D Iris layer zero, depth zero and non-pupil codes leave vertex positions identical', () => {
    const pos=iv(.1,1.6,.03),V=ino(iv(.5,.2,1));
    for(const code of [-1.0986,0,.4,1.0986,11.0986]) for(const options of [{_EFPupilLayer:0},{_EFPupilDepth:0}])
        assert.strictEqual(irisPupilPositionCPU(pos,{a:code,rg:iv(.2342,-.0708)},V,options),pos);
    for(const code of [0,.4,.5,1.0986,11.0986])
        assert.strictEqual(irisPupilPositionCPU(pos,{a:code,rg:iv(.2342,-.0708)},V),pos);
    assert.ok(il(is(irisPupilPositionCPU(pos,{a:-1.0986,rg:iv(.2342,-.0708)},V),pos))>.001);
});

const irisViews=[[0,0],[30,0],[-30,0],[0,15],[0,-15]];
const irisView=(yaw,pitch)=>{const y=yaw*Math.PI/180,p=pitch*Math.PI/180;return iv(Math.sin(y)*Math.cos(p),Math.sin(p),Math.cos(y)*Math.cos(p));};
test('3D Iris two iterations solve the pupil fixed point within 0.001 iris unit at depth 0.3 and 0.5', () => {
    // The travel clamp intentionally takes priority over an exact solution near the rim at larger depths.
    for(const t of [.3,.5]) for(const [yaw,pitch] of irisViews) for(const left of [true,false]) {
        const I=irisView(yaw,pitch),k=1.0986,a=iv((left?1:-1)*.2342,-.0708);
        const p=ia(a,irisPupilShiftCPU(iv(left?.18:.82,.82),I,k,{_EFPupilDepth:t})),art=irisOpticsCPU(p,I,k).pArt;
        const error=il(is(ia(p,im(is(art,p),t)),a));
        assert.ok(error<.001,`t=${t} yaw=${yaw} pitch=${pitch} left=${left}: ${error}`);
    }
});

test('3D Iris travel clamp bounds the centre and leaves the rest pose below its knee untouched', () => {
    const k=1.0986,a=irisDefaults._EFPupilOffset,limit=.82-.2068*Math.max(1,1.27/k);
    for(const t of [0,.3,.5,.7,1]) for(let yaw=-80;yaw<=80;yaw+=5) for(const pitch of [-30,0,30]) {
        const p=ia(a,irisPupilShiftCPU(iv(.18,.82),irisView(yaw,pitch),k,{_EFPupilDepth:t}));
        assert.ok(Math.hypot(p.x,p.y/k)<=limit+1e-12);
    }
    assert.equal(irisSoftClamp(Math.hypot(a.x,a.y/k),limit,.3),Math.hypot(a.x,a.y/k));
});

test('3D Iris dish lift follows the view ray with zero displacement in the view-plane projection', () => {
    const pos=iv(.1,1.6,.03),p0=iv(.2342,-.0708),k=1.0986;
    for(const [yaw,pitch] of irisViews) {
        const V=irisView(yaw,pitch),s=irisPupilShiftCPU(iv(.18,.82),V,k);
        const flat=irisPupilPositionCPU(pos,{a:-k,rg:p0},V,{_EFIrisDish:0});
        const lift=is(irisPupilPositionCPU(pos,{a:-k,rg:p0},V),flat);
        assert.ok(il(is(lift,im(V,id(lift,V))))<1e-15,'orthographic/view-plane projection unchanged');
        const rise=.24*((p0.x+s.x)**2+((p0.y+s.y)/k)**2-p0.x**2-(p0.y/k)**2);
        assert.ok(Math.abs(id(lift,iv(0,0,1))-rise*.01233)<1e-15);
    }
});

test('3D Iris pupil matcap equals an iris fragment at the same moved surface point', () => {
    for(const adjustment of [{},irisLabAdjustment])
    for(const [yaw,pitch] of irisViews) for(const p0 of [[.2342,-.0708],[.1,.05],[.4,-.1]]) {
        const I=irisView(yaw,pitch),k=1.0986,s=irisPupilShiftCPU(iv(.18,.82),I,k,adjustment),moved=ia(iv(...p0),s),options={I,_EFCorneaBulge:.2,...adjustment};
        const pupil=irisSurfaceLookup(p0,-k,.82,false,.4,1,options),iris=irisSurfaceLookup([moved.x,moved.y],k,.82,false,.4,1,options);
        assert.deepEqual(pupil.corneaMatcap,iris.corneaMatcap);assert.notEqual(pupil.corneaMatcap,0);
    }
});

test('3D Iris lab captures named keys, optional depth sweep and read-only world-radius fit', () => {
    const capture=fs.readFileSync(path.resolve(root,'../../Assets/Lab/Editor/LabPhase2ECaptureIris.cs'),'utf8');
    for(const key of ['Pupil_Kira2_big','Pupil_Star_big','Pupil_Heart_big','Pupil_OFF','Pupil_Big2','Pupil_Small2','Pupil_Up']) assert.ok(capture.includes('"'+key+'"'));
    assert.match(capture,/GetBlendShapeIndex\(key\)/);assert.match(capture,/iris_pupil_\{tag\}_on/);assert.match(capture,/iris_pupil_\{tag\}_yaw30_on/);
    assert.match(capture,/iris_pupil_kira_fold[\s\S]*pupilLayer: 0f/);assert.match(capture,/-labPupilDepth/);assert.match(capture,/iris_pdepth\{n\}_yaw\{yaw\}/);
    assert.match(hlslBody(capture,'IrisShot'),/RestoreIrisBlendWeights\(\)/);
    assert.match(hlslBody(capture,'RestoreIris'),/RestoreIrisMaterials\(\)[\s\S]*RestoreIrisBlendWeights\(\)/);
    const fit=hlslBody(capture,'LogIrisWorldRadius');
    assert.match(fit,/BakeMesh\(baked, false\)/);assert.match(fit,/localToWorldMatrix/);assert.match(fit,/GetPixelBilinear/);
    assert.match(fit,/1\.0986f/);assert.match(fit,/determinant = xx \* yy - xy \* xy/);
    assert.match(fit,/\[Phase2E\] Iris world radius measured/);assert.match(fit,/0\.1f \* Mathf.Abs\(authored\)/);
    assert.doesNotMatch(fit,/SetFloat|SetDirty|SaveAsset|WriteAll/,'measurement only logs');
});

test('3D Iris layer zero matches 1350 pre-refactor UV, pupil fold and matcap results bit for bit', () => {
    // Digest recorded from the previous implementation before extracting optics; all fixtures select legacy layer 0.
    const rows=[];
    for(const k of [1,1.1,1.32]) for(const code of [-k,k,k+10]) for(const rim of [0,.82,1.5])
    for(const outline of [false,true]) for(const softness of [0,.2,.4,.8,1])
    for(const p of [[0,0],[.2,.1],[.82,0],[-.9,.2],[.8,.5*k]]) {
        const r=irisSurfaceLookup(p,code,rim,outline,softness,0);
        rows.push([r.uv.x,r.uv.y,r.pupilFold,r.corneaMatcap]);
    }
    assert.equal(require('node:crypto').createHash('sha256').update(JSON.stringify(rows)).digest('hex'),
        '1f73c0e0f6a38fb84d10fde8869f848ae82420e6b20e4414326bb1094c9f419b');
});

test('3D Iris dish lift preserves perspective screen position after lateral travel at close camera distances', () => {
    const pos=iv(.1,1.6,.03),p0=iv(.2342,-.0708),k=1.0986;
    for(const [yaw,pitch] of irisViews) for(const distance of [.15,.4,2]) {
        const V=irisView(yaw,pitch),cameraWS=ia(pos,im(V,distance));
        const flat=irisPupilPositionCPU(pos,{a:-k,rg:p0},V,{_EFIrisDish:0,cameraWS});
        const moved=irisPupilPositionCPU(pos,{a:-k,rg:p0},V,{cameraWS});
        const right=ino(irisCross(iv(0,1,0),V)),up=irisCross(V,right);
        const project=p=>{const ray=is(p,cameraWS),z=id(ray,V);return [id(ray,right)/z,id(ray,up)/z];};
        const a=project(flat),b=project(moved);
        a.forEach((value,i)=>assert.ok(Math.abs(value-b[i])<1e-12));
    }
});

const irisLabAdjustment={_EFIrisEdgeFade:.4,_EFIrisVertical:.6,_EFIrisMaxShift:.5};
const irisEllipseLength=(v,k)=>Math.hypot(v.x,v.y/k);
function irisSeededRandom(seed) {
    return ()=>{seed=(1664525*seed+1013904223)>>>0;return seed/4294967296;};
}

test('3D Iris neutral adjustment preserves 324 previous pupil-layer, position and shared-optics results bit for bit', () => {
    // Recorded against the pre-adjustment shader, before changing any production source.
    const rows=[];
    for(const k of [1,1.0986,1.32]) for(const t of [.3,.5,1]) for(const layer of [0,1])
    for(const [yaw,pitch] of [...irisViews,[-80,0],[80,0],[0,-60],[0,60]]) for(const left of [true,false]) {
        const I=irisView(yaw,pitch),uv=iv(left?.18:.82,.82),pos=iv(.1,1.6,.03),p0=iv((left?1:-1)*.2342,-.0708);
        const options={_EFPupilDepth:t,_EFPupilLayer:layer},shift=irisPupilShiftCPU(uv,I,k,options);
        const moved=irisPupilPositionCPU(pos,{a:-k,rg:p0},I,options),art=irisOpticsCPU(ia(p0,shift),I,k,options);
        rows.push([shift.x,shift.y,moved.x,moved.y,moved.z,art.pArt.x,art.pArt.y,art.Nc.x,art.Nc.y,art.Nc.z]);
    }
    assert.equal(require('node:crypto').createHash('sha256').update(JSON.stringify(rows)).digest('hex'),
        '99390ac5372bf801dde154ea8303ef3578c0aaf21a612050d756643ca38adf31');
});

test('3D Iris Max Shift bounds the elliptical displacement and preserves shifts below its soft knee', () => {
    const p=iv(0,0);
    for(const k of [.75,1,1.0986,1.32,2]) for(const cap of [.05,.2,.5,1,1.999])
    for(const amount of [0,1e-8,.25,.5,.75,1,2,10]) for(let angle=0;angle<16;angle++) {
        const theta=angle*Math.PI/8,raw=iv(cap*amount*Math.cos(theta),k*cap*amount*Math.sin(theta));
        const art=irisAdjustedLookupCPU(p,raw,k,{_EFIrisMaxShift:cap});
        assert.ok(irisEllipseLength(is(art,p),k)<=cap+1e-12,`k=${k} cap=${cap} amount=${amount}`);
        if(amount<=.5) assert.ok(Math.abs(art.x-raw.x)<1e-14 && Math.abs(art.y-raw.y)<1e-14,'soft knee leaves small shifts untouched');
    }
    const raw=iv(9,-7);
    assert.strictEqual(irisAdjustedLookupCPU(p,raw,1),raw,'neutral controls bypass subtract/add as well as both clamps');
});

test('3D Iris Vertical Strength zero removes all vertical displacement before fade and Pattern Edge', () => {
    for(const k of [1,1.0986,1.32]) for(const [yaw,pitch] of [...irisViews,[-80,0],[80,0],[0,-80],[0,80]])
    for(const p of [iv(0,0),iv(.23,-.07),iv(.1,.7*k)]) for(const fade of [0,.4,1]) for(const cap of [.05,.5,2]) {
        const art=irisOpticsCPU(p,irisView(yaw,pitch),k,{_EFIrisVertical:0,_EFIrisEdgeFade:fade,_EFIrisMaxShift:cap},true).pArt;
        assert.equal(art.y,p.y,'vertical zero is measured before the independent pattern-radius clamp');
    }
});

test('3D Iris faded lookup stays strictly inside the fixed ellipse through random views up to 80 degrees', () => {
    const random=irisSeededRandom(0x1f3d2026),pick=values=>values[Math.floor(random()*values.length)];
    for(let sample=0;sample<3500;sample++) {
        const k=pick([.75,1,1.0986,1.32,2]),rim=pick([.25,.82,1,1.5]),end=Math.min(rim,1);
        const radius=end*(sample%10===0?1-1e-6:Math.sqrt(random())*(1-1e-6)),theta=2*Math.PI*random();
        const p=iv(radius*Math.cos(theta),k*radius*Math.sin(theta));
        const view=80*Math.PI/180*random(),azimuth=2*Math.PI*random();
        const I=iv(Math.sin(view)*Math.cos(azimuth),Math.sin(view)*Math.sin(azimuth),Math.cos(view));
        const options={_EFIrisRim:rim,_EFIrisEdgeFade:pick([.001,.1,.4,1]),_EFIrisVertical:pick([0,.6,1]),
            _EFIrisMaxShift:pick([.05,.5,1.999,2]),_EFIrisDepth:pick([0,.3,1]),_EFIrisScale:pick([.3,.72,1.5]),
            _EFCorneaBulge:pick([0,.2,1]),_EFIrisRefraction:pick([0,1,2]),_EFIrisEdge:pick([.5,.76,1]),
            _EFIrisEdgeSoftness:pick([0,.4,1])};
        const adjusted=irisOpticsCPU(p,I,k,options,true),final=irisOpticsCPU(p,I,k,options);
        assert.ok(irisEllipseLength(adjusted.pArt,k)<end,`pre-Pattern Edge sample=${sample}, rim=${rim}`);
        assert.ok(irisEllipseLength(final.pArt,k)<end,`post-Pattern Edge sample=${sample}, rim=${rim}`);
        assert.ok(irisEllipseLength(is(adjusted.pArt,p),k)<=.8*(end-radius)+2e-12,'adaptive band reserves 20% of the remaining rim gap');
        const neutral=irisOpticsCPU(p,I,k,{...options,_EFIrisEdgeFade:0,_EFIrisVertical:1,_EFIrisMaxShift:2});
        assert.deepEqual([adjusted.Nc.x,adjusted.Nc.y,adjusted.Nc.z],[neutral.Nc.x,neutral.Nc.y,neutral.Nc.z],'parallax adjustment preserves the cornea normal');
    }
});

test('3D Iris Edge Fade meets the exact surface lookup at and beyond the fixed rim', () => {
    for(const k of [1,1.1,1.32]) for(const rim of [0,.82,1.5]) for(const r of [Math.min(rim,1),1,1.5])
    for(const angle of [0,Math.PI/4,Math.PI]) {
        const p=iv(r*Math.cos(angle),k*r*Math.sin(angle)),raw=ia(p,iv(3,-4));
        const adjusted=irisAdjustedLookupCPU(p,raw,k,{_EFIrisRim:rim,_EFIrisEdgeFade:.4});
        assert.ok(irisEllipseLength(is(adjusted,p),k)<1e-14,'fade reaches identity where the fixed-surface branch begins');
    }
});

test('3D Iris locally constant displacement fade keeps radial projection slope and Jacobian determinant at least 0.2', () => {
    // Ellipse coordinates x=(p.x,p.y/k), D=(d.x,d.y/k). In the active band:
    // J=I-D*u^T/band, det(J)=1-dot(D,u)/band >= 1-|D|/(1.25|D|)=0.2.
    // This is a local constant-D guarantee; the bowl/cornea's spatially varying displacement has its own derivatives.
    const random=irisSeededRandom(0xfade1),h=1e-6;
    for(let sample=0;sample<1800;sample++) {
        const k=[1,1.0986,1.32][sample%3],end=[.25,.82,1][sample%3],r=end*(.01+.97*random());
        const theta=2*Math.PI*random(),angle=2*Math.PI*random(),size=4*random(),D=iv(size*Math.cos(angle),size*Math.sin(angle));
        const options={_EFIrisRim:end,_EFIrisEdgeFade:[.001,.1,.4,1][sample%4]};
        const lookup=(x,y)=>{
            const p=iv(x,k*y),q=irisAdjustedLookupCPU(p,ia(p,iv(D.x,k*D.y)),k,options);
            return iv(q.x,q.y/k);
        };
        const u=iv(Math.cos(theta),Math.sin(theta)),lo=lookup((r-h)*u.x,(r-h)*u.y),hi=lookup((r+h)*u.x,(r+h)*u.y);
        const projectionSlope=id(is(hi,lo),u)/(2*h);
        assert.ok(projectionSlope>=.2-1e-6,`radial projection sample=${sample}: ${projectionSlope}`);
        const x=r*u.x,y=r*u.y,dx=im(is(lookup(x+h,y),lookup(x-h,y)),1/(2*h));
        const dy=im(is(lookup(x,y+h),lookup(x,y-h)),1/(2*h)),det=dx.x*dy.y-dx.y*dy.x;
        assert.ok(det>=.2-1e-5,`Jacobian sample=${sample}: ${det}`);
    }
});

test('3D Iris an opposing constant shift can decrease lookup radius without folding the mapping', () => {
    // Counterexample to claiming |r*u + w(r)*D| is nondecreasing for every ray:
    // u=(1,0), D=(-0.2,0), end=1, fade=0.4 => w=1 near the centre.
    const lookup=r=>irisAdjustedLookupCPU(iv(r,0),iv(r-.2,0),1,{_EFIrisRim:1,_EFIrisEdgeFade:.4});
    const a=lookup(.01),b=lookup(.02);
    assert.ok(Math.abs(il(a)-.19)<1e-14);assert.ok(Math.abs(il(b)-.18)<1e-14);
    assert.ok(il(b)<il(a),'radius decreases as the shifted centre approaches the origin');
    assert.ok(Math.abs((b.x-a.x)/.01-1)<1e-12,'radial projection still increases with slope one');
});

test('3D Iris new lab defaults converge within 0.001 iris unit and report the pupil fixed-point residual', t => {
    let maxResidual=0,worst='',count=0;
    for(const depth of [.3,.5]) for(const [yaw,pitch] of irisViews) for(const left of [true,false]) {
        const k=1.0986,I=irisView(yaw,pitch),a=iv((left?1:-1)*.2342,-.0708);
        const options={...irisLabAdjustment,_EFPupilDepth:depth},p=ia(a,irisPupilShiftCPU(iv(left?.18:.82,.82),I,k,options));
        const art=irisOpticsCPU(p,I,k,options).pArt,error=il(is(ia(p,im(is(art,p),depth)),a));
        assert.ok(error<.001,`new lab defaults pupil residual exceeds 0.001 iris unit: depth=${depth}, yaw=${yaw}, pitch=${pitch}, left=${left}: ${error}`);
        assert.ok(irisEllipseLength(p,k)<=.82-.2068*Math.max(1,1.27/k)+1e-12,'the existing pupil travel bound still applies');
        if(error>maxResidual) {maxResidual=error;worst=`depth=${depth}, yaw=${yaw}, pitch=${pitch}, left=${left}`;}
        count++;
    }
    t.diagnostic(`new lab defaults pupil fixed-point residual: max=${maxResidual.toPrecision(9)} iris units over ${count} cases (${worst}); measured after the intentional travel clamp`);
});
