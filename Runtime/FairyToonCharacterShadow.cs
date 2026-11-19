using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace NW.FairyToon
{
    public enum FairyToonShadowResolution { _2048 = 2048, _4096 = 4096, _8192 = 8192 }
    public enum FairyToonCharacterShadowLight { CameraAligned, MainLight }

    [Serializable]
    public sealed class FairyToonCharacterShadowSettings
    {
        public const int maxCharacters = 4;
        [Tooltip("Draw a separate depth atlas for FairyToon character self-shadow.")]
        public bool enabled = true;
        [Tooltip("Atlas resolution. 8192 is a 256 MB depth texture.")]
        public FairyToonShadowResolution resolution = FairyToonShadowResolution._4096;
        [Tooltip("CameraAligned places a self-shadow projector above the viewer. MainLight follows URP's main light and can shadow nearby characters.")]
        public FairyToonCharacterShadowLight lightMode = FairyToonCharacterShadowLight.CameraAligned;
        [Tooltip("Minimum and maximum camera-aligned projector elevation in degrees.")]
        public Vector2 pitchRange = new Vector2(30f, 35f);
        [Tooltip("Camera-aligned projector yaw offset around world up, in degrees.")]
        [Range(-45f, 45f)] public float yawOffset;
        [Tooltip("Fit the map to the part of the character inside the camera frustum.")]
        public bool cameraFit = true;
        [Tooltip("0 gives a crisp antialiased toon edge; 1 keeps the raw filtered penumbra.")]
        [Range(0f, 1f)] public float softness = 0.15f;
        [Tooltip("Depth bias in shadow texels.")]
        [Range(0f, 4f)] public float depthBias = 1f;
        [Tooltip("Normal bias in shadow texels.")]
        [Range(0f, 4f)] public float normalBias = 1f;
        [Tooltip("Character shadow strength.")]
        [Range(0f, 1f)] public float strength = 1f;
        [Tooltip("Show only character shadow visibility in the receiver shader.")]
        public bool debugView;
    }

    public readonly struct FairyToonCharacterShadowDiagnostic
    {
        public FairyToonCharacter Character { get; }
        public int SliceCount { get; }
        public Vector4 TileRect { get; }
        public float TexelWorldSize { get; }
        /// <summary>The caster bounds the map was fitted to.</summary>
        public Bounds Bounds { get; }

        internal FairyToonCharacterShadowDiagnostic(FairyToonCharacter character, int count, Vector4 rect, float texel, Bounds bounds)
        {
            Character = character;
            SliceCount = count;
            TileRect = rect;
            TexelWorldSize = texel;
            Bounds = bounds;
        }
    }

    internal sealed class FairyToonCharacterShadow : ScriptableRenderPass
    {
        private const int MaxCharacters = FairyToonCharacterShadowSettings.maxCharacters;
        private static readonly int MapId = Shader.PropertyToID("_FT_CharShadowMap");
        private static readonly int ParamsId = Shader.PropertyToID("_FT_CharShadowParams");
        private static readonly int Params2Id = Shader.PropertyToID("_FT_CharShadowParams2");
        private static readonly int MatrixId = Shader.PropertyToID("_FT_CharShadowMatrix");
        private static readonly int RectId = Shader.PropertyToID("_FT_CharShadowRect");
        private static readonly int LightId = Shader.PropertyToID("_FT_CharShadowLight");
        private static readonly int OwnersId = Shader.PropertyToID("_FT_CharShadowOwners");
        private static readonly int CasterVPId = Shader.PropertyToID("_FT_CharShadowCasterVP");
        private static readonly int CasterLightId = Shader.PropertyToID("_FT_CharShadowCasterLight");
        private static readonly int CasterBiasId = Shader.PropertyToID("_FT_CharShadowCasterBias");
        private readonly FairyToonFeature owner;
        private readonly Slice[] slices = new Slice[MaxCharacters];
        private readonly Vector3[] frustumCorners = new Vector3[4];
        private readonly Plane[] frustumPlanes = new Plane[6];
        private RTHandle atlas;
        private int sliceCount;
        private readonly Slice[] gameSlices = new Slice[MaxCharacters];
        private int gameSliceCount;

        private struct Slice
        {
            public FairyToonCharacter character;
            public Bounds bounds;
            public Vector3 towards;
            public Matrix4x4 view;
            public Matrix4x4 projection;
            public float texel;
        }

        private sealed class PassData
        {
            public readonly Slice[] slices = new Slice[MaxCharacters];
            public readonly Matrix4x4[] matrices = new Matrix4x4[MaxCharacters];
            public readonly Vector4[] rects = new Vector4[MaxCharacters];
            public readonly Vector4[] lights = new Vector4[MaxCharacters];
            public readonly Vector4[] owners = new Vector4[MaxCharacters];
            public int count;
            public int resolution;
            public int tileResolution;
            public Vector4 parameters;
            public Vector4 parameters2;
            public float depthBias;
            public float normalBias;
            public Material fallback;
        }

        public FairyToonCharacterShadow(FairyToonFeature owner)
        {
            this.owner = owner;
            profilingSampler = new ProfilingSampler("FairyToon Character Shadow");
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public void Dispose()
        {
            atlas?.Release();
            atlas = null;
            sliceCount = 0;
            gameSliceCount = 0;
        }

        /// <remarks>From the last Game camera: the Scene view renders the same pass with its own fit.</remarks>
        public bool TryGetDiagnostic(FairyToonCharacter character, out FairyToonCharacterShadowDiagnostic diagnostic)
        {
            for (int i = 0; i < gameSliceCount; i++)
                if (gameSlices[i].character == character)
                {
                    float scale = gameSliceCount == 1 ? 1f : 0.5f;
                    float x = (i % 2) * scale, y = (i / 2) * scale;
                    diagnostic = new FairyToonCharacterShadowDiagnostic(character, gameSliceCount,
                        new Vector4(x, y, x + scale, y + scale), gameSlices[i].texel, gameSlices[i].bounds);
                    return true;
                }
            diagnostic = default;
            return false;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var settings = owner.characterShadow;
            var camera = cameraData.camera;
            sliceCount = 0;
            int resolution = (int)settings.resolution;
            Vector3 mainTowards = Vector3.zero;
            if (settings.lightMode == FairyToonCharacterShadowLight.MainLight)
            {
                int index = lightData.mainLightIndex;
                if (index >= 0 && index < lightData.visibleLights.Length)
                {
                    var light = lightData.visibleLights[index].light;
                    if (light != null) mainTowards = -light.transform.forward;
                }
            }
            if (settings.enabled && (settings.lightMode != FairyToonCharacterShadowLight.MainLight || mainTowards.sqrMagnitude > 0.5f))
            {
                if (settings.cameraFit) GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
                var registry = FairyToonCharacter.Registry;
                for (int i = 0; i < registry.Count && sliceCount < MaxCharacters; i++)
                {
                    var character = registry[i];
                    if (character == null || !character.isActiveAndEnabled || !character.CastCharacterShadow
                        || !character.TryGetShadowBounds(out var bounds)) continue;
                    // (characters out of view need no map; with Camera Fit the atlas goes to the ones in view)
                    if (settings.cameraFit && !GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) continue;
                    var slice = new Slice { character = character, bounds = bounds };
                    slice.towards = settings.lightMode == FairyToonCharacterShadowLight.MainLight ? mainTowards
                        : CameraLight(camera.transform.position, bounds.center, settings.pitchRange, settings.yawOffset);
                    if (!Fit(ref slice, camera, settings.cameraFit, resolution / 2)) continue;
                    slices[sliceCount++] = slice;
                }
            }
            int tileResolution = sliceCount == 1 ? resolution : resolution / 2;
            if (sliceCount == 1) Fit(ref slices[0], camera, settings.cameraFit, tileResolution);
            if (cameraData.cameraType == CameraType.Game)
            {
                gameSliceCount = sliceCount;
                Array.Copy(slices, gameSlices, sliceCount);
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
            {
                data.count = sliceCount;
                data.resolution = resolution;
                data.tileResolution = tileResolution;
                data.parameters = sliceCount == 0 ? Vector4.zero
                    : new Vector4(sliceCount, 1f / resolution, Mathf.Clamp01(settings.softness), Mathf.Clamp01(settings.strength));
                data.parameters2 = new Vector4(settings.debugView ? 1f : 0f,
                    settings.lightMode == FairyToonCharacterShadowLight.CameraAligned ? 1f : 0f, 0f, 0f);
                data.depthBias = Mathf.Clamp(settings.depthBias, 0f, 4f);
                data.normalBias = Mathf.Clamp(settings.normalBias, 0f, 4f);
                data.fallback = owner.CharacterShadowFallback;
                if (sliceCount > 0)
                {
                    ShadowUtils.ShadowRTReAllocateIfNeeded(ref atlas, resolution, resolution, 32, name: "_FT_CharShadowMap");
                    var texture = renderGraph.ImportTexture(atlas);
                    builder.SetRenderAttachmentDepth(texture, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(texture, MapId);
                }
                for (int i = 0; i < MaxCharacters; i++)
                {
                    data.matrices[i] = Matrix4x4.zero;
                    data.rects[i] = Vector4.zero;
                    data.lights[i] = Vector4.zero;
                    data.owners[i] = Vector4.zero;
                    if (i >= sliceCount) { data.slices[i] = default; continue; }
                    var slice = slices[i];
                    data.slices[i] = slice;
                    int x = (i % 2) * tileResolution, y = (i / 2) * tileResolution;
                    var shadowSlice = new ShadowSliceData
                    {
                        shadowTransform = ShadowTransform(slice.projection, slice.view),
                        offsetX = x, offsetY = y, resolution = tileResolution,
                    };
                    ShadowUtils.ApplySliceTransform(ref shadowSlice, resolution, resolution);
                    data.matrices[i] = shadowSlice.shadowTransform;
                    data.rects[i] = new Vector4((float)x / resolution, (float)y / resolution,
                        (float)(x + tileResolution) / resolution, (float)(y + tileResolution) / resolution);
                    data.lights[i] = new Vector4(slice.towards.x, slice.towards.y, slice.towards.z, slice.texel);
                    // Use two exactly representable floats to carry the 32-bit ID in a shader vector.
                    uint shadowOwner = slice.character.ShadowOwner;
                    data.owners[i] = new Vector4(shadowOwner & 0xffffu, shadowOwner >> 16, 0f, 0f);
                }
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    if (pass.count > 0)
                    {
                        cmd.SetViewport(new Rect(0f, 0f, pass.resolution, pass.resolution));
                        // (1 = far plane; Unity remaps it for reversed Z, as URP's own clears rely on)
                        cmd.ClearRenderTarget(true, false, Color.black, 1f);
                    }
                    for (int i = 0; i < pass.count; i++)
                    {
                        var slice = pass.slices[i];
                        cmd.SetViewport(new Rect((i % 2) * pass.tileResolution, (i / 2) * pass.tileResolution,
                            pass.tileResolution, pass.tileResolution));
                        cmd.SetGlobalMatrix(CasterVPId, GL.GetGPUProjectionMatrix(slice.projection, true) * slice.view);
                        cmd.SetGlobalVector(CasterLightId, new Vector4(slice.towards.x, slice.towards.y, slice.towards.z, 0f));
                        cmd.SetGlobalVector(CasterBiasId, new Vector4(pass.depthBias * slice.texel, pass.normalBias * slice.texel, 0f, 0f));
                        var draws = slice.character.ShadowDraws;
                        for (int d = 0; d < draws.Count; d++)
                        {
                            var draw = draws[d];
                            if (!FairyToonCharacter.CanDraw(draw)) continue;
                            var material = draw.pass >= 0 ? draw.material : pass.fallback;
                            if (material != null) cmd.DrawRenderer(draw.renderer, material, draw.submesh, draw.pass >= 0 ? draw.pass : 0);
                        }
                    }
                    cmd.SetGlobalVector(ParamsId, pass.parameters);
                    cmd.SetGlobalVector(Params2Id, pass.parameters2);
                    cmd.SetGlobalMatrixArray(MatrixId, pass.matrices);
                    cmd.SetGlobalVectorArray(RectId, pass.rects);
                    cmd.SetGlobalVectorArray(LightId, pass.lights);
                    cmd.SetGlobalVectorArray(OwnersId, pass.owners);
                });
            }
        }

        internal static Vector3 CameraLight(Vector3 camera, Vector3 centre, Vector2 pitchRange, float yawOffset)
        {
            Vector3 delta = camera - centre;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float yaw = horizontal > 0.00001f ? Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg : 0f;
            float pitch = Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));
            float elevation = pitch * Mathf.Deg2Rad;
            Vector3 flat = Quaternion.AngleAxis(yaw + yawOffset, Vector3.up) * Vector3.forward;
            return (flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)).normalized;
        }

        private bool Fit(ref Slice slice, Camera camera, bool cameraFit, int resolution)
        {
            Vector3 ray = -slice.towards;
            Vector3 upRef = Mathf.Abs(Vector3.Dot(ray, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(upRef, ray).normalized;
            Vector3 up = Vector3.Cross(ray, right);
            Vector3 centre = slice.bounds.center, extents = slice.bounds.extents;
            float minR = float.MaxValue, minU = float.MaxValue, minF = float.MaxValue;
            float maxR = float.MinValue, maxU = float.MinValue, maxF = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = centre + new Vector3((i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z);
                float r = Vector3.Dot(corner, right), u = Vector3.Dot(corner, up), f = Vector3.Dot(corner, ray);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minF = Mathf.Min(minF, f); maxF = Mathf.Max(maxF, f);
            }
            if (cameraFit)
            {
                // (the depth range the camera sees the character in: its box corners along the view axis. A sphere
                // around the box would double it, and the projector's angle to the view stretches every extra metre
                // across the map)
                Vector3 origin = camera.transform.position, forward = camera.transform.forward;
                float near = float.MaxValue, far = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = centre + new Vector3((i & 1) == 0 ? -extents.x : extents.x,
                        (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z);
                    float depth = Vector3.Dot(corner - origin, forward);
                    near = Mathf.Min(near, depth);
                    far = Mathf.Max(far, depth);
                }
                near = Mathf.Max(camera.nearClipPlane, near);
                far = Mathf.Min(camera.farClipPlane, far);
                if (far <= near) return false;
                float cameraMinR = float.MaxValue, cameraMinU = float.MaxValue;
                float cameraMaxR = float.MinValue, cameraMaxU = float.MinValue;
                for (int plane = 0; plane < 2; plane++)
                {
                    // (CalculateFrustumCorners also keeps an orthographic camera's constant cross-section)
                    camera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), plane == 0 ? near : far,
                        Camera.MonoOrStereoscopicEye.Mono, frustumCorners);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 corner = camera.transform.TransformPoint(frustumCorners[i]);
                        float r = Vector3.Dot(corner, right), u = Vector3.Dot(corner, up);
                        cameraMinR = Mathf.Min(cameraMinR, r); cameraMaxR = Mathf.Max(cameraMaxR, r);
                        cameraMinU = Mathf.Min(cameraMinU, u); cameraMaxU = Mathf.Max(cameraMaxU, u);
                    }
                }
                minR = Mathf.Max(minR, cameraMinR); maxR = Mathf.Min(maxR, cameraMaxR);
                minU = Mathf.Max(minU, cameraMinU); maxU = Mathf.Min(maxU, cameraMaxU);
                if (maxR <= minR || maxU <= minU) return false;
            }
            // (square texels: the 7x7 tent filters the same distance both ways. With long texels a shallow edge keeps
            // its stair steps, because the filter along the edge is shorter than one step)
            float width = PaddedSize(Mathf.Max(maxR - minR, maxU - minU), resolution), height = width;
            float texelR = width / resolution, texelU = height / resolution;
            float midR = Mathf.Round((minR + maxR) * 0.5f / texelR) * texelR;
            float midU = Mathf.Round((minU + maxU) * 0.5f / texelU) * texelU;
            const float nearClip = 0.01f;
            Vector3 eye = right * midR + up * midU + ray * (minF - 0.05f - nearClip);
            slice.view = LookView(eye, ray, up);
            slice.projection = Matrix4x4.Ortho(-width * 0.5f, width * 0.5f, -height * 0.5f, height * 0.5f,
                nearClip, nearClip + maxF - minF + 0.1f);
            slice.texel = Mathf.Max(texelR, texelU);
            return true;
        }

        internal static float PaddedSize(float size, int resolution)
        {
            float padded = Mathf.Max(0.04f, size) * 1.02f / (1f - 8f / resolution);
            return Mathf.Pow(2f, Mathf.Ceil(Mathf.Log(padded, 2f) * 8f) / 8f);
        }

        private static Matrix4x4 LookView(Vector3 eye, Vector3 forward, Vector3 up)
        {
            return Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                * Matrix4x4.TRS(eye, Quaternion.LookRotation(forward, up), Vector3.one).inverse;
        }

        internal static Matrix4x4 ShadowTransform(Matrix4x4 projection, Matrix4x4 view)
        {
            // (URP GetShadowTransform takes the CPU projection, before GPU conversion)
            if (SystemInfo.usesReversedZBuffer)
            {
                projection.m20 = -projection.m20; projection.m21 = -projection.m21;
                projection.m22 = -projection.m22; projection.m23 = -projection.m23;
            }
            var scaleBias = Matrix4x4.identity;
            scaleBias.m00 = 0.5f; scaleBias.m11 = 0.5f; scaleBias.m22 = 0.5f;
            scaleBias.m03 = 0.5f; scaleBias.m13 = 0.5f; scaleBias.m23 = 0.5f;
            return scaleBias * projection * view;
        }

        internal sealed class ResetPass : ScriptableRenderPass
        {
            private sealed class PassData { }

            public ResetPass()
            {
                profilingSampler = new ProfilingSampler("FairyToon Character Shadow Reset");
                renderPassEvent = RenderPassEvent.AfterRendering;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out _, profilingSampler))
                {
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData _, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(ParamsId, Vector4.zero);
                    });
                }
            }
        }
    }
}
